using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuthenticationServices;
using Avalonia;
using Avalonia.iOS;
using Foundation;
using Fujify.Services;
using Fujify.ViewModels;
using Photos;
using PhotosUI;
using UIKit;

namespace Fujify.iOS;

public class WebAuthPresentationContextProvider : NSObject, IASWebAuthenticationPresentationContextProviding
{
    private readonly UIWindow? _window;

    public WebAuthPresentationContextProvider(UIWindow? window)
    {
        _window = window;
    }

    [Export("presentationAnchorForWebAuthenticationSession:")]
    public UIWindow GetPresentationAnchor(ASWebAuthenticationSession session)
    {
        if (_window != null) return _window;

        var window = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);

        return window ?? UIApplication.SharedApplication.Windows.FirstOrDefault() ?? new UIWindow();
    }
}

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    private static PhotoPickerDelegate? _activePickerDelegate;
    private static ASWebAuthenticationSession? _activeAuthSession;
    private static WebAuthPresentationContextProvider? _activePresentationProvider;

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Global unhandled exception handler
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Crash]: {ex}");
        };

        // Wire native in-app OAuth authentication session (ASWebAuthenticationSession)
        LicenseService.PlatformOAuthLauncher = AuthenticateWithWebSessionAsync;

        // Configure persistent writable staging directory for iOS
        DialogService.PlatformStagingDirectoryResolver = () =>
        {
            string staging = Path.Combine(Path.GetTempPath(), "Fujifier_Staging");
            Directory.CreateDirectory(staging);
            return staging;
        };

        // Wire Open in Lightroom handler using native iOS share sheet & activity view controller
        MainViewModel.PlatformOpenInLightroomCallback = OpenInLightroom;

        // Wire native iOS photo gallery picker (PHPickerViewController)
        MainViewModel.PlatformPickPhotosCallback = PickPhotosAsync;

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

        // Wire native URL opening handler (Safari / External Browser)
        MainViewModel.PlatformOpenUrlCallback = (url) =>
        {
            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var nsUrl = NSUrl.FromString(url);
                    if (nsUrl != null)
                    {
                        UIApplication.SharedApplication.OpenUrl(nsUrl, new UIApplicationOpenUrlOptions(), null);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error in PlatformOpenUrlCallback: {ex}");
                }
            });
        };

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    [Export("application:didFinishLaunchingWithOptions:")]
    public new bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        var result = base.FinishedLaunching(application, launchOptions);
        try
        {
            if (Window != null)
            {
                Window.BackgroundColor = UIColor.FromRGB(24, 24, 27);
            }

            // Initialize Google Mobile Ads (Banner, Splash Video, Interstitial)
            AdManager.Instance.Initialize(Window);

            if (launchOptions != null && launchOptions.ContainsKey(UIApplication.LaunchOptionsUrlKey))
            {
                var launchUrl = launchOptions[UIApplication.LaunchOptionsUrlKey] as NSUrl;
                if (launchUrl != null)
                {
                    HandleIncomingUrl(launchUrl);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error setting Window background or processing launch URL: {ex}");
        }
        return result;
    }

    [Export("application:openURL:options:")]
    public new bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        return HandleIncomingUrl(url);
    }

    [Export("application:openURL:sourceApplication:annotation:")]
    public bool OpenUrl(UIApplication application, NSUrl url, string sourceApplication, NSObject annotation)
    {
        return HandleIncomingUrl(url);
    }

    private static Task<(string Code, string? State)?> AuthenticateWithWebSessionAsync(string authUrl, string callbackScheme, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<(string Code, string? State)?>(TaskCreationOptions.RunContinuationsAsynchronously);

        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var nsUrl = new NSUrl(authUrl);

                var window = UIApplication.SharedApplication.ConnectedScenes
                    .OfType<UIWindowScene>()
                    .SelectMany(s => s.Windows)
                    .FirstOrDefault(w => w.IsKeyWindow);
                _activePresentationProvider = new WebAuthPresentationContextProvider(window);
                _activeAuthSession = new ASWebAuthenticationSession(nsUrl, callbackScheme, (callbackUrl, error) =>
                {
                    try
                    {
                        if (error != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Fujify iOS] ASWebAuthenticationSession error: {error.LocalizedDescription} (code: {error.Code})");
                            if (error.Code == (long)ASWebAuthenticationSessionErrorCode.CanceledLogin)
                            {
                                tcs.TrySetException(new OperationCanceledException("Google Sign-In was cancelled."));
                            }
                            else
                            {
                                tcs.TrySetResult(null);
                            }
                            return;
                        }

                        if (callbackUrl != null)
                        {
                            string? code = null;
                            string? state = null;

                            try
                            {
                                var components = new NSUrlComponents(callbackUrl, true);
                                code = components.QueryItems?.FirstOrDefault(q => q.Name == "code")?.Value;
                                state = components.QueryItems?.FirstOrDefault(q => q.Name == "state")?.Value;
                            }
                            catch { }

                            if (string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(callbackUrl.Query))
                            {
                                foreach (var part in callbackUrl.Query.TrimStart('?').Split('&'))
                                {
                                    var kv = part.Split('=', 2);
                                    if (kv.Length == 2)
                                    {
                                        string k = Uri.UnescapeDataString(kv[0]);
                                        string v = Uri.UnescapeDataString(kv[1]);
                                        if (k == "code") code = v;
                                        else if (k == "state") state = v;
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(code))
                            {
                                tcs.TrySetResult((code, state));
                                return;
                            }
                        }

                        tcs.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error in ASWebAuthenticationSession completion: {ex}");
                        tcs.TrySetResult(null);
                    }
                    finally
                    {
                        _activeAuthSession = null;
                        _activePresentationProvider = null;
                    }
                });

                if (UIDevice.CurrentDevice.CheckSystemVersion(13, 0))
                {
                    _activeAuthSession.PresentationContextProvider = _activePresentationProvider;
                }

                cancellationToken.Register(() =>
                {
                    try
                    {
                        _activeAuthSession?.Cancel();
                    }
                    catch { }
                    _activeAuthSession = null;
                    _activePresentationProvider = null;
                    tcs.TrySetCanceled();
                });

                bool started = _activeAuthSession.Start();
                if (!started)
                {
                    System.Diagnostics.Debug.WriteLine("[Fujify iOS] ASWebAuthenticationSession Start returned false");
                    _activeAuthSession = null;
                    _activePresentationProvider = null;
                    tcs.TrySetResult(null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error launching ASWebAuthenticationSession: {ex}");
                _activeAuthSession = null;
                _activePresentationProvider = null;
                tcs.TrySetResult(null);
            }
        });

        return tcs.Task;
    }

    private bool HandleIncomingUrl(NSUrl? url)
    {
        if (url == null) return false;

        try
        {
            // 1. Handle custom URL scheme OAuth callback (e.g., fujifier://oauth-callback?code=...&state=...)
            if (url.Scheme != null && string.Equals(url.Scheme, "fujifier", StringComparison.OrdinalIgnoreCase))
            {
                string? code = null;
                string? state = null;

                try
                {
                    var components = new NSUrlComponents(url, true);
                    code = components.QueryItems?.FirstOrDefault(q => q.Name == "code")?.Value;
                    state = components.QueryItems?.FirstOrDefault(q => q.Name == "state")?.Value;
                }
                catch { }

                if (string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(url.Query))
                {
                    foreach (var part in url.Query.TrimStart('?').Split('&'))
                    {
                        var kv = part.Split('=', 2);
                        if (kv.Length == 2)
                        {
                            string k = Uri.UnescapeDataString(kv[0]);
                            string v = Uri.UnescapeDataString(kv[1]);
                            if (k == "code") code = v;
                            else if (k == "state") state = v;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(code))
                {
                    LicenseService.OnOAuthCallbackReceived?.Invoke(code, state);
                }
                return true;
            }

            // 2. Handle shared raw file / document import
            string? sourcePath = url.Path;
            if (string.IsNullOrEmpty(sourcePath)) return false;

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
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error handling incoming URL: {ex}");
            return false;
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

    private Task<IReadOnlyList<string>?> PickPhotosAsync()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<string>?>();

        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            var topController = GetTopViewController();
            if (topController == null)
            {
                tcs.TrySetResult(null);
                return;
            }

            var alert = UIAlertController.Create(
                "Import RAW Photos",
                "Choose photo source to import:",
                UIAlertControllerStyle.ActionSheet);

            alert.AddAction(UIAlertAction.Create("Photo Library (Camera Roll)", UIAlertActionStyle.Default, _ =>
            {
                OpenPhotoLibraryPicker(topController, tcs);
            }));

            alert.AddAction(UIAlertAction.Create("Files App (Browse Files)", UIAlertActionStyle.Default, async _ =>
            {
                try
                {
                    var dialogService = new DialogService();
                    var files = await dialogService.ShowOpenFileDialogAsync(null, "Select RAW & DNG files", allowMultiple: true);
                    tcs.TrySetResult(files);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error picking from files: {ex}");
                    tcs.TrySetResult(Array.Empty<string>());
                }
            }));

            alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, _ =>
            {
                tcs.TrySetResult(Array.Empty<string>());
            }));

            // iPad popover configuration
            if (alert.PopoverPresentationController != null && topController.View != null)
            {
                alert.PopoverPresentationController.SourceView = topController.View;
                alert.PopoverPresentationController.SourceRect = new CoreGraphics.CGRect(
                    topController.View.Bounds.GetMidX(),
                    topController.View.Bounds.GetMidY(),
                    0, 0);
                alert.PopoverPresentationController.PermittedArrowDirections = 0;
            }

            topController.PresentViewController(alert, true, null);
        });

        return tcs.Task;
    }

    private static void OpenPhotoLibraryPicker(UIViewController topController, TaskCompletionSource<IReadOnlyList<string>?> tcs)
    {
        try
        {
            var config = new PHPickerConfiguration(PHPhotoLibrary.SharedPhotoLibrary)
            {
                SelectionLimit = 0,
                Filter = PHPickerFilter.ImagesFilter,
                PreferredAssetRepresentationMode = PHPickerConfigurationAssetRepresentationMode.Current
            };

            var picker = new PHPickerViewController(config);
            var pickerDelegate = new PhotoPickerDelegate(tcs);
            picker.Delegate = pickerDelegate;

            _activePickerDelegate = pickerDelegate;
            topController.PresentViewController(picker, true, null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error presenting photo picker: {ex}");
            tcs.TrySetResult(Array.Empty<string>());
        }
    }

    private class PhotoPickerDelegate : PHPickerViewControllerDelegate
    {
        private readonly TaskCompletionSource<IReadOnlyList<string>?> _tcs;

        public PhotoPickerDelegate(TaskCompletionSource<IReadOnlyList<string>?> tcs)
        {
            _tcs = tcs;
        }

        public override void DidFinishPicking(PHPickerViewController picker, PHPickerResult[] results)
        {
            picker.DismissViewController(true, async () =>
            {
                try
                {
                    _activePickerDelegate = null;
                    if (results == null || results.Length == 0)
                    {
                        _tcs.TrySetResult(Array.Empty<string>());
                        return;
                    }

                    var files = await ProcessPickedResultsAsync(results);
                    _tcs.TrySetResult(files);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error in DidFinishPicking: {ex}");
                    _tcs.TrySetResult(Array.Empty<string>());
                }
            });
        }
    }

    private static async Task<IReadOnlyList<string>> ProcessPickedResultsAsync(PHPickerResult[] results)
    {
        var outputPaths = new List<string>();
        string stagingDir = DialogService.GetStagingDirectory();

        foreach (var result in results)
        {
            try
            {
                string? importedPath = null;

                // 1. Try PHAsset direct master resource export (for high-fidelity camera RAW preservation)
                if (!string.IsNullOrEmpty(result.AssetIdentifier))
                {
                    try
                    {
                        var fetch = PHAsset.FetchAssetsUsingLocalIdentifiers(new[] { result.AssetIdentifier }, null);
                        if (fetch.Count > 0 && fetch.FirstObject is PHAsset asset)
                        {
                            var resources = PHAssetResource.GetAssetResources(asset);

                            var targetResource = resources.FirstOrDefault(r =>
                                r.OriginalFilename.EndsWith(".dng", StringComparison.OrdinalIgnoreCase) ||
                                r.OriginalFilename.EndsWith(".raf", StringComparison.OrdinalIgnoreCase) ||
                                r.OriginalFilename.EndsWith(".cr3", StringComparison.OrdinalIgnoreCase) ||
                                r.OriginalFilename.EndsWith(".cr2", StringComparison.OrdinalIgnoreCase) ||
                                r.OriginalFilename.EndsWith(".arw", StringComparison.OrdinalIgnoreCase) ||
                                r.OriginalFilename.EndsWith(".nef", StringComparison.OrdinalIgnoreCase) ||
                                r.OriginalFilename.EndsWith(".orf", StringComparison.OrdinalIgnoreCase) ||
                                r.OriginalFilename.EndsWith(".rw2", StringComparison.OrdinalIgnoreCase))
                                ?? resources.FirstOrDefault(r => r.ResourceType == PHAssetResourceType.Photo || r.ResourceType == PHAssetResourceType.FullSizePhoto)
                                ?? resources.FirstOrDefault();

                            if (targetResource != null)
                            {
                                string fileName = targetResource.OriginalFilename;
                                if (string.IsNullOrWhiteSpace(fileName))
                                {
                                    fileName = $"photo_{Guid.NewGuid():N}.dng";
                                }
                                string destFile = Path.Combine(stagingDir, $"{Guid.NewGuid():N}_{fileName}");
                                var options = new PHAssetResourceRequestOptions
                                {
                                    NetworkAccessAllowed = true
                                };

                                var writeTcs = new TaskCompletionSource<bool>();
                                PHAssetResourceManager.DefaultManager.WriteData(targetResource, NSUrl.FromFilename(destFile), options, err =>
                                {
                                    if (err != null)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[Fujify iOS] PHAssetResourceManager error: {err.LocalizedDescription}");
                                        writeTcs.TrySetResult(false);
                                    }
                                    else
                                    {
                                        writeTcs.TrySetResult(File.Exists(destFile));
                                    }
                                });

                                bool ok = await writeTcs.Task;
                                if (ok && File.Exists(destFile) && new FileInfo(destFile).Length > 0)
                                {
                                    importedPath = destFile;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Exception exporting PHAssetResource: {ex.Message}");
                    }
                }

                // 2. Fallback to NSItemProvider file representation
                if (importedPath == null && result.ItemProvider != null)
                {
                    var item = result.ItemProvider;
                    var typeIds = item.RegisteredTypeIdentifiers;
                    if (typeIds != null && typeIds.Length > 0)
                    {
                        string preferredType = typeIds.FirstOrDefault(id => id.Contains("raw", StringComparison.OrdinalIgnoreCase) || id.Contains("dng", StringComparison.OrdinalIgnoreCase))
                            ?? typeIds.FirstOrDefault(id => id.Equals("public.image", StringComparison.OrdinalIgnoreCase))
                            ?? typeIds[0];

                        var loadTcs = new TaskCompletionSource<string?>();
                        item.LoadFileRepresentation(preferredType, (url, err) =>
                        {
                            if (url != null && !string.IsNullOrEmpty(url.Path) && File.Exists(url.Path))
                            {
                                try
                                {
                                    string origName = Path.GetFileName(url.Path);
                                    if (string.IsNullOrWhiteSpace(origName) || origName.StartsWith("tmp", StringComparison.OrdinalIgnoreCase))
                                    {
                                        origName = item.SuggestedName ?? $"photo_{Guid.NewGuid():N}.raw";
                                    }
                                    string destFile = Path.Combine(stagingDir, $"{Guid.NewGuid():N}_{origName}");
                                    File.Copy(url.Path, destFile, true);
                                    loadTcs.TrySetResult(destFile);
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error copying from LoadFileRepresentation: {ex.Message}");
                                }
                            }
                            loadTcs.TrySetResult(null);
                        });

                        importedPath = await loadTcs.Task;
                    }
                }

                if (!string.IsNullOrEmpty(importedPath) && File.Exists(importedPath))
                {
                    // Detect file extension if missing or generic .raw
                    string ext = Path.GetExtension(importedPath);
                    if (string.IsNullOrEmpty(ext) || ext.Equals(".raw", StringComparison.OrdinalIgnoreCase))
                    {
                        string detected = DialogService.DetectRawExtension(importedPath);
                        if (!string.IsNullOrEmpty(detected) && !detected.Equals(".raw", StringComparison.OrdinalIgnoreCase))
                        {
                            string renamed = Path.ChangeExtension(importedPath, detected);
                            if (File.Exists(renamed)) File.Delete(renamed);
                            File.Move(importedPath, renamed);
                            importedPath = renamed;
                        }
                    }

                    outputPaths.Add(importedPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Fujify iOS] Error processing item: {ex.Message}");
            }
        }

        return outputPaths;
    }
}
