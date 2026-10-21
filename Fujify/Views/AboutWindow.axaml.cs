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
            Common.CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujify");
        }

        private void Releases_Click(object? sender, RoutedEventArgs e)
        {
            Common.CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujify/releases");
        }
    }
}
