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
        private static readonly object _initLock = new();
        private static bool? _isAvailable;

        static DngLabNative()
        {
            NativeLibraryResolver.EnsureRegistered();
        }

        private static IntPtr DllImportResolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            return NativeLibraryResolver.DllImportResolver(libraryName, assembly, searchPath);
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
                // Konica Minolta DYNAX/MAXXUM 5D / ALPHA SWEET DIGITAL, Pentax K2000, Samsung GX-1L, Hasselblad Lunar, Sony FX2, etc.).
                if (errorMsg.Contains("Unknown camera", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("Couldn't find a decoder", StringComparison.OrdinalIgnoreCase))
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

                if (errorMsg.Contains("HighEfficency", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Nikon High Efficiency (HE/HE*) raw compression uses the proprietary intoPIX TicoRAW codec. Open-source decoders cannot decode it. Please set your camera to 'Lossless compression' RAW.");
                }

                if (errorMsg.Contains("arw6", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Sony ARW6 wavelet compression is an experimental format not supported by open-source decoders. Standard Sony Compressed and Lossless Compressed RAW modes are fully supported.");
                }

                if (errorMsg.Contains("PowerShot Pro70", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("PowerShot Pr070", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Canon PowerShot Pro70 is non-working (legacy non-Bayer CYGM sensor).");
                }

                if (errorMsg.Contains("PowerShot G1", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("Canon G1", StringComparison.OrdinalIgnoreCase) ||
                    (errorMsg.Contains("G1", StringComparison.OrdinalIgnoreCase) && errorMsg.Contains("Canon", StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, "Canon G1 is non-working for direct RAW conversion.");
                }

                if (errorMsg.Contains("E5700", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("COOLPIX 5700", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Nikon e5700 is non-working for direct RAW conversion.");
                }

                if (errorMsg.Contains("Flash sync", StringComparison.OrdinalIgnoreCase) ||
                    (errorMsg.Contains("Hasselblad", StringComparison.OrdinalIgnoreCase) && errorMsg.Contains("Flash", StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, "Hasselblad Flash sync is partially working: photos must be converted to DNG first in Lightroom to work.");
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
                    ("ALPHA-5 DIGITAL", "ALPHA-7 DIGITAL"),
                    ("ALPHA SWEET DIGITAL", "ALPHA-7 DIGITAL    ")
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

                // 4. PENTAX K2000 -> PENTAX K-m
                if (errorMsg.Contains("K2000", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("PENTAX K2000");
                    byte[] replacement = Encoding.ASCII.GetBytes("PENTAX K-m  ");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 5. SAMSUNG TECHWIN GX-1L -> PENTAX *ist D
                if (errorMsg.Contains("SAMSUNG TECHWIN", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] targetMake = Encoding.ASCII.GetBytes("SAMSUNG TECHWIN");
                    byte[] replMake = Encoding.ASCII.GetBytes("PENTAX\0        ");
                    int idxMake = FindBytes(data, targetMake);
                    if (idxMake >= 0)
                    {
                        Array.Copy(replMake, 0, data, idxMake, replMake.Length);
                        modified = true;
                    }

                    byte[] targetModel = Encoding.ASCII.GetBytes("GX-1L");
                    byte[] replModel = Encoding.ASCII.GetBytes("*istD");
                    int idxModel = FindBytes(data, targetModel);
                    if (idxModel >= 0)
                    {
                        Array.Copy(replModel, 0, data, idxModel, replModel.Length);
                        modified = true;
                    }
                }

                // 6. HASSELBLAD Lunar -> SONY NEX-7
                if (errorMsg.Contains("Lunar", StringComparison.OrdinalIgnoreCase) ||
                    (errorMsg.Contains("HASSELBLAD", StringComparison.OrdinalIgnoreCase) && FindBytes(data, Encoding.ASCII.GetBytes("Lunar")) >= 0))
                {
                    byte[] targetMake = Encoding.ASCII.GetBytes("HASSELBLAD");
                    byte[] replMake = Encoding.ASCII.GetBytes("SONY\0     ");
                    int idxMake = FindBytes(data, targetMake);
                    if (idxMake >= 0)
                    {
                        Array.Copy(replMake, 0, data, idxMake, replMake.Length);
                        modified = true;
                    }

                    byte[] targetModel = Encoding.ASCII.GetBytes("Lunar");
                    byte[] replModel = Encoding.ASCII.GetBytes("NEX-7");
                    int idxModel = FindBytes(data, targetModel);
                    if (idxModel >= 0)
                    {
                        Array.Copy(replModel, 0, data, idxModel, replModel.Length);
                        modified = true;
                    }
                }

                // 7. SONY ILME-FX2 -> ILME-FX3
                if (errorMsg.Contains("ILME-FX2", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("ILME-FX2");
                    byte[] replacement = Encoding.ASCII.GetBytes("ILME-FX3");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 8. Panasonic DMC-GM1S -> DMC-GM1\0
                if (errorMsg.Contains("DMC-GM1S", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("DMC-GM1S");
                    byte[] replacement = Encoding.ASCII.GetBytes("DMC-GM1\0");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 9. Panasonic DC-FZ45 -> DMC-FZ45
                if (errorMsg.Contains("FZ45", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("DC-FZ45\0");
                    byte[] replacement = Encoding.ASCII.GetBytes("DMC-FZ45");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 10. Nikon 1 S1 -> NIKON 1 J2 (identical 10.1MP CX sensor)
                if (errorMsg.Contains("NIKON 1 S1", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("NIKON 1 S1");
                    byte[] replacement = Encoding.ASCII.GetBytes("NIKON 1 J2");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 11. Nikon 1 J4 -> NIKON 1 V3 (identical 18.4MP CX sensor)
                if (errorMsg.Contains("NIKON 1 J4", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("NIKON 1 J4");
                    byte[] replacement = Encoding.ASCII.GetBytes("NIKON 1 V3");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 12. Nikon D1H -> NIKON D1\0 (identical 2.7MP CCD sensor)
                if (errorMsg.Contains("NIKON D1H", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("NIKON D1H");
                    byte[] replacement = Encoding.ASCII.GetBytes("NIKON D1\0");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 13. Nikon COOLPIX P7700 -> COOLPIX P7800 (identical 12.2MP BSI CMOS sensor)
                if (errorMsg.Contains("P7700", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("COOLPIX P7700");
                    byte[] replacement = Encoding.ASCII.GetBytes("COOLPIX P7800");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 14. Minolta DiMAGE 5 -> DiMAGE 7 (identical MRW sensor pipeline)
                if (errorMsg.Contains("DiMAGE 5", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("DiMAGE 5");
                    byte[] replacement = Encoding.ASCII.GetBytes("DiMAGE 7");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
                    }
                }

                // 15. Samsung Galaxy NX EK-GN120 -> NX300 (identical 20.3MP APS-C sensor)
                if (errorMsg.Contains("EK-GN120", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] target = Encoding.ASCII.GetBytes("EK-GN120");
                    byte[] replacement = Encoding.ASCII.GetBytes("NX300\0\0");
                    int idx = FindBytes(data, target);
                    if (idx >= 0)
                    {
                        Array.Copy(replacement, 0, data, idx, replacement.Length);
                        modified = true;
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
