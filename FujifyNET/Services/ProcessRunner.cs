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
using System.Threading;
using System.Threading.Tasks;

namespace FujifyNET.Services
{
    public class ProcessRunner : IProcessRunner
    {
        public (int ExitCode, string Output, string Error) Run(string fileName, string arguments)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    return (-1, string.Empty, "Failed to start process.");
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                process.WaitForExit();
                Task.WaitAll(outputTask, errorTask);

                return (process.ExitCode, outputTask.Result, errorTask.Result);
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        }

        public async Task<(int ExitCode, string Output, string Error)> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    return (-1, string.Empty, "Failed to start process.");
                }

                using var reg = cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        // Ignore race condition on exit
                    }
                });

                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(outputTask, errorTask);

                return (process.ExitCode, await outputTask, await errorTask);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        }
    }
}
