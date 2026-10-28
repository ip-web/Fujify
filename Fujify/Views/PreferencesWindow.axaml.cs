using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Fujify.ViewModels;

namespace Fujify.Views
{
    public partial class PreferencesWindow : Window
    {
        public PreferencesWindow()
        {
            InitializeComponent();
        }

        public PreferencesWindow(PreferencesViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.OwnerWindow = this;
            viewModel.CloseAction = result => Close(result);
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source is Button ||
                (e.Source is Avalonia.Visual visual && visual.FindAncestorOfType<Button>() != null) ||
                (e.Source is Avalonia.Visual v2 && v2.FindAncestorOfType<TextBox>() != null) ||
                (e.Source is Avalonia.Visual v3 && v3.FindAncestorOfType<Slider>() != null) ||
                (e.Source is Avalonia.Visual v4 && v4.FindAncestorOfType<RadioButton>() != null) ||
                (e.Source is Avalonia.Visual v5 && v5.FindAncestorOfType<ToggleSwitch>() != null) ||
                (e.Source is Avalonia.Visual v6 && v6.FindAncestorOfType<ComboBox>() != null))
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
