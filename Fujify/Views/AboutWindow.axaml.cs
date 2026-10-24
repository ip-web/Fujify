using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Fujify.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            if (!App.SupportsMica)
            {
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                Background = null;
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Website_Click(object? sender, RoutedEventArgs e)
        {
            Common.CrossPlatformLauncher.OpenUrl("https://ipweb.dev");
        }

        private void GitHub_Click(object? sender, RoutedEventArgs e)
        {
            Common.CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujifier");
        }

        private async void Releases_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var updateService = new Services.UpdateService();
                var result = await updateService.CheckForUpdateAsync(force: true);
                if (result.IsUpdateAvailable && result.UpdateInfo != null)
                {
                    await UpdateDialog.ShowUpdateDialogAsync(this, result.UpdateInfo, updateService);
                }
                else
                {
                    Common.CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujifier/releases");
                }
            }
            catch
            {
                Common.CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujifier/releases");
            }
        }
    }
}
