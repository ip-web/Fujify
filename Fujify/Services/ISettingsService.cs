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
    }

    public interface ISettingsService
    {
        AppSettings Settings { get; }
        void Save();
        void Load();
    }
}
