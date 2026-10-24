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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fujify.Common;
using Fujify.Services;
using Fujify.Views;

namespace Fujify.ViewModels
{
    public class QuickProcessViewModel : ViewModelBase
    {
        private readonly IRawConverterService _rawConverterService;
        private readonly IMetadataService _metadataService;
        private readonly IDialogService _dialogService;
        private readonly ISettingsService _settingsService;
        private readonly ILicenseService _licenseService;

        public bool IsPro => _licenseService.IsPro;

        private readonly bool _isProcessTo;
        private readonly ConcurrentQueue<string> _incomingQueue = new();
        private readonly List<string> _allFiles = new();
        private readonly List<string> _convertedDngFiles = new();
        private readonly HashSet<string> _reservedOutputPaths = new(StringComparer.OrdinalIgnoreCase);

        private CancellationTokenSource? _cts;
        private string? _outputFolder;
        private bool _started;

        private string _title = "Processing with Fujifier";
        private string _subtitle = "Preparing files...";
        private string _statusIcon = "⚡";
        private string _statusIconColor = "#01D701";
        private double _progressValue = 0;
        private bool _isProgressIndeterminate = true;
        private string _progressDetailText = string.Empty;
        private string _progressPercentageText = "0%";
        private bool _isProcessing = true;
        private bool _isCompleted = false;
        private bool _canOpenFolder = false;

        public Window? OwnerWindow { get; set; }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Subtitle
        {
            get => _subtitle;
            set => SetProperty(ref _subtitle, value);
        }

        public string StatusIcon
        {
            get => _statusIcon;
            set => SetProperty(ref _statusIcon, value);
        }

