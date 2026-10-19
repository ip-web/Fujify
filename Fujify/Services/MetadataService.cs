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

        public MetadataResult InjectFujifilmMetadata(string dngFilePath, string? outputFolder, bool createBackup, bool isOriginallyDng, string? customOutputFilePath = null)
        {
            // Direct in-memory TIFF/DNG IFD0 rewriting (<2ms, 0 external processes)
            var nativeResult = DngMetadataEditor.InjectFujifilmMetadata(dngFilePath, outputFolder, createBackup, isOriginallyDng, customOutputFilePath);
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

        public async Task<MetadataResult> InjectFujifilmMetadataAsync(string dngFilePath, string? outputFolder, bool createBackup, bool isOriginallyDng, string? customOutputFilePath = null, CancellationToken cancellationToken = default)
        {
            // Direct in-memory TIFF/DNG IFD0 rewriting (<2ms, 0 external processes)
            var nativeResult = await DngMetadataEditor.InjectFujifilmMetadataAsync(
                dngFilePath,
                outputFolder,
                createBackup,
                isOriginallyDng,
                customOutputFilePath,
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

            // 2. Check system PATH
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

            // 3. Fallback on Unix/macOS: run bundled perl script
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string plPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "exiftool_files", "exiftool.pl");
                if (File.Exists(plPath))
                {
                    return ("perl", $"\"{plPath}\"");
                }
            }

            return null;
        }

        private static string ExtractExifValue(string exifOutput, string tagName)
        {
            var match = Regex.Match(exifOutput, $@"{tagName}\s*:\s*(.+)");
            return match.Success ? match.Groups[1].Value.Trim() : "Unknown";
        }
    }
}
