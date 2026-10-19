using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Fujify.ViewModels;

namespace Fujify.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            AddHandler(DragDrop.DragOverEvent, Window_DragOver);
            AddHandler(DragDrop.DropEvent, Window_Drop);
        }

        private void Window_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private async void Window_Drop(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles()?.Select(f => f.Path.LocalPath).ToList();
                if (files != null && files.Count > 0 && DataContext is MainViewModel vm)
                {
                    await vm.AddFilesAsync(files);
                }
            }
        }
    }
}