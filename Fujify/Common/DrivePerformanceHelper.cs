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
    /// <summary>
    /// Utility to detect storage media characteristics (SD Card / Removable media,
    /// mechanical HDD, network share vs NVMe/SSD) and compute optimal I/O concurrency.
    /// </summary>
    public static class DrivePerformanceHelper
    {
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;
        private const uint FILE_SHARE_DELETE = 4;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public uint PropertyId;
            public uint QueryType;
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.I1)]
            public bool IncursSeekPenalty;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            ref STORAGE_PROPERTY_QUERY lpInBuffer,
            uint nInBufferSize,
            out DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        /// <summary>
        /// Returns true if the path resides on an SD card, USB flash drive, external rotational HDD,
        /// or network volume where high concurrency causes head thrashing or bus saturation.
        /// </summary>
        public static bool IsRemovableOrSlowDrive(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                string? root = Path.GetPathRoot(fullPath);
                if (string.IsNullOrEmpty(root)) return false;

                var drive = new DriveInfo(root);

                // Removable media (SD Card, USB flash, CFexpress adapter, Camera mass storage)
                if (drive.DriveType == DriveType.Removable || drive.DriveType == DriveType.Network)
                {
                    return true;
                }

                // Windows seek penalty detection (mechanical HDD vs solid state SSD)
                if (OperatingSystem.IsWindows() && HasSeekPenaltyWindows(root))
                {
                    return true;
                }

                // Linux rotational device detection
                if (OperatingSystem.IsLinux() && IsRotationalLinux(root))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DrivePerformanceHelper] Error evaluating drive speed for '{filePath}': {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Gets optimal concurrency for ingestion (metadata reading and thumbnail generation).
        /// </summary>
        public static int GetOptimalIngestionConcurrency(string? sampleFilePath)
        {
            if (IsRemovableOrSlowDrive(sampleFilePath))
            {
                // Pipelined double-buffering without random head thrashing
                return 2;
            }

            return Math.Clamp(Environment.ProcessorCount, 2, 8);
        }

        /// <summary>
        /// Gets optimal concurrency for heavy RAW to DNG conversion.
        /// </summary>
        public static int GetOptimalConversionConcurrency(string? sampleFilePath, int configuredConcurrency, bool isPro)
        {
            if (!isPro) return 1;

            if (configuredConcurrency > 0 && configuredConcurrency <= Environment.ProcessorCount)
            {
                return configuredConcurrency;
            }

            if (IsRemovableOrSlowDrive(sampleFilePath))
            {
                // Throttled to 2 threads for SD card / HDD to sustain linear sequential write/read speeds
                return Math.Clamp(Environment.ProcessorCount / 4, 1, 2);
            }

            return Math.Max(1, Environment.ProcessorCount / 2);
        }

        /// <summary>
        /// Returns human-readable storage type description for logging and diagnostics.
        /// </summary>
        public static string GetStorageDescription(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return "Standard Storage";

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                string? root = Path.GetPathRoot(fullPath);
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    if (drive.DriveType == DriveType.Removable) return "SD Card / Removable Media";
                    if (drive.DriveType == DriveType.Network) return "Network Share (NAS)";
                    if (OperatingSystem.IsWindows() && HasSeekPenaltyWindows(root)) return "Mechanical Hard Drive (HDD)";
                    if (OperatingSystem.IsLinux() && IsRotationalLinux(root)) return "Rotational Hard Drive (HDD)";
                }
            }
            catch { }

            return "High-Speed SSD / NVMe";
        }

        private static bool HasSeekPenaltyWindows(string rootPath)
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                string driveLetter = rootPath.TrimEnd('\\', '/');
                if (driveLetter.EndsWith(':'))
                {
                    string volumeDevice = @"\\.\" + driveLetter;
                    IntPtr handle = CreateFile(
                        volumeDevice,
                        0,
                        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        0,
                        IntPtr.Zero);

                    if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                    {
                        try
                        {
                            var query = new STORAGE_PROPERTY_QUERY
                            {
                                PropertyId = 7, // StorageDeviceSeekPenaltyProperty
                                QueryType = 0  // PropertyStandardQuery
                            };

                            if (DeviceIoControl(
                                handle,
                                IOCTL_STORAGE_QUERY_PROPERTY,
                                ref query,
                                (uint)Marshal.SizeOf(query),
                                out DEVICE_SEEK_PENALTY_DESCRIPTOR desc,
                                (uint)Marshal.SizeOf(typeof(DEVICE_SEEK_PENALTY_DESCRIPTOR)),
                                out _,
                                IntPtr.Zero))
                            {
                                return desc.IncursSeekPenalty;
                            }
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool IsRotationalLinux(string rootPath)
        {
            if (!OperatingSystem.IsLinux()) return false;

            try
            {
                if (Directory.Exists("/sys/block"))
                {
                    foreach (var dir in Directory.GetDirectories("/sys/block"))
                    {
                        string rotationalFile = Path.Combine(dir, "queue", "rotational");
                        if (File.Exists(rotationalFile))
                        {
                            string val = File.ReadAllText(rotationalFile).Trim();
                            if (val == "1") return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }
    }
}

