using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Fujify.Models;
using Fujify.ViewModels;

namespace Fujify.Views
{
    public partial class FileConflictDialog : Window
    {
        public FileConflictResolution Result { get; private set; } = new();

        public FileConflictDialog()
        {
            InitializeComponent();
        }

        public FileConflictDialog(FileConflictDialogViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.CloseAction = res =>
            {
                Result = res;
                Close(res);
            };
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source is Button ||
                (e.Source is Avalonia.Visual visual && visual.FindAncestorOfType<Button>() != null) ||
                (e.Source is CheckBox) ||
                (e.Source is Avalonia.Visual v2 && v2.FindAncestorOfType<CheckBox>() != null))
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
