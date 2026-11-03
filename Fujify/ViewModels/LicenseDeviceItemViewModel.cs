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
using CommunityToolkit.Mvvm.ComponentModel;
using Fujify.Models;

namespace Fujify.ViewModels
{
    public class LicenseDeviceItemViewModel : ObservableObject
    {
        public LicenseDeviceDto Device { get; }
        public bool IsCurrentDevice { get; }

        public LicenseDeviceItemViewModel(LicenseDeviceDto device, bool isCurrentDevice)
        {
            Device = device;
            IsCurrentDevice = isCurrentDevice;
        }

        public string DeviceId => Device.DeviceId;

        public string DeviceName => !string.IsNullOrWhiteSpace(Device.DeviceName)
            ? Device.DeviceName
            : "Unknown Device";

        public string Platform => Device.Platform ?? string.Empty;

        public string? AppVersion => Device.AppVersion;

        public string? LastCheckIn => Device.LastCheckIn;

        public string PlatformIcon
        {
            get
            {
                string p = Platform.ToLowerInvariant();
                if (p.Contains("ios") || p.Contains("iphone") || p.Contains("ipad") || p.Contains("apple"))
                    return "📱";
                if (p.Contains("android"))
                    return "🤖";
                if (p.Contains("mac") || p.Contains("darwin") || p.Contains("osx"))
                    return "💻";
                if (p.Contains("win"))
                    return "💻";
                if (p.Contains("linux"))
                    return "🐧";
                return "💻";
            }
        }

        public string PlatformDisplay
        {
            get
            {
                string p = Platform.ToLowerInvariant();
                if (p.Contains("ios") || p.Contains("iphone") || p.Contains("ipad"))
                    return "iOS";
                if (p.Contains("android"))
                    return "Android";
                if (p.Contains("mac") || p.Contains("darwin") || p.Contains("osx"))
                    return "macOS";
                if (p.Contains("win"))
                    return "Windows";
                if (p.Contains("linux"))
                    return "Linux";
                return "Desktop";
            }
        }

        public string FormattedLastSeen
        {
            get
            {
                if (IsCurrentDevice) return "Active now";

                if (!string.IsNullOrWhiteSpace(LastCheckIn) && DateTime.TryParse(LastCheckIn, out var dt))
                {
                    var span = DateTime.UtcNow - dt.ToUniversalTime();
                    if (span.TotalMinutes < 5) return "Active now";
                    if (span.TotalHours < 1) return $"Active {(int)span.TotalMinutes}m ago";
                    if (span.TotalDays < 1) return $"Active {(int)span.TotalHours}h ago";
                    if (span.TotalDays < 7) return $"Active {(int)span.TotalDays}d ago";
                    return $"Active {dt:yyyy-MM-dd}";
                }

                return !string.IsNullOrWhiteSpace(LastCheckIn) ? $"Active {LastCheckIn}" : "Active";
            }
        }

        public string DisplaySubtitle => $"{PlatformDisplay} • v{AppVersion ?? "2.0"} • {FormattedLastSeen}";
    }
}

