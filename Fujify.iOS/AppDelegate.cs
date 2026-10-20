using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.iOS;
using Foundation;
using Fujify.Services;
using Fujify.ViewModels;
using Photos;
using UIKit;

namespace Fujify.iOS;

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Global unhandled exception handler
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Crash]: {ex}");
        };

        // Configure persistent writable staging directory for iOS
        DialogService.PlatformStagingDirectoryResolver = () =>
        {
            string staging = Path.Combine(Path.GetTempPath(), "Fujify_Staging");
            Directory.CreateDirectory(staging);
            return staging;
        };

        // Wire Open in Lightroom handler using native iOS share sheet & activity view controller
        MainViewModel.PlatformOpenInLightroomCallback = OpenInLightroom;

        // Wire media library export for newly converted DNG files (saving to iOS Photos Library)
        MainViewModel.PlatformFileCreatedCallback = (filePath) =>
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

                PHPhotoLibrary.RequestAuthorization(PHAccessLevel.AddOnly, status =>
                {
                    if (status == PHAuthorizationStatus.Authorized || status == PHAuthorizationStatus.Limited)
                    {
                        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(() =>
                        {
                            var request = PHAssetCreationRequest.CreationRequestForAsset();
                            request.AddResource(PHAssetResourceType.Photo, NSUrl.FromFilename(filePath), null);
                        }, (success, error) =>
                        {
                            if (!success && error != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error saving to photo library: {error.LocalizedDescription}");
                            }
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Exception in PlatformFileCreatedCallback: {ex}");
            }
        };

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    [Export("application:openURL:options:")]
    public new bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        HandleIncomingUrl(url);
        return true;
    }

    [Export("application:openURL:sourceApplication:annotation:")]
    public bool OpenUrl(UIApplication application, NSUrl url, string sourceApplication, NSObject annotation)
    {
        HandleIncomingUrl(url);
        return true;
    }

    private void HandleIncomingUrl(NSUrl? url)
    {
        if (url == null) return;

        try
        {
            string? sourcePath = url.Path;
            if (string.IsNullOrEmpty(sourcePath)) return;

            string stagingDir = DialogService.GetStagingDirectory();
            string fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"shared_{Guid.NewGuid():N}.raw";
            }

            string destPath = Path.Combine(stagingDir, fileName);

            // Copy file to staging directory to release file lock / inbox sandbox
            File.Copy(sourcePath, destPath, true);

            // Detect extension from magic bytes if extension is missing or generic
            string ext = Path.GetExtension(destPath);
            if (string.IsNullOrEmpty(ext) || ext.Equals(".raw", StringComparison.OrdinalIgnoreCase))
            {
                string detected = DialogService.DetectRawExtension(destPath);
                if (!string.IsNullOrEmpty(detected) && !detected.Equals(".raw", StringComparison.OrdinalIgnoreCase))
                {
                    string renamed = Path.ChangeExtension(destPath, detected);
                    if (File.Exists(renamed)) File.Delete(renamed);
                    File.Move(destPath, renamed);
                    destPath = renamed;
                }
            }

            if (MainViewModel.Instance != null)
            {
                MainViewModel.Instance.ImportSharedFiles(new[] { destPath });
            }
            else
            {
                lock (MainViewModel.PendingInitialFiles)
                {
                    MainViewModel.PendingInitialFiles.Add(destPath);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error handling incoming URL: {ex}");
        }
    }

    private static UIViewController? GetTopViewController()
    {
        var window = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);

        var current = window?.RootViewController;
        while (current?.PresentedViewController != null)
        {
            current = current.PresentedViewController;
        }
        return current;
    }

    private void OpenInLightroom(IReadOnlyList<string> targetFiles)
    {
        if (targetFiles == null || targetFiles.Count == 0) return;

        try
        {
            var fileUrls = targetFiles
                .Where(File.Exists)
                .Select(p => (NSObject)NSUrl.FromFilename(p))
                .ToArray();

            if (fileUrls.Length == 0) return;

            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                var topController = GetTopViewController();
                if (topController == null) return;

                var activityController = new UIActivityViewController(fileUrls, null);

                // Configure iPad popover presentation to avoid crash
                if (activityController.PopoverPresentationController != null && topController.View != null)
                {
                    activityController.PopoverPresentationController.SourceView = topController.View;
                    activityController.PopoverPresentationController.SourceRect = new CoreGraphics.CGRect(
                        topController.View.Bounds.GetMidX(),
                        topController.View.Bounds.GetMidY(),
                        0, 0);
                    activityController.PopoverPresentationController.PermittedArrowDirections = 0;
                }

                topController.PresentViewController(activityController, true, null);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error opening files in Lightroom: {ex}");
        }
    }
}
