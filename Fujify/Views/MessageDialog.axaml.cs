using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Fujify.Views
{
    public partial class MessageDialog : Window
    {
        public MessageDialog()
        {
            InitializeComponent();
            if (!App.SupportsMica)
            {
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                Background = null;
            }
        }

        public MessageDialog(string title, string message, bool isSuccess = true) : this()
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Fujifier" : title;
            TitleText.Text = Title;
            MessageContent.Text = message;

            if (!isSuccess)
            {
                IconText.Text = "✕";
                IconText.Foreground = new SolidColorBrush(Color.Parse("#E74C3C"));
                IconBorder.Background = new SolidColorBrush(Color.FromArgb(40, 231, 76, 60));
            }
            else
            {
                IconText.Text = "✔";
                IconText.Foreground = new SolidColorBrush(Color.Parse("#00C781"));
                IconBorder.Background = new SolidColorBrush(Color.FromArgb(40, 0, 199, 129));
            }
        }

        private void Ok_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
