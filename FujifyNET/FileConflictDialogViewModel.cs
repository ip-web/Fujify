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

using System.IO;
using System.Windows.Input;
using FujifyNET.Common;
using FujifyNET.Models;
using MaterialDesignThemes.Wpf;

namespace FujifyNET
{
    public class FileConflictDialogViewModel : ViewModelBase
    {
        private bool _applyToAll;

        public string FullFilePath { get; }
        public string FileName { get; }
        public string DirectoryPath { get; }

        public bool ApplyToAll
        {
            get => _applyToAll;
            set => SetProperty(ref _applyToAll, value);
        }

        public ICommand OverwriteCommand { get; }
        public ICommand UseUniqueNameCommand { get; }
        public ICommand SkipCommand { get; }

        public FileConflictDialogViewModel(string filePath)
        {
            FullFilePath = filePath;
            FileName = Path.GetFileName(filePath);
            DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;

            OverwriteCommand = new RelayCommand(() => CloseWith(FileConflictAction.Overwrite));
            UseUniqueNameCommand = new RelayCommand(() => CloseWith(FileConflictAction.UseUniqueName));
            SkipCommand = new RelayCommand(() => CloseWith(FileConflictAction.Skip));
        }

        private void CloseWith(FileConflictAction action)
        {
            DialogHost.CloseDialogCommand.Execute(new FileConflictResult(action, ApplyToAll), null);
        }
    }
}
