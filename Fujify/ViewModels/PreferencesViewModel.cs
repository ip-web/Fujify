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
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fujify.Services;

namespace Fujify.ViewModels
{
    public class PreferencesViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private bool _isDarkTheme;
        private bool _embedOriginalRaw;
        private bool _skipThumbnailGeneration;
        private bool _losslessCompression = true;
        private int _maxConcurrentConversions;
        private bool _autoOpenInLightroom;
        private bool _appendSuffix;
        private string _suffix = "-fujified";

        public int TotalThreads => Environment.ProcessorCount;
        public int DefaultConcurrency => Math.Max(1, TotalThreads / 2);

        public bool LosslessCompression
        {
            get => _losslessCompression;
            set => SetProperty(ref _losslessCompression, value);
        }

        public bool AppendSuffix
        {
            get => _appendSuffix;
            set => SetProperty(ref _appendSuffix, value);
        }

        public string Suffix
        {
            get => _suffix;
            set => SetProperty(ref _suffix, value);
        }

        private string _themeMode = "Automatic";

        public string ThemeMode
        {
            get => _themeMode;
            set
            {
                if (SetProperty(ref _themeMode, value))
                {
                    _isDarkTheme = value != "Light";
                    OnPropertyChanged(nameof(IsDarkTheme));
                    OnPropertyChanged(nameof(IsAutoThemeSelected));
                    OnPropertyChanged(nameof(IsDarkThemeSelected));
                    OnPropertyChanged(nameof(IsAmoledThemeSelected));
                    OnPropertyChanged(nameof(IsLightThemeSelected));
                    App.ApplyTheme(value);
                }
            }
        }

        public bool IsAutoThemeSelected
        {
            get => _themeMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) || _themeMode.Equals("Auto", StringComparison.OrdinalIgnoreCase);
            set { if (value) ThemeMode = "Automatic"; }
        }

        public bool IsDarkThemeSelected
        {
            get => _themeMode.Equals("Dark", StringComparison.OrdinalIgnoreCase);
            set { if (value) ThemeMode = "Dark"; }
        }

        public bool IsAmoledThemeSelected
        {
            get => _themeMode.Equals("Amoled", StringComparison.OrdinalIgnoreCase);
            set { if (value) ThemeMode = "Amoled"; }
        }

        public bool IsLightThemeSelected
        {
            get => _themeMode.Equals("Light", StringComparison.OrdinalIgnoreCase);
            set { if (value) ThemeMode = "Light"; }
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetProperty(ref _isDarkTheme, value))
                {
                    ThemeMode = value ? (_themeMode == "Amoled" ? "Amoled" : "Dark") : "Light";
                }
            }
        }

        public bool AutoOpenInLightroom
        {
            get => _autoOpenInLightroom;
            set => SetProperty(ref _autoOpenInLightroom, value);
        }

        public bool EmbedOriginalRaw
        {
            get => _embedOriginalRaw;
            set => SetProperty(ref _embedOriginalRaw, value);
        }

        public bool SkipThumbnailGeneration
        {
            get => _skipThumbnailGeneration;
            set => SetProperty(ref _skipThumbnailGeneration, value);
        }

        public int MaxConcurrentConversions
        {
            get => _maxConcurrentConversions;
            set
            {
                if (SetProperty(ref _maxConcurrentConversions, value))
                {
                    OnPropertyChanged(nameof(ConcurrencyHint));
                }
            }
        }

        public string ConcurrencyHint
        {
            get
            {
                if (_maxConcurrentConversions == DefaultConcurrency)
                    return $"Default: {_maxConcurrentConversions} (Recommended for P-core / E-core balance)";
                if (_maxConcurrentConversions == TotalThreads)
                    return $"Maximum: {_maxConcurrentConversions} (Full saturation - may cause disk I/O bottleneck)";
                if (_maxConcurrentConversions <= 8)
                    return $"{_maxConcurrentConversions} threads (Low CPU load, runs primarily on P-cores)";
                return $"{_maxConcurrentConversions} threads (Utilizes P-cores and E-cores)";
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public Action<bool>? CloseAction { get; set; }

        public PreferencesViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            var settings = _settingsService.Settings;

            _themeMode = string.IsNullOrEmpty(settings.ThemeMode)
                ? (settings.IsDarkTheme ? "Dark" : "Light")
                : settings.ThemeMode;
            _isDarkTheme = _themeMode != "Light";
            _embedOriginalRaw = settings.EmbedOriginalRaw;
            _skipThumbnailGeneration = settings.SkipThumbnailGeneration;
            _autoOpenInLightroom = settings.AutoOpenInLightroom;
            _losslessCompression = settings.LosslessCompression;
            _appendSuffix = settings.AppendSuffix;
            _suffix = string.IsNullOrWhiteSpace(settings.Suffix) ? "-fujified" : settings.Suffix;

            int saved = settings.MaxConcurrentConversions;
            if (saved <= 0 || saved > TotalThreads) saved = DefaultConcurrency;
            _maxConcurrentConversions = saved;

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() =>
            {
                App.ApplyTheme(_settingsService.Settings.ThemeMode);
                CloseAction?.Invoke(false);
            });
        }

        private void Save()
        {
            var s = _settingsService.Settings;
            s.ThemeMode = _themeMode;
            s.IsDarkTheme = _themeMode != "Light";
            s.EmbedOriginalRaw = _embedOriginalRaw;
            s.SkipThumbnailGeneration = _skipThumbnailGeneration;
            s.LosslessCompression = _losslessCompression;
            s.MaxConcurrentConversions = _maxConcurrentConversions;
            s.AutoOpenInLightroom = _autoOpenInLightroom;
            s.AppendSuffix = _appendSuffix;
            s.Suffix = string.IsNullOrWhiteSpace(_suffix) ? "-fujified" : _suffix.Trim();
            _settingsService.Save();
            App.ApplyTheme(_themeMode);

            CloseAction?.Invoke(true);
        }
    }
}
