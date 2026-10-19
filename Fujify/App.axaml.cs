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

    public static void ApplyTheme(bool isDark)
    {
        if (Current != null)
        {
            Current.RequestedThemeVariant = isDark
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var settingsService = new Services.SettingsService();
        ApplyTheme(settingsService.Settings.IsDarkTheme);

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