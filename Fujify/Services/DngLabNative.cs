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

namespace Fujify.Services
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

            // 1. Direct try via standard dynamic linker (e.g. system search path, Android app native library dir)
            if (NativeLibrary.TryLoad(LibraryName, assembly, searchPath, out IntPtr directHandle))
            {
                return directHandle;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string osExt;
            string prefix = "";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                osExt = ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
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

            if (NativeLibrary.TryLoad(fileName, assembly, searchPath, out directHandle))
            {
                return directHandle;
            }

            // 2. Android native library directory (when running on Android)
            try
            {
                var appType = Type.GetType("Android.App.Application, Mono.Android");
                var contextProp = appType?.GetProperty("Context", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var context = contextProp?.GetValue(null);
                if (context != null)
                {
                    var appInfoProp = context.GetType().GetProperty("ApplicationInfo");
                    var appInfo = appInfoProp?.GetValue(context);
                    if (appInfo != null)
                    {
                        var nativeLibDirProp = appInfo.GetType().GetProperty("NativeLibraryDir");
                        var nativeLibDir = nativeLibDirProp?.GetValue(appInfo) as string;
                        if (!string.IsNullOrEmpty(nativeLibDir))
                        {
                            string androidLibPath = Path.Combine(nativeLibDir, fileName);
                            if (File.Exists(androidLibPath) && NativeLibrary.TryLoad(androidLibPath, out IntPtr androidHandle))
                            {
                                return androidHandle;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall through to file system candidate search
            }

            // 3. Candidate locations (including iOS Frameworks and app bundle)
            string[] searchPaths =
            [
                Path.Combine(baseDir, fileName),
                Path.Combine(baseDir, "Frameworks", fileName),
                Path.Combine(baseDir, "Frameworks", $"{LibraryName}.framework", LibraryName),
                Path.Combine(baseDir, "libs", fileName),
                Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName),
                Path.Combine(baseDir, "..", "..", "..", "Fujify", "libs", fileName)
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

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr dnglab_version();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool dnglab_is_extension_supported([MarshalAs(UnmanagedType.LPUTF8Str)] string ext);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int dnglab_convert_file(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string inPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string outPath,
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

                // Upstream rawler/dnglab has mismatched Make strings for certain cameras
                // (e.g. Pentax K200D and K20D where camera writes 'PENTAX Corporation' but rawler's toml defines 'PENTAX').
                if (errorMsg.Contains("Unknown camera", StringComparison.OrdinalIgnoreCase) &&
                    errorMsg.Contains("PENTAX Corporation", StringComparison.OrdinalIgnoreCase))
                {
                    string? tempWithFixedMake = TryCreateTempWithNormalizedMake(inputPath);
                    if (tempWithFixedMake != null)
                    {
                        try
                        {
                            byte[] retryErrorBuf = new byte[2048];
                            int retryResult = dnglab_convert_file(tempWithFixedMake, outputPath, embedRaw, retryErrorBuf, (UIntPtr)retryErrorBuf.Length);
                            if (retryResult == 0)
                            {
                                return (true, null);
                            }
                        }
                        finally
                        {
                            try { if (File.Exists(tempWithFixedMake)) File.Delete(tempWithFixedMake); } catch { }
                        }
                    }
                }

                return (false, errorMsg);
            }
            catch (Exception ex)
            {
                return (false, $"Native call exception: {ex.Message}");
            }
        }

        private static string? TryCreateTempWithNormalizedMake(string inputPath)
        {
            try
            {
                if (!File.Exists(inputPath)) return null;

                byte[] header = new byte[Math.Min(65536, new FileInfo(inputPath).Length)];
                using (var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.ReadExactly(header, 0, header.Length);
                }

                byte[] target = Encoding.ASCII.GetBytes("PENTAX Corporation");
                int targetIndex = -1;
                for (int i = 0; i <= header.Length - target.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < target.Length; j++)
                    {
                        if (header[i + j] != target[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        targetIndex = i;
                        break;
                    }
                }

                if (targetIndex < 0) return null;

                string tempPath = Path.Combine(Path.GetTempPath(), $"fujify_make_fix_{Guid.NewGuid():N}{Path.GetExtension(inputPath)}");
                File.Copy(inputPath, tempPath, true);

                using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    fs.Seek(targetIndex, SeekOrigin.Begin);
                    byte[] replacement = Encoding.ASCII.GetBytes("PENTAX\0           ");
                    fs.Write(replacement, 0, replacement.Length);
                }

                return tempPath;
            }
            catch
            {
                return null;
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
