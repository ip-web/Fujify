using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Fujify.ViewModels;
using Fujify.Views;

[assembly: Avalonia.Metadata.XmlnsDefinition("using:Fujify.ViewModels", "Fujify.ViewModels")]
[assembly: Avalonia.Metadata.XmlnsDefinition("using:Fujify.Models", "Fujify.Models")]
[assembly: Avalonia.Metadata.XmlnsDefinition("using:Fujify.Views", "Fujify.Views")]

namespace Fujify;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static readonly Avalonia.Media.Color FallbackAccentGreen = Avalonia.Media.Color.Parse("#01D701");

    public static Avalonia.Media.Color GetEffectiveAccentColor()
    {
        try
        {
            if (Current?.PlatformSettings != null)
            {
                var colorValues = Current.PlatformSettings.GetColorValues();
                if (colorValues.AccentColor1 is { } accent && accent.A > 0)
                {
                    return accent;
                }
            }
        }
        catch { }

        return FallbackAccentGreen;
    }

    public static void UpdateAccentBrush()
    {
        if (Current == null) return;
        var accentColor = GetEffectiveAccentColor();
        SetOrUpdateBrush("AppAccentBrush", accentColor);
    }

    private static void SetOrUpdateBrush(string key, Avalonia.Media.Color color)
    {
        if (Current == null) return;
        if (Current.Resources.TryGetResource(key, null, out var obj) && obj is Avalonia.Media.SolidColorBrush scb)
        {
            scb.Color = color;
        }
        else
        {
            Current.Resources[key] = new Avalonia.Media.SolidColorBrush(color);
        }
    }

    public static bool SupportsMica => System.OperatingSystem.IsWindows() && System.Environment.OSVersion.Version.Build >= 22000;

    public static void ApplyTheme(string themeMode)
    {
        if (Current == null) return;

        bool isAuto = themeMode.Equals("Automatic", System.StringComparison.OrdinalIgnoreCase) ||
                      themeMode.Equals("Auto", System.StringComparison.OrdinalIgnoreCase);

        bool isSystemLight = Current.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Light;
        bool isLight = themeMode.Equals("Light", System.StringComparison.OrdinalIgnoreCase) || (isAuto && isSystemLight);
        bool isAmoled = themeMode.Equals("Amoled", System.StringComparison.OrdinalIgnoreCase);

        Current.RequestedThemeVariant = isLight
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;

        bool useMica = SupportsMica;

        // When Mica / Acrylic is enabled on Windows 11, translucent background allows the desktop backdrop to tint through.
        // On Linux / non-Mica platforms, use 100% fully-opaque solid colors to avoid unwanted desktop window translucency.
        var bgColor = useMica
            ? (isLight
                ? Avalonia.Media.Color.Parse("#B8F2F2F7")
                : (isAmoled ? Avalonia.Media.Color.Parse("#000000") : Avalonia.Media.Color.Parse("#B8202227")))
            : (isLight
                ? Avalonia.Media.Color.Parse("#FFF2F2F7")
                : (isAmoled ? Avalonia.Media.Color.Parse("#FF000000") : Avalonia.Media.Color.Parse("#FF202227")));

        var surfColor = useMica
            ? (isLight
                ? Avalonia.Media.Color.Parse("#FFFFFF")
                : (isAmoled ? Avalonia.Media.Color.Parse("#101014") : Avalonia.Media.Color.Parse("#D92B2D35")))
            : (isLight
                ? Avalonia.Media.Color.Parse("#FFFFFFFF")
                : (isAmoled ? Avalonia.Media.Color.Parse("#FF101014") : Avalonia.Media.Color.Parse("#FF2B2D35")));

        var surf2Color = isLight
            ? Avalonia.Media.Color.Parse("#E5E5EA")
            : (isAmoled ? Avalonia.Media.Color.Parse("#1A1A20") : Avalonia.Media.Color.Parse("#363842"));

        var headerColor = useMica
            ? (isLight
                ? Avalonia.Media.Color.Parse("#A0F2F2F7")
                : (isAmoled ? Avalonia.Media.Color.Parse("#EE000000") : Avalonia.Media.Color.Parse("#90202227")))
            : (isLight
                ? Avalonia.Media.Color.Parse("#FFF2F2F7")
                : (isAmoled ? Avalonia.Media.Color.Parse("#FF000000") : Avalonia.Media.Color.Parse("#FF202227")));

        var footerColor = useMica
            ? (isLight
                ? Avalonia.Media.Color.Parse("#A0F2F2F7")
                : (isAmoled ? Avalonia.Media.Color.Parse("#EE000000") : Avalonia.Media.Color.Parse("#90202227")))
            : (isLight
                ? Avalonia.Media.Color.Parse("#FFF2F2F7")
                : (isAmoled ? Avalonia.Media.Color.Parse("#FF000000") : Avalonia.Media.Color.Parse("#FF202227")));

        var text1Color = isLight ? Avalonia.Media.Color.Parse("#000000") : Avalonia.Media.Color.Parse("#FFFFFF");
        var text2Color = isLight ? Avalonia.Media.Color.Parse("#6E6E78") : Avalonia.Media.Color.Parse("#A0A0AC");
        var glassPill = isLight ? Avalonia.Media.Color.Parse("#12000000") : Avalonia.Media.Color.Parse("#1AFFFFFF");

        // Solid fully-opaque backgrounds for panels and modals
        var solidBgColor = isLight
            ? Avalonia.Media.Color.Parse("#FFF2F2F7")
            : (isAmoled ? Avalonia.Media.Color.Parse("#FF000000") : Avalonia.Media.Color.Parse("#FF202227"));

        var solidHeaderColor = isLight
            ? Avalonia.Media.Color.Parse("#FFF2F2F7")
            : (isAmoled ? Avalonia.Media.Color.Parse("#FF000000") : Avalonia.Media.Color.Parse("#FF202227"));

        SetOrUpdateBrush("AppBackgroundBrush", bgColor);
        SetOrUpdateBrush("AppSolidBackgroundBrush", solidBgColor);
        SetOrUpdateBrush("AppSurfaceBrush", surfColor);
        SetOrUpdateBrush("AppSurfaceSecondaryBrush", surf2Color);
        SetOrUpdateBrush("AppHeaderBrush", headerColor);
        SetOrUpdateBrush("AppSolidHeaderBrush", solidHeaderColor);
        SetOrUpdateBrush("AppFooterBrush", footerColor);
        SetOrUpdateBrush("AppTextPrimaryBrush", text1Color);
        SetOrUpdateBrush("AppTextSecondaryBrush", text2Color);
        SetOrUpdateBrush("AppGlassPillBrush", glassPill);
        UpdateAccentBrush();
    }

    public static void ApplyTheme(bool isDark) => ApplyTheme(isDark ? "Dark" : "Light");

    public static Common.CommandLineArgs? InitialCommandLineArgs { get; set; }
    private static MainViewModel? _activeViewModel;
    private static MainWindow? _activeMainWindow;
    private static QuickProcessViewModel? _activeQuickViewModel;
    private static QuickProcessWindow? _activeQuickWindow;
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _pendingStartupFiles = new();

    public static void HandleIncomingPayload(Services.IpcPayload payload)
    {
        if (payload == null || payload.Files == null || payload.Files.Count == 0) return;

        if (_activeQuickViewModel != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_activeQuickWindow != null)
                {
                    _activeQuickWindow.Show();
                    if (_activeQuickWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                    {
                        _activeQuickWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                    }
                    _activeQuickWindow.Activate();
                }

                _activeQuickViewModel.AddFiles(payload.Files);
            });
            return;
        }

        HandleIncomingFiles(payload.Files);
    }

    public static void HandleIncomingFiles(System.Collections.Generic.IReadOnlyList<string> files)
    {
        if (files == null || files.Count == 0) return;

        var existingFiles = files.Where(System.IO.File.Exists).ToList();
        if (existingFiles.Count == 0) return;

        if (_activeViewModel != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (_activeMainWindow != null)
                {
                    _activeMainWindow.Show();
                    if (_activeMainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                    {
                        _activeMainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                    }
                    _activeMainWindow.Activate();
                }

                await _activeViewModel.AddFilesAsync(existingFiles);
            });
        }
        else
        {
            foreach (var f in existingFiles)
            {
                _pendingStartupFiles.Enqueue(f);
            }
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var settingsService = new Services.SettingsService();

        if (PlatformSettings != null)
        {
            PlatformSettings.ColorValuesChanged += (s, e) =>
            {
                UpdateAccentBrush();
                var currentTheme = settingsService.Settings.ThemeMode;
                if (currentTheme.Equals("Automatic", System.StringComparison.OrdinalIgnoreCase) ||
                    currentTheme.Equals("Auto", System.StringComparison.OrdinalIgnoreCase))
                {
                    ApplyTheme(currentTheme);
                }
            };
        }

        ApplyTheme(settingsService.Settings.ThemeMode);
        UpdateAccentBrush();

        var licenseService = new Services.LicenseService(settingsService);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Direct Quick Process Mode: Do not boot up full UI
            if (InitialCommandLineArgs?.IsQuickProcess == true)
            {
                var quickVm = new QuickProcessViewModel(
                    InitialCommandLineArgs.Files,
                    isProcessTo: InitialCommandLineArgs.Mode == Common.LaunchMode.ProcessTo,
                    settingsService: settingsService,
                    licenseService: licenseService);

                var quickWindow = new QuickProcessWindow
                {
                    DataContext = quickVm
                };
                quickVm.OwnerWindow = quickWindow;
                desktop.MainWindow = quickWindow;
                _activeQuickViewModel = quickVm;
                _activeQuickWindow = quickWindow;

                if (System.OperatingSystem.IsWindows())
                {
                    if (settingsService.Settings.EnableWindowsContextMenu && licenseService.IsPro)
                    {
                        Services.WindowsContextMenuService.RegisterContextMenu();
                    }
                    else if (!licenseService.IsPro && Services.WindowsContextMenuService.IsContextMenuRegistered())
                    {
                        Services.WindowsContextMenuService.UnregisterContextMenu();
                    }
                }

                quickWindow.Show();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var dialogService = new Services.DialogService(settingsService);
            var vm = new MainViewModel(dialogService: dialogService, settingsService: settingsService, licenseService: licenseService);

            var mainWindow = new MainWindow
            {
                DataContext = vm,
            };
            vm.OwnerWindow = mainWindow;
            desktop.MainWindow = mainWindow;
            _activeMainWindow = mainWindow;
            _activeViewModel = vm;

            // Ensure Windows Explorer context menu is kept up-to-date only if Pro
            if (System.OperatingSystem.IsWindows())
            {
                if (settingsService.Settings.EnableWindowsContextMenu && licenseService.IsPro)
                {
                    Services.WindowsContextMenuService.RegisterContextMenu();
                }
                else if (!licenseService.IsPro && Services.WindowsContextMenuService.IsContextMenuRegistered())
                {
                    Services.WindowsContextMenuService.UnregisterContextMenu();
                }
            }

            var startupFiles = new System.Collections.Generic.List<string>();
            if (InitialCommandLineArgs != null && InitialCommandLineArgs.Files.Count > 0)
            {
                startupFiles.AddRange(InitialCommandLineArgs.Files.Where(System.IO.File.Exists));
            }
            else if (desktop.Args != null && desktop.Args.Length > 0)
            {
                startupFiles.AddRange(desktop.Args.Where(System.IO.File.Exists));
            }

            while (_pendingStartupFiles.TryDequeue(out var pending))
            {
                if (System.IO.File.Exists(pending) && !startupFiles.Contains(pending, System.StringComparer.OrdinalIgnoreCase))
                {
                    startupFiles.Add(pending);
                }
            }

            if (startupFiles.Count > 0)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () => await vm.AddFilesAsync(startupFiles));
            }

            // Free Tier Launch Delay Modal
            if (!licenseService.IsPro)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    await Views.FreeTierDelayDialog.ShowDelayDialogAsync(mainWindow, licenseService, isPostConversion: false);
                });
            }

            // Automatic update check on startup (if enabled)
            if (settingsService.Settings.CheckForUpdatesOnStartup)
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(2500);
                        var updateService = new Services.UpdateService(settingsService);
                        var result = await updateService.CheckForUpdateAsync(force: false);
                        if (result.IsUpdateAvailable && result.UpdateInfo != null)
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                            {
                                await Views.UpdateDialog.ShowUpdateDialogAsync(mainWindow, result.UpdateInfo, updateService);
                            });
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[App] Background update check error: {ex.Message}");
                    }
                });
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var dialogService = new Services.DialogService(settingsService);
            var vm = new MainViewModel(dialogService: dialogService, settingsService: settingsService, licenseService: licenseService);
            var mainView = new MainView
            {
                DataContext = vm,
            };
            singleView.MainView = mainView;

            // Free Tier Launch Delay Modal for mobile
            if (!licenseService.IsPro)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    await Views.FreeTierDelayDialog.ShowDelayDialogAsync(null, licenseService, isPostConversion: false);
                });
            }

            // Automatic update check on startup (if enabled)
            if (settingsService.Settings.CheckForUpdatesOnStartup)
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(3000);
                        var updateService = new Services.UpdateService(settingsService);
                        var result = await updateService.CheckForUpdateAsync(force: false);
                        if (result.IsUpdateAvailable && result.UpdateInfo != null)
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                            {
                                await Views.UpdateDialog.ShowUpdateDialogAsync(null, result.UpdateInfo, updateService);
                            });
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[App] Background update check error: {ex.Message}");
                    }
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}