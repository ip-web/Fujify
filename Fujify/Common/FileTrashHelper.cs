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

namespace Fujify.Common
{
    public static class FileTrashHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_NOERRORUI = 0x0400;
        private const ushort FOF_SILENT = 0x0004;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        /// <summary>
        /// Sends the specified file to the system Recycle Bin / Trash.
        /// Falls back to standard file deletion if Recycle Bin is unsupported or fails.
        /// </summary>
        public static bool SendToTrash(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return SendToTrashWindows(filePath);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    return SendToTrashMac(filePath);
                }
                else if (OperatingSystem.IsLinux())
                {
                    return SendToTrashLinux(filePath);
                }
                else
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileTrashHelper] SendToTrash exception: {ex.Message}");
                try
                {
                    File.Delete(filePath);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static bool SendToTrashWindows(string filePath)
        {
            try
            {
                string fullPath = Path.GetFullPath(filePath);
                var op = new SHFILEOPSTRUCT
                {
                    hwnd = IntPtr.Zero,
                    wFunc = FO_DELETE,
                    pFrom = fullPath + "\0\0", // Double null-terminated
                    pTo = null,
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
                    fAnyOperationsAborted = false,
                    hNameMappings = IntPtr.Zero,
                    lpszProgressTitle = null
                };

                int result = SHFileOperation(ref op);
                if (result == 0 && !op.fAnyOperationsAborted && !File.Exists(fullPath))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileTrashHelper] Windows Recycle Bin error: {ex.Message}");
            }

            // Fallback to regular delete if recycle bin was unavailable or failed
            File.Delete(filePath);
            return true;
        }

        private static bool SendToTrashMac(string filePath)
        {
            try
            {
                string fullPath = Path.GetFullPath(filePath);
                string escaped = fullPath.Replace("\"", "\\\"");
                var psi = new ProcessStartInfo("osascript", $"-e \"tell application \\\"Finder\\\" to delete POSIX file \\\"{escaped}\\\"\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(3000);
                    if (proc.ExitCode == 0 && !File.Exists(fullPath))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileTrashHelper] macOS Finder Trash error: {ex.Message}");
            }

            // Fallback: move to ~/.Trash
            try
            {
                string trashDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
                if (Directory.Exists(trashDir))
                {
                    string fileName = Path.GetFileName(filePath);
                    string target = Path.Combine(trashDir, fileName);
                    int counter = 1;
                    while (File.Exists(target))
                    {
                        string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        target = Path.Combine(trashDir, $"{nameOnly}_{counter++}{ext}");
                    }
                    File.Move(filePath, target);
                    return true;
                }
            }
            catch { }

            File.Delete(filePath);
            return true;
        }

        private static bool SendToTrashLinux(string filePath)
        {
            try
            {
                string fullPath = Path.GetFullPath(filePath);
                var psi = new ProcessStartInfo("gio", $"trash \"{fullPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(3000);
                    if (proc.ExitCode == 0 && !File.Exists(fullPath))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileTrashHelper] Linux gio trash error: {ex.Message}");
            }

            File.Delete(filePath);
            return true;
        }
    }
}

