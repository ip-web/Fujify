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
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fujify.Common;
using Fujify.Localization;
using Fujify.Models;
using Fujify.Services;
using Fujify.Views;

namespace Fujify.ViewModels
{
    public class PreferencesViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly ILicenseService _licenseService;
        private readonly IUpdateService _updateService;
        private readonly IDialogService? _dialogService;
        private readonly ILightroomPresetService _lightroomPresetService;

        public Avalonia.Controls.Window? OwnerWindow { get; set; }

        private bool _isDarkTheme;
        private bool _backupOriginalFile = true;
        private bool _embedOriginalRaw;
        private bool _skipThumbnailGeneration;
        private bool _losslessCompression = true;
        private int _maxConcurrentConversions;
        private bool _autoOpenInLightroom;
        private bool _appendSuffix;
        private string _suffix = "-fujified";
        private bool _createSubfolder;
        private bool _subfolderUseOriginalName = true;
        private string _subfolderSuffix = "-fujified";
        private string _subfolderCustomName = "Fujified";
        private bool _autoOpenOutputFolder;
        private bool _isWindowsContextMenuEnabled;
        private string _themeMode = "Automatic";
        private bool _checkForUpdatesOnStartup = true;
        private bool _enableCaptureOneMode;
        private string? _rafContainerPath;
        private bool _exportFailedConversionsLog;

        private bool _isCheckingForUpdate;
        private string _updateStatusMessage = string.Empty;
        private string _updateStatusColor = "#A0A0AC";
        private bool _hasUpdateStatusMessage;

        private string _licenseKeyInput = string.Empty;
        private string _accountEmailInput = string.Empty;
        private string _accountPasswordInput = string.Empty;
        private bool _isAccountTabActive = false;
        private bool _isGoogleLoggingIn = false;
        private bool _isRetrievingLicense = false;
        private string _licenseMessageText = string.Empty;
        private string _licenseMessageColor = "#A0A0AC";
        private bool _hasLicenseMessage = false;

        public ObservableCollection<LicenseDeviceItemViewModel> RegisteredDevices { get; } = new();
        public bool HasRegisteredDevices => RegisteredDevices.Count > 0;
        private bool _isLoadingDevices;
        public bool IsLoadingDevices
        {
            get => _isLoadingDevices;
            set => SetProperty(ref _isLoadingDevices, value);
        }

        public int TotalThreads => Environment.ProcessorCount;
        public int DefaultConcurrency => Math.Max(1, TotalThreads / 2);

        public bool IsPro => _licenseService.IsPro;

        public string LicenseStatusText => _licenseService.IsPro
            ? "Fujifier Pro"
            : "Fujifier Free Tier";

        public string LicenseDescriptionText => _licenseService.IsPro
            ? (!string.IsNullOrWhiteSpace(_licenseService.CurrentStatus.LicenseeEmail)
                ? $"Licensed to: {_licenseService.CurrentStatus.LicenseeEmail} ({SlotInfoText})"
                : $"Lifetime Pro License Active ({SlotInfoText})")
            : $"Queue limited to 10 photos, {_licenseService.DelaySeconds}s cooldown timer, context menu locked.";

        public string LicenseKeyInput
        {
            get => _licenseKeyInput;
            set => SetProperty(ref _licenseKeyInput, value);
        }

        public string AccountEmailInput
        {
            get => _accountEmailInput;
            set => SetProperty(ref _accountEmailInput, value);
        }

        public string AccountPasswordInput
        {
            get => _accountPasswordInput;
            set => SetProperty(ref _accountPasswordInput, value);
        }

        public bool IsAccountTabActive
        {
            get => _isAccountTabActive;
            set
            {
                if (SetProperty(ref _isAccountTabActive, value))
                {
                    OnPropertyChanged(nameof(IsKeyTabActive));
                }
            }
        }

        public bool IsKeyTabActive => !IsAccountTabActive;

        public bool IsAccountLoggedIn => _licenseService.IsAccountLoggedIn;

        public string AccountDisplayText => _licenseService.AccountEmail ?? "Not connected";

        public string? AccountDisplayName => _settingsService.Settings.AccountDisplayName;

        public bool IsGoogleLoggingIn
        {
            get => _isGoogleLoggingIn;
            set => SetProperty(ref _isGoogleLoggingIn, value);
        }

        public bool IsRetrievingLicense
        {
            get => _isRetrievingLicense;
            set => SetProperty(ref _isRetrievingLicense, value);
        }

        public string SlotInfoText => _licenseService.SlotInfoText;
        public bool ShowDeviceManagement => IsPro || IsAccountLoggedIn || HasRegisteredDevices;
        public bool CanActivateThisDevice => !IsPro && (IsAccountLoggedIn || HasAccountLicense || !string.IsNullOrWhiteSpace(LicenseKeyInput));
        public bool HasAccountLicense => _licenseService.HasAccountLicense;

        public string LicenseMessageText
        {
            get => _licenseMessageText;
            set => SetProperty(ref _licenseMessageText, value);
        }

        public string LicenseMessageColor
        {
            get => _licenseMessageColor;
            set => SetProperty(ref _licenseMessageColor, value);
        }

        public bool HasLicenseMessage
        {
            get => _hasLicenseMessage;
            set => SetProperty(ref _hasLicenseMessage, value);
        }

