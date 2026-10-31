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
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fujify.Common;
using Fujify.Localization;
using Fujify.Models;
using Fujify.Services;
using Fujify.Views;

namespace Fujify.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IRawConverterService _rawConverterService;
        private readonly IMetadataService _metadataService;
        private readonly IThumbnailService _thumbnailService;
        private readonly IDialogService _dialogService;
        private readonly ISettingsService _settingsService;
        private readonly ILicenseService _licenseService;
        private readonly IUpdateService _updateService;
        private readonly IAdService _adService;

        public bool IsPro => _licenseService.IsPro;
        public ILicenseService LicenseService => _licenseService;

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
        private bool _isStopping;
        public bool SuppressBackupConfirmation { get; set; }

        public Window? OwnerWindow { get; set; }

        public ObservableCollection<FileItem> Files { get; } = new();

        public event Action? ProcessingCompleted;

        public MainViewModel(
            IRawConverterService? rawConverterService = null,
            IMetadataService? metadataService = null,
            IThumbnailService? thumbnailService = null,
            IDialogService? dialogService = null,
            ISettingsService? settingsService = null,
            ILicenseService? licenseService = null,
            IUpdateService? updateService = null,
            IAdService? adService = null)
        {
            _rawConverterService = rawConverterService ?? new RawConverterService();
            _metadataService = metadataService ?? new MetadataService();
            _thumbnailService = thumbnailService ?? new ThumbnailService();
            _settingsService = settingsService ?? new SettingsService();
            _licenseService = licenseService ?? Services.LicenseService.Instance ?? new Services.LicenseService(_settingsService);
            _dialogService = dialogService ?? new DialogService(_settingsService, _licenseService);
            _updateService = updateService ?? new UpdateService(_settingsService);
            _adService = adService ?? new AdService(_licenseService);
            _createBackup = _settingsService.Settings.BackupOriginalFile;
            Instance = this;

            void OnLicenseUpdated(LicenseStatus status)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    OnPropertyChanged(nameof(IsPro));
                    OnPropertyChanged(nameof(LosslessCompression));
                    OnPropertyChanged(nameof(CanProcess));
                    OnPropertyChanged(nameof(CanClear));
                    OnPropertyChanged(nameof(CanProcessOrStop));
                    OnPropertyChanged(nameof(OverallStatusText));
                    OnPropertyChanged(nameof(ProgressSummaryText));
                    OnPropertyChanged(nameof(AvatarInitials));
                    OnPropertyChanged(nameof(LicenseService));
                    (ClearCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                    (ProcessCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();

                    if (OperatingSystem.IsWindows())
                    {
                        if (_settingsService.Settings.EnableWindowsContextMenu && status.IsPro)
                        {
                            WindowsContextMenuService.RegisterContextMenu();
                        }
                        else if (!status.IsPro && WindowsContextMenuService.IsContextMenuRegistered())
                        {
                            WindowsContextMenuService.UnregisterContextMenu();
                        }
                    }
                });
            }

            _licenseService.LicenseChanged += (s, status) => OnLicenseUpdated(status);
            Services.LicenseService.AnyLicenseChanged += (s, status) => OnLicenseUpdated(status);

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
                OnPropertyChanged(nameof(ActiveStep));
                OnPropertyChanged(nameof(IsStep1Current));
                OnPropertyChanged(nameof(IsStep2Current));
                OnPropertyChanged(nameof(IsStep3Current));
                OnPropertyChanged(nameof(IsStep4Current));
                (ClearCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (ProcessCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            };
            DeleteSelectedFilesCommand = new RelayCommand<System.Collections.IList>(ExecuteDeleteSelectedFiles);
            OpenFileCommand = new RelayCommand<FileItem>(ExecuteOpenFile);
            OpenFolderCommand = new RelayCommand<FileItem>(ExecuteOpenFolder);
            OpenPreferencesCommand = new AsyncRelayCommand(ExecuteOpenPreferencesAsync);
            OpenAboutCommand = new AsyncRelayCommand(ExecuteOpenAboutAsync);
            OpenHelpCommand = new RelayCommand(ExecuteOpenHelp);
            OpenUpdateCommand = new AsyncRelayCommand(ExecuteOpenUpdateAsync);
            ReportBugCommand = new RelayCommand(ExecuteReportBug);
            ToggleQuickSettingsCommand = new RelayCommand(() => IsQuickSettingsExpanded = !IsQuickSettingsExpanded);
            _isQuickSettingsExpanded = _settingsService.Settings.IsQuickSettingsExpanded;

            SelectInspectorSettingsTabCommand = new RelayCommand(() => InspectorTabIndex = 0);
            SelectInspectorInfoTabCommand = new RelayCommand(() =>
            {
                InspectorTabIndex = 1;
                if (SelectedFile == null && Files.Count > 0)
                {
                    SelectedFile = Files[0];
                }
            });
            ToggleMobileSidebarCommand = new RelayCommand(() =>
            {
                IsMobileAddMenuOpen = false;
                IsMobileSidebarOpen = !IsMobileSidebarOpen;
            });
            CloseMobileSidebarCommand = new RelayCommand(() => IsMobileSidebarOpen = false);
            OpenMobileSidebarCommand = new RelayCommand(() =>
            {
                IsMobileAddMenuOpen = false;
                IsMobileSidebarOpen = true;
            });
            ToggleMobileAddMenuCommand = new RelayCommand(() => IsMobileAddMenuOpen = !IsMobileAddMenuOpen);
            CloseMobileAddMenuCommand = new RelayCommand(() => IsMobileAddMenuOpen = false);
            MobileAddFilesCommand = new AsyncRelayCommand(async () =>
            {
                IsMobileAddMenuOpen = false;
                await ExecuteAddFilesAsync();
            });
            MobileAddFolderCommand = new AsyncRelayCommand(async () =>
            {
                IsMobileAddMenuOpen = false;
                await ExecuteAddFolderAsync();
            });
            ToggleQueueFabMenuCommand = new RelayCommand(() => IsQueueFabMenuOpen = !IsQueueFabMenuOpen);
            CloseQueueFabMenuCommand = new RelayCommand(() => IsQueueFabMenuOpen = false);

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

            OpenOutputFolderCommand = new RelayCommand(ExecuteOpenOutputFolder);
            ClearOutputFolderCommand = new RelayCommand(() => OutputFolderPath = null);

            CloseModalCommand = new RelayCommand(CloseModal);
            ClearQueueAndCloseModalCommand = new AsyncRelayCommand(async () =>
            {
                CloseModal();
                await ExecuteClearAsync();
            });

            OpenWebsiteCommand = new RelayCommand(() => CrossPlatformLauncher.OpenUrl("https://fujifier.cc"));
            OpenGitHubCommand = new RelayCommand(() => CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujify"));
            OpenReleasesCommand = new RelayCommand(() => CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujify/releases"));
            BuyProCommand = new RelayCommand(() => CrossPlatformLauncher.OpenUrl("https://fujifier.cc/pro"));
            DelayGoogleSignInCommand = new AsyncRelayCommand(ExecuteDelayGoogleSignInAsync);
            DelayToggleKeyCommand = new RelayCommand(() => IsDelayKeyInputVisible = !IsDelayKeyInputVisible);
            DelayActivateKeyCommand = new AsyncRelayCommand(ExecuteDelayActivateKeyAsync);

            ConfirmModalActionCommand = new RelayCommand(() => ResolveConfirmation(true));
            CancelModalActionCommand = new RelayCommand(() => ResolveConfirmation(false));

            LocalizationManager.Instance.LanguageChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(Loc));
                OnPropertyChanged(nameof(OverallStatusText));
                OnPropertyChanged(nameof(ProgressSummaryText));
                OnPropertyChanged(nameof(OutputDestinationSummary));
                OnPropertyChanged(nameof(FormattedOutputFolderPath));
                OnPropertyChanged(nameof(ProcessButtonContent));
                OnPropertyChanged(nameof(QuickSettingsSummary));
                OnPropertyChanged(nameof(SuffixPreview));
                OnPropertyChanged(nameof(SubfolderPreview));
                OnPropertyChanged(nameof(ActiveStep));
                foreach (var file in Files)
                {
                    file.NotifyLanguageChanged();
                }
            };

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
        public bool IsOpenInLightroomSupported => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
        public bool CanOpenInLightroom => IsOpenInLightroomSupported && HasRecentlyConvertedFiles && (IsBatchSummaryModal || ModalTitle == "Processing completed" || ModalTitle == LocalizationManager.Tr("BatchCompleteTitle"));
        public bool CanOpenOutputFolder => !OperatingSystem.IsIOS() && !OperatingSystem.IsAndroid() && HasRecentlyConvertedFiles && (ModalTitle == "Processing completed" || IsBatchSummaryModal);
        public LocalizationManager Loc => LocalizationManager.Instance;

        #region Properties

        public ICommand RemoveFileCommand { get; }
        public ICommand SelectFileCommand { get; }
        public ICommand CloseModalCommand { get; }
        public IRelayCommand ConfirmModalActionCommand { get; }
        public IRelayCommand CancelModalActionCommand { get; }
        public ICommand ClearQueueAndCloseModalCommand { get; }
        public ICommand OpenGitHubCommand { get; }
        public ICommand OpenReleasesCommand { get; }
        public ICommand BuyProCommand { get; }
        public ICommand DelayGoogleSignInCommand { get; }
        public ICommand DelayToggleKeyCommand { get; }
        public ICommand DelayActivateKeyCommand { get; }
        public ICommand OpenInLightroomCommand { get; }
        public ICommand InstallLightroomPresetsCommand => Preferences.InstallLightroomPresetsCommand;
        public ICommand ExportLightroomPresetsCommand => Preferences.ExportLightroomPresetsCommand;
        public ICommand ResolveConflictCommand { get; }
        public ICommand SetListViewCommand { get; }
        public ICommand SetGridViewCommand { get; }
        public ICommand ShowPropertiesCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }
        public ICommand ClearOutputFolderCommand { get; }
        public ICommand OpenWithCommand { get; }
        public ICommand ResetStatusCommand { get; }
        public ICommand ToggleQuickSettingsCommand { get; }
        public ICommand SelectInspectorSettingsTabCommand { get; }
        public ICommand SelectInspectorInfoTabCommand { get; }
        public ICommand ToggleMobileSidebarCommand { get; }
        public ICommand CloseMobileSidebarCommand { get; }
        public ICommand OpenMobileSidebarCommand { get; }
        public ICommand ToggleMobileAddMenuCommand { get; }
        public ICommand CloseMobileAddMenuCommand { get; }
        public ICommand MobileAddFilesCommand { get; }
        public ICommand MobileAddFolderCommand { get; }
        public IRelayCommand ToggleQueueFabMenuCommand { get; }
        public IRelayCommand CloseQueueFabMenuCommand { get; }

        private bool _isQueueFabMode;
        public bool IsQueueFabMode
        {
            get => _isQueueFabMode;
            set
            {
                if (SetProperty(ref _isQueueFabMode, value))
                {
                    if (!value)
                    {
                        IsQueueFabMenuOpen = false;
                    }
                }
            }
        }

        private bool _isQueueFabMenuOpen;
        public bool IsQueueFabMenuOpen
        {
            get => _isQueueFabMenuOpen;
            set => SetProperty(ref _isQueueFabMenuOpen, value);
        }

        private bool _isMobileAddMenuOpen;
        public bool IsMobileAddMenuOpen
        {
            get => _isMobileAddMenuOpen;
            set
            {
                if (SetProperty(ref _isMobileAddMenuOpen, value))
                {
                    OnPropertyChanged(nameof(MobileAddMenuAngle));
                }
            }
        }

        public double MobileAddMenuAngle => _isMobileAddMenuOpen ? 45.0 : 0.0;

        private bool _isMobileSidebarOpen;
        public bool IsMobileSidebarOpen
        {
            get => _isMobileSidebarOpen;
            set
            {
                if (SetProperty(ref _isMobileSidebarOpen, value))
                {
                    OnPropertyChanged(nameof(MobileSidebarChevron));
                }
            }
        }

        public string MobileSidebarChevron => _isMobileSidebarOpen ? "▼" : "▲";

        private int _inspectorTabIndex = 0; // 0 = Settings, 1 = Photo info
        public int InspectorTabIndex
        {
            get => _inspectorTabIndex;
            set
            {
                if (SetProperty(ref _inspectorTabIndex, value))
                {
                    OnPropertyChanged(nameof(IsInspectorSettingsTab));
                    OnPropertyChanged(nameof(IsInspectorInfoTab));
                }
            }
        }
        public bool IsInspectorSettingsTab => InspectorTabIndex == 0;
        public bool IsInspectorInfoTab => InspectorTabIndex == 1;

        public string AvatarInitials
        {
            get
            {
                if (_licenseService?.CurrentStatus?.IsPro == true) return "PRO";
                string? email = _licenseService?.CurrentStatus?.AccountEmail;
                if (!string.IsNullOrWhiteSpace(email) && email.Length >= 2)
                {
                    return email.Substring(0, 2).ToUpperInvariant();
                }
                return "FU";
            }
        }

        public string OutputDestinationSummary
        {
            get
            {
                if (CreateSubfolder)
                {
                    string folderName = SubfolderUseOriginalName ? $"(original){SubfolderSuffix}" : SubfolderCustomName;
                    return LocalizationManager.TrFormat("SubfolderPrefix", folderName);
                }
                if (!string.IsNullOrEmpty(_outputFolderPath))
                {
                    return _outputFolderPath;
                }
                return LocalizationManager.Tr("SameFolderAsOriginals");
            }
        }

        public string ActiveStep
        {
            get
            {
                if (Files.Count == 0) return "1";
                if (IsProcessing) return "3";
                if (Files.Count > 0 && Files.All(f => f.IsCompleted)) return "4";
                return "2";
            }
        }
        public bool IsStep1Current => ActiveStep == "1";
        public bool IsStep2Current => ActiveStep == "2";
        public bool IsStep3Current => ActiveStep == "3";
        public bool IsStep4Current => ActiveStep == "4";

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
                    _selectedFile.IsSelected = true;
                    if (!_selectedFile.HasExifLoaded)
                    {
                        LoadExifForFile(_selectedFile);
                    }
                }
                return _selectedFile;
            }
            set
            {
                var previous = _selectedFile;
                if (SetProperty(ref _selectedFile, value))
                {
                    if (previous != null)
                    {
                        previous.IsSelected = false;
                    }
                    if (value != null)
                    {
                        value.IsSelected = true;
                    }
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
            set => SetProperty(ref _targetCameraModel, value);
        }

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

        public bool ShowCaptionButtons =>
            !OperatingSystem.IsAndroid() &&
            !OperatingSystem.IsIOS() &&
            !(Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime);

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
                    OnPropertyChanged(nameof(QuickSettingsSummary));
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
                    OnPropertyChanged(nameof(QuickSettingsSummary));
                }
            }
        }

        public string SuffixPreview => AppendSuffix && !string.IsNullOrWhiteSpace(Suffix)
            ? $"DSC0001{Suffix.Trim()}.dng"
            : "DSC0001.dng";

        public string SuffixLabelContent => $"Add Suffix ({(string.IsNullOrWhiteSpace(Suffix) ? "-fujified" : Suffix.Trim())})";

        public bool CreateSubfolder
        {
            get => _settingsService.Settings.CreateSubfolder;
            set
            {
                if (_settingsService.Settings.CreateSubfolder != value)
                {
                    _settingsService.Settings.CreateSubfolder = value;
                    _settingsService.Save();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SubfolderPreview));
                    OnPropertyChanged(nameof(QuickSettingsSummary));
                    OnPropertyChanged(nameof(OutputDestinationSummary));
                    if (_preferences != null)
                    {
                        _preferences.CreateSubfolder = value;
                    }
                }
            }
        }

        public bool SubfolderUseOriginalName
        {
            get => _settingsService.Settings.SubfolderUseOriginalName;
            set
            {
                if (_settingsService.Settings.SubfolderUseOriginalName != value)
                {
                    _settingsService.Settings.SubfolderUseOriginalName = value;
                    _settingsService.Save();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SubfolderPreview));
                    OnPropertyChanged(nameof(QuickSettingsSummary));
                    OnPropertyChanged(nameof(OutputDestinationSummary));
                    if (_preferences != null)
                    {
                        _preferences.SubfolderUseOriginalName = value;
                    }
                }
            }
        }

        public string SubfolderSuffix
        {
            get => _settingsService.Settings.SubfolderSuffix;
            set
            {
                if (_settingsService.Settings.SubfolderSuffix != value)
                {
                    _settingsService.Settings.SubfolderSuffix = value;
                    _settingsService.Save();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SubfolderPreview));
                    OnPropertyChanged(nameof(QuickSettingsSummary));
                    OnPropertyChanged(nameof(OutputDestinationSummary));
                    if (_preferences != null)
                    {
                        _preferences.SubfolderSuffix = value;
                    }
                }
            }
        }

        public string SubfolderCustomName
        {
            get => _settingsService.Settings.SubfolderCustomName;
            set
            {
                if (_settingsService.Settings.SubfolderCustomName != value)
                {
                    _settingsService.Settings.SubfolderCustomName = value;
                    _settingsService.Save();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SubfolderPreview));
                    OnPropertyChanged(nameof(QuickSettingsSummary));
                    OnPropertyChanged(nameof(OutputDestinationSummary));
                    if (_preferences != null)
                    {
                        _preferences.SubfolderCustomName = value;
                    }
                }
            }
        }

        public string SubfolderPreview
        {
            get
            {
                if (!CreateSubfolder) return string.Empty;

                string folderSample = Files.Count > 0
                    ? (Path.GetFileName(Path.GetDirectoryName(Files[0].FilePath)) ?? "Photos")
                    : "Photos";
                if (string.IsNullOrWhiteSpace(folderSample)) folderSample = "Photos";

                string subfolderName = SubfolderUseOriginalName
                    ? $"{folderSample}{(string.IsNullOrEmpty(SubfolderSuffix) ? "" : SubfolderSuffix)}"
                    : (string.IsNullOrWhiteSpace(SubfolderCustomName) ? "Fujified" : SubfolderCustomName);

                return $"{folderSample}/{subfolderName}/";
            }
        }

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
                    OnPropertyChanged(nameof(QuickSettingsSummary));
                }
            }
        }

        public bool AutoOpenOutputFolder
        {
            get => _settingsService.Settings.AutoOpenOutputFolder;
            set
            {
                if (_settingsService.Settings.AutoOpenOutputFolder != value)
                {
                    _settingsService.Settings.AutoOpenOutputFolder = value;
                    _settingsService.Save();
                    OnPropertyChanged();
                    if (_preferences != null)
                    {
                        _preferences.AutoOpenOutputFolder = value;
                    }
                    OnPropertyChanged(nameof(QuickSettingsSummary));
                }
            }
        }

        private bool _isQuickSettingsExpanded = true;
        public bool IsQuickSettingsExpanded
        {
            get => _isQuickSettingsExpanded;
            set
            {
                if (SetProperty(ref _isQuickSettingsExpanded, value))
                {
                    _settingsService.Settings.IsQuickSettingsExpanded = value;
                    _settingsService.Save();
                }
            }
        }

        public string QuickSettingsSummary
        {
            get
            {
                var parts = new List<string>();
                if (CreateBackup) parts.Add("Backup");
                if (AppendSuffix) parts.Add("Suffix");
                if (CreateSubfolder) parts.Add("Subfolder");
                if (AutoOpenOutputFolder) parts.Add("Open Folder");
                if (IsOpenInLightroomSupported && AutoOpenInLightroom) parts.Add("Lightroom");
                return parts.Count > 0 ? string.Join(" • ", parts) : "Default";
            }
        }

        public void ShowSplashAd() => _adService.ShowSplashAd();

        public bool LosslessCompression
        {
            get => _licenseService.IsPro && _settingsService.Settings.LosslessCompression;
            set
            {
                if (value && !_licenseService.IsPro)
                {
                    OnPropertyChanged(nameof(LosslessCompression));
                    return;
                }

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
                if (IsImporting)
                {
                    return !string.IsNullOrEmpty(ImportStatusText) ? ImportStatusText : LocalizationManager.Tr("ImportingPhotos");
                }
                if (IsProcessing)
                {
                    int done = Files.Count(f => f.Status == "✔" || (f.Status != null && f.Status != "Pending"));
                    return LocalizationManager.TrFormat("ProcessingPhotoCount", Math.Min(done + 1, Files.Count), Files.Count);
                }
                if (!HasFiles)
                {
                    return LocalizationManager.Tr("Ready");
                }
                int pendingCount = Files.Count(f => f.Status == "Pending");
                int successCount = Files.Count(f => f.Status == "✔");
                if (successCount > 0 && pendingCount == 0)
                {
                    return LocalizationManager.TrFormat("AllPhotosConvertedSuccess", Files.Count);
                }
                if (Files.Count == 1)
                {
                    return LocalizationManager.TrFormat("PhotoInQueueSingle", Files.Count, pendingCount);
                }
                return LocalizationManager.TrFormat("PhotosInQueue", Files.Count, pendingCount);
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
            _licenseService.SyncFromSettings();
            _createBackup = _settingsService.Settings.BackupOriginalFile;
            OnPropertyChanged(nameof(CreateBackup));
            OnPropertyChanged(nameof(IsPro));
            OnPropertyChanged(nameof(LosslessCompression));
            OnPropertyChanged(nameof(AvatarInitials));
            OnPropertyChanged(nameof(CanProcess));
            OnPropertyChanged(nameof(CanClear));
            OnPropertyChanged(nameof(CanProcessOrStop));
            OnPropertyChanged(nameof(AppendSuffix));
            OnPropertyChanged(nameof(Suffix));
            OnPropertyChanged(nameof(SuffixPreview));
            OnPropertyChanged(nameof(SuffixLabelContent));
            OnPropertyChanged(nameof(CreateSubfolder));
            OnPropertyChanged(nameof(SubfolderUseOriginalName));
            OnPropertyChanged(nameof(SubfolderSuffix));
            OnPropertyChanged(nameof(SubfolderCustomName));
            OnPropertyChanged(nameof(SubfolderPreview));
            OnPropertyChanged(nameof(AutoOpenInLightroom));
            OnPropertyChanged(nameof(IsOpenInLightroomSupported));
            OnPropertyChanged(nameof(AutoOpenOutputFolder));
            OnPropertyChanged(nameof(OverallStatusText));
            OnPropertyChanged(nameof(ProgressSummaryText));
            (ClearCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            (ProcessCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        }

        public void UpdateLayoutMode(double width, double height)
        {
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0) return;

            IsPhoneLayout = width < 650;
            IsTabletLayout = width >= 650 && width < 900;
            IsQueueFabMode = width < 920;
            if (!IsPhoneLayout)
            {
                IsMobileSidebarOpen = false;
            }
            if (!IsQueueFabMode)
            {
                IsQueueFabMenuOpen = false;
            }
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
                    OnPropertyChanged(nameof(IsFreeTierDelayModal));
                    OnPropertyChanged(nameof(IsBatchSummaryModal));
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
                    OnPropertyChanged(nameof(IsFreeTierDelayModal));
                    OnPropertyChanged(nameof(IsConfirmationModal));
                    OnPropertyChanged(nameof(IsBatchSummaryModal));
                    OnPropertyChanged(nameof(IsBottomSheetOpen));
                }
            }
        }

        public bool IsCameraPickerModal => _modalType == "CameraPicker";
        public bool IsConflictModal => _modalType == "Conflict";
        public bool IsPreferencesModal => _modalType == "Preferences" && _isModalOpen;
        public bool IsAboutModal => _modalType == "About" && _isModalOpen;
        public bool IsMessageModal => _modalType == "Message";
        public bool IsConfirmationModal => _modalType == "Confirmation";
        public bool IsFreeTierDelayModal => _modalType == "FreeTierDelay";
        public bool IsBatchSummaryModal => _modalType == "BatchSummary";
        public bool IsBottomSheetOpen => _isModalOpen && (IsCameraPickerModal || IsConflictModal || IsMessageModal || IsConfirmationModal || IsFreeTierDelayModal || IsBatchSummaryModal);

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
            get => _preferences ??= new PreferencesViewModel(_settingsService, _licenseService, updateService: null, dialogService: _dialogService);
            set => SetProperty(ref _preferences, value);
        }

        private string _modalIcon = "✔";
        public string ModalIcon
        {
            get => _modalIcon;
            set => SetProperty(ref _modalIcon, value);
        }

        private IBrush _modalIconColor = new ImmutableSolidColorBrush(Color.Parse("#00C781"));
        public IBrush ModalIconColor
        {
            get => _modalIconColor;
            set => SetProperty(ref _modalIconColor, value);
        }

        private IBrush _modalBadgeBackground = new ImmutableSolidColorBrush(Color.FromArgb(40, 0, 199, 129));
        public IBrush ModalBadgeBackground
        {
            get => _modalBadgeBackground;
            set => SetProperty(ref _modalBadgeBackground, value);
        }

        public void ShowMessageModal(string title, string message, bool isSuccess = true)
        {
            void Action()
            {
                ModalTitle = title;
                ModalMessage = message;
                ModalType = "Message";

                if (title.Contains("Free Tier", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Cooldown", StringComparison.OrdinalIgnoreCase))
                {
                    ModalIcon = "⏳";
                    ModalIconColor = new SolidColorBrush(Color.Parse("#3498DB"));
                    ModalBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 52, 152, 219));
                }
                else if (!isSuccess || title.Contains("Fail", StringComparison.OrdinalIgnoreCase) ||
                         title.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    ModalIcon = "✕";
                    ModalIconColor = new SolidColorBrush(Color.Parse("#E74C3C"));
                    ModalBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 231, 76, 60));
                }
                else
                {
                    ModalIcon = "✔";
                    ModalIconColor = new SolidColorBrush(Color.Parse("#00C781"));
                    ModalBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 0, 199, 129));
                }

                OnPropertyChanged(nameof(CanOpenInLightroom));
                OnPropertyChanged(nameof(CanOpenOutputFolder));
                IsModalOpen = true;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Action();
            }
            else
            {
                Dispatcher.UIThread.Post(Action);
            }
        }

        private string _batchSummaryPhotosCountText = string.Empty;
        public string BatchSummaryPhotosCountText
        {
            get => _batchSummaryPhotosCountText;
            set => SetProperty(ref _batchSummaryPhotosCountText, value);
        }

        private string _batchSummaryDurationText = string.Empty;
        public string BatchSummaryDurationText
        {
            get => _batchSummaryDurationText;
            set => SetProperty(ref _batchSummaryDurationText, value);
        }

        private string _batchSummarySpeedText = string.Empty;
        public string BatchSummarySpeedText
        {
            get => _batchSummarySpeedText;
            set => SetProperty(ref _batchSummarySpeedText, value);
        }

        private string _batchSummaryVolumeText = string.Empty;
        public string BatchSummaryVolumeText
        {
            get => _batchSummaryVolumeText;
            set => SetProperty(ref _batchSummaryVolumeText, value);
        }

        private string _batchSummaryBadgeText = string.Empty;
        public string BatchSummaryBadgeText
        {
            get => _batchSummaryBadgeText;
            set => SetProperty(ref _batchSummaryBadgeText, value);
        }

        private string _batchSummaryNoticeText = string.Empty;
        public string BatchSummaryNoticeText
        {
            get => _batchSummaryNoticeText;
            set => SetProperty(ref _batchSummaryNoticeText, value);
        }

        public void ShowBatchSummaryModal(int totalCount, int successCount, TimeSpan duration, long totalBytes, bool hasErrors)
        {
            void Action()
            {
                ModalType = "BatchSummary";
                ModalTitle = LocalizationManager.Tr("BatchCompleteTitle");

                int errorCount = Math.Max(0, totalCount - successCount);

                if (errorCount == 0 && !hasErrors)
                {
                    BatchSummaryPhotosCountText = LocalizationManager.TrFormat("PhotosConvertedCount", successCount, totalCount);
                    BatchSummaryBadgeText = LocalizationManager.Tr("AllPhotosConverted");
                    ModalIcon = "✔";
                    ModalIconColor = new SolidColorBrush(Color.Parse("#00C781"));
                    ModalBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 0, 199, 129));
                    BatchSummaryNoticeText = LocalizationManager.Tr("BatchNoticeSuccess");
                }
                else
                {
                    BatchSummaryPhotosCountText = LocalizationManager.TrFormat("PhotosConvertedWithFailures", successCount, totalCount, errorCount);
                    BatchSummaryBadgeText = LocalizationManager.Tr("CompletedWithWarnings");
                    ModalIcon = "⚠";
                    ModalIconColor = new SolidColorBrush(Color.Parse("#E67E22"));
                    ModalBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 230, 126, 34));
                    BatchSummaryNoticeText = LocalizationManager.Tr("BatchNoticeWarnings");
                }

                if (duration.TotalSeconds < 60)
                {
                    BatchSummaryDurationText = $"{duration.TotalSeconds:F1}s";
                }
                else
                {
                    BatchSummaryDurationText = $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
                }

                double photosPerSec = duration.TotalSeconds > 0 ? successCount / duration.TotalSeconds : successCount;
                BatchSummarySpeedText = $"{photosPerSec:F1} photos/s";

                if (totalBytes >= 1024L * 1024 * 1024)
                {
                    BatchSummaryVolumeText = $"{(totalBytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
                }
                else
                {
                    BatchSummaryVolumeText = $"{(totalBytes / (1024.0 * 1024.0)):F1} MB";
                }

                OnPropertyChanged(nameof(CanOpenOutputFolder));
                IsModalOpen = true;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Action();
            }
            else
            {
                Dispatcher.UIThread.Post(Action);
            }
        }

        public void ShowPreferencesModal()
        {
            void Action()
            {
                Preferences = new PreferencesViewModel(_settingsService, _licenseService, updateService: null, dialogService: _dialogService)
                {
                    CloseAction = ok =>
                    {
                        RefreshSettings();
                        CloseModal();
                    }
                };
                ModalTitle = "Settings";
                ModalType = "Preferences";
                IsMobileAddMenuOpen = false;
                IsModalOpen = true;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Action();
            }
            else
            {
                Dispatcher.UIThread.Post(Action);
            }
        }

        public void ShowAboutModal()
        {
            void Action()
            {
                ModalTitle = "About Fujifier";
                ModalType = "About";
                IsMobileAddMenuOpen = false;
                IsModalOpen = true;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Action();
            }
            else
            {
                Dispatcher.UIThread.Post(Action);
            }
        }

        private TaskCompletionSource<bool>? _confirmationTcs;
        private string _confirmationConfirmText = string.Empty;
        public string ConfirmationConfirmText
        {
            get => _confirmationConfirmText;
            set => SetProperty(ref _confirmationConfirmText, value);
        }

        private string _confirmationCancelText = string.Empty;
        public string ConfirmationCancelText
        {
            get => _confirmationCancelText;
            set => SetProperty(ref _confirmationCancelText, value);
        }

        public Task<bool> ShowConfirmationModalAsync(string title, string message, string? confirmText = null, string? cancelText = null)
        {
            var tcs = new TaskCompletionSource<bool>();
            _confirmationTcs = tcs;

            void Action()
            {
                ModalTitle = string.IsNullOrWhiteSpace(title) ? LocalizationManager.Tr("Warning") : title;
                ModalMessage = message;
                ConfirmationConfirmText = string.IsNullOrWhiteSpace(confirmText) ? LocalizationManager.Tr("Done") : confirmText;
                ConfirmationCancelText = string.IsNullOrWhiteSpace(cancelText) ? LocalizationManager.Tr("Cancel") : cancelText;
                ModalIcon = "⚠";
                ModalIconColor = new SolidColorBrush(Color.Parse("#E74C3C"));
                ModalBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 231, 76, 60));
                ModalType = "Confirmation";
                IsModalOpen = true;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Action();
            }
            else
            {
                Dispatcher.UIThread.Post(Action);
            }

            return tcs.Task;
        }

        public void ResolveConfirmation(bool confirmed)
        {
            IsModalOpen = false;
            ModalType = "None";
            _confirmationTcs?.TrySetResult(confirmed);
            _confirmationTcs = null;
        }

        public Task<FileConflictResolution> ShowConflictModalAsync(string filePath)
        {
            var desktopLifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var singleViewLifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime;
            if (desktopLifetime?.MainWindow == null && singleViewLifetime?.MainView == null)
            {
                return Task.FromResult(new FileConflictResolution(ConflictChoice.Skip, false));
            }

            var tcs = new TaskCompletionSource<FileConflictResolution>();
            _conflictTcs = tcs;

            Dispatcher.UIThread.Post(() =>
            {
                ConflictFilePath = filePath;
                ConflictApplyToAll = false;
                ModalTitle = LocalizationManager.Tr("FileConflictTitle");
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

        private TaskCompletionSource<bool>? _freeTierDelayTcs;
        private DispatcherTimer? _delayTimer;
        private int _delayRemainingSeconds = 3;
        private bool _canDismissModal = true;
        private bool _isDelayUnlocked = false;
        private string _delayStatusText = "Cooldown active: 3s remaining...";
        private string _delayDismissButtonText = "Continue with Free Version (3s)";
        private double _delayProgressBarValue = 0;
        private bool _isDelayKeyInputVisible = false;
        private string _delayLicenseKey = string.Empty;
        private string _delayLicenseError = string.Empty;

        public int DelayTotalSeconds => _licenseService?.DelaySeconds ?? 3;

        public bool CanDismissModal
        {
            get => _canDismissModal;
            set => SetProperty(ref _canDismissModal, value);
        }

        public bool IsDelayUnlocked
        {
            get => _isDelayUnlocked;
            set => SetProperty(ref _isDelayUnlocked, value);
        }

        public int DelayRemainingSeconds
        {
            get => _delayRemainingSeconds;
            set => SetProperty(ref _delayRemainingSeconds, value);
        }

        public double DelayProgressBarValue
        {
            get => _delayProgressBarValue;
            set => SetProperty(ref _delayProgressBarValue, value);
        }

        public string DelayStatusText
        {
            get => _delayStatusText;
            set => SetProperty(ref _delayStatusText, value);
        }

        public string DelayDismissButtonText
        {
            get => _delayDismissButtonText;
            set => SetProperty(ref _delayDismissButtonText, value);
        }

        public bool IsDelayKeyInputVisible
        {
            get => _isDelayKeyInputVisible;
            set => SetProperty(ref _isDelayKeyInputVisible, value);
        }

        public string DelayLicenseKey
        {
            get => _delayLicenseKey;
            set => SetProperty(ref _delayLicenseKey, value);
        }

        public string DelayLicenseError
        {
            get => _delayLicenseError;
            set
            {
                if (SetProperty(ref _delayLicenseError, value))
                {
                    OnPropertyChanged(nameof(HasDelayLicenseError));
                }
            }
        }

        public bool HasDelayLicenseError => !string.IsNullOrWhiteSpace(_delayLicenseError);

        public Task<bool> ShowFreeTierDelayModalAsync(ILicenseService licenseService, bool isPostConversion = false)
        {
            if (licenseService.IsPro)
            {
                return Task.FromResult(true);
            }

            var tcs = new TaskCompletionSource<bool>();
            _freeTierDelayTcs = tcs;

            void Action()
            {
                int totalDelay = licenseService.DelaySeconds;
                ModalTitle = isPostConversion ? "Conversion Finished (Free Tier)" : "Fujifier Free Tier";
                ModalMessage = isPostConversion
                    ? $"Your photos have been processed! Under the Free tier, a {totalDelay}-second cooldown is required after conversions. Upgrade to Fujifier Pro to enjoy instant batch processing, unlimited queues, and all pro features."
                    : $"You are currently using the Free tier of Fujifier. Free version is limited to 10 photos per queue with a {totalDelay}-second cooldown timer. Upgrade to Fujifier Pro to enjoy unlimited batch processing, instant execution, and all pro features!";
                ModalIcon = "⏳";
                ModalType = "FreeTierDelay";
                CanDismissModal = false;
                IsDelayUnlocked = false;
                IsDelayKeyInputVisible = false;
                DelayLicenseKey = string.Empty;
                DelayLicenseError = string.Empty;
                DelayRemainingSeconds = totalDelay;
                DelayProgressBarValue = 0;
                DelayStatusText = LocalizationManager.TrFormat("CooldownActive", totalDelay);
                DelayDismissButtonText = $"{LocalizationManager.Tr("ContinueWithFreeVersion")} ({totalDelay}s)";

                _delayTimer?.Stop();
                _delayTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _delayTimer.Tick += (s, e) =>
                {
                    if (licenseService.IsPro)
                    {
                        _delayTimer?.Stop();
                        IsDelayUnlocked = true;
                        CanDismissModal = true;
                        DelayStatusText = "Pro license active.";
                        DelayDismissButtonText = "Start Using Fujifier Pro";
                        DelayProgressBarValue = totalDelay;
                        return;
                    }

                    DelayRemainingSeconds--;
                    if (DelayRemainingSeconds > 0)
                    {
                        DelayProgressBarValue = totalDelay - DelayRemainingSeconds;
                        DelayStatusText = LocalizationManager.TrFormat("CooldownActive", DelayRemainingSeconds);
                        DelayDismissButtonText = $"{LocalizationManager.Tr("ContinueWithFreeVersion")} ({DelayRemainingSeconds}s)";
                    }
                    else
                    {
                        _delayTimer?.Stop();
                        CanDismissModal = true;
                        DelayProgressBarValue = totalDelay;
                        DelayStatusText = LocalizationManager.Tr("CooldownFinished");
                        DelayDismissButtonText = LocalizationManager.Tr("ContinueWithFreeVersion");
                    }
                };

                _delayTimer.Start();
                IsModalOpen = true;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Action();
            }
            else
            {
                Dispatcher.UIThread.Post(Action);
            }

            return tcs.Task;
        }

        private async Task ExecuteDelayGoogleSignInAsync()
        {
            DelayStatusText = "Signing in with Google...";
            var result = await _licenseService.LoginWithGoogleOAuthAsync(msg =>
            {
                DelayStatusText = msg;
            });

            if (result.Success && _licenseService.IsPro)
            {
                _delayTimer?.Stop();
                IsDelayUnlocked = true;
                CanDismissModal = true;
                ModalTitle = "✔ Pro Unlocked!";
                ModalMessage = "Welcome to Fujifier Pro! All tier limits, countdown delays, and advertisements have been removed permanently.";
                DelayStatusText = "Pro license active.";
                DelayDismissButtonText = "Start Using Fujifier Pro";
                DelayProgressBarValue = _licenseService?.DelaySeconds ?? 3;
                IsDelayKeyInputVisible = false;
                RefreshSettings();
            }
            else if (result.Success)
            {
                DelayStatusText = result.ErrorMessage ?? "Signed in. No active Pro license found on fujifier.cc.";
            }
            else
            {
                DelayStatusText = result.ErrorMessage ?? "Google Sign-In failed.";
            }
        }

        private async Task ExecuteDelayActivateKeyAsync()
        {
            string key = DelayLicenseKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                DelayLicenseError = "Please enter a valid license key.";
                return;
            }

            var result = await _licenseService.ActivateLicenseKeyAsync(key);
            if (result.Success)
            {
                _delayTimer?.Stop();
                IsDelayUnlocked = true;
                CanDismissModal = true;
                DelayLicenseError = string.Empty;
                ModalTitle = "✔ Pro Unlocked!";
                ModalMessage = "Thank you for upgrading to Fujifier Pro! All tier limits, countdown delays, and advertisements have been removed permanently.";
                DelayStatusText = "License active.";
                DelayDismissButtonText = "Start Using Fujifier Pro";
                DelayProgressBarValue = _licenseService?.DelaySeconds ?? 3;
                IsDelayKeyInputVisible = false;
                RefreshSettings();
            }
            else
            {
                DelayLicenseError = result.ErrorMessage ?? "Invalid license key.";
            }
        }

        public void CloseModal()
        {
            if (ModalType == "FreeTierDelay" && !CanDismissModal)
            {
                return;
            }

            if (ModalType == "FreeTierDelay")
            {
                _delayTimer?.Stop();
                _delayTimer = null;
                _freeTierDelayTcs?.TrySetResult(_licenseService.IsPro);
                _freeTierDelayTcs = null;
            }

            if (ModalType == "Preferences")
            {
                App.ApplyTheme(_settingsService.Settings.ThemeMode);
            }
            IsModalOpen = false;
            IsMobileAddMenuOpen = false;
            ModalType = "None";
            _conflictTcs?.TrySetResult(new FileConflictResolution(ConflictChoice.Skip, false));
            _conflictTcs = null;
            _confirmationTcs?.TrySetResult(false);
            _confirmationTcs = null;
            OnPropertyChanged(nameof(CanOpenInLightroom));
            OnPropertyChanged(nameof(CanOpenOutputFolder));
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
                    OnPropertyChanged(nameof(OutputDestinationSummary));
                }
            }
        }

        public bool HasCustomOutputFolder => !string.IsNullOrWhiteSpace(_outputFolderPath);

        public string FormattedOutputFolderPath =>
            string.IsNullOrWhiteSpace(_outputFolderPath)
                ? LocalizationManager.Tr("Optional")
                : _outputFolderPath.Replace(Path.DirectorySeparatorChar.ToString(), "  >  ");

        private static bool HasActiveWindowOrView(Window? owner)
        {
            if (owner != null) return true;
            if (Avalonia.Application.Current == null) return false;
            if (Avalonia.Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                return true;
            if (Avalonia.Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime singleView && singleView.MainView != null)
                return true;
            return false;
        }

        private bool _isConfirmingDisableBackup;

        public bool CreateBackup
        {
            get => _createBackup;
            set
            {
                if (_createBackup == value) return;

                if (!value && HasActiveWindowOrView(OwnerWindow) && !SuppressBackupConfirmation)
                {
                    if (_isConfirmingDisableBackup) return;
                    _isConfirmingDisableBackup = true;

                    Dispatcher.UIThread.Post(async () =>
                    {
                        try
                        {
                            bool confirmed = await _dialogService.ShowConfirmationAsync(
                                OwnerWindow,
                                LocalizationManager.Tr("DisableBackupTitle"),
                                LocalizationManager.Tr("DisableBackupWarning"),
                                LocalizationManager.Tr("DisableBackupConfirm"),
                                LocalizationManager.Tr("DisableBackupCancel"));

                            if (confirmed)
                            {
                                _createBackup = false;
                                _settingsService.Settings.BackupOriginalFile = false;
                                _settingsService.Save();
                                if (_preferences != null)
                                {
                                    _preferences.BackupOriginalFile = false;
                                }
                            }
                        }
                        finally
                        {
                            _isConfirmingDisableBackup = false;
                            OnPropertyChanged(nameof(CreateBackup));
                            OnPropertyChanged(nameof(QuickSettingsSummary));
                        }
                    });
                    return;
                }

                _createBackup = value;
                _settingsService.Settings.BackupOriginalFile = value;
                _settingsService.Save();
                if (_preferences != null)
                {
                    _preferences.BackupOriginalFile = value;
                }
                OnPropertyChanged(nameof(CreateBackup));
                OnPropertyChanged(nameof(QuickSettingsSummary));
            }
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
                    OnPropertyChanged(nameof(ActiveStep));
                    OnPropertyChanged(nameof(IsStep1Current));
                    OnPropertyChanged(nameof(IsStep2Current));
                    OnPropertyChanged(nameof(IsStep3Current));
                    OnPropertyChanged(nameof(IsStep4Current));
                    (ClearCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                    (ProcessCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                }
            }
        }

        private bool _isImporting;
        public bool IsImporting
        {
            get => _isImporting;
            private set
            {
                if (SetProperty(ref _isImporting, value))
                {
                    OnPropertyChanged(nameof(OverallStatusText));
                }
            }
        }

        private double _importProgress;
        public double ImportProgress
        {
            get => _importProgress;
            private set
            {
                if (SetProperty(ref _importProgress, value))
                {
                    OnPropertyChanged(nameof(ImportPercentText));
                }
            }
        }

        private string _importStatusText = string.Empty;
        public string ImportStatusText
        {
            get => _importStatusText;
            private set
            {
                if (SetProperty(ref _importStatusText, value))
                {
                    OnPropertyChanged(nameof(OverallStatusText));
                }
            }
        }

        public string ImportPercentText => $"{(int)_importProgress}%";

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
            get
            {
                if (_isStopping) return LocalizationManager.Tr("Stopping");
                if (IsProcessing) return LocalizationManager.Tr("Stop");
                return LocalizationManager.Tr("Process");
            }
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
        public ICommand OpenWebsiteCommand { get; }

        public static Action<string>? PlatformOpenUrlCallback { get; set; }

        #endregion

        #region Actions & Business Logic

        public async Task AddFilesAsync(IEnumerable<string> fileOrFolderPaths)
        {
            IsLoading = true;
            IsImporting = true;
            ImportProgress = 0;
            ImportStatusText = "Scanning folders and files...";
            IsProgressVisible = true;
            ProgressValue = 0;
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

                // Free Tier Queue Limit Enforcement (Max 10 photos)
                if (!_licenseService.IsPro)
                {
                    if (Files.Count >= _licenseService.MaxFreeQueueSize)
                    {
                        Dispatcher.UIThread.Post(async () =>
                        {
                            await _dialogService.ShowMessageAsync(
                                OwnerWindow,
                                "Free Tier Queue Limit",
                                $"The Free tier of Fujifier allows up to {_licenseService.MaxFreeQueueSize} photos in the queue.\nUpgrade to Fujifier Pro to convert unlimited photos at once!");
                        });
                        return;
                    }

                    int availableSlots = _licenseService.MaxFreeQueueSize - Files.Count;
                    if (filesToAdd.Count > availableSlots)
                    {
                        int skipped = filesToAdd.Count - availableSlots;
                        filesToAdd = filesToAdd.Take(availableSlots).ToList();
                        Dispatcher.UIThread.Post(async () =>
                        {
                            await _dialogService.ShowMessageAsync(
                                OwnerWindow,
                                "Free Tier Limit Applied",
                                $"Under the Free tier, the queue is capped at {_licenseService.MaxFreeQueueSize} photos. Added {availableSlots} photos ({skipped} skipped).\nUpgrade to Fujifier Pro for unlimited batch processing!");
                        });
                    }
                }

                var existingPaths = new HashSet<string>(Files.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);

                var validFiles = new List<string>();
                foreach (var filePath in filesToAdd)
                {
                    if (existingPaths.Contains(filePath)) continue;

                    string extension = Path.GetExtension(filePath);
                    if (_supportedFormats.Contains(extension))
                    {
                        existingPaths.Add(filePath);
                        validFiles.Add(filePath);
                    }
                    else
                    {
                        Dispatcher.UIThread.Post(() =>
                            _dialogService.ShowMessageAsync(OwnerWindow, "Unsupported File Format", $"The file format {extension} is not supported."));
                    }
                }

                int totalFiles = validFiles.Count;
                if (totalFiles == 0)
                {
                    return;
                }

                // Stage 1: Instant Ingestion.
                // Add FileItem records with lightweight placeholders to the queue immediately in batches so user gets instant visual feedback.
                var placeholder = _thumbnailService.GetPlaceholderImage();
                var createdItems = new List<FileItem>(totalFiles);

                foreach (var filePath in validFiles)
                {
                    createdItems.Add(new FileItem
                    {
                        FilePath = filePath,
                        Status = "Pending",
                        Thumbnail = placeholder,
                        CameraMakeAndModel = string.Empty,
                        RemoveCommand = RemoveFileCommand
                    });
                }

                // Batch dispatch to UI collection
                const int uiBatchSize = 100;
                for (int i = 0; i < totalFiles; i += uiBatchSize)
                {
                    int count = Math.Min(uiBatchSize, totalFiles - i);
                    var batchSlice = createdItems.GetRange(i, count);
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var item in batchSlice)
                        {
                            Files.Add(item);
                        }
                        if (SelectedFile == null && Files.Count > 0)
                        {
                            SelectedFile = Files[0];
                        }
                    });
                }

                // Stage 2: Concurrent Multi-Threaded Thumbnail & Metadata Processing
                bool skipThumbnails = _settingsService.Settings.SkipThumbnailGeneration;
                int maxConcurrency = DrivePerformanceHelper.GetOptimalIngestionConcurrency(createdItems.FirstOrDefault()?.FilePath);
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxConcurrency
                };

                int processedCount = 0;
                long lastProgressUpdateTicks = 0;
                var progressSw = System.Diagnostics.Stopwatch.StartNew();

                Dispatcher.UIThread.Post(() =>
                {
                    ImportStatusText = $"Loading photo details (0 of {totalFiles})...";
                    ImportProgress = 0;
                    ProgressValue = 0;
                });

                await Task.Run(async () =>
                {
                    await Parallel.ForEachAsync(createdItems, parallelOptions, async (item, ct) =>
                    {
                        try
                        {
                            string cameraModel = _metadataService.GetCameraMakeAndModel(item.FilePath);
                            if (!string.IsNullOrEmpty(cameraModel))
                            {
                                item.CameraMakeAndModel = cameraModel;
                            }

                            if (!skipThumbnails)
                            {
                                var thumbnail = _thumbnailService.GenerateThumbnail(item.FilePath);
                                if (thumbnail != null)
                                {
                                    item.Thumbnail = thumbnail;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Fujifier] Error loading details for {item.FilePath}: {ex.Message}");
                        }
                        finally
                        {
                            int curr = Interlocked.Increment(ref processedCount);
                            long elapsed = progressSw.ElapsedMilliseconds;
                            // Throttle progress bar & status text updates to at most once per 60ms to prevent UI event loop congestion
                            if (elapsed - Interlocked.Read(ref lastProgressUpdateTicks) > 60 || curr == totalFiles)
                            {
                                Interlocked.Exchange(ref lastProgressUpdateTicks, elapsed);
                                double pct = (double)curr / totalFiles * 100.0;
                                Dispatcher.UIThread.Post(() =>
                                {
                                    ImportProgress = pct;
                                    ProgressValue = pct;
                                    ImportStatusText = $"Loading photo details ({curr} of {totalFiles})...";
                                });
                            }
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Fujifier] Exception in AddFilesAsync: {ex}");
                Dispatcher.UIThread.Post(() =>
                    _dialogService.ShowMessageAsync(OwnerWindow, "Import Error", $"Could not import photos: {ex.Message}"));
            }
            finally
            {
                IsLoading = false;
                IsImporting = false;
                ImportProgress = 0;
                ImportStatusText = string.Empty;
                if (!IsProcessing)
                {
                    IsProgressVisible = false;
                    ProgressValue = 0;
                }
                OnPropertyChanged(nameof(OverallStatusText));
                OnPropertyChanged(nameof(ImportPercentText));
            }
        }

        private async Task ExecuteProcessOrStopAsync()
        {
            if (IsProcessing)
            {
                if (!_isStopping)
                {
                    _isStopping = true;
                    _processingCts?.Cancel();
                    OnPropertyChanged(nameof(ProcessButtonContent));
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
            OnPropertyChanged(nameof(CanOpenOutputFolder));

            bool createBackup = CreateBackup;
            string? currentOutputFolder = OutputFolderPath;
            bool hasProcessingErrors = false;

            int totalFiles = files.Count;
            int configuredConcurrency = _settingsService.Settings.MaxConcurrentConversions;
            int maxConcurrency = DrivePerformanceHelper.GetOptimalConversionConcurrency(
                files.FirstOrDefault(),
                configuredConcurrency,
                _licenseService.IsPro);
            int processedCount = 0;
            int successCount = 0;

            var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();
            long totalBytes = 0;
            foreach (var f in files)
            {
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Exists)
                    {
                        totalBytes += fi.Length;
                    }
                }
                catch { }
            }

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
                        Interlocked.Increment(ref successCount);
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
                _isStopping = false;
                IsProcessing = false;
                OnPropertyChanged(nameof(ProcessButtonContent));
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
                SortFilesByStatus();

                OnPropertyChanged(nameof(ActiveStep));
                OnPropertyChanged(nameof(IsStep1Current));
                OnPropertyChanged(nameof(IsStep2Current));
                OnPropertyChanged(nameof(IsStep3Current));
                OnPropertyChanged(nameof(IsStep4Current));

                ProcessingCompleted?.Invoke();

                string message = hasProcessingErrors
                    ? "Processing completed with some errors. Please check the status for details."
                    : "Enjoy your new Film simulations!";

                List<string> convertedToOpen;
                lock (_recentlyConvertedDngFiles)
                {
                    convertedToOpen = _recentlyConvertedDngFiles.ToList();
                }

                if (IsOpenInLightroomSupported && _settingsService.Settings.AutoOpenInLightroom && convertedToOpen.Count > 0)
                {
                    OpenInLightroom(convertedToOpen);
                }

                batchStopwatch.Stop();

                if (_settingsService.Settings.AutoOpenOutputFolder)
                {
                    ExecuteOpenOutputFolder();
                }

                ShowBatchSummaryModal(totalFiles, successCount, batchStopwatch.Elapsed, totalBytes, hasProcessingErrors);

                if (!_licenseService.IsPro)
                {
                    _adService.ShowInterstitialAd();
                    await FreeTierDelayDialog.ShowDelayDialogAsync(OwnerWindow, _licenseService, isPostConversion: true);
                }
            }
        }

        public void ExecuteOpenOutputFolder()
        {
            string folder = string.Empty;
            lock (_recentlyConvertedDngFiles)
            {
                if (_recentlyConvertedDngFiles.Count > 0)
                {
                    var dir = Path.GetDirectoryName(_recentlyConvertedDngFiles[0]);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        folder = dir;
                    }
                }
            }

            if (string.IsNullOrEmpty(folder))
            {
                folder = !string.IsNullOrEmpty(OutputFolderPath) && Directory.Exists(OutputFolderPath)
                    ? OutputFolderPath
                    : (Files.Count > 0 ? (Path.GetDirectoryName(Files[0].FilePath) ?? "") : "");
            }

            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                CrossPlatformLauncher.OpenFolder(folder);
            }
        }

        public void SortFilesByStatus()
        {
            void PerformSort()
            {
                if (Files.Count <= 1) return;

                int GetStatusPriority(FileItem item)
                {
                    if (item.IsError) return 0;
                    if (!item.IsCompleted && item.Status != "✔" && item.Status != "✓") return 1;
                    return 2;
                }

                var sorted = Files
                    .Select((item, index) => (item, index))
                    .OrderBy(x => GetStatusPriority(x.item))
                    .ThenBy(x => x.index)
                    .Select(x => x.item)
                    .ToList();

                for (int i = 0; i < sorted.Count; i++)
                {
                    int currentIndex = Files.IndexOf(sorted[i]);
                    if (currentIndex != i)
                    {
                        Files.Move(currentIndex, i);
                    }
                }

                if (sorted.Count > 0 && sorted[0].IsError)
                {
                    SelectedFile = sorted[0];
                }
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                PerformSort();
            }
            else
            {
                Dispatcher.UIThread.Post(PerformSort);
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

            string initialDestFilePath = OutputLocationHelper.ComputeInitialDestinationFilePath(
                filePath,
                outputFolder,
                _settingsService.Settings);

            string inputDir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string outputDir = Path.GetDirectoryName(initialDestFilePath) ?? string.Empty;
            bool isSameFolder = string.Equals(Path.GetFullPath(inputDir), Path.GetFullPath(outputDir), StringComparison.OrdinalIgnoreCase);
            bool isExactSameFilePath = isOriginallyDng && string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(initialDestFilePath), StringComparison.OrdinalIgnoreCase);

            bool fileAlreadyExists;
            lock (reservedOutputPaths)
            {
                fileAlreadyExists = File.Exists(initialDestFilePath) || reservedOutputPaths.Contains(initialDestFilePath);
            }

            bool destinationConflict = !isExactSameFilePath && !(isOriginallyDng && isSameFolder && createBackup) && fileAlreadyExists;
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
            string? tempInPlaceOutputFile = null;
            string? origBackupPath = null;

            if (isOriginallyDng)
            {
                if (createBackup && isSameFolder)
                {
                    // If the file is already a DNG and the output folder is the same as the input,
                    // change the name of the original file to filenameORIG.DNG
                    string origBaseName = Path.GetFileNameWithoutExtension(filePath);
                    origBackupPath = Path.Combine(inputDir, $"{origBaseName}ORIG.DNG");
                    if (File.Exists(origBackupPath))
                    {
                        File.Delete(origBackupPath);
                    }
                    File.Move(filePath, origBackupPath);
                    dngFilePath = origBackupPath;
                }
                else if (!createBackup && isExactSameFilePath)
                {
                    // Write to a temporary file first so the original can be safely sent to trash upon success
                    string origBaseName = Path.GetFileNameWithoutExtension(filePath);
                    tempInPlaceOutputFile = Path.Combine(outputDir, $"{origBaseName}_tmp_{Guid.NewGuid():N}.dng");
                }
            }

            try
            {
                if (!isOriginallyDng)
                {
                    UpdateFileStatus(filePath, "Converting to DNG");
                    bool embedOriginalRaw = _settingsService.Settings.EmbedOriginalRaw;

                    string targetDngForConversion;
                    if (string.IsNullOrEmpty(outputFolder) && !_settingsService.Settings.CreateSubfolder)
                    {
                        targetDngForConversion = finalDestFilePath;
                    }
                    else
                    {
                        string sourceDir = Path.GetDirectoryName(filePath) ?? string.Empty;
                        targetDngForConversion = Path.Combine(sourceDir, $"{Path.GetFileNameWithoutExtension(filePath)}_tmp_{Guid.NewGuid():N}.dng");
                    }

                    bool useLossless = _licenseService.IsPro && _settingsService.Settings.LosslessCompression;
                    var convResult = await Task.Run(() => _rawConverterService.ConvertToDng(
                        filePath,
                        embedOriginalRaw,
                        targetDngForConversion,
                        useLossless), ct);
                    if (!convResult.Success || string.IsNullOrEmpty(convResult.OutputDngPath))
                    {
                        string err = convResult.ErrorMessage ?? "Conversion failed";
                        UpdateFileStatus(filePath, err);
                        throw new InvalidOperationException(err);
                    }

                    dngFilePath = convResult.OutputDngPath;
                    UpdateFileStatus(filePath, "Conversion successful");
                }

                string? customOutputPath = tempInPlaceOutputFile ?? ((!string.IsNullOrEmpty(outputFolder) || finalDestFilePath != dngFilePath)
                    ? finalDestFilePath
                    : null);

                if (!string.IsNullOrEmpty(customOutputPath))
                {
                    string? destDir = Path.GetDirectoryName(customOutputPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                }

                var metaResult = await Task.Run(() => _metadataService.InjectFujifilmMetadata(
                    dngFilePath,
                    outputFolder,
                    createBackup: false,
                    isOriginallyDng: isOriginallyDng,
                    customOutputFilePath: customOutputPath,
                    targetUniqueCameraModel: TargetCameraModel,
                    sourceRawFilePath: filePath), ct);

                if (metaResult.Success)
                {
                    if (!string.IsNullOrEmpty(tempInPlaceOutputFile))
                    {
                        // Original in-place DNG: send original to trash, then move temporary file to final path
                        FileTrashHelper.SendToTrash(filePath);
                        File.Move(tempInPlaceOutputFile, finalDestFilePath, overwrite: true);
                    }
                    else if (!createBackup)
                    {
                        // If backup is deactivated, send original file to trash for all files that succeeded
                        FileTrashHelper.SendToTrash(filePath);

                        // Also trash original sidecar if it was copied or embedded and differs from the final output sidecar
                        string? origSidecar = DngMetadataEditor.FindSidecarXmp(filePath);
                        if (!string.IsNullOrEmpty(origSidecar) && File.Exists(origSidecar))
                        {
                            string targetSidecar = Path.ChangeExtension(finalDestFilePath, ".xmp");
                            if (!string.Equals(Path.GetFullPath(origSidecar), Path.GetFullPath(targetSidecar), StringComparison.OrdinalIgnoreCase))
                            {
                                FileTrashHelper.SendToTrash(origSidecar);
                            }
                        }
                    }

                    UpdateFileStatus(filePath, "✔");
                    lock (_recentlyConvertedDngFiles)
                    {
                        _recentlyConvertedDngFiles.Add(finalDestFilePath);
                    }
                    PlatformFileCreatedCallback?.Invoke(finalDestFilePath);
                }
                else
                {
                    if (!string.IsNullOrEmpty(tempInPlaceOutputFile) && File.Exists(tempInPlaceOutputFile))
                    {
                        try { File.Delete(tempInPlaceOutputFile); } catch { }
                    }
                    if (isOriginallyDng && createBackup && isSameFolder && !string.IsNullOrEmpty(origBackupPath) && File.Exists(origBackupPath) && !File.Exists(filePath))
                    {
                        try { File.Move(origBackupPath, filePath); } catch { }
                    }
                    string err = metaResult.ErrorMessage ?? "Error writing metadata";
                    UpdateFileStatus(filePath, err);
                    throw new InvalidOperationException(err);
                }
            }
            catch
            {
                if (!string.IsNullOrEmpty(tempInPlaceOutputFile) && File.Exists(tempInPlaceOutputFile))
                {
                    try { File.Delete(tempInPlaceOutputFile); } catch { }
                }
                if (isOriginallyDng && createBackup && isSameFolder && !string.IsNullOrEmpty(origBackupPath) && File.Exists(origBackupPath) && !File.Exists(filePath))
                {
                    try { File.Move(origBackupPath, filePath); } catch { }
                }
                throw;
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
            IsQueueFabMenuOpen = false;
            if (Files.Count == 0) return;
            foreach (var file in Files)
            {
                file.Dispose();
            }
            Files.Clear();
            SelectedFile = null;
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
            IsQueueFabMenuOpen = false;
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
                System.Diagnostics.Debug.WriteLine($"[Fujifier] Error in ExecuteAddFilesAsync: {ex}");
                await _dialogService.ShowMessageAsync(OwnerWindow, "Import Error", $"Failed to select files: {ex.Message}");
            }
        }

        private async Task ExecuteAddFolderAsync()
        {
            IsQueueFabMenuOpen = false;
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
            RemoveFiles(itemsToRemove);
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
                file.Dispose();
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

        private async Task ExecuteOpenUpdateAsync()
        {
            try
            {
                var result = await _updateService.CheckForUpdateAsync(force: true);
                if (result.IsUpdateAvailable && result.UpdateInfo != null)
                {
                    if (OwnerWindow != null)
                    {
                        await UpdateDialog.ShowUpdateDialogAsync(OwnerWindow, result.UpdateInfo, _updateService);
                    }
                    else
                    {
                        await UpdateDialog.ShowUpdateDialogAsync(null, result.UpdateInfo, _updateService);
                    }
                }
                else if (result.Success && !result.IsUpdateAvailable)
                {
                    ShowMessageModal("Up to Date", $"You are running the latest version of Fujifier (v{_updateService.CurrentVersionString}).");
                }
                else
                {
                    ShowMessageModal("Update Check", result.ErrorMessage ?? "Could not check for updates. Please check your internet connection.", isSuccess: false);
                }
            }
            catch (Exception ex)
            {
                ShowMessageModal("Update Check Error", $"An error occurred: {ex.Message}", isSuccess: false);
            }
        }

        private void ExecuteReportBug()
        {
            CrossPlatformLauncher.OpenUrl("https://github.com/ip-web/Fujify/issues");
        }

        #endregion
    }
}
