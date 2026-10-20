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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fujify.Common;
using Fujify.Models;
using Fujify.Services;

namespace Fujify.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IRawConverterService _rawConverterService;
        private readonly IMetadataService _metadataService;
        private readonly IThumbnailService _thumbnailService;
        private readonly IDialogService _dialogService;
        private readonly ISettingsService _settingsService;

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

        public Window? OwnerWindow { get; set; }

        public ObservableCollection<FileItem> Files { get; } = new();

        public event Action? ProcessingCompleted;

        public MainViewModel(
            IRawConverterService? rawConverterService = null,
            IMetadataService? metadataService = null,
            IThumbnailService? thumbnailService = null,
            IDialogService? dialogService = null,
            ISettingsService? settingsService = null)
        {
            _rawConverterService = rawConverterService ?? new RawConverterService();
            _metadataService = metadataService ?? new MetadataService();
            _thumbnailService = thumbnailService ?? new ThumbnailService();
            _dialogService = dialogService ?? new DialogService();
            _settingsService = settingsService ?? new SettingsService();
            Instance = this;

            ProcessCommand = new AsyncRelayCommand(ExecuteProcessOrStopAsync);
            ClearCommand = new AsyncRelayCommand(ExecuteClearAsync, () => CanClear);
            BrowseFolderCommand = new AsyncRelayCommand(ExecuteBrowseFolderAsync);
            AddFilesCommand = new AsyncRelayCommand(ExecuteAddFilesAsync);
            AddFolderCommand = new AsyncRelayCommand(ExecuteAddFolderAsync);

            Files.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasFiles));
                OnPropertyChanged(nameof(CanProcess));
                OnPropertyChanged(nameof(CanClear));
                OnPropertyChanged(nameof(CanProcessOrStop));
                (ClearCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (ProcessCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            };
            DeleteSelectedFilesCommand = new RelayCommand<System.Collections.IList>(ExecuteDeleteSelectedFiles);
            OpenFileCommand = new RelayCommand<FileItem>(ExecuteOpenFile);
            OpenFolderCommand = new RelayCommand<FileItem>(ExecuteOpenFolder);
            OpenPreferencesCommand = new AsyncRelayCommand(ExecuteOpenPreferencesAsync);
            OpenAboutCommand = new AsyncRelayCommand(ExecuteOpenAboutAsync);
            OpenHelpCommand = new RelayCommand(ExecuteOpenHelp);
            OpenUpdateCommand = new RelayCommand(ExecuteOpenUpdate);
            ReportBugCommand = new RelayCommand(ExecuteReportBug);

            RemoveFileCommand = new RelayCommand<FileItem>(file =>
            {
                if (file != null && Files.Contains(file))
                {
                    Files.Remove(file);
                    if (SelectedFile == file)
                    {
                        SelectedFile = Files.FirstOrDefault();
                    }
                }
            });

            SelectFileCommand = new RelayCommand<FileItem>(file =>
            {
                if (file != null)
                {
                    SelectedFile = file;
                }
            });

            OpenCameraPickerCommand = new RelayCommand(() =>
            {
                ModalTitle = "Select Fujifilm Camera Simulation";
                ModalType = "CameraPicker";
                IsModalOpen = true;
            });

            SelectCameraModelCommand = new RelayCommand<string>(model =>
            {
                if (!string.IsNullOrEmpty(model))
                {
                    TargetCameraModel = model;
                }
                CloseModal();
            });

            CloseModalCommand = new RelayCommand(CloseModal);

            OpenInLightroomCommand = new RelayCommand(() =>
            {
                CloseModal();
                OpenInLightroom(_recentlyConvertedDngFiles);
            });

            ResolveConflictCommand = new RelayCommand<string>(choiceStr =>
            {
                if (Enum.TryParse<ConflictChoice>(choiceStr, true, out var choice))
                {
                    ResolveConflict(choice, ConflictApplyToAll);
                }
            });

            if (PendingInitialFiles.Count > 0)
            {
                var pending = PendingInitialFiles.ToList();
                PendingInitialFiles.Clear();
                Dispatcher.UIThread.Post(async () => await AddFilesAsync(pending));
            }
        }

        public static MainViewModel? Instance { get; set; }
        public static readonly List<string> PendingInitialFiles = new();
        public static Action<string>? PlatformFileCreatedCallback { get; set; }
        public static Action<IReadOnlyList<string>>? PlatformOpenInLightroomCallback { get; set; }
        public static Func<Task<IReadOnlyList<string>?>>? PlatformPickPhotosCallback { get; set; }

        private readonly List<string> _recentlyConvertedDngFiles = new();
        public IReadOnlyList<string> RecentlyConvertedDngFiles => _recentlyConvertedDngFiles;
        public bool HasRecentlyConvertedFiles => _recentlyConvertedDngFiles.Count > 0;
        public bool CanOpenInLightroom => HasRecentlyConvertedFiles && ModalTitle == "Processing completed";

        #region Properties

        public ICommand RemoveFileCommand { get; }
        public ICommand SelectFileCommand { get; }
        public ICommand OpenCameraPickerCommand { get; }
        public ICommand SelectCameraModelCommand { get; }
        public ICommand CloseModalCommand { get; }
        public ICommand OpenInLightroomCommand { get; }
        public ICommand ResolveConflictCommand { get; }

        private FileItem? _selectedFile;
        public FileItem? SelectedFile
        {
            get => _selectedFile ?? Files.FirstOrDefault();
            set
            {
                if (SetProperty(ref _selectedFile, value))
                {
                    OnPropertyChanged(nameof(HasSelectedFile));
                }
            }
        }

        public bool HasSelectedFile => SelectedFile != null;

        private string _targetCameraModel = "Fujifilm X-T5";
        public string TargetCameraModel
        {
            get => _targetCameraModel;
            set
            {
                if (SetProperty(ref _targetCameraModel, value))
                {
                    OnPropertyChanged(nameof(ShortTargetModelName));
                }
            }
        }

        public string ShortTargetModelName =>
            _targetCameraModel.StartsWith("Fujifilm ", StringComparison.OrdinalIgnoreCase)
                ? _targetCameraModel.Substring("Fujifilm ".Length).Trim()
                : _targetCameraModel;

        public List<string> TargetCameraModels { get; } = new()
        {
            "Fujifilm X-T5",
            "Fujifilm X-H2",
            "Fujifilm X-T4",
            "Fujifilm X-T3",
            "Fujifilm GFX 100S",
            "Fujifilm X100V",
            "Fujifilm X-Pro3"
        };

        // Adaptive Layout Support (Phone, Tablet, Desktop)
        private bool _isPhoneLayout;
        public bool IsPhoneLayout
        {
            get => _isPhoneLayout;
            set
            {
                if (SetProperty(ref _isPhoneLayout, value))
                {
                    OnPropertyChanged(nameof(IsTabletOrDesktopLayout));
                }
            }
        }

        public bool IsTabletOrDesktopLayout => !_isPhoneLayout;

        private bool _isTabletLayout;
        public bool IsTabletLayout
        {
            get => _isTabletLayout;
            set => SetProperty(ref _isTabletLayout, value);
        }

        public bool AppendSuffix
        {
            get => _settingsService.Settings.AppendSuffix;
            set
            {
                if (_settingsService.Settings.AppendSuffix != value)
                {
                    _settingsService.Settings.AppendSuffix = value;
                    _settingsService.Save();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SuffixPreview));
                }
            }
        }

        public string Suffix
        {
            get => _settingsService.Settings.Suffix;
            set
            {
                if (_settingsService.Settings.Suffix != value)
                {
                    _settingsService.Settings.Suffix = value;
                    _settingsService.Save();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SuffixPreview));
                }
            }
        }

        public string SuffixPreview => AppendSuffix && !string.IsNullOrWhiteSpace(Suffix)
            ? $"DSC0001{Suffix.Trim()}.dng"
            : "DSC0001.dng";

        public void UpdateLayoutMode(double width, double height)
        {
            IsPhoneLayout = width < 650;
            IsTabletLayout = width >= 650 && width < 1050;
            if (_selectedFile == null && Files.Count > 0)
            {
                SelectedFile = Files[0];
            }
        }

        // In-App Modal / Bottom Sheet Support for Mobile & Desktop
        private bool _isModalOpen;
        public bool IsModalOpen
        {
            get => _isModalOpen;
            set => SetProperty(ref _isModalOpen, value);
        }

        private string _modalType = "None";
        public string ModalType
        {
            get => _modalType;
            set
            {
                if (SetProperty(ref _modalType, value))
                {
                    OnPropertyChanged(nameof(IsCameraPickerModal));
                    OnPropertyChanged(nameof(IsConflictModal));
                    OnPropertyChanged(nameof(IsPreferencesModal));
                    OnPropertyChanged(nameof(IsAboutModal));
                    OnPropertyChanged(nameof(IsMessageModal));
                }
            }
        }

        public bool IsCameraPickerModal => _modalType == "CameraPicker";
        public bool IsConflictModal => _modalType == "Conflict";
        public bool IsPreferencesModal => _modalType == "Preferences";
        public bool IsAboutModal => _modalType == "About";
        public bool IsMessageModal => _modalType == "Message";

        private string _modalTitle = string.Empty;
        public string ModalTitle
        {
            get => _modalTitle;
            set => SetProperty(ref _modalTitle, value);
        }

        private string _modalMessage = string.Empty;
        public string ModalMessage
        {
            get => _modalMessage;
            set => SetProperty(ref _modalMessage, value);
        }

        private string _conflictFilePath = string.Empty;
        public string ConflictFilePath
        {
            get => _conflictFilePath;
            set
            {
                if (SetProperty(ref _conflictFilePath, value))
                {
                    OnPropertyChanged(nameof(ConflictFileName));
                }
            }
        }

        public string ConflictFileName => Path.GetFileName(_conflictFilePath);

        private bool _conflictApplyToAll;
        public bool ConflictApplyToAll
        {
            get => _conflictApplyToAll;
            set => SetProperty(ref _conflictApplyToAll, value);
        }

        private TaskCompletionSource<FileConflictResolution>? _conflictTcs;

        private PreferencesViewModel? _preferences;
        public PreferencesViewModel Preferences
        {
            get => _preferences ??= new PreferencesViewModel(_settingsService);
            set => SetProperty(ref _preferences, value);
        }

        public void ShowMessageModal(string title, string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ModalTitle = title;
                ModalMessage = message;
                ModalType = "Message";
                OnPropertyChanged(nameof(CanOpenInLightroom));
                IsModalOpen = true;
            });
        }

        public void ShowPreferencesModal()
        {
            Dispatcher.UIThread.Post(() =>
            {
                Preferences = new PreferencesViewModel(_settingsService)
                {
                    CloseAction = ok => CloseModal()
                };
                ModalTitle = "Preferences";
                ModalType = "Preferences";
                IsModalOpen = true;
            });
        }

        public void ShowAboutModal()
        {
            Dispatcher.UIThread.Post(() =>
            {
                ModalTitle = "About Fujify";
                ModalType = "About";
                IsModalOpen = true;
            });
        }

        public Task<FileConflictResolution> ShowConflictModalAsync(string filePath)
        {
            var tcs = new TaskCompletionSource<FileConflictResolution>();
            _conflictTcs = tcs;

            Dispatcher.UIThread.Post(() =>
            {
                ConflictFilePath = filePath;
                ConflictApplyToAll = false;
                ModalTitle = "File Conflict";
                ModalType = "Conflict";
                IsModalOpen = true;
            });

            return tcs.Task;
        }

        public void ResolveConflict(ConflictChoice choice, bool applyToAll)
        {
            IsModalOpen = false;
            ModalType = "None";
            _conflictTcs?.TrySetResult(new FileConflictResolution(choice, applyToAll));
            _conflictTcs = null;
        }

        public void CloseModal()
        {
            IsModalOpen = false;
            ModalType = "None";
            _conflictTcs?.TrySetResult(new FileConflictResolution(ConflictChoice.Skip, false));
            _conflictTcs = null;
            OnPropertyChanged(nameof(CanOpenInLightroom));
        }

        public void ImportSharedFiles(IEnumerable<string> filePaths)
        {
            Dispatcher.UIThread.Post(async () => await AddFilesAsync(filePaths));
        }

        public void OpenInLightroom(IReadOnlyList<string>? files = null)
        {
            var targetFiles = files ?? _recentlyConvertedDngFiles;
            if (targetFiles == null || targetFiles.Count == 0) return;

            if (PlatformOpenInLightroomCallback != null)
            {
                PlatformOpenInLightroomCallback(targetFiles);
            }
            else
            {
                // Fallback for desktop: open with default system viewer or Lightroom
                foreach (var file in targetFiles)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = file,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
        }

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
                : _outputFolderPath.Replace(Path.DirectorySeparatorChar.ToString(), "  >  ");

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
                    (ClearCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                    (ProcessCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
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
                                Dispatcher.UIThread.Post(() =>
                                    _dialogService.ShowMessageAsync(OwnerWindow, "Error", $"Failed to access files in folder: {ex.Message}"));
                            }
                        }
                        else if (File.Exists(path) || _supportedFormats.Contains(Path.GetExtension(path)))
                        {
                            filesToAdd.Add(path);
                        }
                    }
                });

                if (filesToAdd.Count == 0 && inputList.Count > 0)
                {
                    Dispatcher.UIThread.Post(() =>
                        _dialogService.ShowMessageAsync(OwnerWindow, "No Supported Photos Found", "No supported RAW or DNG photos could be loaded from the selection."));
                }

                var existingPaths = new HashSet<string>(Files.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);

                await Task.Run(() =>
                {
                    var batch = new List<FileItem>();

                    foreach (var filePath in filesToAdd)
                    {
                        try
                        {
                            if (existingPaths.Contains(filePath)) continue;

                            string extension = Path.GetExtension(filePath);

                            if (_supportedFormats.Contains(extension))
                            {
                                existingPaths.Add(filePath);
                                bool skipThumbnails = _settingsService.Settings.SkipThumbnailGeneration;
                                var thumbnail = skipThumbnails
                                    ? _thumbnailService.GetPlaceholderImage()
                                    : _thumbnailService.GenerateThumbnail(filePath);
                                string cameraMakeAndModel = _metadataService.GetCameraMakeAndModel(filePath);

                                batch.Add(new FileItem
                                {
                                    FilePath = filePath,
                                    Status = "Pending",
                                    Thumbnail = thumbnail,
                                    CameraMakeAndModel = cameraMakeAndModel,
                                    RemoveCommand = RemoveFileCommand
                                });

                                if (batch.Count >= 10)
                                {
                                    var currentBatch = batch.ToList();
                                    batch.Clear();
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        foreach (var item in currentBatch) Files.Add(item);
                                    });
                                }
                            }
                            else
                            {
                                Dispatcher.UIThread.Post(() =>
                                    _dialogService.ShowMessageAsync(OwnerWindow, "Unsupported File Format", $"The file format {extension} is not supported."));
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Fujify] Error processing {filePath}: {ex.Message}");
                        }
                    }

                    if (batch.Count > 0)
                    {
                        var remaining = batch.ToList();
                        Dispatcher.UIThread.Post(() =>
                        {
                            foreach (var item in remaining) Files.Add(item);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Fujify] Exception in AddFilesAsync: {ex}");
                Dispatcher.UIThread.Post(() =>
                    _dialogService.ShowMessageAsync(OwnerWindow, "Import Error", $"Could not import photos: {ex.Message}"));
            }
            finally
            {
                IsLoading = false;
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
                await _dialogService.ShowMessageAsync(OwnerWindow, "Warning", "No files to process.");
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
            lock (_recentlyConvertedDngFiles)
            {
                _recentlyConvertedDngFiles.Clear();
            }
            OnPropertyChanged(nameof(HasRecentlyConvertedFiles));
            OnPropertyChanged(nameof(CanOpenInLightroom));

            bool createBackup = CreateBackup;
            string? currentOutputFolder = OutputFolderPath;
            bool hasProcessingErrors = false;

            int totalFiles = files.Count;
            int configuredConcurrency = _settingsService.Settings.MaxConcurrentConversions;
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
                ConflictChoice? batchConflictChoice = null;
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
                            () => batchConflictChoice,
                            choice => batchConflictChoice = choice,
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
                        Dispatcher.UIThread.Post(() => ProgressValue = progressPercentage);
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
                await _dialogService.ShowMessageAsync(OwnerWindow, string.Empty, "Processing was cancelled.");
                foreach (var item in Files.Where(f => f.Status == "Processing"))
                {
                    item.Status = "Pending";
                }
            }
            else if (error != null)
            {
                await _dialogService.ShowMessageAsync(OwnerWindow, "Error", $"An error occurred: {error.Message}");
            }
            else
            {
                ProcessingCompleted?.Invoke();

                string message = hasProcessingErrors
                    ? "Processing completed with some errors. Please check the status for details."
                    : "Enjoy your new Film simulations!";

                List<string> convertedToOpen;
                lock (_recentlyConvertedDngFiles)
                {
                    convertedToOpen = _recentlyConvertedDngFiles.ToList();
                }

                if (_settingsService.Settings.AutoOpenInLightroom && convertedToOpen.Count > 0)
                {
                    OpenInLightroom(convertedToOpen);
                }

                await _dialogService.ShowMessageAsync(OwnerWindow, "Processing completed", message);
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
            Func<ConflictChoice?> getBatchChoice,
            Action<ConflictChoice> setBatchChoice,
            HashSet<string> reservedOutputPaths,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                UpdateFileStatus(filePath, "Invalid file path.");
                throw new ArgumentException("Invalid file path.", nameof(filePath));
            }

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
                ConflictChoice choice;
                var batchChoice = getBatchChoice();

                if (batchChoice.HasValue)
                {
                    choice = batchChoice.Value;
                }
                else
                {
                    await conflictLock.WaitAsync(ct);
                    try
                    {
                        batchChoice = getBatchChoice();
                        if (batchChoice.HasValue)
                        {
                            choice = batchChoice.Value;
                        }
                        else
                        {
                            var result = await _dialogService.PromptFileConflictAsync(OwnerWindow, initialDestFilePath);
                            choice = result.Choice;
                            if (result.ApplyToAll)
                            {
                                setBatchChoice(choice);
                            }
                        }
                    }
                    finally
                    {
                        conflictLock.Release();
                    }
                }

                if (choice == ConflictChoice.Skip)
                {
                    UpdateFileStatus(filePath, "Skipped (Exists)");
                    return;
                }
                else if (choice == ConflictChoice.UseUniqueName)
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

            string? customOutputPath = (!string.IsNullOrEmpty(outputFolder) || finalDestFilePath != dngFilePath)
                ? finalDestFilePath
                : null;

            var metaResult = await Task.Run(() => _metadataService.InjectFujifilmMetadata(
                dngFilePath,
                outputFolder,
                createBackup,
                isOriginallyDng,
                customOutputPath,
                targetUniqueCameraModel: TargetCameraModel), ct);

            if (metaResult.Success)
            {
                UpdateFileStatus(filePath, "✔");
                lock (_recentlyConvertedDngFiles)
                {
                    _recentlyConvertedDngFiles.Add(finalDestFilePath);
                }
                PlatformFileCreatedCallback?.Invoke(finalDestFilePath);
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
            Dispatcher.UIThread.Post(() =>
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

        private async Task ExecuteBrowseFolderAsync()
        {
            string? selectedFolder = await _dialogService.ShowFolderBrowserDialogAsync(OwnerWindow, "Select output folder");
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                OutputFolderPath = selectedFolder;
            }
        }

        private async Task ExecuteAddFilesAsync()
        {
            try
            {
                IReadOnlyList<string>? selectedFiles = null;
                if (PlatformPickPhotosCallback != null)
                {
                    selectedFiles = await PlatformPickPhotosCallback();
                }
                else
                {
                    selectedFiles = await _dialogService.ShowOpenFileDialogAsync(OwnerWindow, "Select RAW & DNG files", allowMultiple: true);
                }

                if (selectedFiles != null && selectedFiles.Count > 0)
                {
                    await AddFilesAsync(selectedFiles);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Fujify] Error in ExecuteAddFilesAsync: {ex}");
                await _dialogService.ShowMessageAsync(OwnerWindow, "Import Error", $"Failed to select files: {ex.Message}");
            }
        }

        private async Task ExecuteAddFolderAsync()
        {
            string? selectedFolder = await _dialogService.ShowFolderBrowserDialogAsync(OwnerWindow, "Select folder containing RAW pictures");
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
            CrossPlatformLauncher.OpenPath(item.FilePath);
        }

        private void ExecuteOpenFolder(FileItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;
            string? folderPath = Path.GetDirectoryName(item.FilePath);
            if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
            {
                CrossPlatformLauncher.OpenFolder(folderPath);
            }
        }

        private async Task ExecuteOpenPreferencesAsync()
        {
            await _dialogService.ShowPreferencesDialogAsync(OwnerWindow);
        }

        private async Task ExecuteOpenAboutAsync()
        {
            await _dialogService.ShowAboutDialogAsync(OwnerWindow);
        }

        private void ExecuteOpenHelp()
        {
            CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujify");
        }

        private void ExecuteOpenUpdate()
        {
            CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujify/releases");
        }

        private void ExecuteReportBug()
        {
            CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujify/issues");
        }

        #endregion
    }
}
