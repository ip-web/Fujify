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

using System;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fujify.Models;

namespace Fujify.ViewModels
{
    public class FileConflictDialogViewModel : ObservableObject
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

        public Action<FileConflictResolution>? CloseAction { get; set; }

        public FileConflictDialogViewModel(string filePath)
        {
            FullFilePath = filePath;
            FileName = Path.GetFileName(filePath);
            DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;

            OverwriteCommand = new RelayCommand(() => CloseWith(ConflictChoice.Overwrite));
            UseUniqueNameCommand = new RelayCommand(() => CloseWith(ConflictChoice.UseUniqueName));
            SkipCommand = new RelayCommand(() => CloseWith(ConflictChoice.Skip));
        }

        private void CloseWith(ConflictChoice choice)
        {
            CloseAction?.Invoke(new FileConflictResolution { Choice = choice, ApplyToAll = ApplyToAll });
        }
    }
}
