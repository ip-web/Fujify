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

                // Upstream rawler/dnglab has mismatched Make strings or missing model aliases for certain cameras
                // (e.g. Pentax K200D/K20D where camera writes 'PENTAX Corporation', Leaf Aptus where camera writes 'Creo/Leaf',
                // and Konica Minolta DYNAX/MAXXUM 5D which shares identical hardware/sensor with DYNAX 7D).
                if (errorMsg.Contains("Unknown camera", StringComparison.OrdinalIgnoreCase))
                {
                    if (errorMsg.Contains("PENTAX Corporation", StringComparison.OrdinalIgnoreCase) ||
                        errorMsg.Contains("Creo/Leaf", StringComparison.OrdinalIgnoreCase) ||
                        errorMsg.Contains("DYNAX 5D", StringComparison.OrdinalIgnoreCase) ||
                        errorMsg.Contains("MAXXUM 5D", StringComparison.OrdinalIgnoreCase) ||
                        errorMsg.Contains("ALPHA-5 DIGITAL", StringComparison.OrdinalIgnoreCase))
                    {
                        var (tempWithFixedMake, is5d) = TryCreateTempWithNormalizedCamera(inputPath, errorMsg);
                        if (tempWithFixedMake != null)
                        {
                            try
                            {
                                byte[] retryErrorBuf = new byte[2048];
                                int retryResult = dnglab_convert_file(tempWithFixedMake, outputPath, embedRaw, retryErrorBuf, (UIntPtr)retryErrorBuf.Length);
                                if (retryResult == 0)
                                {
                                    if (is5d && File.Exists(outputPath))
                                    {
                                        RestoreMinolta5DModelName(outputPath);
                                    }
                                    return (true, null);
                                }
                            }
                            finally
                            {
                                try { if (File.Exists(tempWithFixedMake)) File.Delete(tempWithFixedMake); } catch { }
                            }
                        }
                    }
                }

                if (errorMsg.Contains("PowerShot Pro70", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("PowerShot Pr070", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Canon PowerShot Pro70 uses a legacy non-Bayer CYGM sensor which is unsupported for DNG conversion.");
                }

                return (false, errorMsg);
            }
            catch (Exception ex)
            {
                return (false, $"Native call exception: {ex.Message}");
            }
        }

        private static (string? TempPath, bool Is5d) TryCreateTempWithNormalizedCamera(string inputPath, string errorMsg)
        {
            try
            {
                if (!File.Exists(inputPath)) return (null, false);

                byte[] data = File.ReadAllBytes(inputPath);
                bool modified = false;
                bool is5d = false;

                // 1. PENTAX Corporation -> PENTAX\0           
                if (errorMsg.Contains("PENTAX Corporation", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("PENTAX Corporation");
                    byte[] replacement = Encoding.ASCII.GetBytes("PENTAX\0           ");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 2. Leaf MOS: <tiff:Make>Creo/Leaf</tiff:Make> -> <tiff:Make>Leaf</tiff:Make>     
                if (errorMsg.Contains("Creo/Leaf", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("<tiff:Make>Creo/Leaf</tiff:Make>");
                    byte[] replacement = Encoding.ASCII.GetBytes("<tiff:Make>Leaf</tiff:Make>     ");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 3. Konica Minolta 5D -> 7D
                (string Target, string Replacement)[] pairs =
                [
                    ("DYNAX 5D", "DYNAX 7D"),
                    ("MAXXUM 5D", "MAXXUM 7D"),
                    ("ALPHA-5 DIGITAL", "ALPHA-7 DIGITAL")
                ];

                foreach (var (tgtStr, repStr) in pairs)
                {
                    if (errorMsg.Contains(tgtStr, StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] target = Encoding.ASCII.GetBytes(tgtStr);
                        byte[] replacement = Encoding.ASCII.GetBytes(repStr);
                        int idx = FindBytes(data, target);
                        if (idx >= 0)
                        {
                            Array.Copy(replacement, 0, data, idx, replacement.Length);
                            modified = true;
                            is5d = true;
                        }
                    }
                }

                if (!modified) return (null, false);

                string tempPath = Path.Combine(Path.GetTempPath(), $"fujify_cam_fix_{Guid.NewGuid():N}{Path.GetExtension(inputPath)}");
                File.WriteAllBytes(tempPath, data);
                return (tempPath, is5d);
            }
            catch
            {
                return (null, false);
            }
        }

        private static int FindBytes(byte[] source, byte[] pattern)
        {
            if (pattern.Length == 0 || source.Length < pattern.Length) return -1;
            for (int i = 0; i <= source.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private static void RestoreMinolta5DModelName(string dngPath)
        {
            try
            {
                if (!File.Exists(dngPath)) return;
                byte[] data = File.ReadAllBytes(dngPath);
                (string Target, string Replacement)[] pairs =
                [
                    ("DYNAX 7D", "DYNAX 5D"),
                    ("MAXXUM 7D", "MAXXUM 5D"),
                    ("ALPHA-7 DIGITAL", "ALPHA-5 DIGITAL")
                ];

                bool fixedAny = false;
                foreach (var (targetStr, replStr) in pairs)
                {
                    byte[] target = Encoding.ASCII.GetBytes(targetStr);
                    byte[] replacement = Encoding.ASCII.GetBytes(replStr);
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        fixedAny = true;
                    }
                }

                if (fixedAny)
                {
                    File.WriteAllBytes(dngPath, data);
                }
            }
            catch { }
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
