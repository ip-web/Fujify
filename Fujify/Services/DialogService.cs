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
using System.IO;
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
        private readonly ILicenseService? _licenseService;

        public static Func<string>? PlatformStagingDirectoryResolver { get; set; }

        public static string GetStagingDirectory()
        {
            if (PlatformStagingDirectoryResolver != null)
            {
                try
                {
                    string dir = PlatformStagingDirectoryResolver();
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        string staging = Path.Combine(dir, "Fujifier_Staging");
                        Directory.CreateDirectory(staging);
                        return staging;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Fujifier] Error resolving platform staging dir: {ex.Message}");
                }
            }

            try
            {
                string baseDir = OperatingSystem.IsIOS()
                    ? Path.GetTempPath()
                    : OperatingSystem.IsAndroid()
                        ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                        : Path.GetTempPath();

                if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
                {
                    baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                string stagingDir = Path.Combine(baseDir, "Fujifier_Staging");
                Directory.CreateDirectory(stagingDir);
                return stagingDir;
            }
            catch
            {
                string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fujifier_Staging");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }

        public static string DetectRawExtension(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length < 16) return ".raw";
                byte[] header = new byte[16];
                int read = fs.Read(header, 0, 16);
                if (read < 16) return ".raw";

                // Fujifilm RAF: starts with FUJIFILM
                if (header[0] == 0x46 && header[1] == 0x55 && header[2] == 0x4A && header[3] == 0x49)
                    return ".raf";

                // TIFF based (Sony ARW, Canon CR2, Nikon NEF, Adobe DNG, etc.)
                if ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00) ||
                    (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A))
                {
                    string? camera = RawMetadataReader.TryGetCameraMakeAndModel(filePath);
                    if (!string.IsNullOrEmpty(camera))
                    {
                        if (camera.Contains("SONY", StringComparison.OrdinalIgnoreCase)) return ".arw";
                        if (camera.Contains("CANON", StringComparison.OrdinalIgnoreCase)) return ".cr2";
                        if (camera.Contains("NIKON", StringComparison.OrdinalIgnoreCase)) return ".nef";
                        if (camera.Contains("FUJIFILM", StringComparison.OrdinalIgnoreCase)) return ".raf";
                        if (camera.Contains("LEICA", StringComparison.OrdinalIgnoreCase)) return ".dng";
                        if (camera.Contains("OLYMPUS", StringComparison.OrdinalIgnoreCase) || camera.Contains("OM Digital", StringComparison.OrdinalIgnoreCase)) return ".orf";
                        if (camera.Contains("PANASONIC", StringComparison.OrdinalIgnoreCase)) return ".rw2";
                    }
                    return ".dng";
                }

                // Canon CR3 (ISO BMFF / ftypcrx)
                if (header.Length >= 8 && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
                    return ".cr3";
            }
            catch { }
            return ".arw";
        }

        public DialogService(ISettingsService? settingsService = null, ILicenseService? licenseService = null)
        {
            _settingsService = settingsService ?? new SettingsService();
            _licenseService = licenseService;
        }

        private static bool IsMobileOrSingleView =>
            Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime
            || (MainViewModel.Instance != null && MainViewModel.Instance.IsPhoneLayout);

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

        public async Task ShowMessageAsync(Window? owner, string title, string message, bool isSuccess = true)
        {
            if (IsMobileOrSingleView || (owner == null && (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow == null))
            {
                MainViewModel.Instance?.ShowMessageModal(title, message);
                return;
            }

            var dialog = new MessageDialog(title, message, isSuccess);
            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                await dialog.ShowDialog(desktop.MainWindow);
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
            try
            {
                var storage = GetStorageProvider(owner);
                if (storage == null) return Array.Empty<string>();

                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = allowMultiple,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new("All Supported RAW & Photos")
                        {
                            Patterns = new[] { "*.dng", "*.cr2", "*.cr3", "*.crw", "*.erf", "*.raf", "*.3fr", "*.kdc", "*.dcs", "*.dcr", "*.iiq", "*.mos", "*.mef", "*.mrw", "*.nef", "*.nrw", "*.orf", "*.rw2", "*.pef", "*.srw", "*.arw", "*.srf", "*.sr2", "*.*" },
                            MimeTypes = new[] { "image/*", "application/octet-stream", "*/*" }
                        },
                        new("All Files (*.*)") { Patterns = new[] { "*.*" }, MimeTypes = new[] { "*/*" } }
                    }
                });

                if (files == null || files.Count == 0) return Array.Empty<string>();

                var resultPaths = new List<string>();
                string stagingDir = GetStagingDirectory();

                foreach (var file in files)
                {
                    if (file == null) continue;

                    // 1. Direct POSIX access check
                    try
                    {
                        string? localPath = file.TryGetLocalPath();
                        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                        {
                            using var testStream = File.OpenRead(localPath);
                            if (testStream.Length > 0)
                            {
                                resultPaths.Add(localPath);
                                continue;
                            }
                        }
                    }
                    catch
                    {
                        // Direct read inaccessible on this platform (e.g. Scoped Storage on Android 11+) -> stream via SAF
                    }

                    // 2. Stream from SAF or document provider into private app staging directory
                    try
                    {
                        string fileName = file.Name;
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            try { fileName = Path.GetFileName(file.Path.LocalPath); } catch { }
                        }
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            fileName = $"import_{Guid.NewGuid():N}.raw";
                        }

                        // Sanitize filename to avoid any path traversal or invalid characters
                        fileName = Path.GetFileName(fileName);
                        foreach (char c in Path.GetInvalidFileNameChars())
                        {
                            fileName = fileName.Replace(c, '_');
                        }

                        string destFilePath = Path.Combine(stagingDir, fileName);

                        await using (var sourceStream = await file.OpenReadAsync())
                        await using (var destStream = File.Create(destFilePath))
                        {
                            await sourceStream.CopyToAsync(destStream);
                        }

                        // If file has no extension (e.g. Android SAF content URI gave a name without extension),
                        // inspect the header bytes to detect RAW format and append correct extension:
                        if (string.IsNullOrEmpty(Path.GetExtension(destFilePath)))
                        {
                            string detectedExt = DetectRawExtension(destFilePath);
                            if (!string.IsNullOrEmpty(detectedExt))
                            {
                                string newDestPath = Path.ChangeExtension(destFilePath, detectedExt);
                                if (File.Exists(newDestPath)) File.Delete(newDestPath);
                                File.Move(destFilePath, newDestPath);
                                destFilePath = newDestPath;
                            }
                        }

                        resultPaths.Add(destFilePath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Fujify] Failed to stream storage file: {ex.Message}");
                    }
                }

                return resultPaths;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Fujify] Error in ShowOpenFileDialogAsync: {ex}");
                return Array.Empty<string>();
            }
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

            if (folders.Count == 0) return null;
            var folder = folders[0];
            string? localPath = folder.TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath) && Directory.Exists(localPath))
            {
                return localPath;
            }

            return folder.Path.LocalPath;
        }

        public async Task ShowPreferencesDialogAsync(Window? owner)
        {
            if (IsMobileOrSingleView || (owner == null && (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow == null))
            {
                MainViewModel.Instance?.ShowPreferencesModal();
                return;
            }

            var vm = new PreferencesViewModel(_settingsService, _licenseService, dialogService: this);
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