        public string StatusIconColor
        {
            get => _statusIconColor;
            set => SetProperty(ref _statusIconColor, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set => SetProperty(ref _isProgressIndeterminate, value);
        }

        public string ProgressDetailText
        {
            get => _progressDetailText;
            set => SetProperty(ref _progressDetailText, value);
        }

        public string ProgressPercentageText
        {
            get => _progressPercentageText;
            set => SetProperty(ref _progressPercentageText, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetProperty(ref _isCompleted, value);
        }

        public bool CanOpenFolder
        {
            get => _canOpenFolder;
            set => SetProperty(ref _canOpenFolder, value);
        }

        public ICommand CancelCommand { get; }
        public ICommand DoneCommand { get; }
        public ICommand OpenFolderCommand { get; }

        public QuickProcessViewModel(
            IEnumerable<string>? initialFiles = null,
            bool isProcessTo = false,
            IRawConverterService? rawConverterService = null,
            IMetadataService? metadataService = null,
            IDialogService? dialogService = null,
            ISettingsService? settingsService = null,
            ILicenseService? licenseService = null)
        {
            _isProcessTo = isProcessTo;
            _rawConverterService = rawConverterService ?? new RawConverterService();
            _metadataService = metadataService ?? new MetadataService();
            _settingsService = settingsService ?? new SettingsService();
            _dialogService = dialogService ?? new DialogService(_settingsService);
            _licenseService = licenseService ?? new LicenseService(_settingsService);

            CancelCommand = new RelayCommand(Cancel);
            DoneCommand = new RelayCommand(Done);
            OpenFolderCommand = new RelayCommand(OpenOutputFolder);

            if (initialFiles != null)
            {
                foreach (var file in initialFiles)
                {
                    if (!string.IsNullOrWhiteSpace(file))
                    {
                        _incomingQueue.Enqueue(file);
                    }
                }
            }
        }

        public void AddFiles(IEnumerable<string> files)
        {
            if (files == null) return;
            foreach (var file in files)
            {
                if (!string.IsNullOrWhiteSpace(file))
                {
                    _incomingQueue.Enqueue(file);
                }
            }
        }

        public async Task StartAsync()
        {
            if (_started) return;
            _started = true;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            try
            {
                // 1. If ProcessTo mode, prompt user for destination folder first
                if (_isProcessTo)
                {
                    Subtitle = "Select destination folder...";
                    IsProgressIndeterminate = true;

                    string? selectedFolder = await _dialogService.ShowFolderBrowserDialogAsync(OwnerWindow, "Select Destination Folder for Processed DNGs");
                    if (string.IsNullOrWhiteSpace(selectedFolder) || !Directory.Exists(selectedFolder))
                    {
                        // User cancelled folder picker -> exit cleanly
                        OwnerWindow?.Close();
                        return;
                    }

                    _outputFolder = selectedFolder;
                }

                // 2. Buffer a brief 150ms delay to batch all incoming Explorer secondary instances
                Subtitle = "Gathering files...";
                await Task.Delay(150, ct);

                while (_incomingQueue.TryDequeue(out var f))
                {
                    if (File.Exists(f) && !_allFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
                    {
                        _allFiles.Add(f);
                    }
                }

                if (_allFiles.Count == 0)
                {
                    Title = "No Files to Process";
                    Subtitle = "No supported RAW files were provided.";
                    StatusIcon = "⚠";
                    StatusIconColor = "#F39C12";
                    IsProcessing = false;
                    IsCompleted = true;
                    IsProgressIndeterminate = false;
                    return;
                }

                // Free Tier Queue Limit Enforcement (10 photos max)
                var filesToProcess = _allFiles;
                if (!_licenseService.IsPro && _allFiles.Count > _licenseService.MaxFreeQueueSize)
                {
                    int skipped = _allFiles.Count - _licenseService.MaxFreeQueueSize;
                    filesToProcess = _allFiles.Take(_licenseService.MaxFreeQueueSize).ToList();
                    Subtitle = $"Free Tier: processing first {_licenseService.MaxFreeQueueSize} photos ({skipped} skipped)...";
                }

                int totalCount = filesToProcess.Count;
                int processedCount = 0;
                int successCount = 0;
                int errorCount = 0;

                IsProgressIndeterminate = false;
                ProgressValue = 0;
                ProgressDetailText = $"0 of {totalCount} photos processed";
                ProgressPercentageText = "0%";
                Subtitle = $"Processing {totalCount} photo{(totalCount == 1 ? "" : "s")}...";

                int configuredConcurrency = _settingsService.Settings.MaxConcurrentConversions;
                int maxConcurrency = (configuredConcurrency > 0 && configuredConcurrency <= Environment.ProcessorCount)
                    ? configuredConcurrency
                    : Math.Max(1, Environment.ProcessorCount / 2);

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxConcurrency,
                    CancellationToken = ct
                };

                await Parallel.ForEachAsync(filesToProcess, parallelOptions, async (filePath, token) =>
                {
                    token.ThrowIfCancellationRequested();

                    string fileName = Path.GetFileName(filePath);
                    Dispatcher.UIThread.Post(() =>
                    {
                        Subtitle = $"Converting: {fileName}";
                    });

                    bool success = false;
                    try
                    {
                        string destDng = await ConvertAndInjectSingleFileAsync(filePath, _outputFolder, token);
                        lock (_convertedDngFiles)
                        {
                            _convertedDngFiles.Add(destDng);
                        }
                        success = true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[QuickProcess] Error on '{filePath}': {ex.Message}");
                    }

                    int current = Interlocked.Increment(ref processedCount);
                    if (success) Interlocked.Increment(ref successCount);
                    else Interlocked.Increment(ref errorCount);

                    int pct = (int)((double)current / totalCount * 100);

                    Dispatcher.UIThread.Post(() =>
                    {
                        ProgressValue = pct;
                        ProgressPercentageText = $"{pct}%";
                        ProgressDetailText = $"{current} of {totalCount} photos processed";
                    });
                });

                // Completed successfully
                bool hasErrors = errorCount > 0;
                Title = hasErrors ? "Processing Finished with Errors" : "Processing Complete";
                Subtitle = hasErrors
                    ? $"{successCount} succeeded, {errorCount} failed. Enjoy your new Film simulations!"
                    : (totalCount == 1
                        ? "1 photo converted successfully. Enjoy your new Film simulations!"
                        : $"{totalCount} photos converted successfully. Enjoy your new Film simulations!");

                StatusIcon = hasErrors ? "⚠" : "✔";
                StatusIconColor = hasErrors ? "#F39C12" : "#01D701";

                string? effectiveFolder = !string.IsNullOrEmpty(_outputFolder)
                    ? _outputFolder
                    : (_convertedDngFiles.Count > 0 ? Path.GetDirectoryName(_convertedDngFiles[0]) : null);

                CanOpenFolder = !string.IsNullOrEmpty(effectiveFolder) && Directory.Exists(effectiveFolder);

                // Auto-open in Lightroom if enabled in user preferences
                if (_settingsService.Settings.AutoOpenInLightroom && _convertedDngFiles.Count > 0)
                {
                    foreach (var file in _convertedDngFiles)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = file,
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                }

                // Free Tier Post-Conversion 15s Delay Modal
                if (!_licenseService.IsPro)
                {
                    await FreeTierDelayDialog.ShowDelayDialogAsync(OwnerWindow, _licenseService, isPostConversion: true);
                }
            }
            catch (OperationCanceledException)
            {
                Title = "Processing Cancelled";
                Subtitle = "Conversion was stopped by the user.";
                StatusIcon = "⏹";
                StatusIconColor = "#A0A0AC";
            }
            catch (Exception ex)
            {
                Title = "Error Occurred";
                Subtitle = ex.Message;
                StatusIcon = "⚠";
                StatusIconColor = "#E74C3C";
            }
            finally
            {
                IsProcessing = false;
                IsCompleted = true;
                IsProgressIndeterminate = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task<string> ConvertAndInjectSingleFileAsync(string filePath, string? outputFolder, CancellationToken ct)
        {
            bool isOriginallyDng = filePath.EndsWith(".dng", StringComparison.OrdinalIgnoreCase);
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);

            bool appendSuffix = _settingsService.Settings.AppendSuffix && !string.IsNullOrWhiteSpace(_settingsService.Settings.Suffix);
            if (appendSuffix)
            {
                baseFileName += _settingsService.Settings.Suffix.Trim();
            }

            string initialDestFilePath = !string.IsNullOrEmpty(outputFolder)
                ? Path.Combine(outputFolder, baseFileName + ".dng")
                : Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, baseFileName + ".dng");

            bool isSameFileInPlace = isOriginallyDng && string.IsNullOrEmpty(outputFolder) && !appendSuffix;
            string finalDestFilePath = initialDestFilePath;

            lock (_reservedOutputPaths)
            {
                if (!isSameFileInPlace && (File.Exists(initialDestFilePath) || _reservedOutputPaths.Contains(initialDestFilePath)))
                {
                    finalDestFilePath = GetUniqueFilePath(initialDestFilePath, _reservedOutputPaths);
                }
                else
                {
                    _reservedOutputPaths.Add(initialDestFilePath);
                }
            }

            string? dngFilePath = filePath;
            if (!isOriginallyDng)
            {
                bool embedOriginalRaw = _settingsService.Settings.EmbedOriginalRaw;

                string targetDngForConversion;
                if (string.IsNullOrEmpty(outputFolder))
                {
                    targetDngForConversion = finalDestFilePath;
                }
                else
                {
                    string sourceDir = Path.GetDirectoryName(filePath) ?? string.Empty;
                    targetDngForConversion = Path.Combine(sourceDir, $"{Path.GetFileNameWithoutExtension(filePath)}_tmp_{Guid.NewGuid():N}.dng");
                }

                var convResult = await Task.Run(() => _rawConverterService.ConvertToDng(
                    filePath,
                    embedOriginalRaw,
                    targetDngForConversion,
                    _settingsService.Settings.LosslessCompression), ct);

                if (!convResult.Success || string.IsNullOrEmpty(convResult.OutputDngPath))
                {
                    throw new InvalidOperationException(convResult.ErrorMessage ?? "Conversion failed");
                }

                dngFilePath = convResult.OutputDngPath;
            }

            string? customOutputPath = (!string.IsNullOrEmpty(outputFolder) || finalDestFilePath != dngFilePath)
                ? finalDestFilePath
                : null;

            var metaResult = await Task.Run(() => _metadataService.InjectFujifilmMetadata(
                dngFilePath,
                outputFolder,
                createBackup: false,
                isOriginallyDng: isOriginallyDng,
                customOutputFilePath: customOutputPath,
                targetUniqueCameraModel: "Fujifilm X-T5"), ct);

            if (!metaResult.Success)
            {
                throw new InvalidOperationException(metaResult.ErrorMessage ?? "Error injecting Fujifilm metadata");
            }

            return finalDestFilePath;
        }

        private static string GetUniqueFilePath(string filePath, HashSet<string>? reservedPaths = null)
        {
            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);

            int counter = 1;
            string newFilePath;
            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension} ({counter}){extension}");
                counter++;
            } while (File.Exists(newFilePath) || (reservedPaths != null && reservedPaths.Contains(newFilePath)));

            reservedPaths?.Add(newFilePath);
            return newFilePath;
        }

        private void Cancel()
        {
            if (IsProcessing)
            {
                _cts?.Cancel();
                Subtitle = "Cancelling processing...";
            }
        }

        private void Done()
        {
            OwnerWindow?.Close();
        }

        private void OpenOutputFolder()
        {
            string? folder = !string.IsNullOrEmpty(_outputFolder)
                ? _outputFolder
                : (_convertedDngFiles.Count > 0 ? Path.GetDirectoryName(_convertedDngFiles[0]) : null);

            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                CrossPlatformLauncher.OpenFolder(folder);
            }
        }
    }
}
