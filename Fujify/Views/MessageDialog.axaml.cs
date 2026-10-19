using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Fujify.Views
{
    public partial class MessageDialog : Window
    {
        public MessageDialog()
        {
            InitializeComponent();
        }

        public MessageDialog(string title, string message) : this()
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Fujify" : title;
            TitleText.Text = Title;
            MessageContent.Text = message;
        }

        private void Ok_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
