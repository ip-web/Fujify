using Avalonia.Controls;
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
            if (!App.SupportsMica)
            {
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                Background = null;
            }
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
    }
}
