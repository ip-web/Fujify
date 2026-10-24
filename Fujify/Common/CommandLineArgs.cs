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

namespace Fujify.Common
{
    public enum LaunchMode
    {
        Queue,
        Process,
        ProcessTo
    }

    public class CommandLineArgs
    {
        public LaunchMode Mode { get; set; } = LaunchMode.Queue;
        public List<string> Files { get; set; } = new();

        public bool IsQuickProcess => Mode == LaunchMode.Process || Mode == LaunchMode.ProcessTo;

        public static CommandLineArgs Parse(string[]? args)
        {
            var result = new CommandLineArgs();
            if (args == null || args.Length == 0) return result;

            foreach (var raw in args)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string arg = raw.Trim().Trim('"');

                if (arg.Equals("--process", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-p", StringComparison.OrdinalIgnoreCase))
                {
                    result.Mode = LaunchMode.Process;
                }
                else if (arg.Equals("--process-to", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("-pt", StringComparison.OrdinalIgnoreCase))
                {
                    result.Mode = LaunchMode.ProcessTo;
                }
                else if (arg.Equals("--queue", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("-q", StringComparison.OrdinalIgnoreCase))
                {
                    result.Mode = LaunchMode.Queue;
                }
                else
                {
                    if (File.Exists(arg))
                    {
                        result.Files.Add(Path.GetFullPath(arg));
                    }
                    else if (!arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        // Even if file doesn't exist yet (e.g. URI or relative path), keep it
                        result.Files.Add(arg);
                    }
                }
            }

            return result;
        }
    }
}
