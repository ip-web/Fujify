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
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FujifyNET.Services
{
    /// <summary>
    /// Native C-ABI bindings for DNGLab / rawler engine (dnglab_ffi).
    /// Enables high-performance, in-process raw camera conversion to DNG
    /// without spawning external processes.
    /// </summary>
    public static class DngLabNative
    {
        private const string LibraryName = "dnglab_ffi";
        private static bool _resolverRegistered;
        private static readonly object _initLock = new();
        private static bool? _isAvailable;

        static DngLabNative()
        {
            RegisterLibraryResolver();
        }

        private static void RegisterLibraryResolver()
        {
            lock (_initLock)
            {
                if (_resolverRegistered) return;
                try
                {
                    NativeLibrary.SetDllImportResolver(typeof(DngLabNative).Assembly, DllImportResolver);
                    _resolverRegistered = true;
                }
                catch
                {
                    // Resolver might already be set or not supported
                }
            }
        }

        private static IntPtr DllImportResolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != LibraryName) return IntPtr.Zero;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string osExt;
            string prefix = "";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                osExt = ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                osExt = ".dylib";
                prefix = "lib";
            }
            else
            {
                osExt = ".so";
                prefix = "lib";
            }

            string fileName = $"{prefix}{LibraryName}{osExt}";

            // Candidate locations
            string[] searchPaths =
            [
                Path.Combine(baseDir, fileName),
                Path.Combine(baseDir, "libs", fileName),
                Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName),
                Path.Combine(baseDir, "..", "..", "..", "FujifyNET", "libs", fileName)
            ];

            foreach (var path in searchPaths)
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr dnglab_version();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool dnglab_is_extension_supported(string ext);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int dnglab_convert_file(
            string inPath,
            string outPath,
            [MarshalAs(UnmanagedType.I1)] bool embedRaw,
            byte[] errBuf,
            UIntPtr errBufLen);

        /// <summary>
        /// Checks whether the native dnglab_ffi library is available and can be loaded.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue) return _isAvailable.Value;
                lock (_initLock)
                {
                    if (_isAvailable.HasValue) return _isAvailable.Value;
                    try
                    {
                        IntPtr vPtr = dnglab_version();
                        _isAvailable = vPtr != IntPtr.Zero;
                    }
                    catch
                    {
                        _isAvailable = false;
                    }
                    return _isAvailable.Value;
                }
            }
        }

        /// <summary>
        /// Gets the dnglab_ffi native library version.
        /// </summary>
        public static string? GetVersion()
        {
            try
            {
                if (!IsAvailable) return null;
                IntPtr vPtr = dnglab_version();
                return Marshal.PtrToStringAnsi(vPtr);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true if the file extension is supported for RAW decoding.
        /// </summary>
        public static bool IsExtensionSupported(string extension)
        {
            try
            {
                if (!IsAvailable) return false;
                return dnglab_is_extension_supported(extension);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Converts a RAW camera file to DNG synchronously in-process.
        /// </summary>
        public static (bool Success, string? ErrorMessage) ConvertFile(string inputPath, string outputPath, bool embedRaw)
        {
            if (!IsAvailable)
            {
                return (false, "Native dnglab_ffi library is not available.");
            }

            try
            {
                byte[] errorBuf = new byte[2048];
                int result = dnglab_convert_file(inputPath, outputPath, embedRaw, errorBuf, (UIntPtr)errorBuf.Length);

                if (result == 0)
                {
                    return (true, null);
                }

                string errorMsg = Encoding.UTF8.GetString(errorBuf).TrimEnd('\0', ' ', '\r', '\n');
                if (string.IsNullOrWhiteSpace(errorMsg))
                {
                    errorMsg = $"Native conversion failed with exit code {result}";
                }
                return (false, errorMsg);
            }
            catch (Exception ex)
            {
                return (false, $"Native call exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts a RAW camera file to DNG asynchronously on a background thread.
        /// </summary>
        public static Task<(bool Success, string? ErrorMessage)> ConvertFileAsync(
            string inputPath,
            string outputPath,
            bool embedRaw,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ConvertFile(inputPath, outputPath, embedRaw);
            }, cancellationToken);
        }
    }
}