        public bool LosslessCompression
        {
            get => _licenseService.IsPro && _losslessCompression;
            set
            {
                if (value && !_licenseService.IsPro)
                {
                    LicenseMessageText = "Lossless DNG compression is exclusive to Fujifier Pro.";
                    LicenseMessageColor = "#F39C12";
                    HasLicenseMessage = true;
                    OnPropertyChanged(nameof(LosslessCompression));
                    return;
                }

                SetProperty(ref _losslessCompression, value);
            }
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

        public bool CreateSubfolder
        {
            get => IsSubfolderOptionSupported && _createSubfolder;
            set
            {
                if (!IsSubfolderOptionSupported) return;
                if (SetProperty(ref _createSubfolder, value))
                {
                    OnPropertyChanged(nameof(SubfolderPreview));
                }
            }
        }

        public bool SubfolderUseOriginalName
        {
            get => _subfolderUseOriginalName;
            set
            {
                if (SetProperty(ref _subfolderUseOriginalName, value))
                {
                    OnPropertyChanged(nameof(SubfolderPreview));
                }
            }
        }

        public string SubfolderSuffix
        {
            get => _subfolderSuffix;
            set
            {
                if (SetProperty(ref _subfolderSuffix, value))
                {
                    OnPropertyChanged(nameof(SubfolderPreview));
                }
            }
        }

        public string SubfolderCustomName
        {
            get => _subfolderCustomName;
            set
            {
                if (SetProperty(ref _subfolderCustomName, value))
                {
                    OnPropertyChanged(nameof(SubfolderPreview));
                }
            }
        }

        public string SubfolderPreview => CreateSubfolder
            ? (SubfolderUseOriginalName
                ? $"Photos → Photos/{(!string.IsNullOrEmpty(_subfolderSuffix) ? "Photos" + _subfolderSuffix : "Photos")}/"
                : $"Photos → Photos/{(!string.IsNullOrWhiteSpace(_subfolderCustomName) ? _subfolderCustomName : "Fujified")}/")
            : string.Empty;

        public bool AutoOpenOutputFolder
        {
            get => !IsMobile && _autoOpenOutputFolder;
            set => SetProperty(ref _autoOpenOutputFolder, IsMobile ? false : value);
        }

        public bool IsMobile => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

        public bool IsOpenInLightroomSupported => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

        public bool IsSubfolderOptionSupported => true;

        public bool EnableCaptureOneMode
        {
            get => _enableCaptureOneMode;
            set
            {
                if (SetProperty(ref _enableCaptureOneMode, value))
                {
                    _settingsService.Settings.EnableCaptureOneMode = value;
                    _settingsService.Save();
                    OnPropertyChanged(nameof(ShowCaptureOneContainerSettings));
                    if (MainViewModel.Instance != null)
                    {
                        MainViewModel.Instance.RefreshCaptureOneModeState();
                    }
                }
            }
        }

        public bool ShowCaptureOneContainerSettings => EnableCaptureOneMode;

        public ObservableCollection<RafContainerItemViewModel> RafContainers { get; } = new();
        public bool HasRafContainers => RafContainers.Count > 0;

        public string RafContainerPath
        {
            get => _rafContainerPath ?? string.Empty;
            set
            {
                if (SetProperty(ref _rafContainerPath, value))
                {
                    LoadContainersFromSetting(value);
                    _settingsService.Settings.RafContainerPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    _settingsService.Save();
                    OnPropertyChanged(nameof(HasRafContainerPath));
                    OnPropertyChanged(nameof(HasRafContainers));
                    OnPropertyChanged(nameof(RafContainerCountText));
                    OnPropertyChanged(nameof(RafContainerStatusText));
                }
            }
        }

        public bool HasRafContainerPath => !string.IsNullOrWhiteSpace(_rafContainerPath) || HasRafContainers;

        public string RafContainerCountText => RafContainers.Count switch
        {
            0 => string.Empty,
            1 => LocalizationManager.Tr("RafContainerCountSingle"),
            _ => LocalizationManager.TrFormat("RafContainerCountMultiple", RafContainers.Count)
        };

        public string RafContainerStatusText
        {
            get
            {
                if (RafContainers.Count == 0)
                {
                    return LocalizationManager.Tr("NoRafContainerConfigured");
                }
                return RafContainers.Count == 1
                    ? LocalizationManager.TrFormat("RafContainerConfigured", RafContainers[0].DisplayText)
                    : $"{RafContainers.Count} donor containers configured";
            }
        }

        public bool IsWindows => OperatingSystem.IsWindows();

        public bool IsWindowsContextMenuEnabled
        {
            get => _licenseService.IsPro && _isWindowsContextMenuEnabled;
            set
            {
                if (value && !_licenseService.IsPro)
                {
                    LicenseMessageText = "Windows Explorer context menu integration is exclusive to Fujifier Pro.";
                    LicenseMessageColor = "#F39C12";
                    HasLicenseMessage = true;
                    OnPropertyChanged(nameof(IsWindowsContextMenuEnabled));
                    return;
                }

                SetProperty(ref _isWindowsContextMenuEnabled, value);
            }
        }

        public string ThemeMode
        {
            get => _themeMode;
            set
            {
                string normalized = value?.Trim() ?? "Automatic";
                if (normalized.Equals("Light", StringComparison.OrdinalIgnoreCase)) normalized = "Light";
                else if (normalized.Equals("Amoled", StringComparison.OrdinalIgnoreCase)) normalized = "Amoled";
                else if (normalized.Equals("Dark", StringComparison.OrdinalIgnoreCase)) normalized = "Dark";
                else normalized = "Automatic";

                if (SetProperty(ref _themeMode, normalized))
                {
                    _isDarkTheme = !normalized.Equals("Light", StringComparison.OrdinalIgnoreCase);
                    OnPropertyChanged(nameof(IsDarkTheme));
                    OnPropertyChanged(nameof(IsAutoThemeSelected));
                    OnPropertyChanged(nameof(IsDarkThemeSelected));
                    OnPropertyChanged(nameof(IsAmoledThemeSelected));
                    OnPropertyChanged(nameof(IsLightThemeSelected));
                    App.ApplyTheme(normalized);
                }
            }
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetProperty(ref _isDarkTheme, value))
                {
                    _themeMode = value ? "Dark" : "Light";
                    OnPropertyChanged(nameof(ThemeMode));
                    OnPropertyChanged(nameof(IsAutoThemeSelected));
                    OnPropertyChanged(nameof(IsDarkThemeSelected));
                    OnPropertyChanged(nameof(IsAmoledThemeSelected));
                    OnPropertyChanged(nameof(IsLightThemeSelected));
                    App.ApplyTheme(_themeMode);
                }
            }
        }

        public bool IsAutoThemeSelected
        {
            get => _themeMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase) || _themeMode.Equals("Auto", StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value)
                {
                    ThemeMode = "Automatic";
                }
            }
        }

        public bool IsDarkThemeSelected
        {
            get => _themeMode.Equals("Dark", StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value)
                {
                    ThemeMode = "Dark";
                }
            }
        }

        public bool IsAmoledThemeSelected
        {
            get => _themeMode.Equals("Amoled", StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value)
                {
                    ThemeMode = "Amoled";
                }
            }
        }

        public bool IsLightThemeSelected
        {
            get => _themeMode.Equals("Light", StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value)
                {
                    ThemeMode = "Light";
                }
            }
        }

        public LocalizationManager Loc => LocalizationManager.Instance;

        public System.Collections.Generic.IReadOnlyList<LanguageItem> AvailableLanguages => LocalizationManager.AvailableLanguages;

        public LanguageItem? SelectedLanguageItem
        {
            get
            {
                string norm = LocalizationManager.NormalizeLanguageCode(_language);
                foreach (var item in AvailableLanguages)
                {
                    if (item.Code.Equals(norm, StringComparison.OrdinalIgnoreCase))
                        return item;
                }
                return AvailableLanguages.Count > 0 ? AvailableLanguages[0] : null;
            }
            set
            {
                if (value != null && !string.Equals(value.Code, _language, StringComparison.OrdinalIgnoreCase))
                {
                    Language = value.Code;
                    OnPropertyChanged(nameof(SelectedLanguageItem));
                }
            }
        }

        private string _language = "en";
        public string Language
        {
            get => _language;
            set
            {
                string normalized = LocalizationManager.NormalizeLanguageCode(value);
                if (SetProperty(ref _language, normalized))
                {
                    OnPropertyChanged(nameof(SelectedLanguageItem));
                    OnPropertyChanged(nameof(IsEnglishSelected));
                    OnPropertyChanged(nameof(IsFrenchSelected));
                    OnPropertyChanged(nameof(IsSpanishSelected));
                    OnPropertyChanged(nameof(IsGermanSelected));
                    LocalizationManager.Instance.SetLanguage(normalized);
                }
            }
        }

        public bool IsEnglishSelected
        {
            get => _language.Equals("en", StringComparison.OrdinalIgnoreCase);
            set { if (value) Language = "en"; }
        }

        public bool IsFrenchSelected
        {
            get => _language.Equals("fr", StringComparison.OrdinalIgnoreCase);
            set { if (value) Language = "fr"; }
        }

        public bool IsSpanishSelected
        {
            get => _language.Equals("es", StringComparison.OrdinalIgnoreCase);
            set { if (value) Language = "es"; }
        }

        public bool IsGermanSelected
        {
            get => _language.Equals("de", StringComparison.OrdinalIgnoreCase);
            set { if (value) Language = "de"; }
        }

        public bool SuppressBackupConfirmation { get; set; }
        private static bool HasActiveWindowOrView(Avalonia.Controls.Window? owner)
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

        public void SetBackupOriginalFileDirect(bool value)
        {
            _backupOriginalFile = value;
            OnPropertyChanged(nameof(BackupOriginalFile));
        }

        public bool BackupOriginalFile
        {
            get => IsMobile || _backupOriginalFile;
            set
            {
                if (IsMobile) return;
                if (_backupOriginalFile == value) return;

                if (!value && HasActiveWindowOrView(OwnerWindow) && !SuppressBackupConfirmation)
                {
                    if (_isConfirmingDisableBackup) return;
                    _isConfirmingDisableBackup = true;

                    Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                    {
                        try
                        {
                            var dialogService = _dialogService ?? new DialogService(_settingsService, _licenseService);
                            bool confirmed = await dialogService.ShowConfirmationAsync(
                                OwnerWindow,
                                LocalizationManager.Tr("DisableBackupTitle"),
                                LocalizationManager.Tr("DisableBackupWarning"),
                                LocalizationManager.Tr("DisableBackupConfirm"),
                                LocalizationManager.Tr("DisableBackupCancel"));

                            if (confirmed)
                            {
                                _backupOriginalFile = false;
                                _settingsService.Settings.BackupOriginalFile = false;
                                _settingsService.Save();
                                if (MainViewModel.Instance != null)
                                {
                                    MainViewModel.Instance.SetCreateBackupDirect(false);
                                }
                                OnPropertyChanged(nameof(BackupOriginalFile));
                            }
                            else
                            {
                                // User clicked "Keep Backup" -> restore / keep enabled
                                _backupOriginalFile = false;
                                OnPropertyChanged(nameof(BackupOriginalFile));
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    _backupOriginalFile = true;
                                    _settingsService.Settings.BackupOriginalFile = true;
                                    _settingsService.Save();
                                    if (MainViewModel.Instance != null)
                                    {
                                        MainViewModel.Instance.SetCreateBackupDirect(true);
                                    }
                                    OnPropertyChanged(nameof(BackupOriginalFile));
                                }, Avalonia.Threading.DispatcherPriority.Input);
                            }
                        }
                        finally
                        {
                            _isConfirmingDisableBackup = false;
                        }
                    });
                    return;
                }

                _backupOriginalFile = value;
                _settingsService.Settings.BackupOriginalFile = value;
                _settingsService.Save();
                if (MainViewModel.Instance != null)
                {
                    MainViewModel.Instance.SetCreateBackupDirect(value);
                }
                OnPropertyChanged(nameof(BackupOriginalFile));
            }
        }

        public bool EmbedOriginalRaw
        {
            get => _embedOriginalRaw;
            set => SetProperty(ref _embedOriginalRaw, value);
        }

        public bool AutoOpenInLightroom
        {
            get => _autoOpenInLightroom;
            set => SetProperty(ref _autoOpenInLightroom, value);
        }

        public bool ExportFailedConversionsLog
        {
            get => _exportFailedConversionsLog;
            set
            {
                if (SetProperty(ref _exportFailedConversionsLog, value))
                {
                    _settingsService.Settings.ExportFailedConversionsLog = value;
                    _settingsService.Save();
                }
            }
        }

        public bool HasLatestFailureLog => ConversionLogService.HasLatestFailureLog();

        public bool SkipThumbnailGeneration
        {
            get => _skipThumbnailGeneration;
            set
            {
                if (SetProperty(ref _skipThumbnailGeneration, value))
                {
                    OnPropertyChanged(nameof(ImportPicturesInstantly));
                }
            }
        }

        public bool ImportPicturesInstantly
        {
            get => SkipThumbnailGeneration;
            set => SkipThumbnailGeneration = value;
        }

        public int MaxConcurrentConversions
        {
            get => _licenseService.IsPro ? _maxConcurrentConversions : 1;
            set
            {
                if (!_licenseService.IsPro)
                {
                    if (value > 1)
                    {
                        LicenseMessageText = "Concurrent batch conversions are exclusive to Fujifier Pro (Free tier is limited to 1 thread).";
                        LicenseMessageColor = "#F39C12";
                        HasLicenseMessage = true;
                    }
                    SetProperty(ref _maxConcurrentConversions, 1);
                    OnPropertyChanged(nameof(MaxConcurrentConversions));
                    OnPropertyChanged(nameof(ConcurrencyHint));
                    return;
                }

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
                if (!_licenseService.IsPro)
                    return "1 thread (Free tier: sequential conversion. Upgrade to Pro for multi-threaded conversions)";
                if (_maxConcurrentConversions == DefaultConcurrency)
                    return $"Default: {_maxConcurrentConversions} (Recommended for P-core / E-core balance)";
                if (_maxConcurrentConversions == TotalThreads)
                    return $"Maximum: {_maxConcurrentConversions} (Full saturation - may cause disk I/O bottleneck)";
                if (_maxConcurrentConversions <= 8)
                    return $"{_maxConcurrentConversions} threads (Low CPU load, runs primarily on P-cores)";
                return $"{_maxConcurrentConversions} threads (Utilizes P-cores and E-cores)";
            }
        }

        public bool CheckForUpdatesOnStartup
        {
            get => _checkForUpdatesOnStartup;
            set => SetProperty(ref _checkForUpdatesOnStartup, value);
        }

        public bool IsCheckingForUpdate
        {
            get => _isCheckingForUpdate;
            set => SetProperty(ref _isCheckingForUpdate, value);
        }

        public string UpdateStatusMessage
        {
            get => _updateStatusMessage;
            set => SetProperty(ref _updateStatusMessage, value);
        }

        public string UpdateStatusColor
        {
            get => _updateStatusColor;
            set => SetProperty(ref _updateStatusColor, value);
        }

        public bool HasUpdateStatusMessage
        {
            get => _hasUpdateStatusMessage;
            set => SetProperty(ref _hasUpdateStatusMessage, value);
        }

        public string CurrentAppVersionText => $"Current Version: v{_updateService.CurrentVersionString}";

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand UpgradeToProCommand { get; }
        public ICommand ActivateLicenseCommand { get; }
        public ICommand DeactivateLicenseCommand { get; }
        public ICommand RefreshDevicesCommand { get; }
        public ICommand DeactivateDeviceCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand AccountLoginCommand { get; }
        public ICommand ForgotPasswordCommand { get; }
        public ICommand LogoutAccountCommand { get; }
        public ICommand LoginWithGoogleCommand { get; }
        public ICommand RetrieveLicenseCommand { get; }
        public ICommand SetKeyTabCommand { get; }
        public ICommand SetAccountTabCommand { get; }
        public ICommand OpenAccountDashboardCommand { get; }
        public ICommand ActivateThisDeviceCommand { get; }
        public ICommand BrowseRafContainerFileCommand { get; }
        public ICommand BrowseRafContainerFolderCommand { get; }
        public ICommand ClearRafContainerCommand { get; }
        public ICommand OpenPixlsUsCommand { get; }
        public ICommand OpenDngLabDbCommand { get; }
        public ICommand OpenLatestFailureLogCommand { get; }

        public Action<bool>? CloseAction { get; set; }

        public ICommand InstallLightroomPresetsCommand { get; }
        public ICommand ExportLightroomPresetsCommand { get; }
        public bool IsLightroomDirectoryDetected => _lightroomPresetService.IsLightroomDirectoryDetected;
        public string LightroomDetectedPath => _lightroomPresetService.DetectLightroomSettingsDirectory() ?? "Not detected";

        private bool _isInstallingPresets;
        public bool IsInstallingPresets
        {
            get => _isInstallingPresets;
            set => SetProperty(ref _isInstallingPresets, value);
        }

        private double _presetInstallProgress;
        public double PresetInstallProgress
        {
            get => _presetInstallProgress;
            set
            {
                if (SetProperty(ref _presetInstallProgress, value))
                    OnPropertyChanged(nameof(PresetInstallPercentText));
            }
        }

        private string _presetInstallStatusText = string.Empty;
        public string PresetInstallStatusText
        {
            get => _presetInstallStatusText;
            set => SetProperty(ref _presetInstallStatusText, value);
        }

        public string PresetInstallPercentText => $"{(int)_presetInstallProgress}%";

        public PreferencesViewModel(
            ISettingsService settingsService,
            ILicenseService? licenseService = null,
            IUpdateService? updateService = null,
            IDialogService? dialogService = null,
            ILightroomPresetService? lightroomPresetService = null)
        {
            _settingsService = settingsService;
            _licenseService = licenseService ?? LicenseService.Instance ?? new LicenseService(_settingsService);
            _updateService = updateService ?? new UpdateService(_settingsService);
            _dialogService = dialogService;
            _lightroomPresetService = lightroomPresetService ?? LightroomPresetService.Instance ?? new LightroomPresetService(_licenseService);
            var settings = _settingsService.Settings;

            _checkForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;

            bool wasPro = _licenseService.IsPro;
            _licenseService.LicenseChanged += (s, status) =>
            {
                bool becamePro = status.IsPro && !wasPro;
                wasPro = status.IsPro;

                if (status.IsPro)
                {
                    _losslessCompression = true;
                    _maxConcurrentConversions = DefaultConcurrency;
                }

                if (becamePro)
                {
                    _ = RefreshDevicesAsync();
                }
                else
                {
                    UpdateRegisteredDevicesCollection();
                }
                OnPropertyChanged(nameof(IsPro));
                OnPropertyChanged(nameof(LicenseStatusText));
                OnPropertyChanged(nameof(LicenseDescriptionText));
                OnPropertyChanged(nameof(IsWindowsContextMenuEnabled));
                OnPropertyChanged(nameof(LosslessCompression));
                OnPropertyChanged(nameof(MaxConcurrentConversions));
                OnPropertyChanged(nameof(ConcurrencyHint));
                OnPropertyChanged(nameof(IsAccountLoggedIn));
                OnPropertyChanged(nameof(AccountDisplayText));
                OnPropertyChanged(nameof(AccountDisplayName));
                OnPropertyChanged(nameof(SlotInfoText));
                OnPropertyChanged(nameof(ShowDeviceManagement));
                OnPropertyChanged(nameof(CanActivateThisDevice));
                OnPropertyChanged(nameof(HasAccountLicense));
            };

            _licenseService.DevicesChanged += (s, e) => UpdateRegisteredDevicesCollection();

            _language = LocalizationManager.NormalizeLanguageCode(settings.Language);
            _themeMode = string.IsNullOrEmpty(settings.ThemeMode)
                ? (settings.IsDarkTheme ? "Dark" : "Light")
                : settings.ThemeMode;
            _isDarkTheme = _themeMode != "Light";
            _backupOriginalFile = settings.BackupOriginalFile;
            _embedOriginalRaw = settings.EmbedOriginalRaw;
            _skipThumbnailGeneration = settings.SkipThumbnailGeneration;
            _autoOpenInLightroom = settings.AutoOpenInLightroom;
            _losslessCompression = settings.LosslessCompression;
            _appendSuffix = settings.AppendSuffix;
            _suffix = string.IsNullOrWhiteSpace(settings.Suffix) ? "-fujified" : settings.Suffix;
            _createSubfolder = settings.CreateSubfolder;
            _subfolderUseOriginalName = settings.SubfolderUseOriginalName;
            _subfolderSuffix = settings.SubfolderSuffix ?? "-fujified";
            _subfolderCustomName = string.IsNullOrWhiteSpace(settings.SubfolderCustomName) ? "Fujified" : settings.SubfolderCustomName;
            _autoOpenOutputFolder = settings.AutoOpenOutputFolder;
            _isWindowsContextMenuEnabled = OperatingSystem.IsWindows() && _licenseService.IsPro &&
                (WindowsContextMenuService.IsContextMenuRegistered() || settings.EnableWindowsContextMenu);

            int saved = settings.MaxConcurrentConversions;
            if (saved <= 0 || saved > TotalThreads) saved = DefaultConcurrency;
            _maxConcurrentConversions = saved;

            _enableCaptureOneMode = settings.EnableCaptureOneMode;
            _rafContainerPath = settings.RafContainerPath;
            LoadContainersFromSetting(settings.RafContainerPath);
            _exportFailedConversionsLog = settings.ExportFailedConversionsLog;
            _exportFailedConversionsLog = settings.ExportFailedConversionsLog;

            BrowseRafContainerFileCommand = new AsyncRelayCommand(ExecuteBrowseRafContainerFileAsync);
            BrowseRafContainerFolderCommand = new AsyncRelayCommand(ExecuteBrowseRafContainerFolderAsync);
            ClearRafContainerCommand = new RelayCommand(ClearAllContainers);
            OpenPixlsUsCommand = new RelayCommand(() => CrossPlatformLauncher.OpenUrl("https://raw.pixls.us/"));
            OpenDngLabDbCommand = new RelayCommand(() => CrossPlatformLauncher.OpenUrl("https://rawdb.dnglab.org/"));
            OpenLatestFailureLogCommand = new RelayCommand(() =>
            {
                string logPath = ConversionLogService.GetLatestFailureLogPath();
                if (File.Exists(logPath))
                {
                    CrossPlatformLauncher.OpenPath(logPath);
                }
            });

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() =>
            {
                LocalizationManager.Instance.SetLanguage(_settingsService.Settings.Language);
                App.ApplyTheme(_settingsService.Settings.ThemeMode);
                CloseAction?.Invoke(false);
            });

            UpgradeToProCommand = new RelayCommand(() =>
            {
                CrossPlatformLauncher.OpenUrl("https://fujifier.cc/pro");
            });

            InstallLightroomPresetsCommand = new AsyncRelayCommand(InstallLightroomPresetsAsync);
            ExportLightroomPresetsCommand = new AsyncRelayCommand(ExportLightroomPresetsAsync);

            SetKeyTabCommand = new RelayCommand(() => IsAccountTabActive = false);
            SetAccountTabCommand = new RelayCommand(() => IsAccountTabActive = true);

            LoginWithGoogleCommand = new AsyncRelayCommand(async () =>
            {
                if (IsGoogleLoggingIn) return;
                IsGoogleLoggingIn = true;
                LicenseMessageText = "Opening browser for Google Sign-In...";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;

                try
                {
                    var res = await _licenseService.LoginWithGoogleOAuthAsync(msg =>
                    {
                        LicenseMessageText = msg;
                        LicenseMessageColor = "#A0A0AC";
                        HasLicenseMessage = true;
                    });

                    if (res.Success)
                    {
                        if (_licenseService.IsPro)
                        {
                            _losslessCompression = true;
                            _maxConcurrentConversions = DefaultConcurrency;
                            _settingsService.Settings.LosslessCompression = true;
                            _settingsService.Settings.MaxConcurrentConversions = DefaultConcurrency;
                            _settingsService.Save();
                        }
                        string msg = res.ErrorMessage ?? "Signed in with Google and license synced!";
                        LicenseMessageText = $"✔ {msg}";
                        LicenseMessageColor = "#01D701";
                        HasLicenseMessage = true;
                        await ShowResultDialogAsync("Google Sign-In Successful", msg, isSuccess: true);
                    }
                    else
                    {
                        string msg = res.ErrorMessage ?? "Google Sign-In failed.";
                        LicenseMessageText = msg;
                        LicenseMessageColor = "#E74C3C";
                        HasLicenseMessage = true;
                    }
                }
                finally
                {
                    IsGoogleLoggingIn = false;
                }
            });

            RetrieveLicenseCommand = new AsyncRelayCommand(async () =>
            {
                if (IsRetrievingLicense) return;
                IsRetrievingLicense = true;
                LicenseMessageText = "Retrieving license from fujifier.cc...";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;

                try
                {
                    var res = await _licenseService.RetrieveLicenseFromAccountAsync();
                    if (res.Success && _licenseService.IsPro)
                    {
                        string msg = res.ErrorMessage ?? "Pro license retrieved and activated on this device!";
                        LicenseMessageText = $"✔ {msg}";
                        LicenseMessageColor = "#01D701";
                        HasLicenseMessage = true;
                        _losslessCompression = true;
                        _maxConcurrentConversions = DefaultConcurrency;
                        _settingsService.Settings.LosslessCompression = true;
                        _settingsService.Settings.MaxConcurrentConversions = DefaultConcurrency;
                        _settingsService.Save();
                        await ShowResultDialogAsync("License Retrieved", msg, isSuccess: true);
                    }
                    else if (res.Success)
                    {
                        string msg = res.ErrorMessage ?? "Checked account. No active Pro license found on fujifier.cc.";
                        LicenseMessageText = msg;
                        LicenseMessageColor = "#F39C12";
                        HasLicenseMessage = true;
                    }
                    else
                    {
                        string msg = res.ErrorMessage ?? "Failed to retrieve license.";
                        LicenseMessageText = msg;
                        LicenseMessageColor = "#E74C3C";
                        HasLicenseMessage = true;
                    }
                }
                finally
                {
                    IsRetrievingLicense = false;
                }
            });

            AccountLoginCommand = new AsyncRelayCommand(async () =>
            {
                if (string.IsNullOrWhiteSpace(AccountEmailInput) || string.IsNullOrWhiteSpace(AccountPasswordInput))
                {
                    LicenseMessageText = "Please enter both your account email and password.";
                    LicenseMessageColor = "#E74C3C";
                    HasLicenseMessage = true;
                    return;
                }

                LicenseMessageText = "Signing in to fujifier.cc...";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;

                var res = await _licenseService.LoginWithAccountAsync(AccountEmailInput, AccountPasswordInput);
                if (res.Success)
                {
                    if (_licenseService.IsPro)
                    {
                        _losslessCompression = true;
                        _maxConcurrentConversions = DefaultConcurrency;
                        _settingsService.Settings.LosslessCompression = true;
                        _settingsService.Settings.MaxConcurrentConversions = DefaultConcurrency;
                        _settingsService.Save();
                    }
                    string msg = res.ErrorMessage ?? "Signed in successfully and license synced!";
                    LicenseMessageText = $"✔ {msg}";
                    LicenseMessageColor = "#01D701";
                    HasLicenseMessage = true;
                    AccountPasswordInput = string.Empty;
                    await ShowResultDialogAsync("Sign-In Successful", msg, isSuccess: true);
                }
                else
                {
                    string msg = res.ErrorMessage ?? "Authentication failed.";
                    LicenseMessageText = msg;
                    LicenseMessageColor = "#E74C3C";
                    HasLicenseMessage = true;
                }
            });

            ForgotPasswordCommand = new AsyncRelayCommand(async () =>
            {
                if (string.IsNullOrWhiteSpace(AccountEmailInput))
                {
                    LicenseMessageText = "Please enter your account email above first.";
                    LicenseMessageColor = "#F39C12";
                    HasLicenseMessage = true;
                    return;
                }

                LicenseMessageText = "Sending password reset link...";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;

                var (success, msg) = await _licenseService.RequestPasswordResetAsync(AccountEmailInput.Trim());
                LicenseMessageText = success ? $"✔ {msg}" : msg;
                LicenseMessageColor = success ? "#01D701" : "#E74C3C";
                HasLicenseMessage = true;
            });

            LogoutAccountCommand = new AsyncRelayCommand(async () =>
            {
                await _licenseService.LogoutAccountAsync();
                LicenseMessageText = "Disconnected from Fujifier account.";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;
            });

            ActivateLicenseCommand = new AsyncRelayCommand(async () =>
            {
                if (string.IsNullOrWhiteSpace(LicenseKeyInput))
                {
                    LicenseMessageText = "Please enter your license key (e.g. FUJI-XXXX-XXXX-XXXX).";
                    LicenseMessageColor = "#E74C3C";
                    HasLicenseMessage = true;
                    return;
                }

                LicenseMessageText = "Verifying license and activating device slot...";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;

                var res = await _licenseService.ActivateLicenseKeyAsync(LicenseKeyInput);
                if (res.Success)
                {
                    _losslessCompression = true;
                    _maxConcurrentConversions = DefaultConcurrency;
                    _settingsService.Settings.LosslessCompression = true;
                    _settingsService.Settings.MaxConcurrentConversions = DefaultConcurrency;
                    _settingsService.Save();
                    LicenseMessageText = "✔ Pro license activated on this device!";
                    LicenseMessageColor = "#01D701";
                    HasLicenseMessage = true;
                    LicenseKeyInput = string.Empty;
                    await ShowResultDialogAsync("Activation Successful", "Pro license has been verified and activated on this device!", isSuccess: true);
                }
                else
                {
                    string msg = res.ErrorMessage ?? "Invalid license key.";
                    LicenseMessageText = msg;
                    LicenseMessageColor = "#E74C3C";
                    HasLicenseMessage = true;
                }
            });

            DeactivateLicenseCommand = new AsyncRelayCommand(async () =>
            {
                var dialogService = _dialogService ?? new DialogService(_settingsService, _licenseService);
                bool confirmed = await dialogService.ShowConfirmationAsync(
                    OwnerWindow,
                    LocalizationManager.Tr("ConfirmDeactivateDeviceTitle"),
                    LocalizationManager.TrFormat("ConfirmDeactivateDeviceMessage", _licenseService.RegisteredDevices.FirstOrDefault(d => string.Equals(d.DeviceId, _licenseService.CurrentDeviceId, StringComparison.OrdinalIgnoreCase))?.DeviceName ?? "this device"),
                    LocalizationManager.Tr("Deactivate"),
                    LocalizationManager.Tr("Cancel"));

                if (!confirmed) return;

                LicenseMessageText = LocalizationManager.Tr("DeactivatingDevice");
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;

                await _licenseService.DeactivateLicenseAsync();
                _losslessCompression = false;
                _maxConcurrentConversions = 1;
                _settingsService.Settings.LosslessCompression = false;
                _settingsService.Settings.MaxConcurrentConversions = 1;
                _settingsService.Save();
                UpdateRegisteredDevicesCollection();
                LicenseMessageText = "License deactivated. Device slot freed on fujifier.cc.";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;
            });

            RefreshDevicesCommand = new AsyncRelayCommand(RefreshDevicesAsync);
            DeactivateDeviceCommand = new AsyncRelayCommand<LicenseDeviceItemViewModel>(DeactivateDeviceAsync);

            OpenAccountDashboardCommand = new RelayCommand(() =>
            {
                CrossPlatformLauncher.OpenUrl(_licenseService.AccountDashboardUrl);
            });

            ActivateThisDeviceCommand = new AsyncRelayCommand(async () =>
            {
                LicenseMessageText = "Activating Pro license on this device...";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;

                var res = await _licenseService.ActivateCurrentDeviceAsync();
                if (res.Success && _licenseService.IsPro)
                {
                    _losslessCompression = true;
                    _maxConcurrentConversions = DefaultConcurrency;
                    _settingsService.Settings.LosslessCompression = true;
                    _settingsService.Settings.MaxConcurrentConversions = DefaultConcurrency;
                    _settingsService.Save();

                    string msg = res.ErrorMessage ?? "Pro license successfully activated on this device!";
                    LicenseMessageText = $"✔ {msg}";
                    LicenseMessageColor = "#01D701";
                    HasLicenseMessage = true;
                    UpdateRegisteredDevicesCollection();
                    await ShowResultDialogAsync("Device Activated", msg, isSuccess: true);
                }
                else
                {
                    string msg = res.ErrorMessage ?? "Could not activate this device. Please check your available device slots.";
                    LicenseMessageText = msg;
                    LicenseMessageColor = "#E74C3C";
                    HasLicenseMessage = true;
                    await ShowResultDialogAsync("Activation Failed", msg, isSuccess: false);
                }
            });

            CheckForUpdatesCommand = new AsyncRelayCommand(async () =>
            {
                IsCheckingForUpdate = true;
                UpdateStatusMessage = "Checking GitHub releases...";
                UpdateStatusColor = "#A0A0AC";
                HasUpdateStatusMessage = true;

                try
                {
                    var result = await _updateService.CheckForUpdateAsync(force: true);
                    if (result.IsUpdateAvailable && result.UpdateInfo != null)
                    {
                        UpdateStatusMessage = $"Update available: {result.UpdateInfo.TagName}!";
                        UpdateStatusColor = "#01D701";

                        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            await UpdateDialog.ShowUpdateDialogAsync(desktop.MainWindow, result.UpdateInfo, _updateService);
                        }
                        else
                        {
                            await UpdateDialog.ShowUpdateDialogAsync(null, result.UpdateInfo, _updateService);
                        }
                    }
                    else if (result.Success && !result.IsUpdateAvailable)
                    {
                        UpdateStatusMessage = $"You're on the latest version ({_updateService.CurrentVersionString}).";
                        UpdateStatusColor = "#01D701";
                    }
                    else
                    {
                        UpdateStatusMessage = result.ErrorMessage ?? "Could not check for updates.";
                        UpdateStatusColor = "#E74C3C";
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatusMessage = $"Error: {ex.Message}";
                    UpdateStatusColor = "#E74C3C";
                }
                finally
                {
                    IsCheckingForUpdate = false;
                }
            });

            UpdateRegisteredDevicesCollection();
            if (_licenseService.IsPro || _licenseService.IsAccountLoggedIn)
            {
                _ = RefreshDevicesAsync();
            }
        }

        private void Save()
        {
            var s = _settingsService.Settings;
            s.Language = _language;
            LocalizationManager.Instance.SetLanguage(_language);
            s.ThemeMode = _themeMode;
            s.BackupOriginalFile = _backupOriginalFile;
            s.EmbedOriginalRaw = _embedOriginalRaw;
            s.SkipThumbnailGeneration = _skipThumbnailGeneration;
            s.LosslessCompression = _licenseService.IsPro && _losslessCompression;
            s.MaxConcurrentConversions = _licenseService.IsPro ? _maxConcurrentConversions : 1;
            s.AutoOpenInLightroom = _autoOpenInLightroom;
            s.AppendSuffix = _appendSuffix;
            s.Suffix = string.IsNullOrWhiteSpace(_suffix) ? "-fujified" : _suffix.Trim();
            s.CreateSubfolder = IsSubfolderOptionSupported && _createSubfolder;
            s.SubfolderUseOriginalName = _subfolderUseOriginalName;
            s.SubfolderSuffix = _subfolderSuffix;
            s.SubfolderCustomName = string.IsNullOrWhiteSpace(_subfolderCustomName) ? "Fujified" : _subfolderCustomName.Trim();
            s.AutoOpenOutputFolder = _autoOpenOutputFolder;
            s.ExportFailedConversionsLog = _exportFailedConversionsLog;
            s.CheckForUpdatesOnStartup = _checkForUpdatesOnStartup;
            s.EnableCaptureOneMode = _enableCaptureOneMode;
            if (RafContainers.Count > 0)
            {
                _rafContainerPath = string.Join(";", RafContainers.Select(c => c.FilePath));
            }
            s.RafContainerPath = string.IsNullOrWhiteSpace(_rafContainerPath) ? null : _rafContainerPath.Trim();

            if (OperatingSystem.IsWindows())
            {
                if (_isWindowsContextMenuEnabled && _licenseService.IsPro)
                {
                    WindowsContextMenuService.RegisterContextMenu();
                    s.EnableWindowsContextMenu = true;
                }
                else if (s.EnableWindowsContextMenu || WindowsContextMenuService.IsContextMenuRegistered())
                {
                    WindowsContextMenuService.UnregisterContextMenu();
                    s.EnableWindowsContextMenu = false;
                }
            }

            _settingsService.Save();
            App.ApplyTheme(_themeMode);

            CloseAction?.Invoke(true);
        }

        public void RefreshFromSettings()
        {
            var settings = _settingsService.Settings;
            _checkForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
            _language = LocalizationManager.NormalizeLanguageCode(settings.Language);
            _themeMode = string.IsNullOrEmpty(settings.ThemeMode)
                ? (settings.IsDarkTheme ? "Dark" : "Light")
                : settings.ThemeMode;
            _isDarkTheme = _themeMode != "Light";
            _backupOriginalFile = settings.BackupOriginalFile;
            _embedOriginalRaw = settings.EmbedOriginalRaw;
            _skipThumbnailGeneration = settings.SkipThumbnailGeneration;
            _autoOpenInLightroom = settings.AutoOpenInLightroom;
            _losslessCompression = settings.LosslessCompression;
            _appendSuffix = settings.AppendSuffix;
            _suffix = string.IsNullOrWhiteSpace(settings.Suffix) ? "-fujified" : settings.Suffix;
            _createSubfolder = settings.CreateSubfolder;
            _subfolderUseOriginalName = settings.SubfolderUseOriginalName;
            _subfolderSuffix = settings.SubfolderSuffix ?? "-fujified";
            _subfolderCustomName = string.IsNullOrWhiteSpace(settings.SubfolderCustomName) ? "Fujified" : settings.SubfolderCustomName;
            _autoOpenOutputFolder = settings.AutoOpenOutputFolder;
            _isWindowsContextMenuEnabled = OperatingSystem.IsWindows() && _licenseService.IsPro &&
                (WindowsContextMenuService.IsContextMenuRegistered() || settings.EnableWindowsContextMenu);

            int saved = settings.MaxConcurrentConversions;
            if (saved <= 0 || saved > TotalThreads) saved = DefaultConcurrency;
            _maxConcurrentConversions = saved;

            _enableCaptureOneMode = settings.EnableCaptureOneMode;
            _rafContainerPath = settings.RafContainerPath;
            LoadContainersFromSetting(settings.RafContainerPath);
            _exportFailedConversionsLog = settings.ExportFailedConversionsLog;

            UpdateRegisteredDevicesCollection();
            if (_licenseService.IsPro || _licenseService.IsAccountLoggedIn)
            {
                _ = RefreshDevicesAsync();
            }

            OnPropertyChanged(string.Empty);
        }

        private void UpdateRegisteredDevicesCollection()
        {
            if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
            {
                ApplyRegisteredDevices();
            }
            else
            {
                Dispatcher.UIThread.Post(ApplyRegisteredDevices);
            }

            void ApplyRegisteredDevices()
            {
                RegisteredDevices.Clear();
                string curId = _licenseService.CurrentDeviceId;
                foreach (var d in _licenseService.RegisteredDevices)
                {
                    RegisteredDevices.Add(new LicenseDeviceItemViewModel(d, string.Equals(d.DeviceId, curId, StringComparison.OrdinalIgnoreCase)));
                }
                OnPropertyChanged(nameof(HasRegisteredDevices));
                OnPropertyChanged(nameof(ShowDeviceManagement));
                OnPropertyChanged(nameof(CanActivateThisDevice));
                OnPropertyChanged(nameof(HasAccountLicense));
                OnPropertyChanged(nameof(SlotInfoText));
                OnPropertyChanged(nameof(LicenseDescriptionText));
            }
        }

        public async Task RefreshDevicesAsync()
        {
            if (IsLoadingDevices) return;
            if (!_licenseService.IsPro && !_licenseService.IsAccountLoggedIn)
            {
                UpdateRegisteredDevicesCollection();
                return;
            }

            IsLoadingDevices = true;
            try
            {
                await _licenseService.FetchRegisteredDevicesAsync();
                UpdateRegisteredDevicesCollection();
            }
            finally
            {
                IsLoadingDevices = false;
            }
        }

        private async Task DeactivateDeviceAsync(LicenseDeviceItemViewModel? deviceItem)
        {
            if (deviceItem == null) return;

            var dialogService = _dialogService ?? new DialogService(_settingsService, _licenseService);
            bool confirmed = await dialogService.ShowConfirmationAsync(
                OwnerWindow,
                LocalizationManager.Tr("ConfirmDeactivateDeviceTitle"),
                LocalizationManager.TrFormat("ConfirmDeactivateDeviceMessage", deviceItem.DeviceName),
                LocalizationManager.Tr("Deactivate"),
                LocalizationManager.Tr("Cancel"));

            if (!confirmed) return;

            LicenseMessageText = LocalizationManager.Tr("DeactivatingDevice");
            LicenseMessageColor = "#A0A0AC";
            HasLicenseMessage = true;

            bool success = await _licenseService.DeactivateRemoteDeviceAsync(deviceItem.DeviceId);
            if (success)
            {
                if (deviceItem.IsCurrentDevice)
                {
                    _losslessCompression = false;
                    _maxConcurrentConversions = 1;
                    _settingsService.Settings.LosslessCompression = false;
                    _settingsService.Settings.MaxConcurrentConversions = 1;
                    _settingsService.Save();
                }

                string msg = LocalizationManager.TrFormat("DeviceDeactivatedSuccess", deviceItem.DeviceName);
                LicenseMessageText = $"✔ {msg}";
                LicenseMessageColor = "#01D701";
                HasLicenseMessage = true;
                UpdateRegisteredDevicesCollection();
                await ShowResultDialogAsync(LocalizationManager.Tr("ConfirmDeactivateDeviceTitle"), msg, isSuccess: true);
            }
            else
            {
                string errorMsg = "Failed to deactivate device on server. Please check your internet connection.";
                LicenseMessageText = errorMsg;
                LicenseMessageColor = "#E74C3C";
                HasLicenseMessage = true;
                await ShowResultDialogAsync("Deactivation Failed", errorMsg, isSuccess: false);
            }
        }

        private async Task InstallLightroomPresetsAsync()
        {
            if (!_licenseService.IsPro)
            {
                await ShowResultDialogAsync(
                    "Fujifier Pro Feature",
                    "Automatic 1-click Lightroom preset installation is exclusive to Fujifier Pro.\n\nUpgrade to Pro to unlock instant preset installation directly into Adobe Lightroom & Camera Raw.",
                    isSuccess: false);
                return;
            }

            try
            {
                IsInstallingPresets = true;
                PresetInstallProgress = 0;
                PresetInstallStatusText = "Preparing presets...";

                var progress = new Progress<(int current, int total, string presetName)>(info =>
                {
                    PresetInstallProgress = (double)info.current / info.total * 100.0;
                    PresetInstallStatusText = $"Installing {info.presetName} ({info.current}/{info.total})...";
                });

                PresetInstallResult result = null!;
                await Task.Run(async () =>
                {
                    string? baseDir = _lightroomPresetService.DetectLightroomSettingsDirectory();
                    if (string.IsNullOrEmpty(baseDir))
                    {
                        result = new PresetInstallResult(false, 0, null, "Adobe Camera Raw / Lightroom settings directory could not be located on this system. You can use 'Export Presets (.zip)' to save the preset pack manually.");
                        return;
                    }

                    string targetDir = Path.Combine(baseDir, "Fujifier Film Simulations");
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    int count = 0;
                    int total = _lightroomPresetService.Presets.Count;
                    foreach (var preset in _lightroomPresetService.Presets)
                    {
                        string xml = _lightroomPresetService.GeneratePresetXml(preset);
                        string path = Path.Combine(targetDir, preset.FileName);
                        File.WriteAllText(path, xml, System.Text.Encoding.UTF8);
                        count++;
                        ((IProgress<(int, int, string)>)progress).Report((count, total, preset.DisplayName));
                        await Task.Delay(25);
                    }

                    result = new PresetInstallResult(true, count, targetDir, null);
                });

                if (result.Success)
                {
                    string msg = $"Successfully installed {result.Count} Fujifilm film simulation presets into Adobe Lightroom & Camera Raw!\n\n" +
                                 $"Installed folder:\n{result.TargetPath}\n\n" +
                                 $"Restart or open Lightroom/Camera Raw to see them in your Develop Presets panel under 'Fujifilm Film Simulations (Fujifier)'.\n\n" +
                                 $"⚠️ Important Notice: Photos must be converted to DNG with Fujifier beforehand for Lightroom to recognize the Fujifilm camera profiles.";
                    await ShowResultDialogAsync("Lightroom Presets Installed", msg, isSuccess: true);
                }
                else
                {
                    await ShowResultDialogAsync("Installation Failed", result.ErrorMessage ?? "Could not install presets to Lightroom.", isSuccess: false);
                }
            }
            finally
            {
                IsInstallingPresets = false;
                PresetInstallProgress = 0;
                PresetInstallStatusText = string.Empty;
            }
        }

        private async Task ExportLightroomPresetsAsync()
        {
            if (!_licenseService.IsPro)
            {
                await ShowResultDialogAsync(
                    "Fujifier Pro Feature",
                    "Exporting Lightroom preset packs is exclusive to Fujifier Pro.\n\nUpgrade to Pro to unlock preset exports for Lightroom Mobile, iPad, and desktop.",
                    isSuccess: false);
                return;
            }

            try
            {
                IsInstallingPresets = true;
                PresetInstallProgress = 0;
                PresetInstallStatusText = "Exporting preset pack...";

                string targetDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
                {
                    targetDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
                if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
                {
                    targetDir = Path.GetTempPath();
                }

                string zipPath = Path.Combine(targetDir, "Fujifier-Lightroom-Presets.zip");

                var progress = new Progress<(int current, int total, string presetName)>(info =>
                {
                    PresetInstallProgress = (double)info.current / info.total * 100.0;
                    PresetInstallStatusText = $"Packaging {info.presetName} ({info.current}/{info.total})...";
                });

                byte[] zipBytes = null!;
                await Task.Run(async () =>
                {
                    zipBytes = _lightroomPresetService.GeneratePresetsZip(progress);
                    await Task.Delay(100);
                });

                File.WriteAllBytes(zipPath, zipBytes);

                string msg = $"Lightroom Presets exported successfully!\n\n" +
                             $"Saved file:\n{zipPath}\n\n" +
                             $"You can import this .zip file directly into Lightroom Classic, Lightroom CC, or Lightroom Mobile on iOS/Android.\n\n" +
                             $"⚠️ Important Notice: Photos must be converted to DNG using Fujifier beforehand for Fujifilm simulation profiles to take effect.";
                await ShowResultDialogAsync("Presets Exported", msg, isSuccess: true);
            }
            catch (Exception ex)
            {
                await ShowResultDialogAsync("Export Failed", $"Failed to export presets: {ex.Message}", isSuccess: false);
            }
            finally
            {
                IsInstallingPresets = false;
                PresetInstallProgress = 0;
                PresetInstallStatusText = string.Empty;
            }
        }

        private void LoadContainersFromSetting(string? setting)
        {
            RafContainers.Clear();
            if (string.IsNullOrWhiteSpace(setting))
            {
                OnPropertyChanged(nameof(HasRafContainers));
                OnPropertyChanged(nameof(HasRafContainerPath));
                OnPropertyChanged(nameof(RafContainerCountText));
                OnPropertyChanged(nameof(RafContainerStatusText));
                return;
            }

            var parts = setting.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                AddContainerPathInternal(part, save: false);
            }
            OnPropertyChanged(nameof(HasRafContainers));
            OnPropertyChanged(nameof(HasRafContainerPath));
            OnPropertyChanged(nameof(RafContainerCountText));
            OnPropertyChanged(nameof(RafContainerStatusText));
        }

        private void AddContainerPathInternal(string path, bool save = true)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string cleanPath = path.Trim('\"', '\'', ' ');
            if (string.IsNullOrWhiteSpace(cleanPath)) return;

            if (Directory.Exists(cleanPath))
            {
                var rafFiles = Directory.GetFiles(cleanPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".raf", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (var file in rafFiles)
                {
                    AddSingleContainerFile(file);
                }
                if (rafFiles.Length == 0)
                {
                    AddSingleContainerFile(cleanPath);
                }
            }
            else
            {
                AddSingleContainerFile(cleanPath);
            }

            if (save)
            {
                SyncContainersToSetting();
            }
        }

        private void AddSingleContainerFile(string filePath)
        {
            if (RafContainers.Any(c => string.Equals(c.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                return;

            string camera = "Fujifilm";
            if (File.Exists(filePath))
            {
                var inspection = RafConverterService.InspectContainerStatic(filePath);
                if (!string.IsNullOrWhiteSpace(inspection.CameraModel))
                {
                    camera = inspection.Width > 0 && inspection.Height > 0
                        ? $"{inspection.CameraModel} ({inspection.Width} × {inspection.Height})"
                        : inspection.CameraModel;
                }
                else
                {
                    camera = RawMetadataReader.TryGetCameraMakeAndModel(filePath) ?? "Fujifilm Camera";
                }
            }
            else if (Directory.Exists(filePath))
            {
                camera = "Folder";
            }

            RafContainers.Add(new RafContainerItemViewModel(filePath, camera, RemoveContainerItem));
        }

        private void RemoveContainerItem(RafContainerItemViewModel item)
        {
            RafContainers.Remove(item);
            SyncContainersToSetting();
        }

        private void ClearAllContainers()
        {
            RafContainers.Clear();
            SyncContainersToSetting();
        }

        private void SyncContainersToSetting()
        {
            _rafContainerPath = RafContainers.Count > 0 ? string.Join(";", RafContainers.Select(c => c.FilePath)) : string.Empty;
            _settingsService.Settings.RafContainerPath = string.IsNullOrWhiteSpace(_rafContainerPath) ? null : _rafContainerPath.Trim();
            _settingsService.Save();
            OnPropertyChanged(nameof(RafContainerPath));
            OnPropertyChanged(nameof(HasRafContainerPath));
            OnPropertyChanged(nameof(HasRafContainers));
            OnPropertyChanged(nameof(RafContainerCountText));
            OnPropertyChanged(nameof(RafContainerStatusText));
        }

        private async Task ExecuteBrowseRafContainerFileAsync()
        {
            var dialogService = _dialogService ?? new DialogService(_settingsService, _licenseService);
            var files = await dialogService.ShowOpenFileDialogAsync(OwnerWindow, LocalizationManager.Tr("RafContainerFilePickerTitle"), allowMultiple: true);
            if (files != null && files.Count > 0)
            {
                await AddContainerPathsAsync(files);
            }
        }

        private async Task ExecuteBrowseRafContainerFolderAsync()
        {
            var dialogService = _dialogService ?? new DialogService(_settingsService, _licenseService);
            var folder = await dialogService.ShowFolderBrowserDialogAsync(OwnerWindow, LocalizationManager.Tr("RafContainerFolderPickerTitle"));
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                var rafFiles = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".raf", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (rafFiles.Count == 0)
                {
                    await ShowResultDialogAsync(
                        LocalizationManager.Tr("RafContainerNoneFoundTitle"),
                        LocalizationManager.Tr("RafContainerNoneFoundMessage"),
                        isSuccess: false);
                    return;
                }

                await AddContainerPathsAsync(rafFiles);
            }
        }

        private async Task AddContainerPathsAsync(IEnumerable<string> paths)
        {
            var skippedXTrans = new List<string>();
            var skippedInvalid = new List<string>();
            int addedCount = 0;

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                string cleanPath = path.Trim('\"', '\'', ' ');
                if (!File.Exists(cleanPath)) continue;

                // Inspect binary file data directly (checking CFA Meta Directory for X-Trans 6x6 pattern vs Bayer)
                var inspection = RafConverterService.InspectContainerStatic(cleanPath);

                if (inspection.SensorType == RafSensorType.XTrans)
                {
                    string desc = !string.IsNullOrWhiteSpace(inspection.CameraModel)
                        ? $"{Path.GetFileName(cleanPath)} ({inspection.CameraModel})"
                        : Path.GetFileName(cleanPath);
                    skippedXTrans.Add(desc);
                    continue;
                }

                if (!inspection.IsValid || inspection.SensorType != RafSensorType.Bayer)
                {
                    skippedInvalid.Add(Path.GetFileName(cleanPath));
                    continue;
                }

                if (RafContainers.Any(c => string.Equals(c.FilePath, cleanPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string cameraDisplay = !string.IsNullOrWhiteSpace(inspection.CameraModel)
                    ? (inspection.Width > 0 && inspection.Height > 0
                        ? $"{inspection.CameraModel} ({inspection.Width} × {inspection.Height})"
                        : inspection.CameraModel)
                    : (RawMetadataReader.TryGetCameraMakeAndModel(cleanPath) ?? "Fujifilm Bayer");

                RafContainers.Add(new RafContainerItemViewModel(cleanPath, cameraDisplay, RemoveContainerItem));
                addedCount++;
            }

            if (addedCount > 0)
            {
                SyncContainersToSetting();
            }

            if (skippedXTrans.Count > 0)
            {
                string skippedList = string.Join("\n• ", skippedXTrans.Take(5)) +
                    (skippedXTrans.Count > 5 ? $"\n...and {skippedXTrans.Count - 5} more" : "");

                if (addedCount == 0)
                {
                    await ShowResultDialogAsync(
                        LocalizationManager.Tr("RafContainerXTransRejectedTitle"),
                        LocalizationManager.TrFormat("RafContainerXTransRejectedMessage", skippedList),
                        isSuccess: false);
                }
                else
                {
                    await ShowResultDialogAsync(
                        LocalizationManager.Tr("RafContainerPartialSkippedTitle"),
                        LocalizationManager.TrFormat("RafContainerPartialSkippedMessage", addedCount, skippedList),
                        isSuccess: true);
                }
            }
            else if (skippedInvalid.Count > 0 && addedCount == 0)
            {
                await ShowResultDialogAsync(
                    LocalizationManager.Tr("RafContainerInvalidTitle"),
                    LocalizationManager.Tr("RafContainerInvalidMessage"),
                    isSuccess: false);
            }
        }

        private async Task ShowResultDialogAsync(string title, string message, bool isSuccess)
        {
            try
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowMessageAsync(OwnerWindow, title, message, isSuccess);
                    return;
                }

                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var win = OwnerWindow ?? System.Linq.Enumerable.LastOrDefault(desktop.Windows, w => w.IsActive && w.IsVisible) ?? desktop.MainWindow;
                    var dialog = new Views.MessageDialog(title, message, isSuccess);
                    if (win != null)
                    {
                        await dialog.ShowDialog(win);
                    }
                    else
                    {
                        dialog.Show();
                    }
                }
                else
                {
                    MainViewModel.Instance?.ShowMessageModal(title, message, isSuccess);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreferencesViewModel] Failed to display result dialog: {ex.Message}");
            }
        }
    }

    public class RafContainerItemViewModel : ViewModelBase
    {
        public string FilePath { get; }
        public string FileName => Path.GetFileName(FilePath);
        public string CameraModel { get; }
        public string DisplayText => string.IsNullOrWhiteSpace(CameraModel)
            ? FileName
            : $"{FileName} - {CameraModel}";

        public ICommand RemoveCommand { get; }

        public RafContainerItemViewModel(string filePath, string cameraModel, Action<RafContainerItemViewModel> onRemove)
        {
            FilePath = filePath;
            CameraModel = cameraModel;
            RemoveCommand = new RelayCommand(() => onRemove(this));
        }
    }
}
