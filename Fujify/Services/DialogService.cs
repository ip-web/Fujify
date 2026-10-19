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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Fujify.Models;
using Fujify.ViewModels;
using Fujify.Views;

namespace Fujify.Services
{
    public class DialogService : IDialogService
    {
        private readonly ISettingsService _settingsService;

        public DialogService(ISettingsService? settingsService = null)
        {
            _settingsService = settingsService ?? new SettingsService();
        }

        private static bool IsMobileOrSingleView =>
            Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime;

        private static IStorageProvider? GetStorageProvider(Window? owner)
        {
            if (owner != null)
            {
                return owner.StorageProvider;
            }

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow?.StorageProvider;
            }

            if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView && singleView.MainView != null)
            {
                return TopLevel.GetTopLevel(singleView.MainView)?.StorageProvider;
            }

            return null;
        }

        public async Task ShowMessageAsync(Window? owner, string title, string message)
        {
            if (IsMobileOrSingleView || (owner == null && (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow == null))
            {
                MainViewModel.Instance?.ShowMessageModal(title, message);
                return;
            }

            var dialog = new MessageDialog(title, message);
            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }
        }

        public async Task<FileConflictResolution> PromptFileConflictAsync(Window? owner, string destinationFilePath)
        {
            if (IsMobileOrSingleView || (owner == null && (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow == null))
            {
                if (MainViewModel.Instance != null)
                {
                    return await MainViewModel.Instance.ShowConflictModalAsync(destinationFilePath);
                }
                return new FileConflictResolution(ConflictChoice.Skip, false);
            }

            var vm = new FileConflictDialogViewModel(destinationFilePath);
            var dialog = new FileConflictDialog(vm);

            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }

            return dialog.Result;
        }

        public async Task<IReadOnlyList<string>> ShowOpenFileDialogAsync(Window? owner, string title, bool allowMultiple = true)
        {
            var storage = GetStorageProvider(owner);
            if (storage == null) return Array.Empty<string>();

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = allowMultiple,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("RAW & DNG files")
                    {
                        Patterns = new[] { "*.dng", "*.cr3", "*.crw", "*.erf", "*.raf", "*.3fr", "*.kdc", "*.dcs", "*.dcr", "*.iiq", "*.mos", "*.mef", "*.mrw", "*.nef", "*.nrw", "*.orf", "*.rw2", "*.pef", "*.srw", "*.arw", "*.srf", "*.sr2", "*.*" }
                    },
                    new("All files") { Patterns = new[] { "*.*" } }
                }
            });

            return files.Select(f => f.Path.LocalPath).ToList();
        }

        public async Task<string?> ShowFolderBrowserDialogAsync(Window? owner, string title)
        {
            var storage = GetStorageProvider(owner);
            if (storage == null) return null;

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }

        public async Task ShowPreferencesDialogAsync(Window? owner)
        {
            if (IsMobileOrSingleView || (owner == null && (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow == null))
            {
                MainViewModel.Instance?.ShowPreferencesModal();
                return;
            }

            var vm = new PreferencesViewModel(_settingsService);
            var dialog = new PreferencesWindow(vm);

            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }
        }

        public async Task ShowAboutDialogAsync(Window? owner)
        {
            if (IsMobileOrSingleView || (owner == null && (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow == null))
            {
                MainViewModel.Instance?.ShowAboutModal();
                return;
            }

            var dialog = new AboutWindow();
            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }
        }
    }
}
