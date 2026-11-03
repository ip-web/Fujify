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



        // CLI options

        public bool IsCliMode { get; set; }

        public bool ShowHelp { get; set; }

        public bool ShowVersion { get; set; }

        public bool ListModels { get; set; }

        public bool ListSimulations { get; set; }

        public string? OutputDirectory { get; set; }

        public string CameraModel { get; set; } = "Fujifilm X-T5";

        public string? FilmSimulation { get; set; }

        public bool BackupOriginalFile { get; set; } = true;

        public bool EmbedOriginalRaw { get; set; } = false;

        public bool LosslessCompression { get; set; } = true;

        public bool Quiet { get; set; } = false;



        public static CommandLineArgs Parse(string[]? args)

        {

            var result = new CommandLineArgs();

            if (args == null || args.Length == 0) return result;



            for (int i = 0; i < args.Length; i++)

            {

                string raw = args[i];

                if (string.IsNullOrWhiteSpace(raw)) continue;

                string arg = raw.Trim().Trim('"');



                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||

                    arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||

                    arg.Equals("-?", StringComparison.OrdinalIgnoreCase))

                {

                    result.ShowHelp = true;

                    result.IsCliMode = true;

                }

                else if (arg.Equals("--version", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("-v", StringComparison.OrdinalIgnoreCase))

                {

                    result.ShowVersion = true;

                    result.IsCliMode = true;

                }

                else if (arg.Equals("--cli", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("-c", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("--headless", StringComparison.OrdinalIgnoreCase))

                {

                    result.IsCliMode = true;

                }

                else if (arg.Equals("--list-models", StringComparison.OrdinalIgnoreCase))

                {

                    result.ListModels = true;

                    result.IsCliMode = true;

                }

                else if (arg.Equals("--list-simulations", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("--list-presets", StringComparison.OrdinalIgnoreCase))

                {

                    result.ListSimulations = true;

                    result.IsCliMode = true;

                }

                else if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase))

                {

                    result.Quiet = true;

                }

                else if (arg.Equals("--backup", StringComparison.OrdinalIgnoreCase))

                {

                    result.BackupOriginalFile = true;

                }

                else if (arg.Equals("--no-backup", StringComparison.OrdinalIgnoreCase))

                {

                    result.BackupOriginalFile = false;

                }

                else if (arg.Equals("--embed-raw", StringComparison.OrdinalIgnoreCase))

                {

                    result.EmbedOriginalRaw = true;

                }

                else if (arg.Equals("--no-embed-raw", StringComparison.OrdinalIgnoreCase))

                {

                    result.EmbedOriginalRaw = false;

                }

                else if (arg.Equals("--lossless", StringComparison.OrdinalIgnoreCase))

                {

                    result.LosslessCompression = true;

                }

                else if (arg.Equals("--uncompressed", StringComparison.OrdinalIgnoreCase))

                {

                    result.LosslessCompression = false;

                }

                else if (arg.Equals("--process", StringComparison.OrdinalIgnoreCase) ||

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

                else if (arg.Equals("--out", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("-o", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("--output", StringComparison.OrdinalIgnoreCase))

                {

                    if (i + 1 < args.Length)

                    {

                        result.OutputDirectory = args[++i].Trim().Trim('"');

                    }

                }

                else if (arg.StartsWith("--out=", StringComparison.OrdinalIgnoreCase) ||

                         arg.StartsWith("-o=", StringComparison.OrdinalIgnoreCase) ||

                         arg.StartsWith("--output=", StringComparison.OrdinalIgnoreCase))

                {

                    result.OutputDirectory = arg.Substring(arg.IndexOf('=') + 1).Trim().Trim('"');

                }

                else if (arg.Equals("--model", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("-m", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("--camera", StringComparison.OrdinalIgnoreCase))

                {

                    if (i + 1 < args.Length)

                    {

                        result.CameraModel = args[++i].Trim().Trim('"');

                    }

                }

                else if (arg.StartsWith("--model=", StringComparison.OrdinalIgnoreCase) ||

                         arg.StartsWith("-m=", StringComparison.OrdinalIgnoreCase) ||

                         arg.StartsWith("--camera=", StringComparison.OrdinalIgnoreCase))

                {

                    result.CameraModel = arg.Substring(arg.IndexOf('=') + 1).Trim().Trim('"');

                }

                else if (arg.Equals("--simulation", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("-s", StringComparison.OrdinalIgnoreCase) ||

                         arg.Equals("--preset", StringComparison.OrdinalIgnoreCase))

                {

                    if (i + 1 < args.Length)

                    {

                        result.FilmSimulation = args[++i].Trim().Trim('"');

                    }

                }

                else if (arg.StartsWith("--simulation=", StringComparison.OrdinalIgnoreCase) ||

                         arg.StartsWith("-s=", StringComparison.OrdinalIgnoreCase) ||

                         arg.StartsWith("--preset=", StringComparison.OrdinalIgnoreCase))

                {

                    result.FilmSimulation = arg.Substring(arg.IndexOf('=') + 1).Trim().Trim('"');

                }

                else

                {

                    if (File.Exists(arg) || Directory.Exists(arg))

                    {

                        result.Files.Add(Path.GetFullPath(arg));

                    }

                    else if (!arg.StartsWith("-", StringComparison.Ordinal))

                    {

                        result.Files.Add(arg);

                    }

                }

            }



            return result;

        }

    }

}

