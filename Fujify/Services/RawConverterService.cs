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
using System.Diagnostics;
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
                return new ConversionResult(false, null, GetMissingDngLabErrorMessage());
            }

            return ConvertWithCli(dngLabPath, filePath, dngFilePath, embedOriginalRaw, losslessCompression);
        }

        public async Task<ConversionResult> ConvertToDngAsync(string filePath, bool embedOriginalRaw, string? targetDngPath = null, bool losslessCompression = true, CancellationToken cancellationToken = default)
        {
            string dngFilePath = !string.IsNullOrEmpty(targetDngPath) ? targetDngPath : Path.ChangeExtension(filePath, ".dng");

            // 1. On Linux / macOS: Prefer system-installed dnglab dependency first to leverage the latest installed version
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !OperatingSystem.IsIOS())
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
                return new ConversionResult(false, null, GetMissingDngLabErrorMessage());
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
                    if (errorMsg.Contains("Bad CPU type", StringComparison.OrdinalIgnoreCase))
                    {
                        string arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "Intel Mac (x86_64)" : "Apple Silicon (ARM64)";
                        errorMsg += $". The binary at '{dngLabPath}' is not compatible with {arch}. On macOS, please install a compatible DNGLab binary using 'brew install dnglab' or 'cargo install dnglab'.";
                    }
                    return new ConversionResult(false, null, $"Conversion failed: {errorMsg}");
                }

                return new ConversionResult(true, dngFilePath, null);
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                if (msg.Contains("Bad CPU type", StringComparison.OrdinalIgnoreCase))
                {
                    string arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "Intel Mac (x86_64)" : "Apple Silicon (ARM64)";
                    msg += $". The binary at '{dngLabPath}' is not compatible with {arch}. On macOS, please install a compatible DNGLab binary using 'brew install dnglab' or 'cargo install dnglab'.";
                }
                return new ConversionResult(false, null, $"Exception during conversion: {msg}");
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
                    if (errorMsg.Contains("Bad CPU type", StringComparison.OrdinalIgnoreCase))
                    {
                        string arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "Intel Mac (x86_64)" : "Apple Silicon (ARM64)";
                        errorMsg += $". The binary at '{dngLabPath}' is not compatible with {arch}. On macOS, please install a compatible DNGLab binary using 'brew install dnglab' or 'cargo install dnglab'.";
                    }
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
                string msg = ex.Message;
                if (msg.Contains("Bad CPU type", StringComparison.OrdinalIgnoreCase))
                {
                    string arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "Intel Mac (x86_64)" : "Apple Silicon (ARM64)";
                    msg += $". The binary at '{dngLabPath}' is not compatible with {arch}. On macOS, please install a compatible DNGLab binary using 'brew install dnglab' or 'cargo install dnglab'.";
                }
                return new ConversionResult(false, null, $"Exception during conversion: {msg}");
            }
        }

        private static string GetMissingDngLabErrorMessage()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string archName = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "Intel Mac (x86_64)" : "Apple Silicon (ARM64)";
                return $"DNGLab executable compatible with {archName} not found. On macOS, you can install DNGLab via Homebrew ('brew install dnglab') or Cargo ('cargo install dnglab'), or ensure an {archName} compatible 'dnglab' binary is located in the application's 'libs' folder.";
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "DNGLab executable or native library not found. On Linux, you can install DNGLab via your package manager or Cargo ('cargo install dnglab'), or ensure 'dnglab' is located in the 'libs' folder.";
            }
            return "DNGLab executable or native library not found.";
        }

        private static bool IsExecutableCompatibleWithCurrentArchitecture(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            // Only Mach-O binaries on macOS need architecture validation against CPU
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return true;
            }

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length < 8) return false;

                byte[] header = new byte[8];
                int bytesRead = fs.Read(header, 0, 8);
                if (bytesRead < 8) return false;

                // Shell script or text wrapper (starts with #!)
                if (header[0] == '#' && header[1] == '!')
                {
                    return true;
                }

                // Mach-O Fat / Universal binary: 0xCAFEBABE or 0xCAFEBABF (big/little endian)
                uint magicBigEndian = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
                uint magicLittleEndian = BitConverter.ToUInt32(header, 0);

                if (magicBigEndian == 0xCAFEBABE || magicBigEndian == 0xCAFEBABF ||
                    magicLittleEndian == 0xCAFEBABE || magicLittleEndian == 0xCAFEBABF)
                {
                    return true;
                }

                // Mach-O 64-bit binary: 0xFEEDFACF (little endian: CF FA ED FE)
                if (magicLittleEndian == 0xFEEDFACF)
                {
                    uint cpuType = BitConverter.ToUInt32(header, 4);
                    // CPU_TYPE_X86_64 = 0x01000007
                    // CPU_TYPE_ARM64  = 0x0100000C
                    if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                    {
                        // On Intel Mac, only x86_64 binaries can run. ARM64 binaries will fail with "Bad CPU type".
                        return cpuType == 0x01000007;
                    }
                    else if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                    {
                        // On Apple Silicon Mac, ARM64 runs natively, and x86_64 can run under Rosetta 2
                        return true;
                    }
                }

                // Mach-O 64-bit big endian: 0xFEEDFACF
                if (magicBigEndian == 0xFEEDFACF)
                {
                    uint cpuType = ((uint)header[4] << 24) | ((uint)header[5] << 16) | ((uint)header[6] << 8) | header[7];
                    if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                    {
                        return cpuType == 0x01000007;
                    }
                    else if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                    {
                        return true;
                    }
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        private static void EnsureExecutablePermission(string filePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !File.Exists(filePath))
            {
                return;
            }

            try
            {
                var mode = File.GetUnixFileMode(filePath);
                var needed = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((mode & needed) != needed)
                {
                    File.SetUnixFileMode(filePath, mode | needed);
                }
            }
            catch
            {
                try
                {
                    using var proc = Process.Start(new ProcessStartInfo("chmod", $"+x \"{filePath}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    proc?.WaitForExit(1000);
                }
                catch { }
            }
        }

        private static string? FindSystemDngLabPath()
        {
            string binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dnglab.exe" : "dnglab";
            var standardPaths = new System.Collections.Generic.List<string>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                standardPaths.Add("/opt/homebrew/bin/dnglab");
                standardPaths.Add("/usr/local/bin/dnglab");
                standardPaths.Add("/opt/local/bin/dnglab");
                standardPaths.Add("/usr/bin/dnglab");
                standardPaths.Add("/bin/dnglab");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                standardPaths.Add("/usr/bin/dnglab");
                standardPaths.Add("/usr/local/bin/dnglab");
                standardPaths.Add("/snap/bin/dnglab");
                standardPaths.Add("/opt/homebrew/bin/dnglab");
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userHome))
                {
                    standardPaths.Add(Path.Combine(userHome, ".cargo", "bin", binaryName));
                    standardPaths.Add(Path.Combine(userHome, ".local", "bin", binaryName));
                    standardPaths.Add(Path.Combine(userHome, "bin", binaryName));
                }
            }

            foreach (var path in standardPaths)
            {
                if (File.Exists(path) && IsExecutableCompatibleWithCurrentArchitecture(path))
                {
                    EnsureExecutablePermission(path);
                    return path;
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
                        string trimmed = dir.Trim().Trim('\"');
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        string fullPath = Path.Combine(trimmed, binaryName);
                        if (File.Exists(fullPath) && IsExecutableCompatibleWithCurrentArchitecture(fullPath))
                        {
                            EnsureExecutablePermission(fullPath);
                            return fullPath;
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private static string? FindDngLabPath()
        {
            string binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dnglab.exe" : "dnglab";
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. Check local / bundled application directories
            var localCandidates = new System.Collections.Generic.List<string>();

            // Architecture-specific bundled subdirectories
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    localCandidates.Add(Path.Combine(baseDir, "libs", "osx-x64", binaryName));
                }
                else
                {
                    localCandidates.Add(Path.Combine(baseDir, "libs", "osx-arm64", binaryName));
                }
                localCandidates.Add(Path.Combine(baseDir, "libs", binaryName));
                localCandidates.Add(Path.Combine(baseDir, binaryName));
                localCandidates.Add(Path.Combine(baseDir, "libs", "mac", binaryName));
                // macOS .app bundle structure: Contents/Resources, Contents/MacOS
                localCandidates.Add(Path.Combine(baseDir, "..", "Resources", "libs", binaryName));
                localCandidates.Add(Path.Combine(baseDir, "..", "Resources", binaryName));
                localCandidates.Add(Path.Combine(baseDir, "..", "MacOS", "libs", binaryName));
                localCandidates.Add(Path.Combine(baseDir, "..", "MacOS", binaryName));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                localCandidates.Add(Path.Combine(baseDir, "libs", "linux-x64", binaryName));
                localCandidates.Add(Path.Combine(baseDir, "libs", "linux-arm64", binaryName));
                localCandidates.Add(Path.Combine(baseDir, "libs", binaryName));
                localCandidates.Add(Path.Combine(baseDir, binaryName));
            }
            else
            {
                localCandidates.Add(Path.Combine(baseDir, "libs", binaryName));
                localCandidates.Add(Path.Combine(baseDir, binaryName));
            }

            // RuntimeIdentifier native folder
            localCandidates.Add(Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", binaryName));

            // Dev / debug fallback relative paths
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                localCandidates.Add(Path.Combine(baseDir, "..", "..", "..", "Fujify", "libs", "osx-x64", binaryName));
            }
            else
            {
                localCandidates.Add(Path.Combine(baseDir, "..", "..", "..", "Fujify", "libs", "osx-arm64", binaryName));
            }
            localCandidates.Add(Path.Combine(baseDir, "..", "..", "..", "Fujify", "libs", "linux-x64", binaryName));
            localCandidates.Add(Path.Combine(baseDir, "..", "..", "..", "Fujify", "libs", binaryName));
            localCandidates.Add(Path.Combine(baseDir, "..", "..", "..", "build_assets", "tools", "mac", binaryName));
            localCandidates.Add(Path.Combine(baseDir, "..", "..", "..", "build_assets", "tools", binaryName));
            localCandidates.Add(Path.Combine(baseDir, "..", "..", "..", "..", "build_assets", "tools", "mac", binaryName));

            foreach (var candidate in localCandidates)
            {
                if (File.Exists(candidate) && IsExecutableCompatibleWithCurrentArchitecture(candidate))
                {
                    EnsureExecutablePermission(candidate);
                    return candidate;
                }
            }

            // 2. Fall back to system / package manager / PATH lookup
            return FindSystemDngLabPath();
        }
    }
}
