using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Fujify.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
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

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source is Button || (e.Source is Avalonia.Visual visual && visual.FindAncestorOfType<Button>() != null))
            {
                return;
            }

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }
    }
}
