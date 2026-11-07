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
using System.Text;

namespace Fujify.Services
{
    public record FailedConversionEntry(
        string FilePath,
        string CameraModel,
        string Lens,
        string TargetMode,
        string TargetCamera,
        string ErrorMessage,
        string? StackTrace,
        DateTime Timestamp
    );

    public static class ConversionLogService
    {
        public static string GetLatestFailureLogPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Fujifier", "logs", "latest_failures.log");
        }

        public static bool HasLatestFailureLog()
        {
            string path = GetLatestFailureLogPath();
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }

        public static void ClearLatestFailureLog()
        {
            try
            {
                string path = GetLatestFailureLogPath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }

        public static string GenerateLogContent(
            IReadOnlyList<FailedConversionEntry> failures,
            int totalCount,
            int successCount,
            TimeSpan duration)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("FUJIFY - FAILED CONVERSIONS REPORT");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Timestamp          : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"OS Version         : {Environment.OSVersion} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");
            sb.AppendLine($"Total Processed    : {totalCount}");
            sb.AppendLine($"Succeeded          : {successCount}");
            sb.AppendLine($"Failed             : {failures.Count}");
            sb.AppendLine($"Duration           : {duration.TotalSeconds:F1}s");
            sb.AppendLine("================================================================================");
            sb.AppendLine();
            sb.AppendLine("FAILED ITEMS DETAILS:");
            sb.AppendLine("--------------------------------------------------------------------------------");

            for (int i = 0; i < failures.Count; i++)
            {
                var entry = failures[i];
                sb.AppendLine($"[{i + 1}] File           : {entry.FilePath}");
                sb.AppendLine($"    Camera Model   : {entry.CameraModel}");
                if (!string.IsNullOrWhiteSpace(entry.Lens))
                {
                    sb.AppendLine($"    Lens           : {entry.Lens}");
                }
                sb.AppendLine($"    Target Mode    : {entry.TargetMode}");
                sb.AppendLine($"    Target Profile : {entry.TargetCamera}");
                sb.AppendLine($"    Failure Time   : {entry.Timestamp:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"    Error Message  : {entry.ErrorMessage}");
                if (!string.IsNullOrWhiteSpace(entry.StackTrace))
                {
                    sb.AppendLine("    Stack Trace / Details:");
                    foreach (var line in entry.StackTrace.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
                    {
                        sb.AppendLine($"      {line}");
                    }
                }
                sb.AppendLine("--------------------------------------------------------------------------------");
            }
            sb.AppendLine("================================================================================");
            sb.AppendLine("END OF LOG");
            sb.AppendLine("================================================================================");
            return sb.ToString();
        }

        public static string? WriteFailureLog(
            IReadOnlyList<FailedConversionEntry> failures,
            int totalCount,
            int successCount,
            TimeSpan duration,
            string? exportDirectory,
            bool exportToDirectory)
        {
            if (failures == null || failures.Count == 0) return null;

            string content = GenerateLogContent(failures, totalCount, successCount, duration);
            string latestLogPath = GetLatestFailureLogPath();

            try
            {
                string? dir = Path.GetDirectoryName(latestLogPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(latestLogPath, content, Encoding.UTF8);
            }
            catch { }

            string? exportedFilePath = null;
            if (exportToDirectory && !string.IsNullOrWhiteSpace(exportDirectory) && Directory.Exists(exportDirectory))
            {
                try
                {
                    string fileName = $"fujify_failures_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                    exportedFilePath = Path.Combine(exportDirectory, fileName);
                    File.WriteAllText(exportedFilePath, content, Encoding.UTF8);
                }
                catch
                {
                    exportedFilePath = null;
                }
            }

            return exportedFilePath ?? latestLogPath;
        }
    }
}

