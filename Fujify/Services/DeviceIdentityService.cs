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
using System.Security.Cryptography;
using System.Text;

namespace Fujify.Services
{
    public class DeviceIdentityService
    {
        private readonly ISettingsService _settingsService;

        public DeviceIdentityService(ISettingsService? settingsService = null)
        {
            _settingsService = settingsService ?? new SettingsService();
        }

        public string GetOrCreateDeviceId()
        {
            var stored = _settingsService.Settings.DeviceId;
            if (!string.IsNullOrWhiteSpace(stored))
            {
                return stored;
            }

            string newId = GenerateHardwareGuid();
            _settingsService.Settings.DeviceId = newId;
            _settingsService.Save();
            return newId;
        }

        public string GetDeviceName()
        {
            try
            {
                string machine = Environment.MachineName;
                if (!string.IsNullOrWhiteSpace(machine))
                {
                    return machine;
                }
            }
            catch { }

            return "Fujifier Device";
        }

        public string GetPlatform()
        {
            if (OperatingSystem.IsWindows()) return "windows";
            if (OperatingSystem.IsMacOS()) return "macos";
            if (OperatingSystem.IsLinux()) return "linux";
            if (OperatingSystem.IsAndroid()) return "android";
            if (OperatingSystem.IsIOS()) return "ios";
            return "desktop";
        }

        private static string GenerateHardwareGuid()
        {
            try
            {
                string seed = $"{Environment.MachineName}-{Environment.OSVersion.Platform}-{Guid.NewGuid()}";
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
                byte[] guidBytes = new byte[16];
                Array.Copy(hash, guidBytes, 16);
                return new Guid(guidBytes).ToString("D");
            }
            catch
            {
                return Guid.NewGuid().ToString("D");
            }
        }
    }
}
