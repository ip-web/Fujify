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

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsService = new Services.SettingsService();
            var dialogService = new Services.DialogService(settingsService);
            var vm = new MainViewModel(dialogService: dialogService, settingsService: settingsService);

            var mainWindow = new MainWindow
            {
                DataContext = vm,
            };
            vm.OwnerWindow = mainWindow;
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}