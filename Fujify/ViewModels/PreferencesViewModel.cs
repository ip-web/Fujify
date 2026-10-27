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
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fujify.Common;
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

        public Avalonia.Controls.Window? OwnerWindow { get; set; }

        private bool _isDarkTheme;
        private bool _embedOriginalRaw;
        private bool _skipThumbnailGeneration;
        private bool _losslessCompression = true;
        private int _maxConcurrentConversions;
        private bool _autoOpenInLightroom;
        private bool _appendSuffix;
        private string _suffix = "-fujified";
        private bool _isWindowsContextMenuEnabled;
        private string _themeMode = "Automatic";
        private bool _checkForUpdatesOnStartup = true;

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

        public int TotalThreads => Environment.ProcessorCount;
        public int DefaultConcurrency => Math.Max(1, TotalThreads / 2);

        public bool IsPro => _licenseService.IsPro;

        public string LicenseStatusText => _licenseService.IsPro
            ? "Fujifier Pro (Activated)"
            : "Fujifier Free Tier";

        public string LicenseDescriptionText => _licenseService.IsPro
            ? (!string.IsNullOrWhiteSpace(_licenseService.CurrentStatus.LicenseeEmail)
                ? $"Licensed to: {_licenseService.CurrentStatus.LicenseeEmail} ({SlotInfoText})"
                : $"Lifetime Pro License Active ({SlotInfoText})")
            : "Queue limited to 10 photos, 15s cooldown timer, context menu locked.";

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

        public bool SkipThumbnailGeneration
        {
            get => _skipThumbnailGeneration;
            set => SetProperty(ref _skipThumbnailGeneration, value);
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
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand AccountLoginCommand { get; }
        public ICommand LogoutAccountCommand { get; }
        public ICommand LoginWithGoogleCommand { get; }
        public ICommand RetrieveLicenseCommand { get; }
        public ICommand SetKeyTabCommand { get; }
        public ICommand SetAccountTabCommand { get; }

        public Action<bool>? CloseAction { get; set; }

        public PreferencesViewModel(
            ISettingsService settingsService,
            ILicenseService? licenseService = null,
            IUpdateService? updateService = null,
            IDialogService? dialogService = null)
        {
            _settingsService = settingsService;
            _licenseService = licenseService ?? new LicenseService(_settingsService);
            _updateService = updateService ?? new UpdateService(_settingsService);
            _dialogService = dialogService;
            var settings = _settingsService.Settings;

            _checkForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;

            _licenseService.LicenseChanged += (s, status) =>
            {
                if (status.IsPro)
                {
                    _losslessCompression = true;
                    _maxConcurrentConversions = DefaultConcurrency;
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
            };

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
            _isWindowsContextMenuEnabled = OperatingSystem.IsWindows() && _licenseService.IsPro &&
                (WindowsContextMenuService.IsContextMenuRegistered() || settings.EnableWindowsContextMenu);

            int saved = settings.MaxConcurrentConversions;
            if (saved <= 0 || saved > TotalThreads) saved = DefaultConcurrency;
            _maxConcurrentConversions = saved;

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() =>
            {
                App.ApplyTheme(_settingsService.Settings.ThemeMode);
                CloseAction?.Invoke(false);
            });

            UpgradeToProCommand = new RelayCommand(() =>
            {
                CrossPlatformLauncher.OpenUrl("https://fujifier.cc/pro");
            });

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
                        await ShowResultDialogAsync("Google Sign-In Failed", msg, isSuccess: false);
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
                        await ShowResultDialogAsync("License Retrieved", msg, isSuccess: true);
                    }
                    else if (res.Success)
                    {
                        string msg = res.ErrorMessage ?? "Checked account. No active Pro license found on fujifier.cc.";
                        LicenseMessageText = msg;
                        LicenseMessageColor = "#F39C12";
                        HasLicenseMessage = true;
                        await ShowResultDialogAsync("No Active License", msg, isSuccess: false);
                    }
                    else
                    {
                        string msg = res.ErrorMessage ?? "Failed to retrieve license.";
                        LicenseMessageText = msg;
                        LicenseMessageColor = "#E74C3C";
                        HasLicenseMessage = true;
                        await ShowResultDialogAsync("License Retrieval Failed", msg, isSuccess: false);
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
                    await ShowResultDialogAsync("Sign-In Incomplete", "Please enter both your account email and password.", isSuccess: false);
                    return;
                }

                LicenseMessageText = "Signing in to fujifier.cc...";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;

                var res = await _licenseService.LoginWithAccountAsync(AccountEmailInput, AccountPasswordInput);
                if (res.Success)
                {
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
                    await ShowResultDialogAsync("Sign-In Failed", msg, isSuccess: false);
                }
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
                    await ShowResultDialogAsync("License Key Required", "Please enter your license key (e.g. FUJI-XXXX-XXXX-XXXX).", isSuccess: false);
                    return;
                }

                LicenseMessageText = "Verifying license and activating device slot...";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;

                var res = await _licenseService.ActivateLicenseKeyAsync(LicenseKeyInput);
                if (res.Success)
                {
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
                    await ShowResultDialogAsync("Activation Failed", msg, isSuccess: false);
                }
            });

            DeactivateLicenseCommand = new AsyncRelayCommand(async () =>
            {
                LicenseMessageText = "Deactivating license and releasing device slot...";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;

                await _licenseService.DeactivateLicenseAsync();
                LicenseMessageText = "License deactivated. Device slot freed on fujifier.cc.";
                LicenseMessageColor = "#A0A0AC";
                HasLicenseMessage = true;
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
        }

        private void Save()
        {
            var s = _settingsService.Settings;
            s.ThemeMode = _themeMode;
            s.EmbedOriginalRaw = _embedOriginalRaw;
            s.SkipThumbnailGeneration = _skipThumbnailGeneration;
            s.LosslessCompression = _licenseService.IsPro && _losslessCompression;
            s.MaxConcurrentConversions = _licenseService.IsPro ? _maxConcurrentConversions : 1;
            s.AutoOpenInLightroom = _autoOpenInLightroom;
            s.AppendSuffix = _appendSuffix;
            s.Suffix = string.IsNullOrWhiteSpace(_suffix) ? "-fujified" : _suffix.Trim();
            s.CheckForUpdatesOnStartup = _checkForUpdatesOnStartup;

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
                    MainViewModel.Instance?.ShowMessageModal(title, message);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreferencesViewModel] Failed to display result dialog: {ex.Message}");
            }
        }
    }
}
