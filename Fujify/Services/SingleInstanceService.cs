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
using Fujify.Common;

namespace Fujify.Services
{
    public class IpcPayload
    {
        public string Mode { get; set; } = "queue"; // "queue", "process", "process-to"
        public List<string> Files { get; set; } = new();
    }

    public static class SingleInstanceService
    {
        public const string ChannelMain = "main";
        public const string ChannelQuick = "quick";

        private static readonly Dictionary<string, Mutex> _mutexes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CancellationTokenSource> _serverCtss = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Action<IpcPayload>> _payloadCallbacks = new(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, ConcurrentQueue<IpcPayload>> _incomingQueues = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _syncLock = new();

        public static string GetPipeName(string channel = ChannelMain)
        {
            string user = Environment.UserName.Replace("\\", "_").Replace("/", "_");
            return channel.Equals(ChannelQuick, StringComparison.OrdinalIgnoreCase)
                ? $"Fujifier_QuickProcess_Pipe_{user}"
                : $"Fujifier_IPC_Pipe_{user}";
        }

        public static string GetMutexName(string channel = ChannelMain)
        {
            string user = Environment.UserName.Replace("\\", "_").Replace("/", "_");
            return channel.Equals(ChannelQuick, StringComparison.OrdinalIgnoreCase)
                ? $"Fujifier_QuickProcess_Mutex_{user}"
                : $"Fujifier_SingleInstance_Mutex_{user}";
        }

        /// <summary>
        /// Attempts to acquire the single-instance mutex for the specified channel ("main" or "quick").
        /// Returns true if this is the first running instance for this channel.
        /// </summary>
        public static bool TryAcquireMutex(string channel = ChannelMain)
        {
            try
            {
                lock (_syncLock)
                {
                    if (_mutexes.TryGetValue(channel, out var existing))
                    {
                        return true;
                    }

                    var mutex = new Mutex(true, GetMutexName(channel), out bool createdNew);
                    _mutexes[channel] = mutex;
                    return createdNew;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstanceService] Mutex error on channel '{channel}': {ex.Message}");
                return true; // Fallback to running if mutex fails
            }
        }

        /// <summary>
        /// Sends an IPC payload to the running primary instance of the specified channel via Named Pipe.
        /// </summary>
        public static bool SendPayloadToExistingInstance(IpcPayload payload, string channel = ChannelMain, int timeoutMs = 3500)
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", GetPipeName(channel), PipeDirection.Out);
                pipeClient.Connect(timeoutMs);

                using var writer = new StreamWriter(pipeClient, Encoding.UTF8) { AutoFlush = true };
                string json = JsonSerializer.Serialize(payload ?? new IpcPayload());
                writer.WriteLine(json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstanceService] Failed to send payload to primary instance ({channel}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Backwards-compatible overload: parses command-line args and forwards them to the appropriate channel.
        /// </summary>
        public static bool SendArgsToExistingInstance(string[] args, int timeoutMs = 3500)
        {
            var parsed = CommandLineArgs.Parse(args);
            string channel = parsed.IsQuickProcess ? ChannelQuick : ChannelMain;
            return SendPayloadToExistingInstance(new IpcPayload
            {
                Mode = parsed.Mode.ToString().ToLowerInvariant(),
                Files = parsed.Files
            }, channel, timeoutMs);
        }

        /// <summary>
        /// Starts the Named Pipe server loop in the background for the specified channel.
        /// </summary>
        public static void StartServer(Action<IpcPayload> onPayloadReceived, string channel = ChannelMain)
        {
            lock (_syncLock)
            {
                _payloadCallbacks[channel] = onPayloadReceived;
                if (_serverCtss.TryGetValue(channel, out var existingCts))
                {
                    existingCts.Cancel();
                    existingCts.Dispose();
                }

                var cts = new CancellationTokenSource();
                _serverCtss[channel] = cts;

                Task.Run(async () => await RunServerLoopAsync(channel, cts.Token));
            }
        }

        /// <summary>
        /// Backwards-compatible overload for full UI file callback.
        /// </summary>
        public static void StartServer(Action<IReadOnlyList<string>> onFilesReceived)
        {
            StartServer(payload => onFilesReceived(payload.Files), ChannelMain);
        }

        private static async Task RunServerLoopAsync(string channel, CancellationToken token)
        {
            string pipeName = GetPipeName(channel);

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

                    // Process client connection asynchronously
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
                                    IpcPayload? payload = null;
                                    if (line.TrimStart().StartsWith("{", StringComparison.Ordinal))
                                    {
                                        payload = JsonSerializer.Deserialize<IpcPayload>(line);
                                    }
                                    else if (line.TrimStart().StartsWith("[", StringComparison.Ordinal))
                                    {
                                        var rawArray = JsonSerializer.Deserialize<string[]>(line);
                                        if (rawArray != null)
                                        {
                                            var parsed = CommandLineArgs.Parse(rawArray);
                                            payload = new IpcPayload
                                            {
                                                Mode = parsed.Mode.ToString().ToLowerInvariant(),
                                                Files = parsed.Files
                                            };
                                        }
                                    }

                                    if (payload != null)
                                    {
                                        EnqueueIncomingPayload(channel, payload);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[SingleInstanceService] Client reader error on channel '{channel}': {ex.Message}");
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
                    Debug.WriteLine($"[SingleInstanceService] Server accept loop error on channel '{channel}': {ex.Message}");
                    try
                    {
                        await Task.Delay(100, token);
                    }
                    catch { break; }
                }
            }
        }

        private static void EnqueueIncomingPayload(string channel, IpcPayload payload)
        {
            var queue = _incomingQueues.GetOrAdd(channel, _ => new ConcurrentQueue<IpcPayload>());
            queue.Enqueue(payload);

            lock (_syncLock)
            {
                if (_debounceTimers.TryGetValue(channel, out var timer))
                {
                    timer.Dispose();
                }

                _debounceTimers[channel] = new Timer(_ =>
                {
                    var allFiles = new List<string>();
                    string mode = "queue";

                    while (queue.TryDequeue(out var p))
                    {
                        if (p.Files != null)
                        {
                            allFiles.AddRange(p.Files);
                        }
                        if (!string.IsNullOrWhiteSpace(p.Mode))
                        {
                            mode = p.Mode;
                        }
                    }

                    if (allFiles.Count > 0 && _payloadCallbacks.TryGetValue(channel, out var callback))
                    {
                        callback.Invoke(new IpcPayload
                        {
                            Mode = mode,
                            Files = allFiles
                        });
                    }
                }, null, 150, Timeout.Infinite);
            }
        }

        public static void Release(string? channel = null)
        {
            lock (_syncLock)
            {
                if (channel != null)
                {
                    ReleaseChannel(channel);
                }
                else
                {
                    foreach (var key in new List<string>(_mutexes.Keys))
                    {
                        ReleaseChannel(key);
                    }
                }
            }
        }

        private static void ReleaseChannel(string channel)
        {
            try
            {
                if (_serverCtss.TryGetValue(channel, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                    _serverCtss.Remove(channel);
                }

                if (_debounceTimers.TryGetValue(channel, out var timer))
                {
                    timer.Dispose();
                    _debounceTimers.TryRemove(channel, out _);
                }

                if (_mutexes.TryGetValue(channel, out var mutex))
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                    _mutexes.Remove(channel);
                }
            }
            catch { }
        }
    }
}
