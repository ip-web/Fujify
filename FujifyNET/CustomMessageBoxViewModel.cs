using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FujifyNET
{
    public class CustomMessageBoxViewModel
    {
        // Properties for title and message
        public string Title { get; set; }
        public string Message { get; set; }

        // Commands for OK and Cancel buttons
        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        // Constructor to initialize the ViewModel with title and message
        public CustomMessageBoxViewModel(string title, string message)
        {
            Title = title;
            Message = message;
            OkCommand = new RelayCommand(OnOk);
            CancelCommand = new RelayCommand(OnCancel);
        }

        // Method to handle OK button click
        private void OnOk()
        {
            DialogHost.CloseDialogCommand.Execute(true, null);
        }

        // Method to handle Cancel button click
        private void OnCancel()
        {
            DialogHost.CloseDialogCommand.Execute(false, null);
        }
    }
}
