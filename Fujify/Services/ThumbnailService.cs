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
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Fujify.Services
{
    public class ThumbnailService : IThumbnailService
    {
        private readonly IProcessRunner _processRunner;
        private static Bitmap? _cachedPlaceholder;
        private static readonly object _placeholderLock = new();

        public ThumbnailService(IProcessRunner? processRunner = null)
        {
            _processRunner = processRunner ?? new ProcessRunner();
        }

        public Bitmap? GenerateThumbnail(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return GetPlaceholderImage();
            }

            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            Bitmap? bitmap = null;

            try
            {
                if (extension != ".dng")
                {
                    string thumbnailPath = ExtractThumbnailWithDcraw(filePath);
                    if (!string.IsNullOrEmpty(thumbnailPath))
                    {
                        try
                        {
                            string thumbnailExtension = Path.GetExtension(thumbnailPath).ToLowerInvariant();
                            if (thumbnailExtension == ".ppm")
                            {
                                return GetPlaceholderImage();
                            }

                            bitmap = CreateBitmapFromPath(thumbnailPath);
                        }
                        finally
                        {
                            if (File.Exists(thumbnailPath) && !thumbnailPath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    File.Delete(thumbnailPath);
                                }
                                catch (IOException ioEx)
                                {
                                    Debug.WriteLine($"Error deleting thumbnail file {thumbnailPath}: {ioEx.Message}");
                                }
                            }
                        }
                    }
                }
                else
                {
                    bitmap = CreateBitmapFromPath(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating thumbnail for {filePath}: {ex.Message}");
            }

            return bitmap ?? GetPlaceholderImage();
        }

        private string ExtractThumbnailWithDcraw(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;

            string dcrawName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "simple_dcraw.exe" : "simple_dcraw";
            string dcrawPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "libraw", dcrawName);
            if (!File.Exists(dcrawPath)) return string.Empty;

            try
            {
                _processRunner.Run(dcrawPath, $"-e \"{filePath}\"");

                string? directory = Path.GetDirectoryName(filePath);
                if (directory == null) return string.Empty;

                string thumbnailPathJpg = Path.Combine(directory, Path.GetFileName(filePath) + ".thumb.jpg");
                if (File.Exists(thumbnailPathJpg)) return thumbnailPathJpg;

                string thumbnailPathPpm = Path.Combine(directory, Path.GetFileName(filePath) + ".thumb.ppm");
                if (File.Exists(thumbnailPathPpm)) return thumbnailPathPpm;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error running dcraw: {ex.Message}");
            }

            return string.Empty;
        }

        public Bitmap? GetPlaceholderImage()
        {
            if (_cachedPlaceholder != null) return _cachedPlaceholder;

            lock (_placeholderLock)
            {
                if (_cachedPlaceholder != null) return _cachedPlaceholder;

                try
                {
                    var uri = new Uri("avares://Fujify/Assets/images/placeholder.png");
                    if (AssetLoader.Exists(uri))
                    {
                        using var stream = AssetLoader.Open(uri);
                        _cachedPlaceholder = new Bitmap(stream);
                        return _cachedPlaceholder;
                    }
                }
                catch
                {
                    // Fallback to disk if asset loader fails
                    string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "images", "placeholder.png");
                    if (File.Exists(diskPath))
                    {
                        _cachedPlaceholder = new Bitmap(diskPath);
                        return _cachedPlaceholder;
                    }
                }

                return null;
            }
        }

        private static Bitmap? CreateBitmapFromPath(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, 70);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating Bitmap from path {path}: {ex.Message}");
                return null;
            }
        }
    }
}
