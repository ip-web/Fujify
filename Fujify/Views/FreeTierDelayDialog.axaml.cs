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
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Fujify.Common;
using Fujify.Services;
using Fujify.ViewModels;

namespace Fujify.Views
{
    public partial class FreeTierDelayDialog : Window
    {
        private readonly ILicenseService _licenseService;
        private readonly DispatcherTimer _timer;
        private int _remainingSeconds = 15;
        private bool _isUnlocked = false;

        public FreeTierDelayDialog() : this(new LicenseService(), false)
        {
        }

        public FreeTierDelayDialog(ILicenseService licenseService, bool isPostConversion = false)
        {
            InitializeComponent();
            _licenseService = licenseService;

            if (isPostConversion)
            {
                TitleText.Text = "Conversion Finished (Free Tier)";
                MessageText.Text = "Your photos have been processed! Under the Free tier, a 15-second cooldown is required after conversions. Upgrade to Fujifier Pro to enjoy instant batch processing, unlimited queues, and Windows Explorer integration.";
            }

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;

            Loaded += (s, e) =>
            {
                CountdownProgressBar.Maximum = 15;
                CountdownProgressBar.Value = 0;
                _timer.Start();
            };

            Closed += (s, e) =>
            {
                _timer.Stop();
            };
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _remainingSeconds--;

            if (_remainingSeconds > 0)
            {
                ContinueButton.Content = $"Continue with Free Version ({_remainingSeconds}s)";
                CountdownStatusText.Text = $"Cooldown active: {_remainingSeconds}s remaining...";
                CountdownProgressBar.Value = 15 - _remainingSeconds;
            }
            else
            {
                _timer.Stop();
                ContinueButton.IsEnabled = true;
                ContinueButton.Content = "Continue with Free Version";
                CountdownStatusText.Text = "Cooldown finished. You may now continue.";
                CountdownProgressBar.Value = 15;
            }
        }

        private void Continue_Click(object? sender, RoutedEventArgs e)
        {
            _timer.Stop();
            Close(_isUnlocked);
        }

        private void Upgrade_Click(object? sender, RoutedEventArgs e)
        {
            CrossPlatformLauncher.OpenUrl("https://fujifier.net/pro");
        }

        private void ToggleLicenseKey_Click(object? sender, RoutedEventArgs e)
        {
            LicenseKeySection.IsVisible = !LicenseKeySection.IsVisible;
            if (LicenseKeySection.IsVisible)
            {
                LicenseKeyInput.Focus();
            }
        }

        private async void ActivateKey_Click(object? sender, RoutedEventArgs e)
        {
            string key = LicenseKeyInput.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                LicenseActivationError.Text = "Please enter a valid license key.";
                LicenseActivationError.IsVisible = true;
                return;
            }

            var result = await _licenseService.ActivateLicenseKeyAsync(key);
            if (result.Success)
            {
                _isUnlocked = true;
                _timer.Stop();
                LicenseActivationError.IsVisible = false;
                TitleText.Text = "✔ Pro Unlocked!";
                MessageText.Text = "Thank you for upgrading to Fujifier Pro! All tier limits, countdown delays, and advertisements have been removed permanently.";
                ContinueButton.IsEnabled = true;
                ContinueButton.Content = "Start Using Fujifier Pro";
                ContinueButton.Classes.Add("accent");
                CountdownProgressBar.Value = 15;
                CountdownStatusText.Text = "License active.";
                LicenseKeySection.IsVisible = false;
                ToggleKeyButton.IsVisible = false;
            }
            else
            {
                LicenseActivationError.Text = result.ErrorMessage ?? "Invalid license key.";
                LicenseActivationError.IsVisible = true;
            }
        }

        /// <summary>
        /// Displays the 15-second cooldown modal if the user is on the Free tier.
        /// Returns true if user is Pro or successfully unlocked Pro.
        /// </summary>
        public static async Task<bool> ShowDelayDialogAsync(Window? owner, ILicenseService licenseService, bool isPostConversion = false)
        {
            if (licenseService.IsPro)
            {
                return true;
            }

            // Mobile / ISingleViewApplicationLifetime check: Window cannot be created or shown on Android/iOS
            if (Avalonia.Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                || (owner == null && desktop.MainWindow == null))
            {
                if (MainViewModel.Instance != null)
                {
                    return await ShowMobileDelayAsync(MainViewModel.Instance, licenseService, isPostConversion);
                }

                await Task.Delay(15000);
                return licenseService.IsPro;
            }

            var dialog = new FreeTierDelayDialog(licenseService, isPostConversion);

            if (owner != null && owner.IsVisible)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>();
                dialog.Closed += (s, e) => tcs.TrySetResult(licenseService.IsPro);
                dialog.Show();
                await tcs.Task;
            }

            return licenseService.IsPro;
        }

        private static async Task<bool> ShowMobileDelayAsync(MainViewModel vm, ILicenseService licenseService, bool isPostConversion)
        {
            string title = isPostConversion ? "Conversion Finished (Free Tier)" : "Welcome to Fujifier (Free Tier)";
            for (int sec = 15; sec > 0; sec--)
            {
                if (licenseService.IsPro) break;
                vm.ShowMessageModal(title, $"Free tier active (Queue limited to 10 files).\n\nCooldown timer: {sec}s remaining before you can continue...\n\nUpgrade to Fujifier Pro to remove all limits!");
                await Task.Delay(1000);
            }

            if (!licenseService.IsPro)
            {
                vm.ShowMessageModal(title, "Cooldown finished. You can now use Fujifier Free.\n\nTip: Upgrade to Fujifier Pro in Preferences to unlock unlimited queues and instant processing!");
            }

            return licenseService.IsPro;
        }
    }
}
