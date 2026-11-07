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
using System.Threading;
using System.Threading.Tasks;

namespace Fujify.Services
{
    public class RafConverterService : IRafConverterService
    {
        private readonly IProcessRunner _processRunner;
        public bool PreferNative { get; set; }

        public RafConverterService(IProcessRunner? processRunner = null, bool? preferNative = null)
        {
            _processRunner = processRunner ?? new ProcessRunner();
            PreferNative = preferNative ?? (processRunner == null);
        }

        public bool IsAvailable => (PreferNative && Raw2RafNative.IsAvailable) || !string.IsNullOrEmpty(FindRaw2RafPath());

        public string? GetRaw2RafPath() => (PreferNative && Raw2RafNative.IsAvailable) ? "in-process (raw2raf_ffi)" : FindRaw2RafPath();

        public RafConversionResult ConvertToRaf(string inputRawPath, string outputRafPath, string containerArg, string? exiftoolPath = null)
        {
            if (string.IsNullOrWhiteSpace(containerArg))
            {
                return new RafConversionResult(false, null, "No reference Fujifilm RAF container provided. Please configure a reference RAF file or folder in Settings.");
            }

            // 1. Prefer high-performance in-process native library (raw2raf_ffi)
            if (PreferNative && Raw2RafNative.IsAvailable)
            {
                try
                {
                    var (nativeSuccess, nativeError) = Raw2RafNative.ConvertFile(
                        inputRawPath,
                        outputRafPath,
                        containerArg,
                        "GFX50S II",
                        autoContainer: true);

                    if (!nativeSuccess || !File.Exists(outputRafPath))
                    {
                        return new RafConversionResult(false, null, $"Capture One RAF conversion failed: {nativeError}");
                    }

                    // In-process pure C# metadata migration (zero external process)
                    RafMetadataModifier.InjectMetadata(outputRafPath, inputRawPath, "GFX50S II");

                    return new RafConversionResult(true, outputRafPath, null);
                }
                catch (Exception ex)
                {
                    return new RafConversionResult(false, null, $"Exception during native RAF conversion: {ex.Message}");
                }
            }

            // 2. Mobile platforms require native library (external processes are forbidden)
            if (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid())
            {
                return new RafConversionResult(false, null, "Native conversion library (raw2raf_ffi) is required on mobile platforms. External process execution is not permitted.");
            }

            // 3. Fallback to CLI process on desktop platforms
            string? raw2RafPath = FindRaw2RafPath();
            if (string.IsNullOrEmpty(raw2RafPath) || !File.Exists(raw2RafPath))
            {
                return new RafConversionResult(false, null, "Raw2Raf native library or executable not found. Please ensure raw2raf is installed.");
            }

            try
            {
                string arguments = BuildArguments(inputRawPath, outputRafPath, containerArg, exiftoolPath);
                var (exitCode, output, error) = _processRunner.Run(raw2RafPath, arguments);

                if (exitCode != 0 || output.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) || !File.Exists(outputRafPath))
                {
                    string errorMsg = !string.IsNullOrWhiteSpace(error) ? error : output;
                    return new RafConversionResult(false, null, $"Capture One RAF conversion failed: {errorMsg}");
                }

                // In-process pure C# metadata migration (zero external process)
                RafMetadataModifier.InjectMetadata(outputRafPath, inputRawPath, "GFX50S II");

                return new RafConversionResult(true, outputRafPath, null);
            }
            catch (Exception ex)
            {
                return new RafConversionResult(false, null, $"Exception during RAF conversion: {ex.Message}");
            }
        }

        public async Task<RafConversionResult> ConvertToRafAsync(string inputRawPath, string outputRafPath, string containerArg, string? exiftoolPath = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(containerArg))
            {
                return new RafConversionResult(false, null, "No reference Fujifilm RAF container provided. Please configure a reference RAF file or folder in Settings.");
            }

            // 1. Prefer high-performance in-process native library (raw2raf_ffi)
            if (PreferNative && Raw2RafNative.IsAvailable)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (nativeSuccess, nativeError) = await Task.Run(() =>
                        Raw2RafNative.ConvertFile(
                            inputRawPath,
                            outputRafPath,
                            containerArg,
                            "GFX50S II",
                            autoContainer: true),
                        cancellationToken);

