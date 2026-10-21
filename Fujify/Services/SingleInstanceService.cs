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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Fujify.Services
{
    public static class SingleInstanceService
    {
        private static Mutex? _mutex;
        private static CancellationTokenSource? _serverCts;
        private static Action<IReadOnlyList<string>>? _filesReceivedCallback;

        private static readonly ConcurrentQueue<string> _incomingQueue = new();
        private static readonly object _timerLock = new();
        private static Timer? _debounceTimer;

        private static string GetPipeName()
        {
            string user = Environment.UserName.Replace("\\", "_").Replace("/", "_");
            return $"Fujify_IPC_Pipe_{user}";
        }

        private static string GetMutexName()
        {
            string user = Environment.UserName.Replace("\\", "_").Replace("/", "_");
            return $"Fujify_SingleInstance_Mutex_{user}";
        }

        /// <summary>
        /// Attempts to acquire the single-instance mutex.
        /// Returns true if this is the first running instance.
        /// </summary>
        public static bool TryAcquireMutex()
        {
            try
            {
                _mutex = new Mutex(true, GetMutexName(), out bool createdNew);
                return createdNew;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstanceService] Mutex error: {ex.Message}");
                return true; // Fallback to running if mutex fails
            }
        }

        /// <summary>
        /// Sends command-line arguments to the running primary instance via Named Pipe.
        /// Returns true if successfully delivered.
        /// </summary>
        public static bool SendArgsToExistingInstance(string[] args, int timeoutMs = 3500)
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", GetPipeName(), PipeDirection.Out);
                pipeClient.Connect(timeoutMs);

                using var writer = new StreamWriter(pipeClient, Encoding.UTF8) { AutoFlush = true };
                string json = JsonSerializer.Serialize(args ?? Array.Empty<string>());
                writer.WriteLine(json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstanceService] Failed to send args to primary instance: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Starts the Named Pipe server loop in the background to accept files from other instances.
        /// </summary>
        public static void StartServer(Action<IReadOnlyList<string>> onFilesReceived)
        {
            _filesReceivedCallback = onFilesReceived;
            _serverCts = new CancellationTokenSource();

            Task.Run(async () => await RunServerLoopAsync(_serverCts.Token));
        }

        private static async Task RunServerLoopAsync(CancellationToken token)
        {
            string pipeName = GetPipeName();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    // Process client connection asynchronously so next instance can connect immediately
                    _ = Task.Run(async () =>
                    {
                        using (server)
                        using (var reader = new StreamReader(server, Encoding.UTF8))
                        {
                            try
                            {
                                var line = await reader.ReadLineAsync(token);
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    var files = JsonSerializer.Deserialize<string[]>(line);
                                    if (files != null)
                                    {
                                        EnqueueIncomingFiles(files);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[SingleInstanceService] Client reader error: {ex.Message}");
                            }
                        }
                    }, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SingleInstanceService] Server accept loop error: {ex.Message}");
                    try
                    {
                        await Task.Delay(100, token);
                    }
                    catch { break; }
                }
            }
        }

        /// <summary>
        /// Debounces incoming files so multiple simultaneous processes (e.g. from Windows Explorer context menu)
        /// are batched into a single unified queue update.
        /// </summary>
        public static void EnqueueIncomingFiles(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                if (!string.IsNullOrWhiteSpace(file))
                {
                    _incomingQueue.Enqueue(file);
                }
            }

            lock (_timerLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(_ =>
                {
                    var batch = new List<string>();
                    while (_incomingQueue.TryDequeue(out var f))
                    {
                        batch.Add(f);
                    }

                    if (batch.Count > 0)
                    {
                        _filesReceivedCallback?.Invoke(batch);
                    }
                }, null, 150, Timeout.Infinite);
            }
        }

        public static void Release()
        {
            try
            {
                _serverCts?.Cancel();
                _serverCts?.Dispose();
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch { }
        }
    }
}
