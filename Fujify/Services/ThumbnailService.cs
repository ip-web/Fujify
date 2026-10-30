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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Fujify.Services
{
    public class ThumbnailService : IThumbnailService
    {
        private readonly IProcessRunner _processRunner;
        private static Bitmap? _cachedPlaceholder;
        public static Bitmap? CachedPlaceholder => _cachedPlaceholder;
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

                int origW = codec.Info.Width;
                int origH = codec.Info.Height;
                int maxDim = Math.Max(origW, origH);

                SKBitmap? originalBitmap = null;
                // High-performance hardware DCT sub-sampling: decodes directly at thumbnail scale
                if (maxDim > 320)
                {
                    float targetScale = 320f / maxDim;
                    var scaledDim = codec.GetScaledDimensions(targetScale);
                    var scaledInfo = new SKImageInfo(scaledDim.Width, scaledDim.Height, codec.Info.ColorType, codec.Info.AlphaType);
                    originalBitmap = SKBitmap.Decode(codec, scaledInfo);
                }

                if (originalBitmap == null)
                {
                    originalBitmap = SKBitmap.Decode(codec);
                }

                if (originalBitmap == null) return null;

                using (originalBitmap)
                {
                    var orientedBitmap = ApplyOrientation(originalBitmap, orientation);
                    bool isOrientedSeparate = !ReferenceEquals(orientedBitmap, originalBitmap);

                    try
                    {
                        // Scale down to max dimension 320 for optimal memory footprint and crisp preview
                        int currentMax = Math.Max(orientedBitmap.Width, orientedBitmap.Height);
                        SKBitmap displayBitmap;
                        bool shouldDisposeDisplay = false;

                        if (currentMax > 320)
                        {
                            float scale = 320f / currentMax;
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
                            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
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

        internal static byte[]? TryExtractEmbeddedJpeg(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 16384);
                if (fs.Length < 32) return null;

                byte[] header = new byte[32];
                int read = fs.Read(header, 0, 32);
                if (read < 16) return null;

                // 1. Fujifilm RAF: embedded JPEG preview offset at 84 (0x54) and length at 88 (0x58)
                if (header[0] == 0x46 && header[1] == 0x55 && header[2] == 0x4A && header[3] == 0x49 &&
                    header[4] == 0x46 && header[5] == 0x49 && header[6] == 0x4C && header[7] == 0x4D)
                {
                    var rafBytes = TryExtractRafJpeg(fs);
                    if (rafBytes != null) return rafBytes;
                }

                // 2. Canon CR3: ISOBMFF container starting with 'ftyp' at offset 4
                if (header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
                {
                    var cr3Bytes = TryExtractCr3Jpeg(fs);
                    if (cr3Bytes != null) return cr3Bytes;
                }

                // 2.5 Canon CIFF (.CRW): Camera Image File Format used by early Canon DSLRs and PowerShots (D30, D60, 10D, 300D, G1-G6, etc.)
                bool isLittleEndian = header[0] == 0x49 && header[1] == 0x49;
                bool isBigEndian = header[0] == 0x4D && header[1] == 0x4D;
                if ((isLittleEndian || isBigEndian) && read >= 14 &&
                    header[6] == 0x48 && header[7] == 0x45 && header[8] == 0x41 && header[9] == 0x50 &&
                    header[10] == 0x43 && header[11] == 0x43 && header[12] == 0x44 &&
                    (header[13] == 0x52 || header[13] == 0x42))
                {
                    var ciffBytes = TryExtractCiffJpeg(fs, header, isLittleEndian);
                    if (ciffBytes != null) return ciffBytes;
                }

                // 3. TIFF-based RAW (DNG, Sony ARW, Canon CR2, Nikon NEF, Olympus/OM System ORF, Pentax PEF, Panasonic RW2)
                var tiffBytes = TryExtractTiffJpeg(fs, header);
                if (tiffBytes != null) return tiffBytes;

                // 4. Universal bounded JPEG stream scanner fallback (scans up to first 8MB for valid JPEG stream)
                var scannedBytes = TryScanForEmbeddedJpeg(fs);
                if (scannedBytes != null) return scannedBytes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Fujifier] Embedded JPEG extraction failed for {filePath}: {ex.Message}");
            }
            return null;
        }

        private static bool IsValidJpegHeader(byte[] data)
        {
            return data != null && data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;
        }

        private static ushort ReadUInt16(byte[] data, int offset, bool isLittleEndian)
        {
            return isLittleEndian
                ? (ushort)(data[offset] | (data[offset + 1] << 8))
                : (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static uint ReadUInt32(byte[] data, int offset, bool isLittleEndian)
        {
            return isLittleEndian
                ? (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24))
                : (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static byte[]? TryExtractRafJpeg(FileStream fs)
        {
            try
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
                            if (fs.Read(jpegBytes, 0, (int)jlen) == jlen && IsValidJpegHeader(jpegBytes))
                            {
                                return jpegBytes;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static byte[]? TryExtractCr3Jpeg(FileStream fs)
        {
            try
            {
                long maxLen = Math.Min(fs.Length, 25 * 1024 * 1024);
                fs.Seek(0, SeekOrigin.Begin);
                byte[] hdr = new byte[8];

                while (fs.Position < maxLen - 8)
                {
                    long boxStart = fs.Position;
                    if (fs.Read(hdr, 0, 8) < 8) break;

                    uint boxSize = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
                    string boxType = Encoding.ASCII.GetString(hdr, 4, 4);

                    long boxDataSize = boxSize;
                    long headerLen = 8;
                    if (boxSize == 1)
                    {
                        byte[] largeSizeBuf = new byte[8];
                        if (fs.Read(largeSizeBuf, 0, 8) < 8) break;
                        boxDataSize = (long)(((ulong)largeSizeBuf[0] << 56) | ((ulong)largeSizeBuf[1] << 48) |
                                             ((ulong)largeSizeBuf[2] << 40) | ((ulong)largeSizeBuf[3] << 32) |
                                             ((ulong)largeSizeBuf[4] << 24) | ((ulong)largeSizeBuf[5] << 16) |
                                             ((ulong)largeSizeBuf[6] << 8) | (ulong)largeSizeBuf[7]);
                        headerLen = 16;
                    }
                    else if (boxSize == 0)
                    {
                        boxDataSize = fs.Length - boxStart;
                    }

                    if (boxDataSize < headerLen || boxStart + boxDataSize > fs.Length) break;

                    if (boxType == "PRVW" || boxType == "THMB")
                    {
                        var jpeg = ReadJpegFromPrvwBox(fs, boxStart, boxDataSize, headerLen);
                        if (jpeg != null) return jpeg;
                    }
                    else if (boxType == "moov" || boxType == "uuid")
                    {
                        long containerEnd = boxStart + boxDataSize;
                        if (boxType == "uuid")
                        {
                            fs.Seek(boxStart + headerLen + 16, SeekOrigin.Begin);
                        }

                        while (fs.Position < containerEnd - 8)
                        {
                            long subStart = fs.Position;
                            if (fs.Read(hdr, 0, 8) < 8) break;
                            uint subSize = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
                            string subType = Encoding.ASCII.GetString(hdr, 4, 4);

                            if (subSize < 8 || subStart + subSize > containerEnd) break;

                            if (subType == "PRVW" || subType == "THMB")
                            {
                                var jpeg = ReadJpegFromPrvwBox(fs, subStart, subSize, 8);
                                if (jpeg != null) return jpeg;
                            }
                            else if (subType == "uuid")
                            {
                                long uuidEnd = subStart + subSize;
                                fs.Seek(subStart + 8 + 16, SeekOrigin.Begin);
                                while (fs.Position < uuidEnd - 8)
                                {
                                    long uSubStart = fs.Position;
                                    if (fs.Read(hdr, 0, 8) < 8) break;
                                    uint uSubSize = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
                                    string uSubType = Encoding.ASCII.GetString(hdr, 4, 4);
                                    if (uSubSize < 8 || uSubStart + uSubSize > uuidEnd) break;
                                    if (uSubType == "PRVW" || uSubType == "THMB")
                                    {
                                        var jpeg = ReadJpegFromPrvwBox(fs, uSubStart, uSubSize, 8);
                                        if (jpeg != null) return jpeg;
                                    }
                                    fs.Seek(uSubStart + uSubSize, SeekOrigin.Begin);
                                }
                            }

                            fs.Seek(subStart + subSize, SeekOrigin.Begin);
                        }
                    }

                    fs.Seek(boxStart + boxDataSize, SeekOrigin.Begin);
                }
            }
            catch { }

            return null;
        }

        private static byte[]? ReadJpegFromPrvwBox(FileStream fs, long boxStart, long boxSize, long headerLen)
        {
            try
            {
                long payloadStart = boxStart + headerLen;
                long payloadSize = boxSize - headerLen;
                if (payloadSize <= 16 || payloadSize > 20 * 1024 * 1024) return null;

                fs.Seek(payloadStart, SeekOrigin.Begin);
                byte[] preHeader = new byte[Math.Min(64, (int)payloadSize)];
                if (fs.Read(preHeader, 0, preHeader.Length) < preHeader.Length) return null;

                int soiIndex = -1;
                for (int i = 0; i < preHeader.Length - 1; i++)
                {
                    if (preHeader[i] == 0xFF && preHeader[i + 1] == 0xD8)
                    {
                        soiIndex = i;
                        break;
                    }
                }

                if (soiIndex >= 0)
                {
                    long jpegStart = payloadStart + soiIndex;
                    long jpegLen = payloadSize - soiIndex;

                    if (soiIndex >= 4)
                    {
                        uint specLen = (uint)((preHeader[soiIndex - 4] << 24) | (preHeader[soiIndex - 3] << 16) |
                                              (preHeader[soiIndex - 2] << 8) | preHeader[soiIndex - 1]);
                        if (specLen > 1000 && specLen <= jpegLen)
                        {
                            jpegLen = specLen;
                        }
                    }

                    fs.Seek(jpegStart, SeekOrigin.Begin);
                    byte[] jpegBytes = new byte[jpegLen];
                    int readBytes = fs.Read(jpegBytes, 0, (int)jpegLen);
                    if (readBytes == jpegLen && IsValidJpegHeader(jpegBytes))
                    {
                        return jpegBytes;
                    }
                }
            }
            catch { }
            return null;
        }

        private static byte[]? TryExtractCiffJpeg(FileStream fs, byte[] header, bool isLittleEndian)
        {
            try
            {
                uint headerLength = ReadUInt32(header, 2, isLittleEndian);
                if (headerLength < 26 || headerLength > fs.Length - 4)
                {
                    headerLength = 26;
                }

                var queue = new Queue<(long Base, long Length, int Depth)>();
                queue.Enqueue((headerLength, fs.Length - headerLength, 0));

                long bestOffset = -1;
                long bestLength = 0;

                byte[] buf4 = new byte[4];
                byte[] buf2 = new byte[2];

                while (queue.Count > 0)
                {
                    var (baseOff, blockLen, depth) = queue.Dequeue();
                    if (depth > 8 || blockLen < 8) continue;

                    long dirOffsetPtr = baseOff + blockLen - 4;
                    if (dirOffsetPtr + 4 > fs.Length || dirOffsetPtr < baseOff) continue;

                    fs.Seek(dirOffsetPtr, SeekOrigin.Begin);
                    if (fs.Read(buf4, 0, 4) != 4) continue;
                    uint dirOffset = ReadUInt32(buf4, 0, isLittleEndian);

                    long dirPos = baseOff + dirOffset;
                    if (dirPos < baseOff || dirPos + 2 > baseOff + blockLen || dirPos + 2 > fs.Length) continue;

                    fs.Seek(dirPos, SeekOrigin.Begin);
                    if (fs.Read(buf2, 0, 2) != 2) continue;
                    ushort numRecords = ReadUInt16(buf2, 0, isLittleEndian);
                    if (numRecords == 0 || numRecords > 512) continue;

                    int recBytesToRead = numRecords * 10;
                    if (dirPos + 2 + recBytesToRead > fs.Length) continue;

                    byte[] recBuf = new byte[recBytesToRead];
                    if (fs.Read(recBuf, 0, recBytesToRead) != recBytesToRead) continue;

                    for (int i = 0; i < numRecords; i++)
                    {
                        int idx = i * 10;
                        ushort tag = ReadUInt16(recBuf, idx, isLittleEndian);
                        uint len = ReadUInt32(recBuf, idx + 2, isLittleEndian);
                        uint off = ReadUInt32(recBuf, idx + 6, isLittleEndian);

                        // Bit 14 indicates in-record immediate data (not a heap offset)
                        if ((tag & 0x4000) != 0) continue;

                        ushort tagType = (ushort)(tag & 0x3800);
                        if (tagType == 0x2800 || tagType == 0x3000)
                        {
                            // Subdirectory
                            long subBase = baseOff + off;
                            long subLen = len;
                            if (subBase + subLen <= fs.Length && subLen >= 8 && depth < 8)
                            {
                                queue.Enqueue((subBase, subLen, depth + 1));
                            }
                        }
                        else
                        {
                            // Data record
                            long dataOff = baseOff + off;
                            if (dataOff >= 0 && dataOff + len <= fs.Length && len >= 256)
                            {
                                fs.Seek(dataOff, SeekOrigin.Begin);
                                if (fs.Read(buf4, 0, 4) == 4 && buf4[0] == 0xFF && buf4[1] == 0xD8)
                                {
                                    if (len > bestLength)
                                    {
                                        bestLength = len;
                                        bestOffset = dataOff;
                                    }
                                }
                            }
                        }
                    }
                }

                if (bestOffset >= 0 && bestLength >= 256 && bestOffset + bestLength <= fs.Length)
                {
                    byte[] jpegBytes = new byte[bestLength];
                    fs.Seek(bestOffset, SeekOrigin.Begin);
                    int totalRead = 0;
                    while (totalRead < bestLength)
                    {
                        int r = fs.Read(jpegBytes, totalRead, (int)bestLength - totalRead);
                        if (r <= 0) break;
                        totalRead += r;
                    }

                    if (totalRead == bestLength && IsValidJpegHeader(jpegBytes))
                    {
                        return jpegBytes;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Fujifier] CIFF JPEG extraction failed: {ex.Message}");
            }

            return null;
        }

        private static byte[]? TryExtractTiffJpeg(FileStream fs, byte[] header)
        {
            try
            {
                bool isLittleEndian;
                if (header[0] == 0x49 && header[1] == 0x49)
                {
                    isLittleEndian = true;
                    ushort magic = (ushort)(header[2] | (header[3] << 8));
                    if (magic != 42 && magic != 85 && magic != 0x4F52 && magic != 0x5352 && magic != 0x524F)
                        return null;
                }
                else if (header[0] == 0x4D && header[1] == 0x4D)
                {
                    isLittleEndian = false;
                    ushort magic = (ushort)((header[2] << 8) | header[3]);
                    if (magic != 42 && magic != 0x4F52 && magic != 0x524F)
                        return null;
                }
                else
                {
                    return null;
                }

                uint ifd0Offset = ReadUInt32(header, 4, isLittleEndian);
                if (ifd0Offset == 0 || ifd0Offset + 2 > fs.Length) return null;

                uint bestOffset = 0;
                uint bestLength = 0;

                void ConsiderCandidate(uint offset, uint length)
                {
                    if (offset > 0 && length >= 256 && offset + length <= fs.Length && length < 25 * 1024 * 1024)
                    {
                        if (length > bestLength)
                        {
                            long curPos = fs.Position;
                            fs.Seek(offset, SeekOrigin.Begin);
                            byte[] m = new byte[2];
                            int r = fs.Read(m, 0, 2);
                            fs.Seek(curPos, SeekOrigin.Begin);

                            if (r == 2 && m[0] == 0xFF && m[1] == 0xD8)
                            {
                                bestOffset = offset;
                                bestLength = length;
                            }
                        }
                    }
                }

                uint ParseIfd(uint ifdOffset, bool isSubIfd = false)
                {
                    if (ifdOffset == 0 || ifdOffset + 2 > fs.Length) return 0;
                    fs.Seek(ifdOffset, SeekOrigin.Begin);

                    byte[] cntBuf = new byte[2];
                    if (fs.Read(cntBuf, 0, 2) < 2) return 0;
                    ushort count = ReadUInt16(cntBuf, 0, isLittleEndian);
                    if (count == 0 || count > 500) return 0;

                    byte[] entries = new byte[count * 12];
                    if (fs.Read(entries, 0, entries.Length) < entries.Length) return 0;

                    uint nextIfd = 0;
                    byte[] nextBuf = new byte[4];
                    if (fs.Read(nextBuf, 0, 4) == 4)
                    {
                        nextIfd = ReadUInt32(nextBuf, 0, isLittleEndian);
                    }

                    uint curJpegOffset = 0;
                    uint curJpegLength = 0;
                    var subIfdOffsets = new List<uint>();
                    uint exifIfdOffset = 0;

                    for (int i = 0; i < count; i++)
                    {
                        int o = i * 12;
                        ushort tag = ReadUInt16(entries, o, isLittleEndian);
                        uint valCount = ReadUInt32(entries, o + 4, isLittleEndian);
                        uint val = ReadUInt32(entries, o + 8, isLittleEndian);

                        if (tag == 0x0201 || (tag == 0x0111 && curJpegOffset == 0))
                        {
                            curJpegOffset = val;
                        }
                        else if (tag == 0x0202 || (tag == 0x0117 && curJpegLength == 0))
                        {
                            curJpegLength = val;
                        }
                        else if (tag == 0x014A) // SubIFDs
                        {
                            if (valCount == 1)
                            {
                                subIfdOffsets.Add(val);
                            }
                            else if (valCount > 1 && valCount <= 16 && val + valCount * 4 <= fs.Length)
                            {
                                long saved = fs.Position;
                                fs.Seek(val, SeekOrigin.Begin);
                                byte[] sBuf = new byte[valCount * 4];
                                if (fs.Read(sBuf, 0, sBuf.Length) == sBuf.Length)
                                {
                                    for (int si = 0; si < valCount; si++)
                                    {
                                        subIfdOffsets.Add(ReadUInt32(sBuf, si * 4, isLittleEndian));
                                    }
                                }
                                fs.Seek(saved, SeekOrigin.Begin);
                            }
                        }
                        else if (tag == 0x8769) // ExifIFD
                        {
                            exifIfdOffset = val;
                        }
                    }

                    ConsiderCandidate(curJpegOffset, curJpegLength);

                    if (!isSubIfd)
                    {
                        foreach (var subOff in subIfdOffsets)
                        {
                            ParseIfd(subOff, isSubIfd: true);
                        }
                    }

                    if (!isSubIfd && exifIfdOffset > 0 && exifIfdOffset + 2 <= fs.Length)
                    {
                        ParseExifAndMakerNote(fs, exifIfdOffset, isLittleEndian, ConsiderCandidate);
                    }

                    return nextIfd;
                }

                // 1. IFD0
                uint next = ParseIfd(ifd0Offset);

                // 2. IFD1 (common in ORF and CR2)
                if (next > 0)
                {
                    ParseIfd(next, isSubIfd: true);
                }

                if (bestOffset > 0 && bestLength > 0)
                {
                    fs.Seek(bestOffset, SeekOrigin.Begin);
                    byte[] jpegBytes = new byte[bestLength];
                    int r = fs.Read(jpegBytes, 0, (int)bestLength);
                    if (r == bestLength && IsValidJpegHeader(jpegBytes))
                    {
                        return jpegBytes;
                    }
                }
            }
            catch { }

            return null;
        }

        private static void ParseExifAndMakerNote(FileStream fs, uint exifIfdOffset, bool isLittleEndian, Action<uint, uint> considerCandidate)
        {
            try
            {
                fs.Seek(exifIfdOffset, SeekOrigin.Begin);
                byte[] cntBuf = new byte[2];
                if (fs.Read(cntBuf, 0, 2) < 2) return;
                ushort count = ReadUInt16(cntBuf, 0, isLittleEndian);
                if (count == 0 || count > 500) return;

                byte[] entries = new byte[count * 12];
                if (fs.Read(entries, 0, entries.Length) < entries.Length) return;

                for (int i = 0; i < count; i++)
                {
                    int o = i * 12;
                    ushort tag = ReadUInt16(entries, o, isLittleEndian);
                    uint val = ReadUInt32(entries, o + 8, isLittleEndian);

                    if (tag == 0x927C) // MakerNote
                    {
                        uint mnOffset = val;
                        if (mnOffset + 16 > fs.Length) continue;

                        fs.Seek(mnOffset, SeekOrigin.Begin);
                        byte[] mnHeader = new byte[16];
                        if (fs.Read(mnHeader, 0, 16) < 16) continue;

                        // A. Nikon MakerNote 2 (starts with "Nikon\0\x02")
                        if (mnHeader[0] == 'N' && mnHeader[1] == 'i' && mnHeader[2] == 'k' && mnHeader[3] == 'o' && mnHeader[4] == 'n' && mnHeader[5] == 0)
                        {
                            long tiffHeaderPos = mnOffset + 10;
                            if (tiffHeaderPos + 8 <= fs.Length)
                            {
                                fs.Seek(tiffHeaderPos, SeekOrigin.Begin);
                                byte[] tHdr = new byte[8];
                                if (fs.Read(tHdr, 0, 8) == 8)
                                {
                                    bool mnLE = (tHdr[0] == 0x49);
                                    uint mnIfdOff = ReadUInt32(tHdr, 4, mnLE);
                                    if (tiffHeaderPos + mnIfdOff + 2 <= fs.Length)
                                    {
                                        fs.Seek(tiffHeaderPos + mnIfdOff, SeekOrigin.Begin);
                                        byte[] mnCntBuf = new byte[2];
                                        if (fs.Read(mnCntBuf, 0, 2) == 2)
                                        {
                                            ushort mnCount = ReadUInt16(mnCntBuf, 0, mnLE);
                                            if (mnCount > 0 && mnCount <= 500)
                                            {
                                                byte[] mnEntries = new byte[mnCount * 12];
                                                if (fs.Read(mnEntries, 0, mnEntries.Length) == mnEntries.Length)
                                                {
                                                    uint pStart = 0;
                                                    uint pLen = 0;
                                                    for (int mi = 0; mi < mnCount; mi++)
                                                    {
                                                        int mo = mi * 12;
                                                        ushort mTag = ReadUInt16(mnEntries, mo, mnLE);
                                                        uint mVal = ReadUInt32(mnEntries, mo + 8, mnLE);
                                                        if (mTag == 0x0011) pStart = mVal;
                                                        else if (mTag == 0x0012) pLen = mVal;
                                                    }

                                                    if (pStart > 0 && pLen > 0)
                                                    {
                                                        considerCandidate((uint)(tiffHeaderPos + pStart), pLen);
                                                        considerCandidate(pStart, pLen);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        // B. Olympus / OM System MakerNote (starts with "OLYMP\0" or "OLYMPUS\0")
                        else if (mnHeader[0] == 'O' && mnHeader[1] == 'L' && mnHeader[2] == 'Y' && mnHeader[3] == 'M' && mnHeader[4] == 'P')
                        {
                            int mnHdrLen = (mnHeader[5] == 'U' && mnHeader[6] == 'S') ? 12 : 8;
                            long mnIfdPos = mnOffset + mnHdrLen;
                            if (mnIfdPos + 2 <= fs.Length)
                            {
                                fs.Seek(mnIfdPos, SeekOrigin.Begin);
                                byte[] mnCntBuf = new byte[2];
                                if (fs.Read(mnCntBuf, 0, 2) == 2)
                                {
                                    ushort mnCount = ReadUInt16(mnCntBuf, 0, isLittleEndian);
                                    if (mnCount > 0 && mnCount <= 500)
                                    {
                                        byte[] mnEntries = new byte[mnCount * 12];
                                        if (fs.Read(mnEntries, 0, mnEntries.Length) == mnEntries.Length)
                                        {
                                            uint oPrevStart = 0;
                                            uint oPrevLen = 0;
                                            for (int mi = 0; mi < mnCount; mi++)
                                            {
                                                int mo = mi * 12;
                                                ushort mTag = ReadUInt16(mnEntries, mo, isLittleEndian);
                                                uint mVal = ReadUInt32(mnEntries, mo + 8, isLittleEndian);
                                                if (mTag == 0x0102) oPrevStart = mVal;
                                                else if (mTag == 0x0103) oPrevLen = mVal;
                                            }

                                            if (oPrevStart > 0 && oPrevLen > 0)
                                            {
                                                considerCandidate((uint)(mnOffset + oPrevStart), oPrevLen);
                                                considerCandidate(oPrevStart, oPrevLen);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static byte[]? TryScanForEmbeddedJpeg(FileStream fs)
        {
            try
            {
                long scanLen = Math.Min(fs.Length, 8 * 1024 * 1024);
                if (scanLen < 1024) return null;

                fs.Seek(0, SeekOrigin.Begin);
                byte[] buffer = new byte[scanLen];
                int read = fs.Read(buffer, 0, (int)scanLen);
                if (read < 1024) return null;

                int bestOffset = -1;
                int bestLen = 0;

                for (int i = 0; i < read - 100; i++)
                {
                    if (buffer[i] == 0xFF && buffer[i + 1] == 0xD8 && buffer[i + 2] == 0xFF)
                    {
                        byte m = buffer[i + 3];
                        if (m == 0xDB || m == 0xE0 || m == 0xE1 || m == 0xEE || m == 0xC4 || m == 0xC0 || m == 0xC2)
                        {
                            int eoi = -1;
                            for (int j = i + 10; j < read - 1; j++)
                            {
                                if (buffer[j] == 0xFF && buffer[j + 1] == 0xD9)
                                {
                                    eoi = j + 2;
                                    break;
                                }
                            }

                            if (eoi > i)
                            {
                                int len = eoi - i;
                                if (len > bestLen)
                                {
                                    bestLen = len;
                                    bestOffset = i;
                                }
                            }
                        }
                    }
                }

                if (bestOffset >= 0 && bestLen >= 256)
                {
                    byte[] jpeg = new byte[bestLen];
                    Buffer.BlockCopy(buffer, bestOffset, jpeg, 0, bestLen);
                    return jpeg;
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
