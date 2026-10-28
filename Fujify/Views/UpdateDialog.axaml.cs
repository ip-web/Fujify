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
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fujify.Common;
using Fujify.Models;
using Fujify.Services;
using Fujify.ViewModels;

namespace Fujify.Views
{
    public partial class UpdateDialog : Window
    {
        private readonly UpdateInfo _updateInfo;
        private readonly IUpdateService _updateService;
        private readonly ISettingsService _settingsService;
        private readonly CancellationTokenSource _cts = new();
        private bool _isDownloading = false;

        public UpdateDialog() : this(new UpdateInfo(), new UpdateService(), new SettingsService())
        {
        }

        public UpdateDialog(UpdateInfo updateInfo, IUpdateService? updateService = null, ISettingsService? settingsService = null)
        {
            InitializeComponent();
            _updateInfo = updateInfo;
            _settingsService = settingsService ?? new SettingsService();
            _updateService = updateService ?? new UpdateService(_settingsService);

            VersionBadgeText.Text = $"v{_updateInfo.VersionString}";
            CurrentVsNewText.Text = $"Current: v{_updateService.CurrentVersionString} • Latest: v{_updateInfo.VersionString}";
            ReleaseTitleText.Text = string.IsNullOrWhiteSpace(_updateInfo.Title) ? _updateInfo.TagName : _updateInfo.Title;
            ReleaseDateText.Text = _updateInfo.PublishedAt?.ToLocalTime().ToString("MMM dd, yyyy") ?? string.Empty;

            string notes = _updateInfo.ReleaseNotes;
            if (string.IsNullOrWhiteSpace(notes))
            {
                notes = "No release notes were provided for this release. Visit GitHub to read the complete commit history.";
            }
            ReleaseNotesText.Text = notes;

            if (_updateInfo.MatchingAsset != null)
            {
                DownloadStatusText.Text = $"Package: {_updateInfo.MatchingAsset.Name} ({_updateInfo.MatchingAsset.FormattedSize})";
                InstallButton.Content = "Download & Install";
            }
            else
            {
                DownloadStatusText.Text = "Direct installer package not found. Click below to view release assets on GitHub.";
                InstallButton.Content = "View on GitHub";
            }

            Closed += (s, e) =>
            {
                if (_isDownloading)
                {
                    _cts.Cancel();
                }
            };
        }

        private async void Install_Click(object? sender, RoutedEventArgs e)
        {
            if (_updateInfo.MatchingAsset == null || string.IsNullOrWhiteSpace(_updateInfo.MatchingAsset.DownloadUrl))
            {
                CrossPlatformLauncher.OpenUrl(_updateInfo.ReleaseUrl);
                Close();
                return;
            }

            try
            {
                _isDownloading = true;
                InstallButton.IsEnabled = false;
                DownloadProgressBar.IsVisible = true;
                DownloadProgressBar.Value = 0;
                DownloadErrorText.IsVisible = false;
                DownloadStatusText.Text = "Downloading update package from GitHub...";

                var progress = new Progress<double>(ratio =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        DownloadProgressBar.Value = ratio * 100.0;
                        DownloadPercentText.Text = $"{(int)(ratio * 100)}%";
                    });
                });

                string downloadedFilePath = await _updateService.DownloadUpdateAsync(_updateInfo.MatchingAsset, progress, _cts.Token);

                DownloadStatusText.Text = "Launching installer...";
                DownloadPercentText.Text = "100%";

                bool launched = _updateService.LaunchInstallerAndExit(downloadedFilePath);
                if (launched)
                {
                    await Task.Delay(500);
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                }
                else
                {
                    DownloadErrorText.Text = "Failed to launch installer automatically. The file has been downloaded to your temporary folder.";
                    DownloadErrorText.IsVisible = true;
                    InstallButton.IsEnabled = true;
                }
            }
            catch (OperationCanceledException)
            {
                DownloadStatusText.Text = "Download canceled.";
                InstallButton.IsEnabled = true;
                DownloadProgressBar.IsVisible = false;
            }
            catch (Exception ex)
            {
                DownloadErrorText.Text = $"Download failed: {ex.Message}";
                DownloadErrorText.IsVisible = true;
                InstallButton.IsEnabled = true;
                DownloadStatusText.Text = "An error occurred during update download.";
            }
            finally
            {
                _isDownloading = false;
            }
        }

        private void SkipVersion_Click(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_updateInfo.VersionString))
            {
                _settingsService.Settings.SkippedVersion = _updateInfo.VersionString;
                _settingsService.Save();
            }
            Close();
        }

        private void Later_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        public static async Task ShowUpdateDialogAsync(Window? owner, UpdateInfo updateInfo, IUpdateService? updateService = null, ISettingsService? settingsService = null)
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || (owner == null && desktop.MainWindow == null))
            {
                // Mobile or single-view fallback
                if (MainViewModel.Instance != null)
                {
                    MainViewModel.Instance.ShowMessageModal(
                        "New Version Available",
                        $"A new version of Fujifier is available: v{updateInfo.VersionString}\n\nVisit GitHub to download the latest release.");
                }
                return;
            }

            var dialog = new UpdateDialog(updateInfo, updateService, settingsService);

            if (owner != null && owner.IsVisible)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }
        }

        private void Window_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e.Source is Button || (e.Source is Avalonia.Visual visual && visual.FindAncestorOfType<Button>() != null))
            {
                return;
            }

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }
    }
}
