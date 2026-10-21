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
        public System.Windows.Input.ICommand? RemoveCommand { get; set; }

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
                    OnPropertyChanged(nameof(StatusIcon));
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(IsError));
                    OnPropertyChanged(nameof(IsActive));
                }
            }
        }

        public string StatusIcon => IsCompleted ? "✓" : (IsError ? "✕" : (IsActive ? "⚡" : "⏳"));

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

        private string? _lens;
        public string? Lens
        {
            get => _lens;
            set
            {
                if (_lens != value)
                {
                    _lens = value;
                    OnPropertyChanged(nameof(Lens));
                    OnPropertyChanged(nameof(HasLens));
                }
            }
        }
        public bool HasLens => !string.IsNullOrWhiteSpace(_lens);

        private string? _aperture;
        public string? Aperture
        {
            get => _aperture;
            set
            {
                if (_aperture != value)
                {
                    _aperture = value;
                    OnPropertyChanged(nameof(Aperture));
                    OnPropertyChanged(nameof(FormattedExposure));
                }
            }
        }

        private string? _shutterSpeed;
        public string? ShutterSpeed
        {
            get => _shutterSpeed;
            set
            {
                if (_shutterSpeed != value)
                {
                    _shutterSpeed = value;
                    OnPropertyChanged(nameof(ShutterSpeed));
                    OnPropertyChanged(nameof(FormattedExposure));
                }
            }
        }

        private string? _iso;
        public string? Iso
        {
            get => _iso;
            set
            {
                if (_iso != value)
                {
                    _iso = value;
                    OnPropertyChanged(nameof(Iso));
                    OnPropertyChanged(nameof(FormattedExposure));
                }
            }
        }

        private string? _focalLength;
        public string? FocalLength
        {
            get => _focalLength;
            set
            {
                if (_focalLength != value)
                {
                    _focalLength = value;
                    OnPropertyChanged(nameof(FocalLength));
                    OnPropertyChanged(nameof(FormattedExposure));
                }
            }
        }

        private string? _dateTimeOriginal;
        public string? DateTimeOriginal
        {
            get => _dateTimeOriginal;
            set
            {
                if (_dateTimeOriginal != value)
                {
                    _dateTimeOriginal = value;
                    OnPropertyChanged(nameof(DateTimeOriginal));
                }
            }
        }

        private string? _orientation;
        public string? Orientation
        {
            get => _orientation;
            set
            {
                if (_orientation != value)
                {
                    _orientation = value;
                    OnPropertyChanged(nameof(Orientation));
                    OnPropertyChanged(nameof(HasOrientation));
                }
            }
        }
        public bool HasOrientation => !string.IsNullOrWhiteSpace(_orientation);

        public bool HasExifLoaded { get; set; }

        public string FormattedExposure
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(Aperture)) parts.Add(Aperture.StartsWith("ƒ") || Aperture.StartsWith("f/") ? Aperture : $"ƒ/{Aperture}");
                if (!string.IsNullOrWhiteSpace(ShutterSpeed)) parts.Add(ShutterSpeed.EndsWith("s") ? ShutterSpeed : $"{ShutterSpeed}s");
                if (!string.IsNullOrWhiteSpace(Iso)) parts.Add(Iso.StartsWith("ISO", System.StringComparison.OrdinalIgnoreCase) ? Iso : $"ISO {Iso}");
                if (!string.IsNullOrWhiteSpace(FocalLength)) parts.Add(FocalLength);
                return parts.Count > 0 ? string.Join("  •  ", parts) : "";
            }
        }

        public bool HasExposureInfo => !string.IsNullOrEmpty(FormattedExposure);

        private long _fileSizeBytes = -1;
        public long FileSizeBytes
        {
            get
            {
                if (_fileSizeBytes < 0)
                {
                    try
                    {
                        if (File.Exists(_filePath))
                        {
                            _fileSizeBytes = new FileInfo(_filePath).Length;
                        }
                    }
                    catch { _fileSizeBytes = 0; }
                }
                return _fileSizeBytes;
            }
        }

        public string FormattedFileSize
        {
            get
            {
                long bytes = FileSizeBytes;
                if (bytes > 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
                if (bytes > 1024) return $"{bytes / 1024.0:F1} KB";
                if (bytes > 0) return $"{bytes} B";
                return "";
            }
        }

        public bool IsCompleted => _status.Contains("Done", System.StringComparison.OrdinalIgnoreCase) 
            || _status.Contains("Complete", System.StringComparison.OrdinalIgnoreCase);

        public bool IsError => _status.Contains("Error", System.StringComparison.OrdinalIgnoreCase) 
            || _status.Contains("Fail", System.StringComparison.OrdinalIgnoreCase);

        public bool IsActive => _status.Contains("...", System.StringComparison.OrdinalIgnoreCase) 
            || _status.Contains("Converting", System.StringComparison.OrdinalIgnoreCase);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
