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
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;

namespace FujifyNET.Services
{
    public class ThumbnailService : IThumbnailService
    {
        private readonly IProcessRunner _processRunner;
        private static BitmapImage? _cachedPlaceholder;
        private static readonly object _placeholderLock = new();

        public ThumbnailService(IProcessRunner? processRunner = null)
        {
            _processRunner = processRunner ?? new ProcessRunner();
        }

        public BitmapImage GenerateThumbnail(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return GetPlaceholderImage();
            }

            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            BitmapImage? bitmapImage = null;

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

                            bitmapImage = CreateBitmapImageFromPath(thumbnailPath);
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
                    bitmapImage = CreateBitmapImageFromPath(filePath);
                }
            }
            catch (NotSupportedException notSupEx)
            {
                Debug.WriteLine($"Error generating thumbnail for {filePath}: {notSupEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error generating thumbnail for {filePath}: {ex.Message}");
            }

            return bitmapImage ?? GetPlaceholderImage();
        }

        private string ExtractThumbnailWithDcraw(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;

            string dcrawPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "libraw", "simple_dcraw.exe");
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

        public BitmapImage GetPlaceholderImage()
        {
            if (_cachedPlaceholder != null) return _cachedPlaceholder;

            lock (_placeholderLock)
            {
                if (_cachedPlaceholder != null) return _cachedPlaceholder;

                string placeholderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "images", "placeholder.png");
                var image = CreateBitmapImageFromPath(placeholderPath) ?? CreateFallbackImage();
                _cachedPlaceholder = image;
                return _cachedPlaceholder;
            }
        }

        private static BitmapImage CreateFallbackImage()
        {
            using var bmp = new Bitmap(1, 1);
            bmp.SetPixel(0, 0, System.Drawing.Color.Transparent);

            using var memoryStream = new MemoryStream();
            bmp.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = new MemoryStream(memoryStream.ToArray());
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            return bitmapImage;
        }

        private static BitmapImage? CreateBitmapImageFromPath(string path)
        {
            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.DecodePixelWidth = 70;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(path, UriKind.Absolute);
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating BitmapImage from path {path}: {ex.Message}");
                return null;
            }
        }
    }
}
