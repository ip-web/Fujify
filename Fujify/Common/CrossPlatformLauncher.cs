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
using System.Threading;

namespace Fujify.Common
{
    public static class CrossPlatformLauncher
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpVerb;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpFile;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpParameters;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        private const int SW_SHOW = 5;
        private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

        [DllImport("shell32.dll", EntryPoint = "SHOpenWithDialog", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHOpenWithDialog(IntPtr hWndParent, ref OPENASINFO oOAI);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENASINFO
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string cszFile;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? cszClass;

            public OPENASINFOFLAGS oaifFlags;
        }

        [Flags]
        private enum OPENASINFOFLAGS : uint
        {
            OAIF_ALLOW_REGISTRATION = 0x00000001,
            OAIF_REGISTER_EXT = 0x00000002,
            OAIF_EXEC = 0x00000004,
            OAIF_FORCE_REGISTRATION = 0x00000008,
            OAIF_HIDE_REGISTRATION = 0x00000020,
            OAIF_URLPROTOCOL = 0x00000040,
            OAIF_FILE_MOVED = 0x00000080,
            OAIF_IS_URI = 0x00000100
        }

        public static void OpenPath(string path)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                    psi.ArgumentList.Add(path);
                    Process.Start(psi);
                }
                else
                {
                    var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                    psi.ArgumentList.Add(path);
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error launching path {path}: {ex.Message}");
            }
        }

        public static void OpenWith(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (!File.Exists(filePath)) return;
                    string fullPath = Path.GetFullPath(filePath);
                    OpenWithWindows(fullPath);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    if (!File.Exists(filePath)) return;
                    string escapedPath = filePath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    RunOsaScript(
                        "tell application \"System Events\" to activate",
                        "try",
                        $"set targetFile to (POSIX file \"{escapedPath}\") as alias",
                        "set chosenApp to choose application with prompt \"Choose an application to open this file with:\"",
                        "tell application \"Finder\" to open targetFile using chosenApp",
                        "end try"
                    );
                }
                else
                {
                    var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                    psi.ArgumentList.Add(filePath);
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error launching Open With for {filePath}: {ex.Message}");
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void OpenWithWindows(string fullPath)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    var oai = new OPENASINFO
                    {
                        cszFile = fullPath,
                        cszClass = null,
                        oaifFlags = OPENASINFOFLAGS.OAIF_EXEC | OPENASINFOFLAGS.OAIF_ALLOW_REGISTRATION
                    };

                    int hr = SHOpenWithDialog(IntPtr.Zero, ref oai);
                    if (hr < 0)
                    {
                        Debug.WriteLine($"SHOpenWithDialog failed with HRESULT 0x{hr:X8}, trying fallback");
                        TryRundll32Fallback(fullPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SHOpenWithDialog error: {ex.Message}");
                    TryRundll32Fallback(fullPath);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        private static void TryRundll32Fallback(string fullPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"shell32.dll,OpenAs_RunDLL \"{fullPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fallback OpenAs_RunDLL failed: {ex.Message}");
            }
        }

        public static void ShowFileProperties(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var info = new SHELLEXECUTEINFO();
                    info.cbSize = Marshal.SizeOf(info);
                    info.lpVerb = "properties";
                    info.lpFile = filePath;
                    info.nShow = SW_SHOW;
                    info.fMask = SEE_MASK_INVOKEIDLIST;
                    ShellExecuteEx(ref info);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    if (!File.Exists(filePath) && !Directory.Exists(filePath)) return;
                    string escapedPath = filePath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    RunOsaScript(
                        "tell application \"Finder\"",
                        "activate",
                        "try",
                        $"open information window of (POSIX file \"{escapedPath}\" as alias)",
                        "on error",
                        $"reveal (POSIX file \"{escapedPath}\" as alias)",
                        "end try",
                        "end tell"
                    );
                }
                else
                {
                    OpenPath(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error displaying properties for {filePath}: {ex.Message}");
            }
        }

        private static void RunOsaScript(params string[] scriptLines)
        {
            var psi = new ProcessStartInfo("osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var line in scriptLines)
            {
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(line);
            }
            Process.Start(psi);
        }

        public static void OpenFolder(string folderPath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", folderPath) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", $"\"{folderPath}\"");
                }
                else
                {
                    Process.Start("xdg-open", $"\"{folderPath}\"");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error launching folder {folderPath}: {ex.Message}");
            }
        }

        public static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                // 1. Mobile platform callback (iOS / Android)
                if (Fujify.ViewModels.MainViewModel.PlatformOpenUrlCallback != null)
                {
                    Fujify.ViewModels.MainViewModel.PlatformOpenUrlCallback(url);
                    return;
                }

                // 2. Avalonia TopLevel Launcher API
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime singleView && singleView.MainView != null)
                {
                    var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(singleView.MainView);
                    if (topLevel?.Launcher != null)
                    {
                        _ = topLevel.Launcher.LaunchUriAsync(new Uri(url));
                        return;
                    }
                }
                else if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                {
                    var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow);
                    if (topLevel?.Launcher != null)
                    {
                        _ = topLevel.Launcher.LaunchUriAsync(new Uri(url));
                        return;
                    }
                }

                // 3. Desktop OS fallback
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                    psi.ArgumentList.Add(url);
                    Process.Start(psi);
                }
                else
                {
                    var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                    psi.ArgumentList.Add(url);
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening URL {url}: {ex.Message}");
            }
        }
    }
}
