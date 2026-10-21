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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Fujify.Services
{
    public class MetadataService : IMetadataService
    {
        private const string CommonArgs = "-CameraProfilesMake=\"FUJIFILM\" -CameraProfilesModel=\"X-T5\" -CameraProfilesUniqueCameraModel=\"Fujifilm X-T5\" -CameraProfilesCameraRawProfile=\"True\" -UniqueCameraModel=\"Fujifilm X-T5\"";
        private readonly IProcessRunner _processRunner;

        public MetadataService(IProcessRunner? processRunner = null)
        {
            _processRunner = processRunner ?? new ProcessRunner();
        }

        public string GetCameraMakeAndModel(string filePath)
        {
            // Direct in-memory reader (<1ms vs 300ms ExifTool process)
            string? quickInfo = DngMetadataEditor.ReadCameraMakeAndModel(filePath) 
                ?? RawMetadataReader.TryGetCameraMakeAndModel(filePath);

            if (!string.IsNullOrWhiteSpace(quickInfo))
            {
                return quickInfo;
            }

            var runner = FindExifToolRunner();
            if (runner == null)
            {
                return "Unknown Camera";
            }

            try
            {
                string rawArgs = $"-Make -Model \"{filePath}\"";
                string arguments = !string.IsNullOrEmpty(runner.Value.PrefixArgs)
                    ? $"{runner.Value.PrefixArgs} {rawArgs}"
                    : rawArgs;

                var (exitCode, output, error) = _processRunner.Run(runner.Value.Command, arguments);

                if (exitCode == 0)
                {
                    string cameraMake = ExtractExifValue(output, "Make");
                    string cameraModel = ExtractExifValue(output, "Camera Model Name");

                    // Shorten "NIKON CORPORATION" to "NIKON"
                    if (cameraMake.Equals("NIKON CORPORATION", StringComparison.OrdinalIgnoreCase))
                    {
                        cameraMake = "NIKON";
                    }

                    return $"{cameraMake} {cameraModel}".Trim();
                }

                return !string.IsNullOrWhiteSpace(error) ? $"Error: {error}" : "Unknown Camera";
            }
            catch (Exception ex)
            {
                return $"Exception: {ex.Message}";
            }
        }

        public int GetOrientation(string filePath)
        {
            int? direct = RawMetadataReader.TryGetOrientation(filePath);
            if (direct.HasValue) return direct.Value;

            var runner = FindExifToolRunner();
            if (runner != null)
            {
                try
                {
                    string rawArgs = $"-Orientation# -n -s3 \"{filePath}\"";
                    string arguments = !string.IsNullOrEmpty(runner.Value.PrefixArgs)
                        ? $"{runner.Value.PrefixArgs} {rawArgs}"
                        : rawArgs;

                    var (exitCode, output, _) = _processRunner.Run(runner.Value.Command, arguments);
                    if (exitCode == 0 && int.TryParse(output.Trim(), out int exifOrient) && exifOrient >= 1 && exifOrient <= 8)
                    {
                        return exifOrient;
                    }
                }
                catch { }
            }

            return 1;
        }

        public ExifData? GetExifData(string filePath)
        {
            var runner = FindExifToolRunner();
            if (runner == null) return null;

            try
            {
                string rawArgs = $"-LensModel -Lens -FNumber -Aperture -ExposureTime -ShutterSpeed -ISO -FocalLength -DateTimeOriginal -CreateDate -Orientation \"{filePath}\"";
                string arguments = !string.IsNullOrEmpty(runner.Value.PrefixArgs)
                    ? $"{runner.Value.PrefixArgs} {rawArgs}"
                    : rawArgs;

                var (exitCode, output, error) = _processRunner.Run(runner.Value.Command, arguments);
                if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    string lens = ExtractExifValue(output, "Lens Model", "Lens", "Lens Type", "Lens Spec");
                    string aperture = ExtractExifValue(output, "F Number", "Aperture Value", "Aperture");
                    if (aperture.StartsWith("f/", StringComparison.OrdinalIgnoreCase) || aperture.StartsWith("ƒ/", StringComparison.OrdinalIgnoreCase))
                    {
                        aperture = aperture.Substring(2).Trim();
                    }
                    string shutter = ExtractExifValue(output, "Exposure Time", "Shutter Speed Value", "Shutter Speed");
                    string iso = ExtractExifValue(output, "ISO");
                    if (iso.StartsWith("ISO", StringComparison.OrdinalIgnoreCase))
                    {
                        iso = iso.Substring(3).Trim();
                    }
                    string focal = ExtractExifValue(output, "Focal Length");
                    string dt = ExtractExifValue(output, "Date/Time Original", "Create Date", "Date Time Original");
                    string? orientation = ExtractExifValue(output, "Orientation");
                    if (orientation.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        int? direct = RawMetadataReader.TryGetOrientation(filePath);
                        orientation = direct switch
                        {
                            3 => "Rotate 180",
                            6 => "Rotate 90 CW",
                            8 => "Rotate 270 CW",
                            _ => null
                        };
                    }

                    return new ExifData(
                        lens.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : lens,
                        aperture.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : aperture,
                        shutter.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : shutter,
                        iso.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : iso,
                        focal.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : focal,
                        dt.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : dt,
                        orientation?.Equals("Unknown", StringComparison.OrdinalIgnoreCase) == true ? null : orientation
                    );
                }
            }
            catch { }
            return null;
        }

        public MetadataResult InjectFujifilmMetadata(string dngFilePath, string? outputFolder, bool createBackup, bool isOriginallyDng, string? customOutputFilePath = null, string targetUniqueCameraModel = "Fujifilm X-T5")
        {
            // Direct in-memory TIFF/DNG IFD0 rewriting (<2ms, 0 external processes)
            var nativeResult = DngMetadataEditor.InjectFujifilmMetadata(
                dngFilePath,
                outputFolder,
                createBackup,
                isOriginallyDng,
                customOutputFilePath,
                targetUniqueCameraModel: targetUniqueCameraModel);

            if (nativeResult.Success)
            {
                return nativeResult;
            }

            // Fallback to ExifTool CLI if native editor encountered an issue
            var runner = FindExifToolRunner();
            if (runner == null)
            {
                return nativeResult;
            }

            string baseArguments = CommonArgs;
            bool useOutputPath = !string.IsNullOrEmpty(outputFolder) || !string.IsNullOrEmpty(customOutputFilePath);

            if (useOutputPath)
            {
                string outputFilePath = !string.IsNullOrEmpty(customOutputFilePath)
                    ? customOutputFilePath
                    : Path.Combine(outputFolder!, Path.GetFileNameWithoutExtension(dngFilePath) + ".dng");

                if (File.Exists(outputFilePath))
                {
                    try
                    {
                        File.Delete(outputFilePath);
                    }
                    catch (Exception ex)
                    {
                        return new MetadataResult(false, $"Failed to delete existing file in output folder: {ex.Message}");
                    }
                }

                baseArguments += $" -o \"{outputFilePath}\"";
            }
            else
            {
                // In-place modification
                if (isOriginallyDng)
                {
                    if (!createBackup)
                    {
                        baseArguments += " -overwrite_original_in_place";
                    }
                }
                else
                {
                    baseArguments += " -overwrite_original";
                }
            }

            string rawArgs = $"{baseArguments} \"{dngFilePath}\"";
            string arguments = !string.IsNullOrEmpty(runner.Value.PrefixArgs)
                ? $"{runner.Value.PrefixArgs} {rawArgs}"
                : rawArgs;

            try
            {
                var (exitCode, output, error) = _processRunner.Run(runner.Value.Command, arguments);

                if (exitCode == 0)
                {
                    if (output.Contains("image files updated") || output.Contains("image files created"))
                    {
                        CleanupTemporaryFiles(dngFilePath, useOutputPath, createBackup, isOriginallyDng);
                        return new MetadataResult(true, null);
                    }
                    else
                    {
                        return new MetadataResult(false, "Error writing the metadata");
                    }
                }

                string errorMsg = !string.IsNullOrWhiteSpace(error) ? error : output;
                return new MetadataResult(false, $"Error: {errorMsg}");
            }
            catch (Exception ex)
            {
                return new MetadataResult(false, $"Exception: {ex.Message}");
            }
        }

        public async Task<MetadataResult> InjectFujifilmMetadataAsync(string dngFilePath, string? outputFolder, bool createBackup, bool isOriginallyDng, string? customOutputFilePath = null, string targetUniqueCameraModel = "Fujifilm X-T5", CancellationToken cancellationToken = default)
        {
            // Direct in-memory TIFF/DNG IFD0 rewriting (<2ms, 0 external processes)
            var nativeResult = await DngMetadataEditor.InjectFujifilmMetadataAsync(
                dngFilePath,
                outputFolder,
                createBackup,
                isOriginallyDng,
                customOutputFilePath,
                targetUniqueCameraModel: targetUniqueCameraModel,
                cancellationToken: cancellationToken);

            if (nativeResult.Success)
            {
                return nativeResult;
            }

            // Fallback to ExifTool CLI if native editor encountered an issue
            var runner = FindExifToolRunner();
            if (runner == null)
            {
                return nativeResult;
            }

            string baseArguments = CommonArgs;
            bool useOutputPath = !string.IsNullOrEmpty(outputFolder) || !string.IsNullOrEmpty(customOutputFilePath);

            if (useOutputPath)
            {
                string outputFilePath = !string.IsNullOrEmpty(customOutputFilePath)
                    ? customOutputFilePath
                    : Path.Combine(outputFolder!, Path.GetFileNameWithoutExtension(dngFilePath) + ".dng");

                if (File.Exists(outputFilePath))
                {
                    try
                    {
                        File.Delete(outputFilePath);
                    }
                    catch (Exception ex)
                    {
                        return new MetadataResult(false, $"Failed to delete existing file in output folder: {ex.Message}");
                    }
                }

                baseArguments += $" -o \"{outputFilePath}\"";
            }
            else
            {
                // In-place modification
                if (isOriginallyDng)
                {
                    if (!createBackup)
                    {
                        baseArguments += " -overwrite_original_in_place";
                    }
                }
                else
                {
                    baseArguments += " -overwrite_original";
                }
            }

            string rawArgs = $"{baseArguments} \"{dngFilePath}\"";
            string arguments = !string.IsNullOrEmpty(runner.Value.PrefixArgs)
                ? $"{runner.Value.PrefixArgs} {rawArgs}"
                : rawArgs;

            try
            {
                var (exitCode, output, error) = await _processRunner.RunAsync(runner.Value.Command, arguments, cancellationToken);

                if (exitCode == 0)
                {
                    if (output.Contains("image files updated") || output.Contains("image files created"))
                    {
                        CleanupTemporaryFiles(dngFilePath, useOutputPath, createBackup, isOriginallyDng);
                        return new MetadataResult(true, null);
                    }
                    else
                    {
                        return new MetadataResult(false, "Error writing the metadata");
                    }
                }

                string errorMsg = !string.IsNullOrWhiteSpace(error) ? error : output;
                return new MetadataResult(false, $"Error: {errorMsg}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new MetadataResult(false, $"Exception: {ex.Message}");
            }
        }

        public void CleanupTemporaryFiles(string dngFilePath, bool useOutputPath, bool createBackup, bool isOriginallyDng)
        {
            try
            {
                if (useOutputPath)
                {
                    // Only delete intermediate DNG if it was converted from a non-DNG RAW.
                    // NEVER delete the user's original source DNG!
                    if (!isOriginallyDng && File.Exists(dngFilePath))
                    {
                        File.Delete(dngFilePath);
                    }
                }
                else
                {
                    string temporaryFilePath = Path.ChangeExtension(dngFilePath, ".dng_original");

                    if (isOriginallyDng && createBackup)
                    {
                        // Keep .dng_original file if it's an original DNG and backup is requested
                        return;
                    }

                    if (File.Exists(temporaryFilePath))
                    {
                        File.Delete(temporaryFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete temporary files: {ex.Message}");
            }
        }

        private static (string Command, string? PrefixArgs)? FindExifToolRunner()
        {
            // 1. Check local libs directory
            string binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "exiftool.exe" : "exiftool";
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", binaryName);
            if (File.Exists(localPath)) return (localPath, null);

            // 2. Check well-known Linux / macOS system paths directly
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string[] standardPaths = new[]
                {
                    "/usr/bin/exiftool",
                    "/usr/local/bin/exiftool",
                    "/opt/homebrew/bin/exiftool",
                    "/snap/bin/exiftool",
                    "/usr/pkg/bin/exiftool"
                };
                foreach (var path in standardPaths)
                {
                    if (File.Exists(path)) return (path, null);
                }
            }

            // 3. Check system PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                char sep = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
                foreach (var dir in pathEnv.Split(sep))
                {
                    try
                    {
                        string fullPath = Path.Combine(dir.Trim(), binaryName);
                        if (File.Exists(fullPath)) return (fullPath, null);
                    }
                    catch { }
                }
            }

            // 4. Fallback on Windows only: bundled strawberry perl script
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string plPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "exiftool_files", "exiftool.pl");
                if (File.Exists(plPath))
                {
                    return ("perl", $"\"{plPath}\"");
                }
            }

            return null;
        }

        private static string ExtractExifValue(string exifOutput, params string[] tagNames)
        {
            foreach (var tagName in tagNames)
            {
                var match = Regex.Match(exifOutput, $@"(?:^|\n)\s*{Regex.Escape(tagName)}\s*:\s*(.+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var val = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(val) && !val.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        return val;
                    }
                }
            }
            return "Unknown";
        }
    }
}
