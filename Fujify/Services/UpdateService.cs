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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fujify.Common;
using Fujify.Models;

namespace Fujify.Services
{
    public class UpdateService : IUpdateService
    {
        public const string PrimaryRepo = "ip-web/Fujify";
        public const string FallbackRepo = "ip-web/Fujifier";

        private readonly ISettingsService _settingsService;
        private readonly HttpClient _httpClient;

        public static readonly Version FallbackVersion = new(2, 1, 0);

        public Version CurrentVersion
        {
            get
            {
                var asmVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (asmVersion != null && asmVersion >= FallbackVersion)
                {
                    return new Version(asmVersion.Major, asmVersion.Minor, Math.Max(0, asmVersion.Build));
                }
                return FallbackVersion;
            }
        }

        public string CurrentVersionString => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

        public UpdateService(ISettingsService? settingsService = null, HttpClient? httpClient = null)
        {
            _settingsService = settingsService ?? new SettingsService();
            _httpClient = httpClient ?? new HttpClient();

            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _httpClient.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("Fujifier-Updater", CurrentVersionString));
            }
        }

        public async Task<UpdateCheckResult> CheckForUpdateAsync(bool force = false, CancellationToken ct = default)
        {
            try
            {
                _settingsService.Settings.LastUpdateCheck = DateTime.UtcNow;
                _settingsService.Save();

                var release = await FetchLatestReleaseAsync(PrimaryRepo, ct) 
                           ?? await FetchLatestReleaseAsync(FallbackRepo, ct);

                if (release == null)
                {
                    return UpdateCheckResult.Failure("Could not retrieve latest release information from GitHub.", CurrentVersionString);
                }

                if (release.Version == null)
                {
                    return UpdateCheckResult.Failure($"Could not parse release version from tag '{release.TagName}'.", CurrentVersionString);
                }

                bool isNewer = release.Version > CurrentVersion;

                if (!isNewer)
                {
                    return UpdateCheckResult.UpToDate(CurrentVersionString, release);
                }

                // Check if user previously chose to skip this specific version (unless forced)
                if (!force && !string.IsNullOrWhiteSpace(_settingsService.Settings.SkippedVersion))
                {
                    if (string.Equals(_settingsService.Settings.SkippedVersion, release.VersionString, StringComparison.OrdinalIgnoreCase))
                    {
                        return UpdateCheckResult.UpToDate(CurrentVersionString, release);
                    }
                }

                return UpdateCheckResult.UpdateAvailable(release, CurrentVersionString);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Error checking for updates: {ex.Message}");
                return UpdateCheckResult.Failure($"Update check failed: {ex.Message}", CurrentVersionString);
            }
        }

        private async Task<UpdateInfo?> FetchLatestReleaseAsync(string repo, CancellationToken ct)
        {
            try
            {
                string url = $"https://api.github.com/repos/{repo}/releases/latest";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[UpdateService] GitHub API returned status {response.StatusCode} for repo {repo}");
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync(ct);
                return ParseReleaseJson(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Error fetching release from {repo}: {ex.Message}");
                return null;
            }
        }

        public static UpdateInfo? ParseReleaseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Check for draft or prerelease
                if (root.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean())
                {
                    return null;
                }

                string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? string.Empty : string.Empty;
                string name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
                string htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;
                string body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;

                DateTime? publishedAt = null;
                if (root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var dt))
                {
                    publishedAt = dt;
                }

                var info = new UpdateInfo
                {
                    TagName = tagName,
                    Version = ParseVersion(tagName),
                    Title = string.IsNullOrWhiteSpace(name) ? tagName : name,
                    ReleaseNotes = body,
                    ReleaseUrl = htmlUrl,
                    PublishedAt = publishedAt
                };

                if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var assetElem in assetsProp.EnumerateArray())
                    {
                        string assetName = assetElem.TryGetProperty("name", out var aName) ? aName.GetString() ?? string.Empty : string.Empty;
                        string downloadUrl = assetElem.TryGetProperty("browser_download_url", out var aUrl) ? aUrl.GetString() ?? string.Empty : string.Empty;
                        long size = assetElem.TryGetProperty("size", out var aSize) ? aSize.GetInt64() : 0;
                        string contentType = assetElem.TryGetProperty("content_type", out var aType) ? aType.GetString() ?? string.Empty : string.Empty;

                        if (!string.IsNullOrWhiteSpace(assetName) && !string.IsNullOrWhiteSpace(downloadUrl))
                        {
                            info.Assets.Add(new UpdateAsset
                            {
                                Name = assetName,
                                DownloadUrl = downloadUrl,
                                Size = size,
                                ContentType = contentType
                            });
                        }
                    }
                }

                info.MatchingAsset = SelectMatchingAsset(info.Assets);
                return info;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Error parsing release JSON: {ex.Message}");
                return null;
            }
        }

        public static Version? ParseVersion(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string clean = raw.Trim().TrimStart('v', 'V');
            int dashIndex = clean.IndexOfAny(new[] { '-', '+' });
            if (dashIndex > 0)
            {
                clean = clean.Substring(0, dashIndex);
            }

            var parts = clean.Split('.');
            if (parts.Length == 1 && int.TryParse(parts[0], out int major))
            {
                return new Version(major, 0, 0);
            }
            if (parts.Length == 2 && int.TryParse(parts[0], out major) && int.TryParse(parts[1], out int minor))
            {
                return new Version(major, minor, 0);
            }
            if (Version.TryParse(clean, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        public static UpdateAsset? SelectMatchingAsset(IEnumerable<UpdateAsset> assets)
        {
            var list = assets?.ToList();
            if (list == null || list.Count == 0) return null;

            if (OperatingSystem.IsWindows())
            {
                // Prefer installer/setup executable
                var setupExe = list.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                                        (a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                                                         a.Name.Contains("installer", StringComparison.OrdinalIgnoreCase)));
                if (setupExe != null) return setupExe;

                var anyExe = list.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                if (anyExe != null) return anyExe;

                return list.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                                (a.Name.Contains("win", StringComparison.OrdinalIgnoreCase) ||
                                                 a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase)));
            }

            if (OperatingSystem.IsMacOS())
            {
                // Prefer macOS zip or dmg matching architecture
                bool isArm = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64;
                string archKeyword = isArm ? "arm" : "intel";

                var matchedArch = list.FirstOrDefault(a => (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || a.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)) &&
                                                           a.Name.Contains(archKeyword, StringComparison.OrdinalIgnoreCase));
                if (matchedArch != null) return matchedArch;

                return list.FirstOrDefault(a => (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || a.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)) &&
                                                (a.Name.Contains("mac", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("osx", StringComparison.OrdinalIgnoreCase)));
            }

            if (OperatingSystem.IsLinux())
            {
                return list.FirstOrDefault(a => a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) &&
                                                (a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                                                 a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)));
            }

            return list.FirstOrDefault();
        }

        public async Task<string> DownloadUpdateAsync(UpdateAsset asset, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.DownloadUrl))
            {
                throw new ArgumentException("Asset download URL is invalid.", nameof(asset));
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "Fujifier_Updates");
            Directory.CreateDirectory(tempDir);
            string destinationPath = Path.Combine(tempDir, asset.Name);

            using var response = await _httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
            long totalRead = 0;

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            byte[] buffer = new byte[81920];
            int read;

            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, ct);
                totalRead += read;

                if (totalBytes > 0 && progress != null)
                {
                    double ratio = Math.Clamp((double)totalRead / totalBytes, 0.0, 1.0);
                    progress.Report(ratio);
                }
            }

            progress?.Report(1.0);
            return destinationPath;
        }

        public bool LaunchInstallerAndExit(string installerFilePath)
        {
            if (string.IsNullOrWhiteSpace(installerFilePath) || !File.Exists(installerFilePath))
            {
                return false;
            }

            try
            {
                if (OperatingSystem.IsWindows() && installerFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = installerFilePath,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    return true;
                }

                // Fallback for non-windows or archives: open container directory / launcher
                CrossPlatformLauncher.OpenUrl(installerFilePath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Failed to launch installer: {ex.Message}");
                return false;
            }
        }
    }
}
