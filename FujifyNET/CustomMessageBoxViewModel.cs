/*
 * This file is part of Fujify.
 *
 * Copyright (C) 2024 Isidore Paulin contact@ipweb.dev
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using MaterialDesignThemes.Wpf;
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
