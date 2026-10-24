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
        public const string VerbName = "Fujifier";
        public const string MenuText = "Fujifier";

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
                path = Path.Combine(AppContext.BaseDirectory, "Fujifier.exe");
                if (!File.Exists(path))
                {
                    path = Path.Combine(AppContext.BaseDirectory, "Fujify.exe");
                }
            }

            return Path.GetFullPath(path);
        }

        public static bool IsContextMenuRegistered()
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                string exePath = GetExecutablePath();
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\.arw\shell\Fujifier\shell\01_queue\command");
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
                string iconValue = $"\"{exePath}\",0";

                foreach (var ext in RawExtensions)
                {
                    RegisterExtensionVerb(@"Software\Classes\SystemFileAssociations\" + ext, exePath, iconValue);
                    RegisterExtensionVerb(@"Software\Classes\" + ext, exePath, iconValue);
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
        private static void RegisterExtensionVerb(string basePath, string exePath, string iconValue)
        {
            try
            {
                // Clean any existing key to prevent collisions between legacy flat and new cascading structures
                UnregisterExtensionVerb(basePath);

                string verbKeyPath = $@"{basePath}\shell\{VerbName}";
                using var verbKey = Registry.CurrentUser.CreateSubKey(verbKeyPath, true);
                if (verbKey != null)
                {
                    verbKey.SetValue(null, MenuText);
                    verbKey.SetValue("MUIVerb", MenuText);
                    verbKey.SetValue("Icon", iconValue);
                    verbKey.SetValue("SubCommands", string.Empty);

                    using var shellKey = verbKey.CreateSubKey("shell", true);
                    if (shellKey != null)
                    {
                        // 1. Add to queue
                        using var qKey = shellKey.CreateSubKey("01_queue", true);
                        if (qKey != null)
                        {
                            qKey.SetValue(null, "Add to queue");
                            qKey.SetValue("MUIVerb", "Add to queue");
                            qKey.SetValue("Icon", iconValue);
                            using var cmd = qKey.CreateSubKey("command", true);
                            cmd?.SetValue(null, $"\"{exePath}\" --queue \"%1\"");
                        }

                        // 2. Process with Fujifier (default config)
                        using var pKey = shellKey.CreateSubKey("02_process", true);
                        if (pKey != null)
                        {
                            pKey.SetValue(null, "Process with Fujifier");
                            pKey.SetValue("MUIVerb", "Process with Fujifier");
                            pKey.SetValue("Icon", iconValue);
                            using var cmd = pKey.CreateSubKey("command", true);
                            cmd?.SetValue(null, $"\"{exePath}\" --process \"%1\"");
                        }

                        // 3. Process with Fujifier to... (specific destination folder)
                        using var ptKey = shellKey.CreateSubKey("03_processto", true);
                        if (ptKey != null)
                        {
                            ptKey.SetValue(null, "Process with Fujifier to...");
                            ptKey.SetValue("MUIVerb", "Process with Fujifier to...");
                            ptKey.SetValue("Icon", iconValue);
                            using var cmd = ptKey.CreateSubKey("command", true);
                            cmd?.SetValue(null, $"\"{exePath}\" --process-to \"%1\"");
                        }
                    }
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
                // Also clean up legacy Fujify key if present
                Registry.CurrentUser.DeleteSubKeyTree($@"{basePath}\shell\Fujify", throwOnMissingSubKey: false);
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
