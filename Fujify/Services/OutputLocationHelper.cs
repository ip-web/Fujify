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

namespace Fujify.Services
{
    public static class OutputLocationHelper
    {
        public static string ComputeTargetDirectory(
            string sourceFilePath,
            string? customOutputFolder,
            bool createSubfolder,
            bool subfolderUseOriginalName,
            string? subfolderSuffix,
            string? subfolderCustomName)
        {
            string sourceDir = Path.GetDirectoryName(sourceFilePath) ?? string.Empty;
            string baseDir = !string.IsNullOrEmpty(customOutputFolder) ? customOutputFolder : sourceDir;

            if (!createSubfolder)
            {
                return baseDir;
            }

            string subfolderName;
            if (subfolderUseOriginalName)
            {
                string originalDirName = Path.GetFileName(sourceDir);
                if (string.IsNullOrWhiteSpace(originalDirName))
                {
                    originalDirName = "Photos";
                }

                string suffix = subfolderSuffix?.Trim() ?? string.Empty;
                subfolderName = originalDirName + suffix;
            }
            else
            {
                subfolderName = !string.IsNullOrWhiteSpace(subfolderCustomName)
                    ? subfolderCustomName.Trim()
                    : "Fujified";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                subfolderName = subfolderName.Replace(c, '_');
            }

            if (string.IsNullOrWhiteSpace(subfolderName))
            {
                subfolderName = "Fujified";
            }

            return Path.Combine(baseDir, subfolderName);
        }

        public static string ComputeInitialDestinationFilePath(
            string sourceFilePath,
            string? customOutputFolder,
            AppSettings settings)
        {
            string baseFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            if (settings.AppendSuffix && !string.IsNullOrWhiteSpace(settings.Suffix))
            {
                baseFileName += settings.Suffix.Trim();
            }

            string targetDir = ComputeTargetDirectory(
                sourceFilePath,
                customOutputFolder,
                settings.CreateSubfolder,
                settings.SubfolderUseOriginalName,
                settings.SubfolderSuffix,
                settings.SubfolderCustomName);

            return Path.Combine(targetDir, baseFileName + ".dng");
        }
    }
}

