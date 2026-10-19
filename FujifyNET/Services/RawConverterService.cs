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
using System.Threading;
using System.Threading.Tasks;

namespace FujifyNET.Services
{
    public class RawConverterService : IRawConverterService
    {
        private readonly IProcessRunner _processRunner;

        public RawConverterService(IProcessRunner? processRunner = null)
        {
            _processRunner = processRunner ?? new ProcessRunner();
        }

        public ConversionResult ConvertToDng(string filePath, bool embedOriginalRaw, string? targetDngPath = null)
        {
            string dngFilePath = !string.IsNullOrEmpty(targetDngPath) ? targetDngPath : Path.ChangeExtension(filePath, ".dng");

            // 1. Try high-performance in-process native library conversion
            if (DngLabNative.IsAvailable)
            {
                var (success, errorMsg) = DngLabNative.ConvertFile(filePath, dngFilePath, embedOriginalRaw);
                if (success && File.Exists(dngFilePath))
                {
                    return new ConversionResult(true, dngFilePath, null);
                }

                string cliPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "dnglab.exe");
                if (!File.Exists(cliPath))
                {
                    return new ConversionResult(false, null, errorMsg ?? "Native conversion failed.");
                }
            }

            // 2. Fallback to external DNGLab CLI process
            string dngLabPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "dnglab.exe");

            if (!File.Exists(dngLabPath))
            {
                return new ConversionResult(false, null, "DNGLab executable or native library not found.");
            }

            try
            {
                string arguments = "convert --override";
                if (!embedOriginalRaw)
                {
                    arguments += " --embed-raw false";
                }
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

        public async Task<ConversionResult> ConvertToDngAsync(string filePath, bool embedOriginalRaw, string? targetDngPath = null, CancellationToken cancellationToken = default)
        {
            string dngFilePath = !string.IsNullOrEmpty(targetDngPath) ? targetDngPath : Path.ChangeExtension(filePath, ".dng");

            // 1. Try high-performance in-process native library conversion
            if (DngLabNative.IsAvailable)
            {
                var (success, errorMsg) = await DngLabNative.ConvertFileAsync(filePath, dngFilePath, embedOriginalRaw, cancellationToken);
                if (success && File.Exists(dngFilePath))
                {
                    return new ConversionResult(true, dngFilePath, null);
                }

                string cliPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "dnglab.exe");
                if (!File.Exists(cliPath))
                {
                    return new ConversionResult(false, null, errorMsg ?? "Native conversion failed.");
                }
            }

            // 2. Fallback to external DNGLab CLI process
            string dngLabPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "dnglab.exe");

            if (!File.Exists(dngLabPath))
            {
                return new ConversionResult(false, null, "DNGLab executable or native library not found.");
            }

            try
            {
                string arguments = "convert --override";
                if (!embedOriginalRaw)
                {
                    arguments += " --embed-raw false";
                }
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
    }
}
