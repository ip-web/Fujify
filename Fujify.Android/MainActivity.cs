using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using Avalonia.Skia;
using Fujify.Services;
using Fujify.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Fujify.Android;

[Activity(
    Label = "Fujifier",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    HardwareAccelerated = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
[IntentFilter(
    new[] { Intent.ActionSend },
    Categories = new[] { Intent.CategoryDefault },
    DataMimeType = "image/*")]
[IntentFilter(
    new[] { Intent.ActionSend },
    Categories = new[] { Intent.CategoryDefault },
    DataMimeType = "application/octet-stream")]
[IntentFilter(
    new[] { Intent.ActionSendMultiple },
    Categories = new[] { Intent.CategoryDefault },
    DataMimeType = "image/*")]
[IntentFilter(
    new[] { Intent.ActionSendMultiple },
    Categories = new[] { Intent.CategoryDefault },
    DataMimeType = "application/octet-stream")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "image/*")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/octet-stream")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "fujifier",
    DataHost = "oauth-callback")]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AdManager.Instance.PreInitialize(this);
        base.OnCreate(savedInstanceState);
        EnableHighRefreshRate();

        // Crash interceptor
        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
        {
            var ex = args.Exception;
            global::Android.Util.Log.Error("FujifierCrash", $"[Fujifier Crash Interceptor]: {ex}");
            try
            {
                string logPath = Path.Combine(CacheDir?.AbsolutePath ?? "", "crash.log");
                File.WriteAllText(logPath, ex.ToString());
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            global::Android.Util.Log.Error("FujifierCrash", $"[Fujifier AppDomain Interceptor]: {ex}");
        };

        // Configure persistent writable staging directory for Android
        DialogService.PlatformStagingDirectoryResolver = () =>
            CacheDir?.AbsolutePath ?? FilesDir?.AbsolutePath ?? "";

        // Wire media scanner and photo album export for newly converted DNG files
        MainViewModel.PlatformFileCreatedCallback = (filePath) =>
        {
            try
            {
                string picturesDir = global::Android.OS.Environment.GetExternalStoragePublicDirectory(
                    global::Android.OS.Environment.DirectoryPictures)?.AbsolutePath ?? "";

                if (!string.IsNullOrEmpty(picturesDir))
                {
                    string fujifyPicturesDir = Path.Combine(picturesDir, "Fujifier");
                    Directory.CreateDirectory(fujifyPicturesDir);
                    string targetPath = Path.Combine(fujifyPicturesDir, Path.GetFileName(filePath));
                    if (!string.Equals(filePath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(filePath, targetPath, true);
                        filePath = targetPath;
                    }
                }

                global::Android.Media.MediaScannerConnection.ScanFile(
                    ApplicationContext,
                    new[] { filePath },
                    new[] { "image/x-adobe-dng", "image/dng" },
                    null);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("Fujifier", $"Error in PlatformFileCreatedCallback: {ex.Message}");
            }
        };

        // Wire Open in Lightroom handler
        MainViewModel.PlatformOpenInLightroomCallback = OpenInLightroom;

        // Wire native URL opening handler (Default Browser)
        MainViewModel.PlatformOpenUrlCallback = (url) =>
        {
            try
            {
                var uri = global::Android.Net.Uri.Parse(url);
                var browserIntent = new Intent(Intent.ActionView, uri);
                browserIntent.AddFlags(ActivityFlags.NewTask);
                StartActivity(browserIntent);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("Fujifier", $"Error opening url {url}: {ex}");
            }
        };

        // Process any incoming share or view intent
        HandleIncomingIntent(Intent);

        // Initialize Google Mobile Ads (Banner, Splash Video, Interstitial)
        AdManager.Instance.Initialize(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        AdManager.Instance.Resume();
    }

    protected override void OnPause()
    {
        base.OnPause();
        AdManager.Instance.Pause();
    }

    protected override void OnDestroy()
    {
        AdManager.Instance.Destroy();
        base.OnDestroy();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleIncomingIntent(intent);
    }

    private void HandleIncomingIntent(Intent? intent)
    {
        if (intent == null) return;
        string? action = intent.Action;
        if (string.IsNullOrEmpty(action)) return;

        var uris = new List<global::Android.Net.Uri>();

        if (action == Intent.ActionSend)
        {
            if (intent.GetParcelableExtra(Intent.ExtraStream) is global::Android.Net.Uri streamUri)
            {
                uris.Add(streamUri);
            }
            else if (intent.Data != null)
            {
                uris.Add(intent.Data);
            }
            else if (intent.ClipData != null && intent.ClipData.ItemCount > 0)
            {
                for (int i = 0; i < intent.ClipData.ItemCount; i++)
                {
                    var u = intent.ClipData.GetItemAt(i)?.Uri;
                    if (u != null) uris.Add(u);
                }
            }
        }
        else if (action == Intent.ActionSendMultiple)
        {
            var list = intent.GetParcelableArrayListExtra(Intent.ExtraStream);
            if (list != null)
            {
                foreach (var item in list)
                {
                    if (item is global::Android.Net.Uri u)
                    {
                        uris.Add(u);
                    }
                }
            }
            if (intent.ClipData != null && intent.ClipData.ItemCount > 0)
            {
                for (int i = 0; i < intent.ClipData.ItemCount; i++)
                {
                    var u = intent.ClipData.GetItemAt(i)?.Uri;
                    if (u != null && !uris.Contains(u)) uris.Add(u);
                }
            }
        }
        else if (action == Intent.ActionView)
        {
            if (intent.Data != null && string.Equals(intent.Data.Scheme, "fujifier", StringComparison.OrdinalIgnoreCase))
            {
                string? code = intent.Data.GetQueryParameter("code");
                string? state = intent.Data.GetQueryParameter("state");
                if (!string.IsNullOrEmpty(code))
                {
                    LicenseService.OnOAuthCallbackReceived?.Invoke(code, state);
                }
                return;
            }

            if (intent.Data != null)
            {
                uris.Add(intent.Data);
            }
            else if (intent.ClipData != null && intent.ClipData.ItemCount > 0)
            {
                for (int i = 0; i < intent.ClipData.ItemCount; i++)
                {
                    var u = intent.ClipData.GetItemAt(i)?.Uri;
                    if (u != null) uris.Add(u);
                }
            }
        }

        if (uris.Count == 0) return;

        // Process incoming URIs on a background thread so UI doesn't block while streaming
        Task.Run(() =>
        {
            var stagedPaths = new List<string>();
            string stagingDir = DialogService.GetStagingDirectory();

            foreach (var uri in uris)
            {
                try
                {
                    string staged = StageUri(uri, stagingDir);
                    if (!string.IsNullOrEmpty(staged) && File.Exists(staged))
                    {
                        stagedPaths.Add(staged);
                    }
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Warn("Fujifier", $"Failed to stage incoming shared URI {uri}: {ex.Message}");
                }
            }

            if (stagedPaths.Count > 0)
            {
                if (MainViewModel.Instance != null)
                {
                    MainViewModel.Instance.ImportSharedFiles(stagedPaths);
                }
                else
                {
                    lock (MainViewModel.PendingInitialFiles)
                    {
                        MainViewModel.PendingInitialFiles.AddRange(stagedPaths);
                    }
                }
            }
        });
    }

    private string StageUri(global::Android.Net.Uri uri, string stagingDir)
    {
        if (uri.Scheme == "file")
        {
            string localPath = uri.Path ?? "";
            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            {
                return localPath;
            }
        }

        string fileName = string.Empty;
        try
        {
            if (ContentResolver != null)
            {
                using var cursor = ContentResolver.Query(uri, null, null, null, null);
                if (cursor != null && cursor.MoveToFirst())
                {
                    int nameIdx = cursor.GetColumnIndex(global::Android.Provider.IOpenableColumns.DisplayName);
                    if (nameIdx >= 0)
                    {
                        string? name = cursor.GetString(nameIdx);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            fileName = Path.GetFileName(name);
                        }
                    }
                }
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            string lastSegment = uri.LastPathSegment ?? "";
            if (!string.IsNullOrWhiteSpace(lastSegment))
            {
                fileName = Path.GetFileName(lastSegment);
            }
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"shared_{Guid.NewGuid():N}.raw";
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }

        string destFilePath = Path.Combine(stagingDir, fileName);

        if (ContentResolver != null)
        {
            using var inStream = ContentResolver.OpenInputStream(uri);
            if (inStream != null)
            {
                using var outStream = File.Create(destFilePath);
                inStream.CopyTo(outStream);
            }
        }

        if (!File.Exists(destFilePath))
        {
            return string.Empty;
        }

        string ext = Path.GetExtension(destFilePath);
        if (string.IsNullOrEmpty(ext) || ext.Equals(".raw", StringComparison.OrdinalIgnoreCase))
        {
            string detected = DialogService.DetectRawExtension(destFilePath);
            if (!string.IsNullOrEmpty(detected) && !detected.Equals(".raw", StringComparison.OrdinalIgnoreCase))
            {
                string renamed = Path.ChangeExtension(destFilePath, detected);
                if (File.Exists(renamed)) File.Delete(renamed);
                File.Move(destFilePath, renamed);
                destFilePath = renamed;
            }
        }

        return destFilePath;
    }

    private void OpenInLightroom(IReadOnlyList<string> targetFiles)
    {
        if (targetFiles == null || targetFiles.Count == 0) return;

        try
        {
            bool isLightroomInstalled = false;
            try
            {
                var pkgInfo = PackageManager?.GetPackageInfo("com.adobe.lrmobile", 0);
                if (pkgInfo != null)
                {
                    isLightroomInstalled = true;
                }
            }
            catch { }

            if (targetFiles.Count == 1)
            {
                string path = targetFiles[0];
                var javaFile = new Java.IO.File(path);
                var contentUri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                    this,
                    "com.ipweb.fujifier.fileprovider",
                    javaFile);

                var intent = new Intent(Intent.ActionSend);
                intent.SetType("image/*");
                intent.PutExtra(Intent.ExtraStream, contentUri);
                intent.ClipData = ClipData.NewRawUri("Converted Photo", contentUri);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);

                if (isLightroomInstalled)
                {
                    intent.SetPackage("com.adobe.lrmobile");
                    try
                    {
                        StartActivity(intent);
                        return;
                    }
                    catch (ActivityNotFoundException)
                    {
                        intent.SetPackage(null);
                    }
                }

                var chooser = Intent.CreateChooser(intent, "Open in Adobe Lightroom");
                if (chooser != null)
                {
                    chooser.AddFlags(ActivityFlags.GrantReadUriPermission);
                    StartActivity(chooser);
                }
                else
                {
                    StartActivity(intent);
                }
            }
            else
            {
                var parcelableUris = new List<IParcelable>();
                ClipData? clipData = null;

                foreach (var path in targetFiles)
                {
                    var javaFile = new Java.IO.File(path);
                    var contentUri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                        this,
                        "com.ipweb.fujifier.fileprovider",
                        javaFile);

                    if (contentUri != null)
                    {
                        parcelableUris.Add(contentUri);

                        if (clipData == null)
                        {
                            clipData = new ClipData("Photos", new[] { "image/*" }, new ClipData.Item(contentUri));
                        }
                        else
                        {
                            clipData.AddItem(new ClipData.Item(contentUri));
                        }
                    }
                }

                var intent = new Intent(Intent.ActionSendMultiple);
                intent.SetType("image/*");
                intent.PutParcelableArrayListExtra(Intent.ExtraStream, parcelableUris);
                if (clipData != null)
                {
                    intent.ClipData = clipData;
                }
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);

                if (isLightroomInstalled)
                {
                    intent.SetPackage("com.adobe.lrmobile");
                    try
                    {
                        StartActivity(intent);
                        return;
                    }
                    catch (ActivityNotFoundException)
                    {
                        intent.SetPackage(null);
                    }
                }

                var chooser = Intent.CreateChooser(intent, "Open in Adobe Lightroom");
                if (chooser != null)
                {
                    chooser.AddFlags(ActivityFlags.GrantReadUriPermission);
                    StartActivity(chooser);
                }
                else
                {
                    StartActivity(intent);
                }
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("Fujifier", $"Error opening files in Lightroom: {ex}");
        }
    }

    private void EnableHighRefreshRate()
    {
        try
        {
            if (Window == null) return;
            var layoutParams = Window.Attributes;
            Display? display = null;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                display = Display;
            }
            else
            {
#pragma warning disable CS0618
                display = WindowManager?.DefaultDisplay;
#pragma warning restore CS0618
            }

            if (display != null)
            {
                var supportedModes = display.GetSupportedModes();
                if (supportedModes != null && supportedModes.Length > 0)
                {
                    int targetModeId = 0;
                    float maxRefreshRate = 0f;
                    foreach (var mode in supportedModes)
                    {
                        if (mode.RefreshRate > maxRefreshRate)
                        {
                            maxRefreshRate = mode.RefreshRate;
                            targetModeId = mode.ModeId;
                        }
                    }
                    if (targetModeId != 0 && layoutParams != null)
                    {
                        layoutParams.PreferredDisplayModeId = targetModeId;
                        Window.Attributes = layoutParams;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("Fujifier", $"EnableHighRefreshRate warning: {ex.Message}");
        }
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 128 * 1024 * 1024
            });
    }
}
