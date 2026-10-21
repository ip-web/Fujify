using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Fujify.Services;
using Fujify.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Fujify.Android;

[Activity(
    Label = "Fujify",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
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
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Crash interceptor
        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
        {
            var ex = args.Exception;
            global::Android.Util.Log.Error("FujifyCrash", $"[Fujify Crash Interceptor]: {ex}");
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
            global::Android.Util.Log.Error("FujifyCrash", $"[Fujify AppDomain Interceptor]: {ex}");
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
                    string fujifyPicturesDir = Path.Combine(picturesDir, "Fujify");
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
                global::Android.Util.Log.Warn("Fujify", $"Error in PlatformFileCreatedCallback: {ex.Message}");
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
                global::Android.Util.Log.Error("Fujify", $"Error opening url {url}: {ex}");
            }
        };

        // Process any incoming share or view intent
        HandleIncomingIntent(Intent);
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
                    global::Android.Util.Log.Warn("Fujify", $"Failed to stage incoming shared URI {uri}: {ex.Message}");
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
                    "com.ipweb.fujify.fileprovider",
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
                        "com.ipweb.fujify.fileprovider",
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
            global::Android.Util.Log.Error("Fujify", $"Error opening files in Lightroom: {ex}");
        }
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
