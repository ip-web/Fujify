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
        private int _maxConcurrentConversions;
        private bool _autoOpenInLightroom;

        public int TotalThreads => Environment.ProcessorCount;
        public int DefaultConcurrency => Math.Max(1, TotalThreads / 2);

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetProperty(ref _isDarkTheme, value))
                {
                    App.ApplyTheme(value);
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

            _isDarkTheme = settings.IsDarkTheme;
            _embedOriginalRaw = settings.EmbedOriginalRaw;
            _skipThumbnailGeneration = settings.SkipThumbnailGeneration;
            _autoOpenInLightroom = settings.AutoOpenInLightroom;

            int saved = settings.MaxConcurrentConversions;
            if (saved <= 0 || saved > TotalThreads) saved = DefaultConcurrency;
            _maxConcurrentConversions = saved;

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() =>
            {
                App.ApplyTheme(_settingsService.Settings.IsDarkTheme);
                CloseAction?.Invoke(false);
            });
        }

        private void Save()
        {
            var s = _settingsService.Settings;
            s.IsDarkTheme = _isDarkTheme;
            s.EmbedOriginalRaw = _embedOriginalRaw;
            s.SkipThumbnailGeneration = _skipThumbnailGeneration;
            s.MaxConcurrentConversions = _maxConcurrentConversions;
            s.AutoOpenInLightroom = _autoOpenInLightroom;
            _settingsService.Save();
            App.ApplyTheme(_isDarkTheme);

            CloseAction?.Invoke(true);
        }
    }
}
