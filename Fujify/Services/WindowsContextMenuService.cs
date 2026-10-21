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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Fujify.Services
{
    public static class WindowsContextMenuService
    {
        public const string VerbName = "Fujify";
        public const string MenuText = "Open in Fujify";

        public static readonly string[] RawExtensions = new[]
        {
            ".arw", ".cr2", ".cr3", ".crw", ".dng", ".nef", ".nrw", ".orf",
            ".raf", ".rw2", ".pef", ".srw", ".srf", ".sr2", ".3fr", ".erf",
            ".kdc", ".dcs", ".dcr", ".iiq", ".mos", ".mef", ".mrw"
        };

        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        public static bool IsSupported => OperatingSystem.IsWindows();

        public static string GetExecutablePath()
        {
            string? path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                try
                {
                    path = Process.GetCurrentProcess().MainModule?.FileName;
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "Fujify.exe");
            }

            return Path.GetFullPath(path);
        }

        public static bool IsContextMenuRegistered()
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                string exePath = GetExecutablePath();
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\.arw\shell\Fujify\command");
                if (key != null)
                {
                    var val = key.GetValue(null)?.ToString();
                    if (!string.IsNullOrWhiteSpace(val) && val.Contains(Path.GetFileName(exePath), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsContextMenuService] Error checking registry: {ex.Message}");
            }

            return false;
        }

        public static bool RegisterContextMenu()
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                string exePath = GetExecutablePath();
                string commandValue = $"\"{exePath}\" \"%1\"";
                string iconValue = $"\"{exePath}\",0";

                foreach (var ext in RawExtensions)
                {
                    RegisterExtensionVerb(@"Software\Classes\SystemFileAssociations\" + ext, commandValue, iconValue);
                    RegisterExtensionVerb(@"Software\Classes\" + ext, commandValue, iconValue);
                }

                NotifyShell();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsContextMenuService] Error registering context menu: {ex.Message}");
                return false;
            }
        }

        public static bool UnregisterContextMenu()
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                foreach (var ext in RawExtensions)
                {
                    UnregisterExtensionVerb(@"Software\Classes\SystemFileAssociations\" + ext);
                    UnregisterExtensionVerb(@"Software\Classes\" + ext);
                }

                NotifyShell();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsContextMenuService] Error unregistering context menu: {ex.Message}");
                return false;
            }
        }

        [SupportedOSPlatform("windows")]
        private static void RegisterExtensionVerb(string basePath, string commandValue, string iconValue)
        {
            try
            {
                string verbKeyPath = $@"{basePath}\shell\{VerbName}";
                using var verbKey = Registry.CurrentUser.CreateSubKey(verbKeyPath, true);
                if (verbKey != null)
                {
                    verbKey.SetValue(null, MenuText);
                    verbKey.SetValue("Icon", iconValue);

                    using var cmdKey = verbKey.CreateSubKey("command", true);
                    cmdKey?.SetValue(null, commandValue);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsContextMenuService] Failed on {basePath}: {ex.Message}");
            }
        }

        [SupportedOSPlatform("windows")]
        private static void UnregisterExtensionVerb(string basePath)
        {
            try
            {
                string verbKeyPath = $@"{basePath}\shell\{VerbName}";
                Registry.CurrentUser.DeleteSubKeyTree(verbKeyPath, throwOnMissingSubKey: false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsContextMenuService] Failed deleting {basePath}: {ex.Message}");
            }
        }

        private static void NotifyShell()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }
        }
    }
}
