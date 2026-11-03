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
    WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateHidden,
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
public class MainActivity : AvaloniaMainActivity
{
    public static MainActivity? Instance { get; private set; }
    private const int StoragePermissionRequestCode = 2001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Instance = this;
        AdManager.Instance.PreInitialize(this);
        base.OnCreate(savedInstanceState);
        Window?.SetSoftInputMode(SoftInput.AdjustResize | SoftInput.StateHidden);
        EnableHighRefreshRate();
        RequestRuntimeStoragePermissions();

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            try
            {
                OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, new BackInvokedCallback(HandleBackNavigation));
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("Fujifier", $"Could not register OnBackInvokedCallback: {ex.Message}");
            }
        }

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

        // Wire folder resolver to translate Android SAF tree URIs into real POSIX storage paths
        DialogService.PlatformFolderResolver = (uri) =>
        {
            try
            {
                if (uri == null) return null;
                var androidUri = global::Android.Net.Uri.Parse(uri.ToString());
                if (androidUri != null)
                {
                    try
                    {
                        if (androidUri.Scheme == "content")
                        {
                            ApplicationContext?.ContentResolver?.TakePersistableUriPermission(
                                androidUri,
                                ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
                        }
                    }
                    catch { }

                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat &&
                        global::Android.Provider.DocumentsContract.IsTreeUri(androidUri))
                    {
                        string? docId = global::Android.Provider.DocumentsContract.GetTreeDocumentId(androidUri);
                        if (!string.IsNullOrEmpty(docId))
                        {
                            return DialogService.ResolveAndroidStoragePathString(docId);
                        }
                    }
                }
                return DialogService.ResolveAndroidStoragePathString(uri.ToString());
            }
            catch
            {
                return null;
            }
        };

        // Wire default public Pictures directory for Android
        DialogService.PlatformDefaultOutputDirectoryResolver = () =>
        {
            try
            {
                var pictures = global::Android.OS.Environment.GetExternalStoragePublicDirectory(
                    global::Android.OS.Environment.DirectoryPictures);
                if (pictures != null)
                {
                    return Path.Combine(pictures.AbsolutePath, "Fujifier");
                }
            }
            catch { }
            return "/storage/emulated/0/Pictures/Fujifier";
        };

        // Wire permission prompt hook
        DialogService.PlatformEnsurePermissionsCallback = () =>
        {
            RequestRuntimeStoragePermissions();
        };

        // Wire media scanner and photo album export for newly converted DNG files
        MainViewModel.PlatformFileCreatedCallback = (filePath) =>
        {
            SaveConvertedPhotoToGallery(this, filePath);
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
        EnableHighRefreshRate();
        AdManager.Instance.Resume();
    }

    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        EnableHighRefreshRate();
    }

    protected override void OnPause()
    {
        base.OnPause();
        AdManager.Instance.Pause();
    }

    protected override void OnDestroy()
    {
        if (Instance == this) Instance = null;
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
            global::Android.Net.Uri? streamUri = null;
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
#pragma warning disable CA1416
                streamUri = intent.GetParcelableExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(global::Android.Net.Uri))) as global::Android.Net.Uri;
#pragma warning restore CA1416
            }
            else
            {
#pragma warning disable CA1422
                streamUri = intent.GetParcelableExtra(Intent.ExtraStream) as global::Android.Net.Uri;
#pragma warning restore CA1422
            }

            if (streamUri != null)
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
            System.Collections.IList? list = null;
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
#pragma warning disable CA1416
                list = intent.GetParcelableArrayListExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(global::Android.Net.Uri)));
#pragma warning restore CA1416
            }
            else
            {
#pragma warning disable CA1422
                list = intent.GetParcelableArrayListExtra(Intent.ExtraStream);
#pragma warning restore CA1422
            }

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
            if (layoutParams == null) return;

            Display? display = null;
#pragma warning disable CA1416
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                display = Display;
            }
