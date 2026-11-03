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

        public static bool IsOpenWithRegistered()
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                string exePath = GetExecutablePath();
                string exeName = Path.GetFileName(exePath);
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Applications\{exeName}\shell\open\command");
                if (key != null)
                {
                    var val = key.GetValue(null)?.ToString();
                    if (!string.IsNullOrWhiteSpace(val) && val.Contains(exeName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsContextMenuService] Error checking Open With: {ex.Message}");
            }

            return false;
        }

        public static bool RegisterOpenWith()
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                string exePath = GetExecutablePath();
                string exeName = Path.GetFileName(exePath);
                string iconValue = $"\"{exePath}\",0";
                string openCommand = $"\"{exePath}\" \"%1\"";

                // 1. HKCU\Software\Classes\Applications\<exeName>
                using (var appKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{exeName}", true))
                {
                    if (appKey != null)
                    {
                        appKey.SetValue(null, "Fujifier");
                        appKey.SetValue("FriendlyAppName", "Fujifier");

                        using var iconKey = appKey.CreateSubKey("DefaultIcon", true);
                        iconKey?.SetValue(null, iconValue);

                        using var cmdKey = appKey.CreateSubKey(@"shell\open\command", true);
                        cmdKey?.SetValue(null, openCommand);

                        using var suppTypes = appKey.CreateSubKey("SupportedTypes", true);
                        if (suppTypes != null)
                        {
                            foreach (var ext in RawExtensions)
                            {
                                suppTypes.SetValue(ext, string.Empty);
                            }
                        }
                    }
                }

                // 2. ProgID: HKCU\Software\Classes\Fujifier.RAW
                using (var progIdKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Fujifier.RAW", true))
                {
                    if (progIdKey != null)
                    {
                        progIdKey.SetValue(null, "Camera RAW & DNG Image");
                        progIdKey.SetValue("FriendlyTypeName", "Camera RAW & DNG Image");

                        using var iconKey = progIdKey.CreateSubKey("DefaultIcon", true);
                        iconKey?.SetValue(null, iconValue);

                        using var cmdKey = progIdKey.CreateSubKey(@"shell\open\command", true);
                        cmdKey?.SetValue(null, openCommand);
                    }
                }

                // 3. Register OpenWithList and OpenWithProgids for all supported formats
                foreach (var ext in RawExtensions)
                {
                    try
                    {
                        using var owlKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithList\{exeName}", true);
                        using var owpKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
                        owpKey?.SetValue("Fujifier.RAW", string.Empty);

                        using var sfaOwlKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\OpenWithList\{exeName}", true);
                        using var sfaOwpKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\OpenWithProgids", true);
                        sfaOwpKey?.SetValue("Fujifier.RAW", string.Empty);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WindowsContextMenuService] Failed associating ext {ext}: {ex.Message}");
                    }
                }

                NotifyShell();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsContextMenuService] Error registering Open With: {ex.Message}");
                return false;
            }
        }

        public static bool UnregisterOpenWith()
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                string exePath = GetExecutablePath();
                string exeName = Path.GetFileName(exePath);

                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{exeName}", throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Fujifier.RAW", throwOnMissingSubKey: false);

                foreach (var ext in RawExtensions)
                {
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ext}\OpenWithList\{exeName}", throwOnMissingSubKey: false);
                        using var owp = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
                        owp?.DeleteValue("Fujifier.RAW", throwOnMissingValue: false);

                        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{ext}\OpenWithList\{exeName}", throwOnMissingSubKey: false);
                        using var sfaOwp = Registry.CurrentUser.OpenSubKey($@"Software\Classes\SystemFileAssociations\{ext}\OpenWithProgids", true);
                        sfaOwp?.DeleteValue("Fujifier.RAW", throwOnMissingValue: false);
                    }
                    catch { }
                }

                NotifyShell();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsContextMenuService] Error unregistering Open With: {ex.Message}");
                return false;
            }
        }

        public static bool RegisterContextMenu()
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                string exePath = GetExecutablePath();
                string iconValue = $"{exePath},0";

                // 1. Create centralized cascading menu structure under HKCU\Software\Classes\Fujifier.Menu\shell
                using (var menuShellKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Fujifier.Menu\shell", true))
                {
                    if (menuShellKey != null)
                    {
                        PopulateSubCommands(menuShellKey, exePath, iconValue);
                    }
                }

                // 2. Register under SystemFileAssociations for all supported RAW extensions
                foreach (var ext in RawExtensions)
                {
                    // Clean any broken direct extension keys (e.g. HKCU\Software\Classes\.arw\shell\Fujifier)
                    UnregisterExtensionVerb(@"Software\Classes\" + ext);

                    // Register proper SystemFileAssociations verb
                    RegisterSystemFileAssociationVerb(@"Software\Classes\SystemFileAssociations\" + ext, exePath, iconValue);
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
                // Remove centralized menu key
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Fujifier.Menu", throwOnMissingSubKey: false);

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
        private static void RegisterSystemFileAssociationVerb(string basePath, string exePath, string iconValue)
        {
            try
            {
                UnregisterExtensionVerb(basePath);

                string verbKeyPath = $@"{basePath}\shell\{VerbName}";
                using var verbKey = Registry.CurrentUser.CreateSubKey(verbKeyPath, true);
                if (verbKey != null)
                {
                    // Important for Windows Shell cascading menus:
                    // DO NOT set the default value on the parent verb key!
                    // Setting (Default) causes Explorer to treat the key as a direct command instead of a cascading submenu container.
                    verbKey.SetValue("MUIVerb", MenuText);
                    verbKey.SetValue("Icon", iconValue);
                    verbKey.SetValue("ExtendedSubCommandsKey", "Fujifier.Menu");
                    verbKey.SetValue("SubCommands", string.Empty);

                    // Also populate local shell subkey for dual-compatibility with shell handlers
                    using var shellKey = verbKey.CreateSubKey("shell", true);
                    if (shellKey != null)
                    {
                        PopulateSubCommands(shellKey, exePath, iconValue);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsContextMenuService] Failed on {basePath}: {ex.Message}");
            }
        }

        [SupportedOSPlatform("windows")]
        private static void PopulateSubCommands(RegistryKey shellKey, string exePath, string iconValue)
        {
            // 1. Add to queue
            using (var qKey = shellKey.CreateSubKey("01_queue", true))
            {
                if (qKey != null)
                {
                    qKey.SetValue("MUIVerb", "Add to queue");
                    qKey.SetValue("Icon", iconValue);
                    using var cmd = qKey.CreateSubKey("command", true);
                    cmd?.SetValue(null, $"\"{exePath}\" --queue \"%1\"");
                }
            }

            // 2. Process with Fujifier (default config - quick conversion)
            using (var pKey = shellKey.CreateSubKey("02_process", true))
            {
                if (pKey != null)
                {
                    pKey.SetValue("MUIVerb", "Process with Fujifier");
                    pKey.SetValue("Icon", iconValue);
                    using var cmd = pKey.CreateSubKey("command", true);
                    cmd?.SetValue(null, $"\"{exePath}\" --process \"%1\"");
                }
            }

            // 3. Process with Fujifier to... (specific destination folder)
            using (var ptKey = shellKey.CreateSubKey("03_processto", true))
            {
                if (ptKey != null)
                {
                    ptKey.SetValue("MUIVerb", "Process with Fujifier to...");
                    ptKey.SetValue("Icon", iconValue);
                    using var cmd = ptKey.CreateSubKey("command", true);
                    cmd?.SetValue(null, $"\"{exePath}\" --process-to \"%1\"");
                }
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
            // Avoid hanging headless CI environments (GitHub Actions, etc.) where explorer.exe is not running and SHChangeNotify hangs
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
            {
                return;
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var thread = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                        }
                        catch { }
                    })
                    {
                        IsBackground = true
                    };
                    thread.Start();
                    thread.Join(1000);
                }
            }
            catch { }
        }
    }
}
