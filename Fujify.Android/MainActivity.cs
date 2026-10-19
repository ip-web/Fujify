using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Fujify.Services;
using Fujify.ViewModels;
using System;
using System.IO;

namespace Fujify.Android;

[Activity(
    Label = "Fujify",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
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
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
