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
using System.Threading;
using System.Threading.Tasks;

namespace Fujify.Services
{
    public class RawConverterService : IRawConverterService
    {
        private readonly IProcessRunner _processRunner;

        public RawConverterService(IProcessRunner? processRunner = null)
        {
            _processRunner = processRunner ?? new ProcessRunner();
        }

        public ConversionResult ConvertToDng(string filePath, bool embedOriginalRaw, string? targetDngPath = null, bool losslessCompression = true)
        {
            string dngFilePath = !string.IsNullOrEmpty(targetDngPath) ? targetDngPath : Path.ChangeExtension(filePath, ".dng");

            // 1. On Linux: Prefer system-installed dnglab dependency first to leverage the latest installed version
            if (OperatingSystem.IsLinux())
            {
                string? systemDngLab = FindSystemDngLabPath();
                if (!string.IsNullOrEmpty(systemDngLab))
                {
                    var cliResult = ConvertWithCli(systemDngLab, filePath, dngFilePath, embedOriginalRaw, losslessCompression);
                    if (cliResult.Success)
                    {
                        return cliResult;
                    }
                    // If system CLI failed, continue to fallback to integrated code
                }
            }

            // 2. Try integrated high-performance native library conversion (dnglab_ffi)
            if (DngLabNative.IsAvailable)
            {
                var (success, errorMsg) = DngLabNative.ConvertFile(filePath, dngFilePath, embedOriginalRaw);
                if (success && File.Exists(dngFilePath))
                {
                    return new ConversionResult(true, dngFilePath, null);
                }

                // If native failed and no CLI runner exists, return the error
                string? cliPath = FindDngLabPath();
                if (string.IsNullOrEmpty(cliPath) || !File.Exists(cliPath))
                {
                    return new ConversionResult(false, null, errorMsg ?? "Native conversion failed.");
                }
            }

            // 3. Fallback to external DNGLab CLI process
            if (OperatingSystem.IsIOS())
            {
                return new ConversionResult(false, null, "Native conversion library (dnglab_ffi) is required on iOS. External process execution is not permitted by iOS.");
            }

            string? dngLabPath = FindDngLabPath();
            if (string.IsNullOrEmpty(dngLabPath) || !File.Exists(dngLabPath))
            {
                return new ConversionResult(false, null, "DNGLab executable or native library not found.");
            }

            return ConvertWithCli(dngLabPath, filePath, dngFilePath, embedOriginalRaw, losslessCompression);
        }

        public async Task<ConversionResult> ConvertToDngAsync(string filePath, bool embedOriginalRaw, string? targetDngPath = null, bool losslessCompression = true, CancellationToken cancellationToken = default)
        {
            string dngFilePath = !string.IsNullOrEmpty(targetDngPath) ? targetDngPath : Path.ChangeExtension(filePath, ".dng");

            // 1. On Linux: Prefer system-installed dnglab dependency first to leverage the latest installed version
            if (OperatingSystem.IsLinux())
            {
                string? systemDngLab = FindSystemDngLabPath();
                if (!string.IsNullOrEmpty(systemDngLab))
                {
                    var cliResult = await ConvertWithCliAsync(systemDngLab, filePath, dngFilePath, embedOriginalRaw, losslessCompression, cancellationToken);
                    if (cliResult.Success)
                    {
                        return cliResult;
                    }
                    // If system CLI failed, continue to fallback to integrated code
                }
            }

            // 2. Try integrated high-performance native library conversion (dnglab_ffi)
            if (DngLabNative.IsAvailable)
            {
                var (success, errorMsg) = await DngLabNative.ConvertFileAsync(filePath, dngFilePath, embedOriginalRaw, cancellationToken);
                if (success && File.Exists(dngFilePath))
                {
                    return new ConversionResult(true, dngFilePath, null);
                }

                string? cliPath = FindDngLabPath();
                if (string.IsNullOrEmpty(cliPath) || !File.Exists(cliPath))
                {
                    return new ConversionResult(false, null, errorMsg ?? "Native conversion failed.");
                }
            }

            // 3. Fallback to external DNGLab CLI process
            if (OperatingSystem.IsIOS())
            {
                return new ConversionResult(false, null, "Native conversion library (dnglab_ffi) is required on iOS. External process execution is not permitted by iOS.");
            }

            string? dngLabPath = FindDngLabPath();
            if (string.IsNullOrEmpty(dngLabPath) || !File.Exists(dngLabPath))
            {
                return new ConversionResult(false, null, "DNGLab executable or native library not found.");
            }

            return await ConvertWithCliAsync(dngLabPath, filePath, dngFilePath, embedOriginalRaw, losslessCompression, cancellationToken);
        }

