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
            ".dng", ".cr2", ".cr3", ".crw", ".erf", ".raf", ".3fr", ".kdc", ".dcs", ".dcr", ".iiq", ".mos",
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
                if (_selectedFile == null && Files.Count > 0)
                {
                    SelectedFile = Files[0];
                }
                OnPropertyChanged(nameof(HasFiles));
                OnPropertyChanged(nameof(CanProcess));
                OnPropertyChanged(nameof(CanClear));
                OnPropertyChanged(nameof(CanProcessOrStop));
                OnPropertyChanged(nameof(OverallStatusText));
                OnPropertyChanged(nameof(ProgressSummaryText));
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

            RemoveFileCommand = new RelayCommand<object>(param =>
            {
                if (param is System.Collections.IList list && list.Count > 0)
                {
                    RemoveFiles(list.OfType<FileItem>());
                }
                else if (param is FileItem file && Files.Contains(file))
                {
                    RemoveFiles(new[] { file });
                }
                else if (SelectedFile != null)
                {
                    RemoveFiles(new[] { SelectedFile });
                }
            });

            SelectFileCommand = new RelayCommand<FileItem>(file =>
            {
                if (file != null)
                {
                    SelectedFile = file;
                }
            });

            SetListViewCommand = new RelayCommand(() => IsGridView = false);
            SetGridViewCommand = new RelayCommand(() => IsGridView = true);
            ShowPropertiesCommand = new RelayCommand<FileItem>(file =>
            {
                if (file != null)
                {
                    SelectedFile = file;
                    CrossPlatformLauncher.ShowFileProperties(file.FilePath);
                }
            });
            OpenWithCommand = new RelayCommand<FileItem>(ExecuteOpenWith);
            ResetStatusCommand = new RelayCommand<object>(ExecuteResetStatus);

            OpenOutputFolderCommand = new RelayCommand(() =>
            {
                string folder = !string.IsNullOrEmpty(OutputFolderPath) && Directory.Exists(OutputFolderPath)
                    ? OutputFolderPath
                    : (Files.Count > 0 ? (Path.GetDirectoryName(Files[0].FilePath) ?? "") : "");
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    CrossPlatformLauncher.OpenFolder(folder);
                }
            });
            ClearOutputFolderCommand = new RelayCommand(() => OutputFolderPath = null);

            OpenCameraPickerCommand = new RelayCommand(() =>
            {
                // X-T5 is the standard unified simulation target profile
            });

            SelectCameraModelCommand = new RelayCommand<string>(model =>
            {
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
        public ICommand SetListViewCommand { get; }
        public ICommand SetGridViewCommand { get; }
        public ICommand ShowPropertiesCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }
        public ICommand ClearOutputFolderCommand { get; }
        public ICommand OpenWithCommand { get; }
        public ICommand ResetStatusCommand { get; }

        private bool _isDragOver;
        public bool IsDragOver
        {
            get => _isDragOver;
            set => SetProperty(ref _isDragOver, value);
        }

        private bool _isGridView;
        public bool IsGridView
        {
            get => _isGridView;
            set
            {
                if (SetProperty(ref _isGridView, value))
                {
                    OnPropertyChanged(nameof(IsListView));
                }
            }
        }
        public bool IsListView => !_isGridView;

        private FileItem? _selectedFile;
        public FileItem? SelectedFile
        {
            get
            {
                if (_selectedFile == null && Files.Count > 0)
                {
                    _selectedFile = Files[0];
                    if (!_selectedFile.HasExifLoaded)
                    {
                        LoadExifForFile(_selectedFile);
                    }
                }
                return _selectedFile;
            }
            set
            {
                if (SetProperty(ref _selectedFile, value))
                {
                    OnPropertyChanged(nameof(HasSelectedFile));
                    if (value != null && !value.HasExifLoaded)
                    {
                        LoadExifForFile(value);
                    }
                }
            }
        }

        public bool HasSelectedFile => SelectedFile != null;

        private void LoadExifForFile(FileItem item)
        {
            Task.Run(() =>
            {
                try
                {
                    var exif = _metadataService.GetExifData(item.FilePath);
                    if (exif != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            item.Lens = exif.Lens;
                            item.Aperture = exif.Aperture;
                            item.ShutterSpeed = exif.ShutterSpeed;
                            item.Iso = exif.Iso;
                            item.FocalLength = exif.FocalLength;
                            item.DateTimeOriginal = exif.DateTimeOriginal;
                            item.Orientation = exif.Orientation;
                            item.HasExifLoaded = true;
                        });
                    }
                    else
                    {
                        item.HasExifLoaded = true;
                    }
                }
                catch
                {
                    item.HasExifLoaded = true;
                }
            });
        }

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
                    OnPropertyChanged(nameof(SuffixLabelContent));
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
                    OnPropertyChanged(nameof(SuffixLabelContent));
                }
            }
        }

        public string SuffixPreview => AppendSuffix && !string.IsNullOrWhiteSpace(Suffix)
            ? $"DSC0001{Suffix.Trim()}.dng"
            : "DSC0001.dng";

        public string SuffixLabelContent => $"Add Suffix ({(string.IsNullOrWhiteSpace(Suffix) ? "-fujified" : Suffix.Trim())})";

        public bool AutoOpenInLightroom
        {
            get => _settingsService.Settings.AutoOpenInLightroom;
            set
            {
                if (_settingsService.Settings.AutoOpenInLightroom != value)
                {
                    _settingsService.Settings.AutoOpenInLightroom = value;
                    _settingsService.Save();
                    OnPropertyChanged();
                    if (_preferences != null)
                    {
                        _preferences.AutoOpenInLightroom = value;
                    }
                }
            }
        }

        public bool LosslessCompression
        {
            get => _settingsService.Settings.LosslessCompression;
            set
            {
                if (_settingsService.Settings.LosslessCompression != value)
                {
                    _settingsService.Settings.LosslessCompression = value;
                    _settingsService.Save();
                    OnPropertyChanged();
                    if (_preferences != null)
                    {
                        _preferences.LosslessCompression = value;
                    }
                }
            }
        }

        public string OverallStatusText
        {
            get
            {
                if (IsProcessing)
                {
                    int done = Files.Count(f => f.Status == "✔" || (f.Status != null && f.Status != "Pending"));
                    return $"Processing photo {Math.Min(done + 1, Files.Count)} of {Files.Count}...";
                }
                if (!HasFiles)
                {
                    return "Ready - Drop RAW photos or click + Add Files to begin";
                }
                int pendingCount = Files.Count(f => f.Status == "Pending");
                int successCount = Files.Count(f => f.Status == "✔");
                if (successCount > 0 && pendingCount == 0)
                {
                    return $"All {Files.Count} photos converted successfully ✔";
                }
                return $"{Files.Count} photo{(Files.Count > 1 ? "s" : "")} in queue ({pendingCount} pending)";
            }
        }

        public string ProgressSummaryText
        {
            get
            {
                if (!HasFiles) return string.Empty;
                if (IsProcessing || ProgressValue > 0)
                {
                    int done = Files.Count(f => f.Status == "✔");
                    return $"{done} / {Files.Count} completed";
                }
                long totalBytes = Files.Sum(f => f.FileSizeBytes);
                if (totalBytes > 0)
                {
                    if (totalBytes >= 1024L * 1024 * 1024)
                        return $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
                    if (totalBytes >= 1024 * 1024)
                        return $"{totalBytes / (1024.0 * 1024.0):F1} MB";
                    if (totalBytes >= 1024)
                        return $"{totalBytes / 1024.0:F1} KB";
                    return $"{totalBytes} B";
                }
                return $"{Files.Count} items";
            }
        }

        public void RefreshSettings()
        {
            OnPropertyChanged(nameof(AppendSuffix));
            OnPropertyChanged(nameof(Suffix));
            OnPropertyChanged(nameof(SuffixPreview));
            OnPropertyChanged(nameof(SuffixLabelContent));
            OnPropertyChanged(nameof(AutoOpenInLightroom));
            OnPropertyChanged(nameof(LosslessCompression));
            OnPropertyChanged(nameof(OverallStatusText));
            OnPropertyChanged(nameof(ProgressSummaryText));
        }

        public void UpdateLayoutMode(double width, double height)
        {
            IsPhoneLayout = width < 650;
            IsTabletLayout = width >= 650 && width < 900;
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
            set
            {
                if (SetProperty(ref _isModalOpen, value))
                {
                    OnPropertyChanged(nameof(IsBottomSheetOpen));
                    OnPropertyChanged(nameof(IsPreferencesModal));
                    OnPropertyChanged(nameof(IsAboutModal));
                    OnPropertyChanged(nameof(IsCameraPickerModal));
                    OnPropertyChanged(nameof(IsConflictModal));
                    OnPropertyChanged(nameof(IsMessageModal));
                }
            }
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
                    OnPropertyChanged(nameof(IsBottomSheetOpen));
                }
            }
        }

        public bool IsCameraPickerModal => _modalType == "CameraPicker";
        public bool IsConflictModal => _modalType == "Conflict";
        public bool IsPreferencesModal => _modalType == "Preferences" && _isModalOpen;
        public bool IsAboutModal => _modalType == "About" && _isModalOpen;
        public bool IsMessageModal => _modalType == "Message";
        public bool IsBottomSheetOpen => _isModalOpen && (IsCameraPickerModal || IsConflictModal || IsMessageModal);

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
                    CloseAction = ok =>
                    {
                        if (ok)
                        {
                            RefreshSettings();
                        }
                        CloseModal();
                    }
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
                    OnPropertyChanged(nameof(HasCustomOutputFolder));
                }
            }
        }

        public bool HasCustomOutputFolder => !string.IsNullOrWhiteSpace(_outputFolderPath);

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
                    OnPropertyChanged(nameof(OverallStatusText));
                    OnPropertyChanged(nameof(ProgressSummaryText));
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
            set
            {
                if (SetProperty(ref _progressValue, value))
                {
                    OnPropertyChanged(nameof(OverallStatusText));
                    OnPropertyChanged(nameof(ProgressSummaryText));
                }
            }
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

                var convResult = await Task.Run(() => _rawConverterService.ConvertToDng(
                    filePath,
                    embedOriginalRaw,
                    targetDngForConversion,
                    _settingsService.Settings.LosslessCompression), ct);
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
                    OnPropertyChanged(nameof(OverallStatusText));
                    OnPropertyChanged(nameof(ProgressSummaryText));
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

        private void ExecuteOpenWith(FileItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;
            CrossPlatformLauncher.OpenWith(item.FilePath);
        }

        private void ExecuteResetStatus(object? param)
        {
            if (param is System.Collections.IList list && list.Count > 0)
            {
                foreach (var item in list.OfType<FileItem>())
                {
                    item.Status = "Pending";
                }
            }
            else if (param is FileItem single)
            {
                single.Status = "Pending";
            }
            else if (SelectedFile != null)
            {
                SelectedFile.Status = "Pending";
            }

            OnPropertyChanged(nameof(CanProcess));
            OnPropertyChanged(nameof(CanProcessOrStop));
            OnPropertyChanged(nameof(OverallStatusText));
        }

        public void RemoveFiles(IEnumerable<FileItem> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            foreach (var file in list)
            {
                Files.Remove(file);
            }
            if (_selectedFile != null && !Files.Contains(_selectedFile))
            {
                SelectedFile = Files.FirstOrDefault();
            }
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(CanClear));
            OnPropertyChanged(nameof(CanProcess));
            OnPropertyChanged(nameof(CanProcessOrStop));
            OnPropertyChanged(nameof(OverallStatusText));
            OnPropertyChanged(nameof(ProgressSummaryText));
        }

        private async Task ExecuteOpenPreferencesAsync()
        {
            if (IsPhoneLayout || Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime)
            {
                ShowPreferencesModal();
            }
            else
            {
                await _dialogService.ShowPreferencesDialogAsync(OwnerWindow);
            }
            RefreshSettings();
        }

        private async Task ExecuteOpenAboutAsync()
        {
            if (IsPhoneLayout || Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime)
            {
                ShowAboutModal();
            }
            else
            {
                await _dialogService.ShowAboutDialogAsync(OwnerWindow);
            }
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
