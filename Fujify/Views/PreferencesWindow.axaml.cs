using Avalonia.Controls;
using Fujify.ViewModels;

namespace Fujify.Views
{
    public partial class PreferencesWindow : Window
    {
        public PreferencesWindow()
        {
            InitializeComponent();
            if (!App.SupportsMica)
            {
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                Background = null;
            }
        }

        public PreferencesWindow(PreferencesViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.OwnerWindow = this;
            viewModel.CloseAction = result => Close(result);
        }
    }
}
