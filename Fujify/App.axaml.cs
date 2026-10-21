using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Fujify.ViewModels;
using Fujify.Views;

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

        var dialogService = new Services.DialogService(settingsService);
        var vm = new MainViewModel(dialogService: dialogService, settingsService: settingsService);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = vm,
            };
            vm.OwnerWindow = mainWindow;
            desktop.MainWindow = mainWindow;

            if (desktop.Args != null && desktop.Args.Length > 0)
            {
                var files = desktop.Args.Where(System.IO.File.Exists).ToList();
                if (files.Count > 0)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(async () => await vm.AddFilesAsync(files));
                }
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var mainView = new MainView
            {
                DataContext = vm,
            };
            singleView.MainView = mainView;
        }

        base.OnFrameworkInitializationCompleted();
    }
}