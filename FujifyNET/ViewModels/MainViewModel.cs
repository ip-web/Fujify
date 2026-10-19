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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Input;
using FujifyNET.Common;
using FujifyNET.Models;
using FujifyNET.Services;

namespace FujifyNET.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IRawConverterService _rawConverterService;
        private readonly IMetadataService _metadataService;
        private readonly IThumbnailService _thumbnailService;
        private readonly IDialogService _dialogService;

        private readonly HashSet<string> _supportedFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".dng", ".cr3", ".crw", ".erf", ".raf", ".3fr", ".kdc", ".dcs", ".dcr", ".iiq", ".mos",
            ".mef", ".mrw", ".nef", ".nrw", ".orf", ".rw2", ".pef", ".srw", ".arw", ".srf", ".sr2"
        };

        private CancellationTokenSource? _processingCts;
        private string? _outputFolderPath;
        private bool _createBackup = true;
        private bool _isProcessing;
        private bool _isLoading;
        private bool _isProgressVisible;
        private double _progressValue;
        private string _processButtonContent = "Process";

        public ObservableCollection<FileItem> Files { get; } = new();

        public event Action? RequestRecalculateColumns;
        public event Action? ProcessingCompleted;

        public MainViewModel(
            IRawConverterService? rawConverterService = null,
            IMetadataService? metadataService = null,
            IThumbnailService? thumbnailService = null,
            IDialogService? dialogService = null)
        {
            _rawConverterService = rawConverterService ?? new RawConverterService();
            _metadataService = metadataService ?? new MetadataService();
            _thumbnailService = thumbnailService ?? new ThumbnailService();
            _dialogService = dialogService ?? new DialogService();

            Files.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasFiles));
                OnPropertyChanged(nameof(CanProcess));
                OnPropertyChanged(nameof(CanClear));
                OnPropertyChanged(nameof(CanProcessOrStop));
            };

            ProcessCommand = new AsyncRelayCommand(ExecuteProcessOrStopAsync);
            ClearCommand = new AsyncRelayCommand(ExecuteClearAsync, () => CanClear);
            BrowseFolderCommand = new RelayCommand(ExecuteBrowseFolder);
            AddFilesCommand = new AsyncRelayCommand(ExecuteAddFilesAsync);
            AddFolderCommand = new AsyncRelayCommand(ExecuteAddFolderAsync);
            DeleteSelectedFilesCommand = new RelayCommand<System.Collections.IList>(ExecuteDeleteSelectedFiles);
            OpenFileCommand = new RelayCommand<FileItem>(ExecuteOpenFile);
            OpenFolderCommand = new RelayCommand<FileItem>(ExecuteOpenFolder);
            OpenPreferencesCommand = new RelayCommand(ExecuteOpenPreferences);
            OpenAboutCommand = new RelayCommand(ExecuteOpenAbout);
            OpenHelpCommand = new RelayCommand(ExecuteOpenHelp);
            OpenUpdateCommand = new RelayCommand(ExecuteOpenUpdate);
            ReportBugCommand = new RelayCommand(ExecuteReportBug);
        }

        #region Properties

        public string? OutputFolderPath
        {
            get => _outputFolderPath;
            set
            {
                if (SetProperty(ref _outputFolderPath, value))
                {
                    OnPropertyChanged(nameof(FormattedOutputFolderPath));
                }
            }
        }

        public string FormattedOutputFolderPath =>
            string.IsNullOrWhiteSpace(_outputFolderPath)
                ? "(Optional)"
                : _outputFolderPath.Replace(@"\", "  >  ").Replace(":  >  ", "  >  ");

        public bool CreateBackup
        {
            get => _createBackup;
            set => SetProperty(ref _createBackup, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    OnPropertyChanged(nameof(CanProcess));
                    OnPropertyChanged(nameof(CanClear));
                    OnPropertyChanged(nameof(CanProcessOrStop));
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public bool HasFiles => Files.Count > 0;

        public bool CanProcess => !IsProcessing && Files.Any(f => f.Status == "Pending");

        public bool CanClear => !IsProcessing && Files.Count > 0;

        public bool CanProcessOrStop
        {
            get
            {
                if (IsProcessing)
                {
                    return _processingCts != null && !_processingCts.IsCancellationRequested;
                }
                return CanProcess;
            }
        }

        public string ProcessButtonContent
        {
            get => _processButtonContent;
            set => SetProperty(ref _processButtonContent, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public bool IsProgressVisible
        {
            get => _isProgressVisible;
            set => SetProperty(ref _isProgressVisible, value);
        }

        #endregion

        #region Commands

        public ICommand ProcessCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand BrowseFolderCommand { get; }
        public ICommand AddFilesCommand { get; }
        public ICommand AddFolderCommand { get; }
        public ICommand DeleteSelectedFilesCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand OpenPreferencesCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand OpenHelpCommand { get; }
        public ICommand OpenUpdateCommand { get; }
        public ICommand ReportBugCommand { get; }

        #endregion

        #region Actions & Business Logic

        public async Task AddFilesAsync(IEnumerable<string> fileOrFolderPaths)
        {
            IsLoading = true;
            try
            {
                var inputList = fileOrFolderPaths.ToList();
                var filesToAdd = new List<string>();

                await Task.Run(() =>
                {
                    foreach (var path in inputList)
                    {
                        if (Directory.Exists(path))
                        {
                            try
                            {
                                var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                                                     .Where(file => _supportedFormats.Contains(Path.GetExtension(file)));
                                filesToAdd.AddRange(files);
                            }
                            catch (Exception ex)
                            {
                                Application.Current.Dispatcher.InvokeAsync(() =>
                                    _dialogService.ShowMessageAsync("Error", $"Failed to access files in folder: {ex.Message}"));
                            }
                        }
                        else if (File.Exists(path))
                        {
                            filesToAdd.Add(path);
                        }
                    }
                });

                var existingPaths = new HashSet<string>(Files.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);

                await Task.Run(() =>
                {
                    var batch = new List<FileItem>();

                    foreach (var filePath in filesToAdd)
                    {
                        if (existingPaths.Contains(filePath)) continue;

                        string extension = Path.GetExtension(filePath);

                        if (_supportedFormats.Contains(extension))
                        {
                            existingPaths.Add(filePath);
                            bool skipThumbnails = Properties.Settings.Default.SkipThumbnailGeneration;
                            var thumbnail = skipThumbnails
                                ? _thumbnailService.GetPlaceholderImage()
                                : _thumbnailService.GenerateThumbnail(filePath);
                            string cameraMakeAndModel = _metadataService.GetCameraMakeAndModel(filePath);

                            batch.Add(new FileItem
                            {
                                FilePath = filePath,
                                Status = "Pending",
                                Thumbnail = thumbnail,
                                CameraMakeAndModel = cameraMakeAndModel
                            });

                            if (batch.Count >= 10)
                            {
                                var currentBatch = batch.ToList();
                                batch.Clear();
                                Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    foreach (var item in currentBatch) Files.Add(item);
                                });
                            }
                        }
                        else
                        {
                            Application.Current.Dispatcher.InvokeAsync(() =>
                                _dialogService.ShowMessageAsync("Unsupported File Format", $"The file format {extension} is not supported. The file was not added."));
                        }
                    }

                    if (batch.Count > 0)
                    {
                        var remaining = batch.ToList();
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var item in remaining) Files.Add(item);
                        });
                    }
                });
            }
            finally
            {
                IsLoading = false;
                RequestRecalculateColumns?.Invoke();
            }
        }

        private async Task ExecuteProcessOrStopAsync()
        {
            if (IsProcessing)
            {
                if (ProcessButtonContent == "Stop")
                {
                    _processingCts?.Cancel();
                    ProcessButtonContent = "Stopping...";
                    OnPropertyChanged(nameof(CanProcessOrStop));
                }
                return;
            }

            var pendingFiles = Files.Where(f => f.Status == "Pending").Select(f => f.FilePath).ToList();

            if (pendingFiles.Count == 0)
            {
                await _dialogService.ShowMessageAsync("Warning", "No files to process.");
                ProcessButtonContent = "Process";
                return;
            }

            await ProcessFilesAsync(pendingFiles);
        }

        public async Task ProcessFilesAsync(List<string> files)
        {
            _processingCts = new CancellationTokenSource();
            var token = _processingCts.Token;

            ProgressValue = 0;
            IsProgressVisible = true;
            IsProcessing = true;
            ProcessButtonContent = "Stop";

            bool createBackup = CreateBackup;
            string? currentOutputFolder = OutputFolderPath;
            bool hasProcessingErrors = false;

            int totalFiles = files.Count;
            int configuredConcurrency = Properties.Settings.Default.MaxConcurrentConversions;
            int maxConcurrency = (configuredConcurrency > 0 && configuredConcurrency <= Environment.ProcessorCount)
                ? configuredConcurrency
                : Math.Max(1, Environment.ProcessorCount / 2);
            int processedCount = 0;

            bool cancelled = false;
            Exception? error = null;

            try
            {
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxConcurrency,
                    CancellationToken = token
                };

                var conflictLock = new SemaphoreSlim(1, 1);
                FileConflictAction? batchConflictAction = null;
                var reservedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                await Parallel.ForEachAsync(files, parallelOptions, async (filePath, ct) =>
                {
                    ct.ThrowIfCancellationRequested();

                    UpdateFileStatus(filePath, "Processing");

                    try
                    {
                        await ProcessSingleFileAsync(
                            filePath,
                            createBackup,
                            currentOutputFolder,
                            conflictLock,
                            () => batchConflictAction,
                            act => batchConflictAction = act,
                            reservedOutputPaths,
                            ct);
                    }
                    catch (OperationCanceledException)
                    {
                        UpdateFileStatus(filePath, "Pending");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        UpdateFileStatus(filePath, $"Error: {ex.Message}");
                        hasProcessingErrors = true;
                    }
                    finally
                    {
                        int current = Interlocked.Increment(ref processedCount);
                        int progressPercentage = (int)((double)current / totalFiles * 100);
                        ProgressValue = progressPercentage;
                    }
                });
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                _processingCts?.Dispose();
                _processingCts = null;
                IsProcessing = false;
                ProcessButtonContent = "Process";
            }

            if (cancelled)
            {
                await _dialogService.ShowMessageAsync(string.Empty, "Processing was cancelled.");
                foreach (var item in Files.Where(f => f.Status == "Processing"))
                {
                    item.Status = "Pending";
                }
            }
            else if (error != null)
            {
                await _dialogService.ShowMessageAsync("Error", $"An error occurred: {error.Message}");
            }
            else
            {
                ProcessingCompleted?.Invoke();

                string message = hasProcessingErrors
                    ? "Processing completed with some errors. Please check the status for details."
                    : "Enjoy your new Film simulations!";

                await _dialogService.ShowMessageAsync("Processing completed", message);
            }
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

        private async Task ProcessSingleFileAsync(
            string filePath,
            bool createBackup,
            string? outputFolder,
            SemaphoreSlim conflictLock,
            Func<FileConflictAction?> getBatchAction,
            Action<FileConflictAction> setBatchAction,
            HashSet<string> reservedOutputPaths,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                UpdateFileStatus(filePath, "Invalid file path.");
                throw new ArgumentException("Invalid file path.", nameof(filePath));
            }

            bool isOriginallyDng = filePath.EndsWith(".dng", StringComparison.OrdinalIgnoreCase);
            string initialDestFilePath = !string.IsNullOrEmpty(outputFolder)
                ? Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(filePath) + ".dng")
                : Path.ChangeExtension(filePath, ".dng");

            bool isSameFileInPlace = isOriginallyDng && string.IsNullOrEmpty(outputFolder);
            bool fileAlreadyExists;
            lock (reservedOutputPaths)
            {
                fileAlreadyExists = File.Exists(initialDestFilePath) || reservedOutputPaths.Contains(initialDestFilePath);
            }

            bool destinationConflict = !isSameFileInPlace && fileAlreadyExists;
            string finalDestFilePath = initialDestFilePath;

            // Check for file conflict in the destination prior to conversion
            if (destinationConflict)
            {
                FileConflictAction action;
                var batchAction = getBatchAction();

                if (batchAction.HasValue)
                {
                    action = batchAction.Value;
                }
                else
                {
                    await conflictLock.WaitAsync(ct);
                    try
                    {
                        batchAction = getBatchAction();
                        if (batchAction.HasValue)
                        {
                            action = batchAction.Value;
                        }
                        else
                        {
                            var result = await _dialogService.PromptFileConflictAsync(initialDestFilePath);
                            action = result.Action;
                            if (result.ApplyToAll)
                            {
                                setBatchAction(action);
                            }
                        }
                    }
                    finally
                    {
                        conflictLock.Release();
                    }
                }

                if (action == FileConflictAction.Skip)
                {
                    UpdateFileStatus(filePath, "Skipped (Exists)");
                    return;
                }
                else if (action == FileConflictAction.UseUniqueName)
                {
                    lock (reservedOutputPaths)
                    {
                        finalDestFilePath = GetUniqueFilePath(initialDestFilePath, reservedOutputPaths);
                    }
                }
                else // Overwrite
                {
                    lock (reservedOutputPaths)
                    {
                        reservedOutputPaths.Add(initialDestFilePath);
                    }
                }
            }
            else
            {
                lock (reservedOutputPaths)
                {
                    reservedOutputPaths.Add(initialDestFilePath);
                }
            }

            string? dngFilePath = filePath;

            if (!isOriginallyDng)
            {
                UpdateFileStatus(filePath, "Converting to DNG");
                bool embedOriginalRaw = Properties.Settings.Default.EmbedOriginalRaw;

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

                var convResult = await Task.Run(() => _rawConverterService.ConvertToDng(filePath, embedOriginalRaw, targetDngForConversion), ct);
                if (!convResult.Success || string.IsNullOrEmpty(convResult.OutputDngPath))
                {
                    string err = convResult.ErrorMessage ?? "Conversion failed";
                    UpdateFileStatus(filePath, err);
                    throw new InvalidOperationException(err);
                }

                dngFilePath = convResult.OutputDngPath;
                UpdateFileStatus(filePath, "Conversion successful");
            }

            string? customOutputPath = !string.IsNullOrEmpty(outputFolder)
                ? finalDestFilePath
                : null;

            var metaResult = await Task.Run(() => _metadataService.InjectFujifilmMetadata(
                dngFilePath,
                outputFolder,
                createBackup,
                isOriginallyDng,
                customOutputPath), ct);

            if (metaResult.Success)
            {
                UpdateFileStatus(filePath, "✔");
            }
            else
            {
                string err = metaResult.ErrorMessage ?? "Error writing metadata";
                UpdateFileStatus(filePath, err);
                throw new InvalidOperationException(err);
            }
        }

        public void UpdateFileStatus(string filePath, string status)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var fileItem = Files.FirstOrDefault(f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (fileItem != null)
                {
                    fileItem.Status = status;
                }
            });
        }

        private async Task ExecuteClearAsync()
        {
            if (Files.Count == 0) return;
            Files.Clear();
            IsProgressVisible = false;
            ProgressValue = 0;
            await Task.CompletedTask;
        }

        private void ExecuteBrowseFolder()
        {
            string? selectedFolder = _dialogService.ShowFolderBrowserDialog("Select a folder to save the processed images");
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                OutputFolderPath = selectedFolder;
            }
        }

        private async Task ExecuteAddFilesAsync()
        {
            string[]? selectedFiles = _dialogService.ShowOpenFileDialog("RAW & DNG files (*.*)|*.*|DNG files (*.dng)|*.dng", multiselect: true);
            if (selectedFiles != null && selectedFiles.Length > 0)
            {
                await AddFilesAsync(selectedFiles);
            }
        }

        private async Task ExecuteAddFolderAsync()
        {
            string? selectedFolder = _dialogService.ShowFolderBrowserDialog("Select folder containing raw pictures");
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                await AddFilesAsync(new[] { selectedFolder });
            }
        }

        private void ExecuteDeleteSelectedFiles(System.Collections.IList? selectedList)
        {
            if (selectedList == null || selectedList.Count == 0) return;

            var itemsToRemove = selectedList.OfType<FileItem>().ToList();
            foreach (var item in itemsToRemove)
            {
                Files.Remove(item);
            }
        }

        private void ExecuteOpenFile(FileItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;

            try
            {
                Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessageAsync("Error", $"Failed to open file: {ex.Message}");
            }
        }

        private void ExecuteOpenFolder(FileItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;

            try
            {
                string? folderPath = Path.GetDirectoryName(item.FilePath);
                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", folderPath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessageAsync("Error", $"Failed to open folder: {ex.Message}");
            }
        }

        private void ExecuteOpenPreferences()
        {
            _dialogService.ShowPreferencesDialog(Application.Current.MainWindow);
        }

        private void ExecuteOpenAbout()
        {
            _dialogService.ShowAboutDialog();
        }

        private void ExecuteOpenHelp()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/ip-web/Fujify",
                UseShellExecute = true
            });
        }

        private void ExecuteOpenUpdate()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/ip-web/Fujify/releases",
                UseShellExecute = true
            });
        }

        private void ExecuteReportBug()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/ip-web/Fujify/issues",
                UseShellExecute = true
            });
        }

        #endregion
    }
}
