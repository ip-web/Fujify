using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Fujify.Localization;

namespace Fujify.Views
{
    public partial class ConfirmationDialog : Window
    {
        public bool Result { get; private set; }

        public ConfirmationDialog()
        {
            InitializeComponent();
        }

        public ConfirmationDialog(string title, string message, string? confirmText = null, string? cancelText = null) : this()
        {
            Title = string.IsNullOrWhiteSpace(title) ? LocalizationManager.Tr("Warning") : title;
            TitleText.Text = Title;
            MessageContent.Text = message;
            if (!string.IsNullOrWhiteSpace(confirmText))
            {
                ConfirmButton.Content = confirmText;
            }
            if (!string.IsNullOrWhiteSpace(cancelText))
            {
                CancelButton.Content = cancelText;
            }
        }

        private void Confirm_Click(object? sender, RoutedEventArgs e)
        {
            Result = true;
            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Result = false;
            Close(false);
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

