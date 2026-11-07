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
using System.Reflection;
using System.Runtime.InteropServices;

namespace Fujify.Services
{
    /// <summary>
    /// Centralized native library resolver for Fujify (dnglab_ffi and raw2raf_ffi).
    /// .NET allows only ONE SetDllImportResolver per assembly; this resolver coordinates
    /// loading native libraries across Windows, macOS, Linux, Android, and iOS.
    /// </summary>
    public static class NativeLibraryResolver
    {
        private static bool _resolverRegistered;
        private static readonly object _initLock = new();

        public static void EnsureRegistered()
        {
            lock (_initLock)
            {
                if (_resolverRegistered) return;
                try
                {
                    NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, DllImportResolver);
                    _resolverRegistered = true;
                }
                catch
                {
                    // Resolver might already be set
                }
            }
        }

        public static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != "dnglab_ffi" && libraryName != "raw2raf_ffi")
            {
                return IntPtr.Zero;
            }

            // 1. Direct try via standard dynamic linker
            if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out IntPtr directHandle))
            {
                return directHandle;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string osExt;
            string prefix = "";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                osExt = ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
            {
                osExt = ".dylib";
                prefix = "lib";
            }
            else
            {
                osExt = ".so";
                prefix = "lib";
            }

            string fileName = $"{prefix}{libraryName}{osExt}";

            if (NativeLibrary.TryLoad(fileName, assembly, searchPath, out directHandle))
            {
                return directHandle;
            }

            // 2. Android native library directory (when running on Android)
            try
            {
                var appType = Type.GetType("Android.App.Application, Mono.Android");
                var contextProp = appType?.GetProperty("Context", BindingFlags.Public | BindingFlags.Static);
                var context = contextProp?.GetValue(null);
                if (context != null)
                {
                    var appInfoProp = context.GetType().GetProperty("ApplicationInfo");
                    var appInfo = appInfoProp?.GetValue(context);
                    if (appInfo != null)
                    {
                        var nativeLibDirProp = appInfo.GetType().GetProperty("NativeLibraryDir");
                        var nativeLibDir = nativeLibDirProp?.GetValue(appInfo) as string;
                        if (!string.IsNullOrEmpty(nativeLibDir))
                        {
                            string androidLibPath = Path.Combine(nativeLibDir, fileName);
                            if (File.Exists(androidLibPath) && NativeLibrary.TryLoad(androidLibPath, out IntPtr androidHandle))
                            {
                                return androidHandle;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall through to candidate path search
            }

            // 3. Candidate search paths across platforms and architectures
            var searchPaths = new List<string>
            {
                Path.Combine(baseDir, fileName),
                Path.Combine(baseDir, "Frameworks", fileName),
                Path.Combine(baseDir, "Frameworks", $"{libraryName}.framework", libraryName),
                Path.Combine(baseDir, "libs", fileName),
                Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName)
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => "win-arm64",
                    Architecture.X64 => "win-x64",
                    Architecture.X86 => "win-x86",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(arch))
                {
                    searchPaths.Add(Path.Combine(baseDir, "libs", arch, fileName));
                    searchPaths.Add(Path.Combine(baseDir, "runtimes", arch, "native", fileName));
                    searchPaths.Add(Path.Combine(baseDir, "..", "..", "..", "Fujify", "libs", arch, fileName));
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
            {
                string arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => "osx-arm64",
                    Architecture.X64 => "osx-x64",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(arch))
                {
                    searchPaths.Add(Path.Combine(baseDir, "libs", arch, fileName));
                    searchPaths.Add(Path.Combine(baseDir, "runtimes", arch, "native", fileName));
                    searchPaths.Add(Path.Combine(baseDir, "..", "..", "..", "Fujify", "libs", arch, fileName));
                }

                searchPaths.Add(Path.Combine(baseDir, "..", "Frameworks", fileName));
                searchPaths.Add(Path.Combine(baseDir, "..", "Frameworks", $"{libraryName}.framework", libraryName));
                searchPaths.Add(Path.Combine(baseDir, "..", "Resources", fileName));
                searchPaths.Add(Path.Combine(baseDir, "..", "Resources", "libs", fileName));
                if (!string.IsNullOrEmpty(arch))
                {
                    searchPaths.Add(Path.Combine(baseDir, "..", "Resources", "libs", arch, fileName));
                }
                searchPaths.Add(Path.Combine("/opt/homebrew/lib", fileName));
                searchPaths.Add(Path.Combine("/usr/local/lib", fileName));
                searchPaths.Add(Path.Combine("/usr/lib", fileName));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => "linux-arm64",
                    Architecture.X64 => "linux-x64",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(arch))
                {
                    searchPaths.Add(Path.Combine(baseDir, "libs", arch, fileName));
                    searchPaths.Add(Path.Combine(baseDir, "runtimes", arch, "native", fileName));
                    searchPaths.Add(Path.Combine(baseDir, "..", "..", "..", "Fujify", "libs", arch, fileName));
                }

                searchPaths.Add(Path.Combine("/usr/lib", fileName));
                searchPaths.Add(Path.Combine("/usr/local/lib", fileName));
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userHome))
                {
                    searchPaths.Add(Path.Combine(userHome, ".local", "lib", fileName));
                    searchPaths.Add(Path.Combine(userHome, "lib", fileName));
                }
            }

            // Developer / source tree fallbacks
            searchPaths.Add(Path.Combine(baseDir, "..", "..", "..", "Fujify", "libs", fileName));
            searchPaths.Add(Path.Combine(baseDir, "..", "..", "..", "native", "dnglab_ffi", "target", "release", fileName));
            searchPaths.Add(Path.Combine(baseDir, "..", "..", "..", "..", "native", "dnglab_ffi", "target", "release", fileName));
            searchPaths.Add(Path.Combine(baseDir, "..", "..", "..", "tools", "Raw2Raf", "target", "release", fileName));
            searchPaths.Add(Path.Combine(baseDir, "..", "..", "..", "..", "tools", "Raw2Raf", "target", "release", fileName));

            foreach (var path in searchPaths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        string companionDir = Path.GetDirectoryName(path) ?? "";
                        string unwindPath = Path.Combine(companionDir, "libunwind.dll");
                        if (File.Exists(unwindPath))
                        {
                            NativeLibrary.TryLoad(unwindPath, assembly, searchPath, out _);
                        }

                        if (NativeLibrary.TryLoad(path, out IntPtr handle))
                        {
                            return handle;
                        }
                    }
                }
                catch
                {
                    // Continue trying next candidate
                }
            }

            return IntPtr.Zero;
        }
    }
}

