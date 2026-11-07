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
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Fujify.Services
{
    /// <summary>
    /// Native C-ABI bindings for Raw2Raf engine (raw2raf_ffi).
    /// Enables high-performance, in-process RAW camera to Fujifilm RAF container conversion
    /// without spawning external child processes, making it fully compatible with
    /// Windows, macOS, Linux, Android, and iOS.
    /// </summary>
    public static class Raw2RafNative
    {
        private const string LibraryName = "raw2raf_ffi";
        private static readonly object _initLock = new();
        private static bool? _isAvailable;

        static Raw2RafNative()
        {
            NativeLibraryResolver.EnsureRegistered();
        }

        private static IntPtr DllImportResolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            return NativeLibraryResolver.DllImportResolver(libraryName, assembly, searchPath);
        }

        #region Native Methods

        [DllImport(LibraryName, EntryPoint = "raw2raf_version", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr raw2raf_version();

        [DllImport(LibraryName, EntryPoint = "raw2raf_convert_file", CallingConvention = CallingConvention.Cdecl)]
        private static extern int raw2raf_convert_file(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string inputPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string outputPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string containerArg,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? nonGfxModel,
            bool autoContainer,
            [Out] byte[] errorBuf,
            UIntPtr errorBufLen);

        [DllImport(LibraryName, EntryPoint = "raw2raf_convert_file_ex", CallingConvention = CallingConvention.Cdecl)]
        private static extern int raw2raf_convert_file_ex(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string inputPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string outputPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string containerArg,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? nonGfxModel,
            bool autoContainer,
            double exposureBias,
            [Out] byte[] errorBuf,
            UIntPtr errorBufLen);

        #endregion

        #region Public Managed API

        /// <summary>
        /// Checks whether the native raw2raf_ffi library is available and can be loaded.
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
                        IntPtr vPtr = raw2raf_version();
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
        /// Gets the raw2raf_ffi native library version.
        /// </summary>
        public static string? GetVersion()
        {
            if (!IsAvailable) return null;

            try
            {
                IntPtr vPtr = raw2raf_version();
                return Marshal.PtrToStringUTF8(vPtr);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Converts a RAW camera file to a Fujifilm RAF file using the specified reference container in-process.
        /// </summary>
        /// <param name="inputPath">Path to input RAW camera file.</param>
        /// <param name="outputPath">Path to target .RAF output file.</param>
        /// <param name="containerArg">Reference RAF container file(s) or directory.</param>
        /// <param name="nonGfxModel">Optional camera model override for non-GFX containers.</param>
        /// <param name="autoContainer">Whether to automatically pick the smallest fitting Bayer container.</param>
        /// <param name="exposureBias">Raw baseline exposure bias (default: -0.72 EV).</param>
        /// <returns>A tuple indicating success and an error message if failed.</returns>
        public static (bool Success, string? ErrorMessage) ConvertFile(
            string inputPath,
            string outputPath,
            string containerArg,
            string? nonGfxModel = "GFX50S II",
            bool autoContainer = true,
            double exposureBias = -0.72)
        {
            if (!IsAvailable)
            {
                return (false, "Native raw2raf_ffi library is not available.");
            }

            byte[] errorBuf = new byte[2048];
            int result = raw2raf_convert_file_ex(
                inputPath,
                outputPath,
                containerArg,
                nonGfxModel,
                autoContainer,
                exposureBias,
                errorBuf,
                (UIntPtr)errorBuf.Length);

            if (result == 0)
            {
                return (true, null);
            }

            int nullIdx = Array.IndexOf(errorBuf, (byte)0);
            int len = nullIdx >= 0 ? nullIdx : errorBuf.Length;
            string errorMsg = Encoding.UTF8.GetString(errorBuf, 0, len).Trim();

            if (string.IsNullOrWhiteSpace(errorMsg))
            {
                errorMsg = $"Native raw2raf conversion failed with exit code {result}.";
            }

            return (false, errorMsg);
        }

        #endregion
    }
}
