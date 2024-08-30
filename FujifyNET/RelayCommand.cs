using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FujifyNET
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute; // Nullable

        public RelayCommand(Action execute, Func<bool>? canExecute = null) // Nullable
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // Determines whether the command can execute
        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        // Executes the command
        public void Execute(object? parameter) => _execute();

        // Event to handle changes in the ability of the command to execute
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}