#pragma warning restore CA1416
            else
            {
#pragma warning disable CS0618
                display = WindowManager?.DefaultDisplay;
#pragma warning restore CS0618
            }

            if (display != null)
            {
                var currentMode = display.GetMode();
                int currentWidth = currentMode?.PhysicalWidth ?? 0;
                int currentHeight = currentMode?.PhysicalHeight ?? 0;

                var supportedModes = display.GetSupportedModes();
                int matchingTargetModeId = 0;
                float maxRefreshRate = 0f;

                if (supportedModes != null && supportedModes.Length > 0)
                {
                    foreach (var mode in supportedModes)
                    {
                        if (mode.RefreshRate > maxRefreshRate)
                        {
                            maxRefreshRate = mode.RefreshRate;
                        }

                        // On foldable dual-screen devices (e.g. Galaxy Z Fold), ONLY match modes
                        // with the physical resolution of the currently active display (folded vs unfolded)
                        // to avoid SurfaceFlinger mode rejection falling back to 30Hz!
                        if (currentWidth > 0 && currentHeight > 0 &&
                            mode.PhysicalWidth == currentWidth && mode.PhysicalHeight == currentHeight)
                        {
                            if (mode.RefreshRate >= maxRefreshRate)
                            {
                                matchingTargetModeId = mode.ModeId;
                            }
                        }
                    }
                }

                if (maxRefreshRate < 60f)
                {
                    maxRefreshRate = 120f;
                }

                // PreferredRefreshRate directly requests 120Hz from the display hardware on the current resolution
                layoutParams.PreferredRefreshRate = maxRefreshRate;

                // PreferredDisplayModeId only if resolution strictly matches current screen
                if (matchingTargetModeId != 0)
                {
                    layoutParams.PreferredDisplayModeId = matchingTargetModeId;
                }
                else
                {
                    layoutParams.PreferredDisplayModeId = 0;
                }

                Window.Attributes = layoutParams;
                global::Android.Util.Log.Info("Fujifier", $"EnableHighRefreshRate: PreferredRefreshRate={maxRefreshRate}Hz, ModeId={matchingTargetModeId}");
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("Fujifier", $"EnableHighRefreshRate warning: {ex.Message}");
        }
    }

    public override void OnBackPressed()
    {
        HandleBackNavigation();
    }

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (keyCode == Keycode.Back && e?.Action == KeyEventActions.Down)
        {
            HandleBackNavigation();
            return true;
        }
        return base.OnKeyDown(keyCode, e);
    }

    private void HandleBackNavigation()
    {
        var vm = MainViewModel.Instance;
        if (vm == null)
        {
            FinishAffinity();
            return;
        }

        // 0. If multi-selection mode is active, exit multi-selection mode without closing the app!
        if (vm.IsMultiSelectMode)
        {
            vm.ExitMultiSelectMode();
            return;
        }

        // 1. If Preferences modal (Settings) or About modal is open, close it without closing the app!
        if (vm.IsPreferencesModal || vm.IsAboutModal)
        {
            vm.CloseModal();
            return;
        }

        // 2. If a confirmation modal is already open (e.g. exit dialog itself or another confirmation), dismiss it
        if (vm.IsConfirmationModal)
        {
            vm.ResolveConfirmation(false);
            return;
        }

        // 3. If any other modal is open (Message, CameraPicker, Conflict, FreeTierDelay, BatchSummary), close it
        if (vm.IsModalOpen)
        {
            vm.CloseModal();
            return;
        }

        // 4. If mobile sidebar / options drawer is open, close it
        if (vm.IsMobileSidebarOpen)
        {
            vm.IsMobileSidebarOpen = false;
            return;
        }

        // 5. If mobile add menu (FAB) is open, close it
        if (vm.IsMobileAddMenuOpen)
        {
            vm.IsMobileAddMenuOpen = false;
            return;
        }

        // 6. On the main view, trigger a confirmation modal asking the user to confirm exiting the app
        PromptExitConfirmation();
    }

    private void PromptExitConfirmation()
    {
        var vm = MainViewModel.Instance;
        if (vm == null)
        {
            FinishAffinity();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            bool confirmExit = await vm.ShowConfirmationModalAsync(
                Fujify.Localization.LocalizationManager.Tr("ExitAppTitle"),
                Fujify.Localization.LocalizationManager.Tr("ExitAppMessage"),
                confirmText: Fujify.Localization.LocalizationManager.Tr("ExitAppConfirm"),
                cancelText: Fujify.Localization.LocalizationManager.Tr("Cancel"));

            if (confirmExit)
            {
                FinishAffinity();
            }
        });
    }

    public void RequestRuntimeStoragePermissions()
    {
        // No runtime storage permissions needed; Scoped Storage and SAF (ActionOpenDocument / ActionOpenDocumentTree)
        // grant transient read URI permissions without broad media library access.
    }

    public void PromptAllFilesAccessPermission()
    {
        RequestRuntimeStoragePermissions();
    }

    public static void SaveConvertedPhotoToGallery(Context context, string filePath)
    {
        try
        {
            string fileName = Path.GetFileName(filePath);
            var vm = MainViewModel.Instance;

            // Determine if a subfolder is requested:
            // Check if the staged filePath is located inside a subfolder under staging dir
            string subfolder = string.Empty;
            string? parentDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                string dirName = Path.GetFileName(parentDir);
                if (!string.IsNullOrEmpty(dirName) &&
                    !dirName.Equals("Fujifier_Staging", StringComparison.OrdinalIgnoreCase) &&
                    !dirName.Equals("cache", StringComparison.OrdinalIgnoreCase))
                {
                    subfolder = dirName;
                }
            }

            if (string.IsNullOrEmpty(subfolder) && vm != null && vm.CreateSubfolder)
            {
                string preview = vm.SubfolderPreview;
                if (!string.IsNullOrWhiteSpace(preview))
                {
                    subfolder = preview.Trim();
                }
            }

            bool mediaStoreSaved = false;

            // 1. On Android 10+ (API >= 29), insert into MediaStore under Pictures/Fujifier (or Pictures/Fujifier/<subfolder>)
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                mediaStoreSaved = SaveToMediaStore(context, filePath, fileName, subfolder);
            }

            // 2. Direct POSIX copy fallback / sync to /storage/emulated/0/Pictures/Fujifier/<subfolder>
            string picturesDir = global::Android.OS.Environment.GetExternalStoragePublicDirectory(
                global::Android.OS.Environment.DirectoryPictures)?.AbsolutePath ?? "";

            string targetPosixPath = string.Empty;
            if (!string.IsNullOrEmpty(picturesDir))
            {
                try
                {
                    string targetFolder = !string.IsNullOrEmpty(subfolder)
                        ? Path.Combine(picturesDir, "Fujifier", subfolder)
                        : Path.Combine(picturesDir, "Fujifier");
                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                    }
                    targetPosixPath = Path.Combine(targetFolder, fileName);
                    if (!string.Equals(filePath, targetPosixPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(filePath, targetPosixPath, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Warn("Fujifier", $"Direct POSIX copy to Pictures/Fujifier not permitted: {ex.Message}");
                }
            }

            // 3. Scan with MediaScannerConnection so device gallery, file managers, and Lightroom detect it
            string scanFolder = !string.IsNullOrEmpty(subfolder)
                ? Path.Combine(picturesDir, "Fujifier", subfolder)
                : Path.Combine(picturesDir, "Fujifier");
            string scanPath = !string.IsNullOrEmpty(targetPosixPath) && File.Exists(targetPosixPath)
                ? targetPosixPath
                : Path.Combine(scanFolder, fileName);

            global::Android.Media.MediaScannerConnection.ScanFile(
                context,
                new[] { scanPath, filePath },
                new[] { "image/x-adobe-dng", "image/dng" },
                null);

            global::Android.Util.Log.Info("Fujifier", $"SaveConvertedPhotoToGallery completed: fileName={fileName}, subfolder={subfolder}, mediaStoreSaved={mediaStoreSaved}, posixExists={File.Exists(targetPosixPath)}");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("Fujifier", $"Error in SaveConvertedPhotoToGallery: {ex}");
        }
    }

    private static bool SaveToMediaStore(Context context, string sourceFilePath, string fileName, string subfolder = "")
    {
        try
        {
            var resolver = context.ContentResolver;
            if (resolver == null) return false;

            var values = new ContentValues();
            values.Put(global::Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
            values.Put(global::Android.Provider.MediaStore.IMediaColumns.MimeType, "image/x-adobe-dng");
            values.Put(global::Android.Provider.MediaStore.IMediaColumns.DateAdded, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            values.Put(global::Android.Provider.MediaStore.IMediaColumns.DateModified, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                string relPath = !string.IsNullOrWhiteSpace(subfolder)
                    ? $"Pictures/Fujifier/{subfolder}"
                    : "Pictures/Fujifier";
                values.Put(global::Android.Provider.MediaStore.IMediaColumns.RelativePath, relPath);
                values.Put(global::Android.Provider.MediaStore.IMediaColumns.IsPending, 1);
            }

            var externalUri = global::Android.Provider.MediaStore.Images.Media.ExternalContentUri;
            if (externalUri == null)
            {
                global::Android.Util.Log.Error("Fujifier", "MediaStore.Images.Media.ExternalContentUri is null");
                return false;
            }

            var uri = resolver.Insert(externalUri, values);
            if (uri == null)
            {
                global::Android.Util.Log.Error("Fujifier", $"ContentResolver.Insert returned null for {fileName}");
                return false;
            }

            using (var inStream = File.OpenRead(sourceFilePath))
            using (var outStream = resolver.OpenOutputStream(uri))
            {
                if (outStream == null)
                {
                    global::Android.Util.Log.Error("Fujifier", $"OpenOutputStream returned null for {uri}");
                    return false;
                }
                inStream.CopyTo(outStream);
                outStream.Flush();
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                values.Clear();
                values.Put(global::Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
                resolver.Update(uri, values, null, null);
            }

            global::Android.Util.Log.Info("Fujifier", $"SaveToMediaStore successfully saved {fileName} to URI: {uri}");
            return true;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("Fujifier", $"SaveToMediaStore failed for {fileName}: {ex}");
            return false;
        }
    }

    private class BackInvokedCallback : Java.Lang.Object, global::Android.Window.IOnBackInvokedCallback
    {
        private readonly Action _action;
        public BackInvokedCallback(Action action) => _action = action;
        public void OnBackInvoked() => _action();
    }
}