                    if (!nativeSuccess || !File.Exists(outputRafPath))
                    {
                        return new RafConversionResult(false, null, $"Capture One RAF conversion failed: {nativeError}");
                    }

                    // In-process pure C# metadata migration (zero external process)
                    RafMetadataModifier.InjectMetadata(outputRafPath, inputRawPath, "GFX50S II");

                    return new RafConversionResult(true, outputRafPath, null);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return new RafConversionResult(false, null, $"Exception during native RAF conversion: {ex.Message}");
                }
            }

            // 2. Mobile platforms require native library (external processes are forbidden)
            if (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid())
            {
                return new RafConversionResult(false, null, "Native conversion library (raw2raf_ffi) is required on mobile platforms. External process execution is not permitted.");
            }

            // 3. Fallback to CLI process on desktop platforms
            string? raw2RafPath = FindRaw2RafPath();
            if (string.IsNullOrEmpty(raw2RafPath) || !File.Exists(raw2RafPath))
            {
                return new RafConversionResult(false, null, "Raw2Raf native library or executable not found. Please ensure raw2raf is installed.");
            }

            try
            {
                string arguments = BuildArguments(inputRawPath, outputRafPath, containerArg, exiftoolPath);
                var (exitCode, output, error) = await _processRunner.RunAsync(raw2RafPath, arguments, cancellationToken);

                if (exitCode != 0 || output.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) || !File.Exists(outputRafPath))
                {
                    string errorMsg = !string.IsNullOrWhiteSpace(error) ? error : output;
                    return new RafConversionResult(false, null, $"Capture One RAF conversion failed: {errorMsg}");
                }

                // In-process pure C# metadata migration (zero external process)
                RafMetadataModifier.InjectMetadata(outputRafPath, inputRawPath, "GFX50S II");

                return new RafConversionResult(true, outputRafPath, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new RafConversionResult(false, null, $"Exception during RAF conversion: {ex.Message}");
            }
        }

        public bool HasValidContainer(string? containerArg)
        {
            return HasValidContainerStatic(containerArg);
        }

        public static bool HasValidContainerStatic(string? containerArg)
        {
            if (string.IsNullOrWhiteSpace(containerArg))
                return false;

            var parts = containerArg.Split(new[] { ';', '|', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var rawPart in parts)
            {
                string part = rawPart.Trim('\"', '\'', ' ');
                if (!string.IsNullOrWhiteSpace(part) && (File.Exists(part) || Directory.Exists(part)))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildArguments(string inputRawPath, string outputRafPath, string containerArg, string? exiftoolPath)
        {
            var containers = containerArg.Split(new[] { ';', '|', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var sb = new StringBuilder();

            foreach (var rawC in containers)
            {
                string c = rawC.Trim('\"', '\'', ' ');
                if (!string.IsNullOrWhiteSpace(c))
                {
                    sb.Append($"--container \"{c}\" ");
                }
            }

            sb.Append("--auto-container ");
            sb.Append("--non-gfx-model \"GFX50S II\" ");
            sb.Append("--no-metadata ");
            sb.Append($"\"{inputRawPath}\" \"{outputRafPath}\"");
            return sb.ToString().Trim();
        }

        public static string? FindRaw2RafPath()
        {
            string binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "raw2raf.exe" : "raw2raf";
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new List<string>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    candidates.Add(Path.Combine(baseDir, "libs", "win-arm64", binaryName));
                }
                candidates.Add(Path.Combine(baseDir, "libs", "win-x64", binaryName));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    candidates.Add(Path.Combine(baseDir, "libs", "osx-x64", binaryName));
                }
                else
                {
                    candidates.Add(Path.Combine(baseDir, "libs", "osx-arm64", binaryName));
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                candidates.Add(Path.Combine(baseDir, "libs", "linux-x64", binaryName));
            }

            candidates.Add(Path.Combine(baseDir, "libs", binaryName));
            candidates.Add(Path.Combine(baseDir, binaryName));

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            // Check system PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                char sep = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
                foreach (var dir in pathEnv.Split(sep))
                {
                    try
                    {
                        string fullPath = Path.Combine(dir.Trim(), binaryName);
                        if (File.Exists(fullPath)) return Path.GetFullPath(fullPath);
                    }
                    catch { }
                }
            }

            return null;
        }

        public RafContainerInspection InspectContainer(string rafFilePath)
        {
            return InspectContainerStatic(rafFilePath);
        }

        public static RafContainerInspection InspectContainerStatic(string rafFilePath)
        {
            if (string.IsNullOrWhiteSpace(rafFilePath) || !File.Exists(rafFilePath))
            {
                return new RafContainerInspection(false, RafSensorType.Invalid, null, 0, 0, "File does not exist.");
            }

            try
            {
                using var fs = new FileStream(rafFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
                if (fs.Length < 148)
                {
                    return new RafContainerInspection(false, RafSensorType.Invalid, null, 0, 0, "File is smaller than a valid RAF header (148 bytes).");
                }

                byte[] header = new byte[148];
                if (fs.Read(header, 0, 148) < 148)
                {
                    return new RafContainerInspection(false, RafSensorType.Invalid, null, 0, 0, "Unable to read RAF header.");
                }

                // 1. Verify 16-byte magic: "FUJIFILMCCD-RAW "
                byte[] magic = Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW ");
                for (int i = 0; i < 16; i++)
                {
                    if (header[i] != magic[i])
                    {
                        return new RafContainerInspection(false, RafSensorType.Invalid, null, 0, 0, "Not a valid Fujifilm RAF file (magic mismatch).");
                    }
                }

                // 2. Read camera model string from header 0x1C..0x3C (32 bytes ASCII)
                string model = Encoding.ASCII.GetString(header, 0x1C, 32).Trim('\0', ' ');

                // 3. Read meta_off at 0x5C (uint32 BE) and meta_len at 0x60 (uint32 BE)
                uint metaOff = (uint)((header[0x5C] << 24) | (header[0x5D] << 16) | (header[0x5E] << 8) | header[0x5F]);
                uint metaLen = (uint)((header[0x60] << 24) | (header[0x61] << 16) | (header[0x62] << 8) | header[0x63]);

                if (metaOff == 0 || metaLen < 4 || metaOff + metaLen > fs.Length)
                {
                    return new RafContainerInspection(false, RafSensorType.Invalid, model, 0, 0, "Invalid CFA Meta Directory offset or length.");
                }

                // 4. Seek to CFA Meta Directory and parse tags directly from file data
                fs.Seek(metaOff, SeekOrigin.Begin);
                byte[] metaBuf = new byte[metaLen];
                if (fs.Read(metaBuf, 0, (int)metaLen) < metaLen)
                {
                    return new RafContainerInspection(false, RafSensorType.Invalid, model, 0, 0, "Unable to read CFA Meta Directory.");
                }

                uint numEntries = (uint)((metaBuf[0] << 24) | (metaBuf[1] << 16) | (metaBuf[2] << 8) | metaBuf[3]);
                int curr = 4;
                bool isXTrans = false;
                int fullW = 0;
                int fullH = 0;
                int cropW = 0;
                int cropH = 0;

                for (int i = 0; i < numEntries; i++)
                {
                    if (curr + 4 > metaLen) break;

                    ushort tag = (ushort)((metaBuf[curr] << 8) | metaBuf[curr + 1]);
                    ushort len = (ushort)((metaBuf[curr + 2] << 8) | metaBuf[curr + 3]);
                    int doff = curr + 4;
                    if (doff + len > metaLen) break;

                    if (tag == 0x0131 && len == 36)
                    {
                        // Tag 0x0131 with length 36 is the 6×6 X-Trans matrix embedded in the raw data
                        isXTrans = true;
                    }
                    else if (tag == 0x0100 && len >= 4)
                    {
                        // Full sensor dimensions: Height (BE uint16), Width (BE uint16)
                        fullH = (ushort)((metaBuf[doff] << 8) | metaBuf[doff + 1]);
                        fullW = (ushort)((metaBuf[doff + 2] << 8) | metaBuf[doff + 3]);
                    }
                    else if (tag == 0x0111 && len >= 4)
                    {
                        // Active crop dimensions: Height (BE uint16), Width (BE uint16)
                        cropH = (ushort)((metaBuf[doff] << 8) | metaBuf[doff + 1]);
                        cropW = (ushort)((metaBuf[doff + 2] << 8) | metaBuf[doff + 3]);
                    }

                    curr += 4 + len;
                }

                int finalW = fullW > 0 ? fullW : cropW;
                int finalH = fullH > 0 ? fullH : cropH;

                // 5. Fallback for dimensions from raw TIFF header (raw_off at 0x64) if meta tags were not found
                if (finalW <= 0 || finalH <= 0)
                {
                    uint rawOff = (uint)((header[0x64] << 24) | (header[0x65] << 16) | (header[0x66] << 8) | header[0x67]);
                    if (rawOff > 0 && rawOff + 100 <= fs.Length)
                    {
                        fs.Seek(rawOff, SeekOrigin.Begin);
                        byte[] rawHead = new byte[256];
                        int read = fs.Read(rawHead, 0, 256);
                        if (read >= 52)
                        {
                            int rw = (int)BitConverter.ToUInt32(rawHead, 36);
                            int rh = (int)BitConverter.ToUInt32(rawHead, 48);
                            if (rw > 0 && rh > 0)
                            {
                                finalW = rw;
                                finalH = rh;
                            }
                        }
                    }
                }

                if (isXTrans)
                {
                    return new RafContainerInspection(
                        IsValid: false,
                        SensorType: RafSensorType.XTrans,
                        CameraModel: model,
                        Width: finalW,
                        Height: finalH,
                        ErrorMessage: $"'{Path.GetFileName(rafFilePath)}' uses an X-Trans sensor (detected 6×6 CFA pattern matrix). Donor containers must be Bayer sensor cameras.");
                }

                return new RafContainerInspection(
                    IsValid: true,
                    SensorType: RafSensorType.Bayer,
                    CameraModel: model,
                    Width: finalW,
                    Height: finalH,
                    ErrorMessage: null);
            }
            catch (Exception ex)
            {
                return new RafContainerInspection(false, RafSensorType.Invalid, null, 0, 0, $"Error reading RAF container: {ex.Message}");
            }
        }

        public bool IsBayerSensor(string rafFilePath)
        {
            if (string.IsNullOrWhiteSpace(rafFilePath) || !File.Exists(rafFilePath))
                return false;

            // 1. Direct binary file inspection of CFA Meta Directory
            var inspection = InspectContainerStatic(rafFilePath);
            if (inspection.SensorType == RafSensorType.XTrans) return false;
            if (inspection.SensorType == RafSensorType.Bayer) return true;

            // 2. Secondary fallback to metadata make/model naming
            string? model = RawMetadataReader.TryGetCameraMakeAndModel(rafFilePath);
            if (!string.IsNullOrWhiteSpace(model))
            {
                string m = model.ToUpperInvariant();
                // Medium format GFX series
                if (m.Contains("GFX")) return true;

                // APS-C Bayer series: X-A series (X-A1, X-A2, X-A3, X-A5, X-A7, X-A10, X-A20)
                if (m.Contains("X-A") || m.Contains("XA1") || m.Contains("XA2") || m.Contains("XA3") ||
                    m.Contains("XA5") || m.Contains("XA7") || m.Contains("XA10") || m.Contains("XA20"))
                    return true;

                // X-T Bayer: X-T100, X-T200
                if (m.Contains("X-T100") || m.Contains("XT100") || m.Contains("X-T200") || m.Contains("XT200"))
                    return true;

                // Fixed lens Bayer: XF10
                if (m.Contains("XF10") || m.Contains("XF 10"))
                    return true;

                // Compacts / FinePix models (all Bayer)
                if (m.Contains("FINEPIX") || m.Contains("S100FS") || m.Contains("S200EXR"))
                    return true;
            }

            return false;
        }

        public async Task<RafConversionResult> PatchModelAsync(string inputRafPath, string outputRafPath, string targetModel, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(inputRafPath))
                {
                    return new RafConversionResult(false, null, $"Source RAF file not found: {inputRafPath}");
                }

                string? outDir = Path.GetDirectoryName(outputRafPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }

                if (!string.Equals(Path.GetFullPath(inputRafPath), Path.GetFullPath(outputRafPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(inputRafPath, outputRafPath, overwrite: true);
                }

                bool success = RafMetadataModifier.PatchModelOnly(outputRafPath, targetModel);
                if (!success)
                {
                    return new RafConversionResult(false, null, "Failed to patch RAF model metadata.");
                }

                return new RafConversionResult(true, outputRafPath, null);
            }
            catch (Exception ex)
            {
                return new RafConversionResult(false, null, $"Failed patching RAF model: {ex.Message}");
            }
        }
    }
}

