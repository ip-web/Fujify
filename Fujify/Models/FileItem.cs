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

using System.ComponentModel;
using System.IO;
using Avalonia.Media.Imaging;

namespace Fujify.Models
{
    public class FileItem : INotifyPropertyChanged
    {
        private string _filePath = string.Empty;
        private string _status = "Pending";
        private Bitmap? _thumbnail;
        private string _cameraMakeAndModel = string.Empty;

        public bool HasAnimationPlayed { get; set; } = false;

        public required string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                    OnPropertyChanged(nameof(FileName));
                }
            }
        }

        public string FileName => Path.GetFileName(_filePath);

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public Bitmap? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (_thumbnail != value)
                {
                    _thumbnail = value;
                    OnPropertyChanged(nameof(Thumbnail));
                }
            }
        }

        public string CameraMakeAndModel
        {
            get => _cameraMakeAndModel;
            set
            {
                if (_cameraMakeAndModel != value)
                {
                    _cameraMakeAndModel = value;
                    OnPropertyChanged(nameof(CameraMakeAndModel));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
