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

namespace Fujify.Models
{
    public class UpdateAsset
    {
        public string Name { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public long Size { get; set; }
        public string ContentType { get; set; } = string.Empty;

        public string FormattedSize
        {
            get
            {
                if (Size <= 0) return string.Empty;
                double mb = Size / (1024.0 * 1024.0);
                return $"{mb:F1} MB";
            }
        }
    }

    public class UpdateInfo
    {
        public string TagName { get; set; } = string.Empty;
        public Version? Version { get; set; }
        public string VersionString => Version?.ToString() ?? TagName.TrimStart('v', 'V');
        public string Title { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
        public List<UpdateAsset> Assets { get; set; } = new();

        public UpdateAsset? MatchingAsset { get; set; }

        public bool IsInstallerAvailable => MatchingAsset != null && !string.IsNullOrWhiteSpace(MatchingAsset.DownloadUrl);
    }

    public class UpdateCheckResult
    {
        public bool Success { get; }
        public bool IsUpdateAvailable { get; }
        public UpdateInfo? UpdateInfo { get; }
        public string CurrentVersion { get; }
        public string? ErrorMessage { get; }

        public UpdateCheckResult(bool success, bool isUpdateAvailable, UpdateInfo? updateInfo, string currentVersion, string? errorMessage = null)
        {
            Success = success;
            IsUpdateAvailable = isUpdateAvailable;
            UpdateInfo = updateInfo;
            CurrentVersion = currentVersion;
            ErrorMessage = errorMessage;
        }

        public static UpdateCheckResult UpToDate(string currentVersion, UpdateInfo? info = null) =>
            new(true, false, info, currentVersion);

        public static UpdateCheckResult UpdateAvailable(UpdateInfo info, string currentVersion) =>
            new(true, true, info, currentVersion);

        public static UpdateCheckResult Failure(string errorMessage, string currentVersion) =>
            new(false, false, null, currentVersion, errorMessage);
    }
}
