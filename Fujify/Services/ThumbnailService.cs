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
using SkiaSharp;

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

        public Bitmap? GenerateThumbnail(string filePath, int? orientation = null)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return GetPlaceholderImage();
            }

            int resolvedOrientation = orientation ?? RawMetadataReader.TryGetOrientation(filePath) ?? 1;

            try
            {
                // 1. Fast direct embedded JPEG extraction (works for Fujifilm RAF, Sony ARW, Adobe DNG, Canon CR2, Nikon NEF)
                var embeddedBytes = TryExtractEmbeddedJpeg(filePath);
                if (embeddedBytes != null && embeddedBytes.Length > 0)
                {
                    var oriented = DecodeAndOrient(embeddedBytes, resolvedOrientation);
                    if (oriented != null)
                    {
                        return oriented;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Fujifier] Direct thumbnail extraction failed: {ex.Message}");
            }

            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            Bitmap? bitmap = null;

            try
            {
                bitmap = CreateBitmapFromPath(filePath, resolvedOrientation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Fujifier] Fallback bitmap load failed for {filePath}: {ex.Message}");
            }

            return bitmap ?? GetPlaceholderImage();
        }

        private static Bitmap? DecodeAndOrient(byte[] imageBytes, int orientation)
        {
            try
            {
                using var ms = new MemoryStream(imageBytes);
                using var codec = SKCodec.Create(ms);
                if (codec == null)
                {
                    using var fallbackMs = new MemoryStream(imageBytes);
                    return new Bitmap(fallbackMs);
                }

                // If orientation was not in the raw header, check if JPEG itself has EncodedOrigin
                if (orientation == 1 && codec.EncodedOrigin != SKEncodedOrigin.TopLeft)
                {
                    orientation = (int)codec.EncodedOrigin;
                }

                using var originalBitmap = SKBitmap.Decode(codec);
                if (originalBitmap == null) return null;

                var orientedBitmap = ApplyOrientation(originalBitmap, orientation);
                bool isOrientedSeparate = !ReferenceEquals(orientedBitmap, originalBitmap);

                try
                {
                    // Scale down to max dimension 320 for optimal memory footprint and crisp preview
                    int maxDim = Math.Max(orientedBitmap.Width, orientedBitmap.Height);
                    SKBitmap displayBitmap;
                    bool shouldDisposeDisplay = false;

                    if (maxDim > 320)
                    {
                        float scale = 320f / maxDim;
                        int targetW = Math.Max(1, (int)(orientedBitmap.Width * scale));
                        int targetH = Math.Max(1, (int)(orientedBitmap.Height * scale));
                        displayBitmap = orientedBitmap.Resize(new SKImageInfo(targetW, targetH), SKFilterQuality.Medium);
                        shouldDisposeDisplay = true;
                    }
                    else
                    {
                        displayBitmap = orientedBitmap;
                    }

                    try
                    {
                        using var image = SKImage.FromBitmap(displayBitmap);
                        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
                        using var outMs = new MemoryStream();
                        data.SaveTo(outMs);
                        outMs.Position = 0;
                        return new Bitmap(outMs);
                    }
                    finally
                    {
                        if (shouldDisposeDisplay)
                        {
                            displayBitmap.Dispose();
                        }
                    }
                }
                finally
                {
                    if (isOrientedSeparate)
                    {
                        orientedBitmap.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Fujifier] DecodeAndOrient failed: {ex.Message}");
                try
                {
                    using var fallbackMs = new MemoryStream(imageBytes);
                    return new Bitmap(fallbackMs);
                }
                catch
                {
                    return null;
                }
            }
        }

        private static SKBitmap ApplyOrientation(SKBitmap bitmap, int orientation)
        {
            switch (orientation)
            {
                case 3: // 180 deg
                {
                    var rotated = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
                    using var canvas = new SKCanvas(rotated);
                    canvas.Translate(bitmap.Width, bitmap.Height);
                    canvas.RotateDegrees(180);
                    canvas.DrawBitmap(bitmap, 0, 0);
                    return rotated;
                }
                case 6: // 90 deg CW (Portrait)
                {
                    var rotated = new SKBitmap(bitmap.Height, bitmap.Width, bitmap.ColorType, bitmap.AlphaType);
                    using var canvas = new SKCanvas(rotated);
                    canvas.Translate(bitmap.Height, 0);
                    canvas.RotateDegrees(90);
                    canvas.DrawBitmap(bitmap, 0, 0);
                    return rotated;
                }
                case 8: // 270 deg CW / 90 deg CCW (Portrait)
                {
                    var rotated = new SKBitmap(bitmap.Height, bitmap.Width, bitmap.ColorType, bitmap.AlphaType);
                    using var canvas = new SKCanvas(rotated);
                    canvas.Translate(0, bitmap.Width);
                    canvas.RotateDegrees(270);
                    canvas.DrawBitmap(bitmap, 0, 0);
                    return rotated;
                }
                default:
                    return bitmap;
            }
        }

        private static byte[]? TryExtractEmbeddedJpeg(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192);
                if (fs.Length < 32) return null;

                byte[] header = new byte[16];
                if (fs.Read(header, 0, 16) < 16) return null;

                // 1. Fujifilm RAF: embedded JPEG preview offset at 84 (0x54) and length at 88 (0x58)
                if (header[0] == 0x46 && header[1] == 0x55 && header[2] == 0x4A && header[3] == 0x49 &&
                    header[4] == 0x46 && header[5] == 0x49 && header[6] == 0x4C && header[7] == 0x4D)
                {
                    if (fs.Length >= 92)
                    {
                        fs.Seek(84, SeekOrigin.Begin);
                        byte[] posLen = new byte[8];
                        if (fs.Read(posLen, 0, 8) == 8)
                        {
                            uint jpos = (uint)((posLen[0] << 24) | (posLen[1] << 16) | (posLen[2] << 8) | posLen[3]);
                            uint jlen = (uint)((posLen[4] << 24) | (posLen[5] << 16) | (posLen[6] << 8) | posLen[7]);
                            if (jpos > 0 && jlen > 0 && jpos + jlen <= fs.Length && jlen < 15 * 1024 * 1024)
                            {
                                fs.Seek(jpos, SeekOrigin.Begin);
                                byte[] jpegBytes = new byte[jlen];
                                int read = fs.Read(jpegBytes, 0, (int)jlen);
                                if (read == jlen && jpegBytes[0] == 0xFF && jpegBytes[1] == 0xD8)
                                {
                                    return jpegBytes;
                                }
                            }
                        }
                    }
                    return null;
                }

                // 2. TIFF-based RAW (DNG, Sony ARW, Canon CR2, Nikon NEF, Olympus ORF, Pentax PEF)
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
                        return jpegBytes;
                    }
                }
            }
            catch { }
            return null;
        }

        public Bitmap? GetPlaceholderImage()
        {
            if (_cachedPlaceholder != null) return _cachedPlaceholder;

            lock (_placeholderLock)
            {
                if (_cachedPlaceholder != null) return _cachedPlaceholder;

                try
                {
                    var uri = new Uri("avares://Fujifier/Assets/images/placeholder.png");
                    if (!AssetLoader.Exists(uri))
                    {
                        uri = new Uri("avares://Fujify/Assets/images/placeholder.png");
                    }
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

        private static Bitmap? CreateBitmapFromPath(string path, int orientation)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                return DecodeAndOrient(bytes, orientation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating Bitmap from path {path}: {ex.Message}");
                return null;
            }
        }
    }
}