        private ConversionResult ConvertWithCli(string dngLabPath, string filePath, string dngFilePath, bool embedOriginalRaw, bool losslessCompression)
        {
            try
            {
                string arguments = "convert --override";
                if (!embedOriginalRaw)
                {
                    arguments += " --embed-raw false";
                }
                arguments += losslessCompression ? " --compression lossless" : " --compression uncompressed";
                arguments += $" \"{filePath}\" \"{dngFilePath}\"";

                var (exitCode, output, error) = _processRunner.Run(dngLabPath, arguments);

                if (exitCode != 0 || output.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || !File.Exists(dngFilePath))
                {
                    string errorMsg = !string.IsNullOrWhiteSpace(error) ? error : output;
                    return new ConversionResult(false, null, $"Conversion failed: {errorMsg}");
                }

                return new ConversionResult(true, dngFilePath, null);
            }
            catch (Exception ex)
            {
                return new ConversionResult(false, null, $"Exception during conversion: {ex.Message}");
            }
        }

        private async Task<ConversionResult> ConvertWithCliAsync(string dngLabPath, string filePath, string dngFilePath, bool embedOriginalRaw, bool losslessCompression, CancellationToken cancellationToken)
        {
            try
            {
                string arguments = "convert --override";
                if (!embedOriginalRaw)
                {
                    arguments += " --embed-raw false";
                }
                arguments += losslessCompression ? " --compression lossless" : " --compression uncompressed";
                arguments += $" \"{filePath}\" \"{dngFilePath}\"";

                var (exitCode, output, error) = await _processRunner.RunAsync(dngLabPath, arguments, cancellationToken);

                if (exitCode != 0 || output.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || !File.Exists(dngFilePath))
                {
                    string errorMsg = !string.IsNullOrWhiteSpace(error) ? error : output;
                    return new ConversionResult(false, null, $"Conversion failed: {errorMsg}");
                }

                return new ConversionResult(true, dngFilePath, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ConversionResult(false, null, $"Exception during conversion: {ex.Message}");
            }
        }

        private static string? FindSystemDngLabPath()
        {
            string binaryName = "dnglab";

            // Check well-known Linux system and user package paths
            string[] standardPaths =
            [
                "/usr/bin/dnglab",
                "/usr/local/bin/dnglab",
                "/snap/bin/dnglab",
                "/opt/homebrew/bin/dnglab",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "dnglab")
            ];

            foreach (var path in standardPaths)
            {
                if (File.Exists(path)) return path;
            }

            // Check system PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(':'))
                {
                    try
                    {
                        string fullPath = Path.Combine(dir.Trim(), binaryName);
                        if (File.Exists(fullPath)) return fullPath;
                    }
                    catch { }
                }
            }

            return null;
        }

        private static string? FindDngLabPath()
        {
            string binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dnglab.exe" : "dnglab";

            // On Linux, prioritize system-installed dnglab
            if (OperatingSystem.IsLinux())
            {
                string? systemPath = FindSystemDngLabPath();
                if (!string.IsNullOrEmpty(systemPath)) return systemPath;

                // Fall back to bundled binary in libs
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", binaryName);
                if (File.Exists(localPath)) return localPath;
            }
            else
            {
                // On Windows/macOS: check local libs directory first
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", binaryName);
                if (File.Exists(localPath)) return localPath;

                // Then check system PATH
                var pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    char sep = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
                    foreach (var dir in pathEnv.Split(sep))
                    {
                        try
                        {
                            string fullPath = Path.Combine(dir.Trim(), binaryName);
                            if (File.Exists(fullPath)) return fullPath;
                        }
                        catch { }
                    }
                }
            }

            return null;
        }
    }
}
