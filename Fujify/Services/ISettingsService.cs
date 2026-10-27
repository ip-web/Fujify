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

namespace Fujify.Services
{
    public class AppSettings
    {
        public string ThemeMode { get; set; } = "Automatic"; // "Automatic", "Dark", "Amoled", "Light"

        public bool IsDarkTheme
        {
            get => !ThemeMode.Equals("Light", System.StringComparison.OrdinalIgnoreCase);
            set => ThemeMode = value ? (ThemeMode.Equals("Amoled", System.StringComparison.OrdinalIgnoreCase) ? "Amoled" : "Dark") : "Light";
        }
        public double WindowHeight { get; set; } = 770;
        public double WindowWidth { get; set; } = 700;
        public bool BatchProcessing { get; set; } = false;
        public bool EmbedOriginalRaw { get; set; } = false;
        public int MaxConcurrentConversions { get; set; } = 0;
        public bool SkipThumbnailGeneration { get; set; } = false;
        public bool AutoOpenInLightroom { get; set; } = false;
        public bool AppendSuffix { get; set; } = false;
        public string Suffix { get; set; } = "-fujified";
        public bool LosslessCompression { get; set; } = true;
        public bool EnableWindowsContextMenu { get; set; } = false;
        public string? LicenseKey { get; set; }
        public string? LicenseeEmail { get; set; }
        public string? LicenseSignature { get; set; }
        public string? DeviceId { get; set; }
        public string? OfflineToken { get; set; }
        public System.DateTime? OfflineTokenExpires { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? AccountEmail { get; set; }
        public string? AccountDisplayName { get; set; }
        public int MaxActivations { get; set; } = 3;
        public int UsedActivations { get; set; } = 0;
        public string LicenseApiBaseUrl { get; set; } = "https://fujifier.cc/wp-json/fujifier/v1";
        public bool CheckForUpdatesOnStartup { get; set; } = true;
        public string? SkippedVersion { get; set; }
        public System.DateTime? LastUpdateCheck { get; set; }
    }

    public interface ISettingsService
    {
        AppSettings Settings { get; }
        void Save();
        void Load();
    }
}
