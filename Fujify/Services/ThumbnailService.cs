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

            try
            {
                // 1. Fast direct embedded JPEG extraction (works for Sony ARW, Adobe DNG, Canon CR2, Nikon NEF)
                var embedded = TryExtractEmbeddedJpeg(filePath);
                if (embedded != null)
                {
                    return embedded;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Fujify] Direct thumbnail extraction failed: {ex.Message}");
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

        private static Bitmap? TryExtractEmbeddedJpeg(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192);
                if (fs.Length < 32) return null;

                byte[] header = new byte[16];
                if (fs.Read(header, 0, 16) < 16) return null;

                bool isLittleEndian;
                if (header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
                    isLittleEndian = true;
                else if (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)
                    isLittleEndian = false;
                else
                    return null;

                uint ifdOffset = isLittleEndian
                    ? BitConverter.ToUInt32(header, 4)
                    : (uint)((header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7]);

                if (ifdOffset == 0 || ifdOffset + 2 > fs.Length) return null;

                fs.Seek(ifdOffset, SeekOrigin.Begin);
                byte[] countBuf = new byte[2];
                if (fs.Read(countBuf, 0, 2) < 2) return null;
                ushort count = isLittleEndian ? BitConverter.ToUInt16(countBuf, 0) : (ushort)((countBuf[0] << 8) | countBuf[1]);
                if (count > 500) return null;

                byte[] entries = new byte[count * 12];
                if (fs.Read(entries, 0, entries.Length) < entries.Length) return null;

                uint jpegOffset = 0;
                uint jpegLength = 0;

                for (int i = 0; i < count; i++)
                {
                    int o = i * 12;
                    ushort tag = isLittleEndian ? BitConverter.ToUInt16(entries, o) : (ushort)((entries[o] << 8) | entries[o + 1]);
                    uint val = isLittleEndian ? BitConverter.ToUInt32(entries, o + 8) : (uint)((entries[o + 8] << 24) | (entries[o + 9] << 16) | (entries[o + 10] << 8) | entries[o + 11]);

                    // Tag 0x0201 = JPEGInterchangeFormat, Tag 0x0111 = StripOffsets / PreviewImageStart
                    if (tag == 0x0201 || (tag == 0x0111 && jpegOffset == 0))
                        jpegOffset = val;
                    // Tag 0x0202 = JPEGInterchangeFormatLength, Tag 0x0117 = StripByteCounts / PreviewImageLength
                    else if (tag == 0x0202 || (tag == 0x0117 && jpegLength == 0))
                        jpegLength = val;
                }

                if (jpegOffset > 0 && jpegLength > 0 && jpegOffset + jpegLength <= fs.Length && jpegLength < 10 * 1024 * 1024)
                {
                    fs.Seek(jpegOffset, SeekOrigin.Begin);
                    byte[] jpegBytes = new byte[jpegLength];
                    int read = fs.Read(jpegBytes, 0, (int)jpegLength);
                    if (read == jpegLength && jpegBytes[0] == 0xFF && jpegBytes[1] == 0xD8)
                    {
                        using var ms = new MemoryStream(jpegBytes);
                        return new Bitmap(ms);
                    }
                }
            }
            catch { }
            return null;
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
