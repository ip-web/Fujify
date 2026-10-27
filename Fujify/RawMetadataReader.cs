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
using System.Globalization;
using System.IO;
using System.Text;
using Fujify.Services;

namespace Fujify
{
    /// <summary>
    /// Fast in-memory reader for standard TIFF-based RAW files (DNG, ARW, CR2, NEF, ORF, PEF)
    /// and Fujifilm RAF files, avoiding the overhead of launching external processes.
    /// </summary>
    public static class RawMetadataReader
    {
        public static string? TryGetCameraMakeAndModel(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
                if (fs.Length < 16) return null;

                byte[] header = new byte[16];
                int read = fs.Read(header, 0, 16);
                if (read < 16) return null;

                // 1. Check for Fujifilm RAF: starts with "FUJIFILM"
                if (header[0] == 0x46 && header[1] == 0x55 && header[2] == 0x4A && header[3] == 0x49 &&
                    header[4] == 0x46 && header[5] == 0x49 && header[6] == 0x4C && header[7] == 0x4D)
                {
                    if (fs.Length >= 60)
                    {
                        fs.Seek(0x1C, SeekOrigin.Begin);
                        byte[] modelBytes = new byte[32];
                        int modelRead = fs.Read(modelBytes, 0, 32);
                        if (modelRead > 0)
                        {
                            string rafModel = Encoding.ASCII.GetString(modelBytes, 0, modelRead).Trim('\0', ' ');
                            if (!string.IsNullOrEmpty(rafModel))
                            {
                                return rafModel.StartsWith("FUJIFILM", StringComparison.OrdinalIgnoreCase)
                                    ? rafModel
                                    : $"FUJIFILM {rafModel}".Trim();
                            }
                        }
                    }
                    return "FUJIFILM";
                }

                // 2. Check for Canon CIFF (.CRW): starts with "II" (or "MM") and "HEAPCCDR" / "HEAPCCDB" at offset 6
                if (header[0] == 0x49 && header[1] == 0x49 && header.Length >= 14 &&
                    header[6] == 0x48 && header[7] == 0x45 && header[8] == 0x41 && header[9] == 0x50 &&
                    header[10] == 0x43 && header[11] == 0x43 && header[12] == 0x44 &&
                    (header[13] == 0x52 || header[13] == 0x42))
                {
                    string? ciffModel = DngMetadataEditor.ReadCanonCiffCameraInfo(fs);
                    if (!string.IsNullOrEmpty(ciffModel)) return ciffModel;
                }

                // 3. Check for Canon CR3: starts with ftyp
                if (header.Length >= 8 && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
                {
                    string? cr3Model = DngMetadataEditor.ReadCanonCr3CameraInfo(fs);
                    if (!string.IsNullOrEmpty(cr3Model)) return cr3Model;
                }

                // 4. Check for TIFF-based RAW (DNG, ARW, CR2, NEF, ORF, PEF, RW2, etc.)
                bool isLittleEndian;
                if (header[0] == 0x49 && header[1] == 0x49)
                {
                    isLittleEndian = true;
                    ushort magic = ReadUInt16(header, 2, true);
                    if (magic != 42 && magic != 85 && magic != 0x4F52 && magic != 0x5352)
                    {
                        return null;
                    }
                }
                else if (header[0] == 0x4D && header[1] == 0x4D)
                {
                    isLittleEndian = false;
                    ushort magic = ReadUInt16(header, 2, false);
                    if (magic != 42 && magic != 0x4F52 && magic != 0x524F)
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }

                uint ifdOffset = ReadUInt32(header, 4, isLittleEndian);
                if (ifdOffset == 0 || ifdOffset + 2 > fs.Length) return null;

                fs.Seek(ifdOffset, SeekOrigin.Begin);
                byte[] countBuf = new byte[2];
                if (fs.Read(countBuf, 0, 2) < 2) return null;

                ushort entryCount = ReadUInt16(countBuf, 0, isLittleEndian);
                if (entryCount > 1000) return null; // Sanity check

                byte[] entries = new byte[entryCount * 12];
                if (fs.Read(entries, 0, entries.Length) < entries.Length) return null;

                string? make = null;
                string? model = null;

                for (int i = 0; i < entryCount; i++)
                {
                    int offset = i * 12;
                    ushort tag = ReadUInt16(entries, offset, isLittleEndian);
                    ushort type = ReadUInt16(entries, offset + 2, isLittleEndian);
                    uint count = ReadUInt32(entries, offset + 4, isLittleEndian);
                    uint valueOrOffset = ReadUInt32(entries, offset + 8, isLittleEndian);

                    // Tag 0x010F = Make, Tag 0x0110 = Model
                    if (tag == 0x010F && make == null)
                    {
                        make = ReadString(fs, type, count, valueOrOffset, entries, offset + 8);
                    }
                    else if (tag == 0x0110 && model == null)
                    {
                        model = ReadString(fs, type, count, valueOrOffset, entries, offset + 8);
                    }

                    if (make != null && model != null)
                        break;
                }

                if (!string.IsNullOrWhiteSpace(make) || !string.IsNullOrWhiteSpace(model))
                {
                    if (make != null && make.Equals("NIKON CORPORATION", StringComparison.OrdinalIgnoreCase))
                    {
                        make = "NIKON";
                    }
                    if (make != null && model != null && model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
                    {
                        return model.Trim();
                    }
                    return $"{make} {model}".Trim();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reads EXIF Orientation (1=Normal, 3=180, 6=90 CW, 8=270 CW) directly from raw/jpeg file headers in &lt;1ms.
        /// </summary>
        public static int? TryGetOrientation(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
                if (fs.Length < 16) return null;

                byte[] header = new byte[16];
                int read = fs.Read(header, 0, 16);
                if (read < 16) return null;

                // 1. Fujifilm RAF
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
                            if (jpos > 0 && jlen > 0 && jpos + jlen <= fs.Length)
                            {
                                fs.Seek(jpos, SeekOrigin.Begin);
                                return TryGetOrientationFromJpegStream(fs);
                            }
                        }
                    }
                    return null;
                }

                // 2. JPEG
                if (header[0] == 0xFF && header[1] == 0xD8)
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    return TryGetOrientationFromJpegStream(fs);
                }

                // Canon CIFF (.CRW)
                if (header[0] == 0x49 && header[1] == 0x49 && header.Length >= 14 &&
                    header[6] == 0x48 && header[7] == 0x45 && header[8] == 0x41 && header[9] == 0x50 &&
                    header[10] == 0x43 && header[11] == 0x43 && header[12] == 0x44 &&
                    (header[13] == 0x52 || header[13] == 0x42))
                {
                    return 1;
                }

                // 3. TIFF-based RAW (DNG, ARW, CR2, NEF, ORF, PEF, RW2, etc.)
                bool isLittleEndian;
                if (header[0] == 0x49 && header[1] == 0x49)
                {
                    isLittleEndian = true;
                    ushort magic = ReadUInt16(header, 2, true);
                    if (magic != 42 && magic != 85 && magic != 0x4F52 && magic != 0x5352)
                    {
                        return null;
                    }
                }
                else if (header[0] == 0x4D && header[1] == 0x4D)
                {
                    isLittleEndian = false;
                    ushort magic = ReadUInt16(header, 2, false);
                    if (magic != 42 && magic != 0x4F52 && magic != 0x524F)
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }

                uint ifdOffset = ReadUInt32(header, 4, isLittleEndian);
                if (ifdOffset == 0 || ifdOffset + 2 > fs.Length) return null;

                fs.Seek(ifdOffset, SeekOrigin.Begin);
                byte[] countBuf = new byte[2];
                if (fs.Read(countBuf, 0, 2) < 2) return null;

                ushort entryCount = ReadUInt16(countBuf, 0, isLittleEndian);
                if (entryCount > 1000) return null;

                byte[] entries = new byte[entryCount * 12];
                if (fs.Read(entries, 0, entries.Length) < entries.Length) return null;

                for (int i = 0; i < entryCount; i++)
                {
                    int offset = i * 12;
                    ushort tag = ReadUInt16(entries, offset, isLittleEndian);
                    if (tag == 0x0112) // Tag 0x0112 = Orientation
                    {
                        ushort val = ReadUInt16(entries, offset + 8, isLittleEndian);
                        if (val >= 1 && val <= 8) return val;
                    }
                }
            }
            catch { }
            return null;
        }

        private static int? TryGetOrientationFromJpegStream(Stream stream)
        {
            try
            {
                byte[] markerBuf = new byte[4];
                long startPos = stream.Position;
                stream.Seek(startPos + 2, SeekOrigin.Begin); // Skip FF D8 SOI

                while (stream.Position < stream.Length - 4)
                {
                    if (stream.Read(markerBuf, 0, 4) < 4) break;
                    if (markerBuf[0] != 0xFF) break;

                    byte marker = markerBuf[1];
                    int len = (markerBuf[2] << 8) | markerBuf[3];
                    if (len < 2) break;

                    // APP1 marker (0xFF 0xE1)
                    if (marker == 0xE1)
                    {
                        byte[] exifId = new byte[6];
                        if (stream.Read(exifId, 0, 6) == 6 &&
                            exifId[0] == 'E' && exifId[1] == 'x' && exifId[2] == 'i' && exifId[3] == 'f' &&
                            exifId[4] == 0 && exifId[5] == 0)
                        {
                            byte[] tiffHdr = new byte[8];
                            if (stream.Read(tiffHdr, 0, 8) == 8)
                            {
                                bool isLittle;
                                if (tiffHdr[0] == 0x49 && tiffHdr[1] == 0x49 && tiffHdr[2] == 0x2A && tiffHdr[3] == 0x00)
                                    isLittle = true;
                                else if (tiffHdr[0] == 0x4D && tiffHdr[1] == 0x4D && tiffHdr[2] == 0x00 && tiffHdr[3] == 0x2A)
                                    isLittle = false;
                                else
                                    return null;

                                uint ifdOff = isLittle
                                    ? (uint)(tiffHdr[4] | (tiffHdr[5] << 8) | (tiffHdr[6] << 16) | (tiffHdr[7] << 24))
                                    : (uint)((tiffHdr[4] << 24) | (tiffHdr[5] << 16) | (tiffHdr[6] << 8) | tiffHdr[7]);

                                long tiffStart = stream.Position - 8;
                                stream.Seek(tiffStart + ifdOff, SeekOrigin.Begin);

                                byte[] cBuf = new byte[2];
                                if (stream.Read(cBuf, 0, 2) == 2)
                                {
                                    ushort count = isLittle
                                        ? (ushort)(cBuf[0] | (cBuf[1] << 8))
                                        : (ushort)((cBuf[0] << 8) | cBuf[1]);

                                    if (count > 0 && count < 500)
                                    {
                                        byte[] entries = new byte[count * 12];
                                        if (stream.Read(entries, 0, entries.Length) == entries.Length)
                                        {
                                            for (int i = 0; i < count; i++)
                                            {
                                                int o = i * 12;
                                                ushort tag = isLittle
                                                    ? (ushort)(entries[o] | (entries[o + 1] << 8))
                                                    : (ushort)((entries[o] << 8) | entries[o + 1]);

                                                if (tag == 0x0112)
                                                {
                                                    ushort val = isLittle
                                                        ? (ushort)(entries[o + 8] | (entries[o + 9] << 8))
                                                        : (ushort)((entries[o + 8] << 8) | entries[o + 9]);
                                                    if (val >= 1 && val <= 8) return val;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    }
                    else if (marker == 0xDA) // Start of Scan
                    {
                        break;
                    }
                    else
                    {
                        stream.Seek(len - 2, SeekOrigin.Current);
                    }
                }
            }
            catch { }
            return null;
        }

        private static ushort ReadUInt16(byte[] buf, int offset, bool littleEndian)
        {
            return littleEndian
                ? (ushort)(buf[offset] | (buf[offset + 1] << 8))
                : (ushort)((buf[offset] << 8) | buf[offset + 1]);
        }

        private static uint ReadUInt32(byte[] buf, int offset, bool littleEndian)
        {
            return littleEndian
                ? (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24))
                : (uint)((buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3]);
        }

        private static string? ReadString(FileStream fs, ushort type, uint count, uint valueOrOffset, byte[] entries, int inlineOffset)
        {
            if (type != 2 || count == 0 || count > 256) return null; // ASCII type is 2

            byte[] strBytes = new byte[count];
            if (count <= 4)
            {
                Array.Copy(entries, inlineOffset, strBytes, 0, (int)count);
            }
            else
            {
                if (valueOrOffset + count > fs.Length) return null;
                long savedPos = fs.Position;
                fs.Seek(valueOrOffset, SeekOrigin.Begin);
                int bytesRead = fs.Read(strBytes, 0, (int)count);
                fs.Seek(savedPos, SeekOrigin.Begin);
                if (bytesRead < count) return null;
            }

            return Encoding.ASCII.GetString(strBytes).Trim('\0', ' ');
        }

        /// <summary>
        /// Reads relevant EXIF metadata (Lens, Aperture, Shutter Speed, ISO, Focal Length, Date Taken, Orientation)
        /// directly in memory in &lt;1ms without launching external processes.
        /// Supports TIFF-based RAWs (DNG, ARW, CR2, NEF, ORF, PEF, RW2), Fujifilm RAF, JPEG, and Canon CR3.
        /// </summary>
        public static ExifData? TryGetExifData(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192);
                if (fs.Length < 16) return null;

                byte[] header = new byte[16];
                int read = fs.Read(header, 0, 16);
                if (read < 16) return null;

                // 1. Fujifilm RAF
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
                            if (jpos > 0 && jlen > 0 && jpos + jlen <= fs.Length)
                            {
                                fs.Seek(jpos, SeekOrigin.Begin);
                                return TryGetExifFromJpegStream(fs);
                            }
                        }
                    }
                    return null;
                }

                // 2. JPEG
                if (header[0] == 0xFF && header[1] == 0xD8)
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    return TryGetExifFromJpegStream(fs);
                }

                // 3. Canon CR3 (ISOBMFF container with 'ftyp' box)
                if (header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    return TryGetExifFromCanonCr3(fs);
                }

                // 4. Canon CIFF (.CRW): starts with "II" and "HEAPCCDR" / "HEAPCCDB" at offset 6
                if (header[0] == 0x49 && header[1] == 0x49 && header.Length >= 14 &&
                    header[6] == 0x48 && header[7] == 0x45 && header[8] == 0x41 && header[9] == 0x50 &&
                    header[10] == 0x43 && header[11] == 0x43 && header[12] == 0x44 &&
                    (header[13] == 0x52 || header[13] == 0x42))
                {
                    return TryGetExifFromCanonCiff(fs);
                }

                // 5. TIFF-based RAW (DNG, ARW, CR2, NEF, ORF, PEF, RW2, etc.)
                bool isLittleEndian;
                if (header[0] == 0x49 && header[1] == 0x49)
                {
                    isLittleEndian = true;
                    ushort magic = ReadUInt16(header, 2, true);
                    if (magic != 42 && magic != 85 && magic != 0x4F52 && magic != 0x5352)
                    {
                        return null;
                    }
                }
                else if (header[0] == 0x4D && header[1] == 0x4D)
                {
                    isLittleEndian = false;
                    ushort magic = ReadUInt16(header, 2, false);
                    if (magic != 42 && magic != 0x4F52 && magic != 0x524F)
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }

                return ReadExifFromTiff(fs, 0, isLittleEndian);
            }
            catch
            {
                return null;
            }
        }

        private static ExifData? TryGetExifFromJpegStream(Stream stream)
        {
            try
            {
                byte[] markerBuf = new byte[4];
                long startPos = stream.Position;
                stream.Seek(startPos + 2, SeekOrigin.Begin); // Skip FF D8 SOI

                while (stream.Position < stream.Length - 4)
                {
                    if (stream.Read(markerBuf, 0, 4) < 4) break;
                    if (markerBuf[0] != 0xFF) break;

                    byte marker = markerBuf[1];
                    int len = (markerBuf[2] << 8) | markerBuf[3];
                    if (len < 2) break;

                    // APP1 marker (0xFF 0xE1)
                    if (marker == 0xE1)
                    {
                        byte[] exifId = new byte[6];
                        if (stream.Read(exifId, 0, 6) == 6 &&
                            exifId[0] == 'E' && exifId[1] == 'x' && exifId[2] == 'i' && exifId[3] == 'f' &&
                            exifId[4] == 0 && exifId[5] == 0)
                        {
                            long tiffStart = stream.Position;
                            return ReadExifFromTiff(stream, tiffStart);
                        }
                        break;
                    }
                    else if (marker == 0xDA) // Start of Scan
                    {
                        break;
                    }
                    else
                    {
                        stream.Seek(len - 2, SeekOrigin.Current);
                    }
                }
            }
            catch { }
            return null;
        }

        private static ExifData? TryGetExifFromCanonCr3(Stream stream)
        {
            try
            {
                long length = Math.Min(stream.Length, 15 * 1024 * 1024);
                byte[] hdr = new byte[8];
                ExifData? cmt1Data = null;
                ExifData? cmt2Data = null;

                while (stream.Position < length - 8)
                {
                    long boxStart = stream.Position;
                    if (stream.Read(hdr, 0, 8) < 8) break;

                    uint boxSize = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
                    string boxType = Encoding.ASCII.GetString(hdr, 4, 4);

                    long boxDataSize = boxSize;
                    long headerLen = 8;
                    if (boxSize == 1)
                    {
                        byte[] largeSizeBuf = new byte[8];
                        if (stream.Read(largeSizeBuf, 0, 8) < 8) break;
                        boxDataSize = (long)(((ulong)largeSizeBuf[0] << 56) | ((ulong)largeSizeBuf[1] << 48) |
                                             ((ulong)largeSizeBuf[2] << 40) | ((ulong)largeSizeBuf[3] << 32) |
                                             ((ulong)largeSizeBuf[4] << 24) | ((ulong)largeSizeBuf[5] << 16) |
                                             ((ulong)largeSizeBuf[6] << 8) | (ulong)largeSizeBuf[7]);
                        headerLen = 16;
                    }
                    else if (boxSize == 0)
                    {
                        boxDataSize = stream.Length - boxStart;
                    }

                    if (boxDataSize < headerLen) break;

                    if (boxType == "moov")
                    {
                        long moovEnd = boxStart + boxDataSize;
                        while (stream.Position < moovEnd - 8)
                        {
                            long subBoxStart = stream.Position;
                            if (stream.Read(hdr, 0, 8) < 8) break;
                            uint subBoxSize = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
                            string subType = Encoding.ASCII.GetString(hdr, 4, 4);
                            if (subBoxSize < 8 || subBoxStart + subBoxSize > moovEnd) break;

                            if (subType == "uuid")
                            {
                                byte[] uuid = new byte[16];
                                if (stream.Read(uuid, 0, 16) == 16)
                                {
                                    // Canon metadata UUID: 85 c0 b6 87 82 0f 11 e0 81 11 f4 ce 46 2b 02 4c
                                    if (uuid[0] == 0x85 && uuid[1] == 0xC0 && uuid[2] == 0xB6 && uuid[3] == 0x87)
                                    {
                                        long cmtEnd = subBoxStart + subBoxSize;
                                        while (stream.Position < cmtEnd - 8)
                                        {
                                            long cmtBoxStart = stream.Position;
                                            if (stream.Read(hdr, 0, 8) < 8) break;
                                            uint cmtSize = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
                                            string cmtType = Encoding.ASCII.GetString(hdr, 4, 4);
                                            if (cmtSize < 8 || cmtBoxStart + cmtSize > cmtEnd) break;

                                            if (cmtType == "CMT1")
                                            {
                                                cmt1Data = ReadExifFromTiff(stream, stream.Position);
                                            }
                                            else if (cmtType == "CMT2")
                                            {
                                                cmt2Data = ReadExifFromTiff(stream, stream.Position);
                                            }

                                            stream.Seek(cmtBoxStart + cmtSize, SeekOrigin.Begin);
                                        }
                                    }
                                }
                            }

                            stream.Seek(subBoxStart + subBoxSize, SeekOrigin.Begin);
                        }
                        break;
                    }

                    stream.Seek(boxStart + boxDataSize, SeekOrigin.Begin);
                }

                if (cmt1Data != null || cmt2Data != null)
                {
                    return new ExifData(
                        cmt2Data?.Lens ?? cmt1Data?.Lens,
                        cmt2Data?.Aperture ?? cmt1Data?.Aperture,
                        cmt2Data?.ShutterSpeed ?? cmt1Data?.ShutterSpeed,
                        cmt2Data?.Iso ?? cmt1Data?.Iso,
                        cmt2Data?.FocalLength ?? cmt1Data?.FocalLength,
                        cmt2Data?.DateTimeOriginal ?? cmt1Data?.DateTimeOriginal,
                        cmt1Data?.Orientation ?? cmt2Data?.Orientation
                    );
                }
            }
            catch { }
            return null;
        }

        private static ExifData? TryGetExifFromCanonCiff(Stream stream)
        {
            try
            {
                long flen = stream.Length;
                if (flen < 30) return null;

                stream.Seek(flen - 4, SeekOrigin.Begin);
                byte[] dirOffsetBuf = new byte[4];
                if (stream.Read(dirOffsetBuf, 0, 4) < 4) return null;

                uint dirOffset = (uint)(dirOffsetBuf[0] | (dirOffsetBuf[1] << 8) | (dirOffsetBuf[2] << 16) | (dirOffsetBuf[3] << 24));
                string? dateTimeOriginal = null;
                ScanCiffForExif(stream, 26, dirOffset, (uint)(flen - 26), 0, ref dateTimeOriginal);

                if (!string.IsNullOrEmpty(dateTimeOriginal))
                {
                    return new ExifData(null, null, null, null, null, dateTimeOriginal, "1");
                }
            }
            catch { }
            return null;
        }

        private static void ScanCiffForExif(Stream stream, long blockBase, uint dirOffset, uint blockLen, int depth, ref string? dateTimeOriginal)
        {
            if (depth > 10 || dirOffset + 2 > blockLen) return;

            stream.Seek(blockBase + dirOffset, SeekOrigin.Begin);
            byte[] countBuf = new byte[2];
            if (stream.Read(countBuf, 0, 2) < 2) return;

            ushort count = (ushort)(countBuf[0] | (countBuf[1] << 8));
            if (count > 256) return;

            byte[] entriesBuf = new byte[count * 10];
            if (stream.Read(entriesBuf, 0, entriesBuf.Length) < entriesBuf.Length) return;

            for (int i = 0; i < count; i++)
            {
                int entryOff = i * 10;
                ushort tag = (ushort)(entriesBuf[entryOff] | (entriesBuf[entryOff + 1] << 8));
                uint length = (uint)(entriesBuf[entryOff + 2] | (entriesBuf[entryOff + 3] << 8) | (entriesBuf[entryOff + 4] << 16) | (entriesBuf[entryOff + 5] << 24));
                uint offset = (uint)(entriesBuf[entryOff + 6] | (entriesBuf[entryOff + 7] << 8) | (entriesBuf[entryOff + 8] << 16) | (entriesBuf[entryOff + 9] << 24));

                ushort tagId = (ushort)(tag & 0x3FFF);
                ushort tagType = (ushort)(tag & 0x3800);

                if (tagId == 0x180E && length >= 4 && dateTimeOriginal == null) // TimeStamp
                {
                    if (offset + 4 <= blockLen)
                    {
                        stream.Seek(blockBase + offset, SeekOrigin.Begin);
                        byte[] tsBuf = new byte[4];
                        if (stream.Read(tsBuf, 0, 4) == 4)
                        {
                            uint unixSecs = (uint)(tsBuf[0] | (tsBuf[1] << 8) | (tsBuf[2] << 16) | (tsBuf[3] << 24));
                            if (unixSecs > 0)
                            {
                                try
                                {
                                    dateTimeOriginal = DateTimeOffset.FromUnixTimeSeconds(unixSecs).DateTime.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
                                }
                                catch { }
                            }
                        }
                    }
                }
                else if (tagType == 0x2800 || tagType == 0x3000) // Sub-directory
                {
                    if (length >= 4 && offset + length <= blockLen)
                    {
                        long subBase = blockBase + offset;
                        stream.Seek(subBase + length - 4, SeekOrigin.Begin);
                        byte[] subDirOffsetBuf = new byte[4];
                        if (stream.Read(subDirOffsetBuf, 0, 4) == 4)
                        {
                            uint subDirOffset = (uint)(subDirOffsetBuf[0] | (subDirOffsetBuf[1] << 8) | (subDirOffsetBuf[2] << 16) | (subDirOffsetBuf[3] << 24));
                            ScanCiffForExif(stream, subBase, subDirOffset, length, depth + 1, ref dateTimeOriginal);
                        }
                    }
                }
            }
        }

        private static ExifData? ReadExifFromTiff(Stream stream, long tiffBase, bool? forceLittleEndian = null)
        {
            try
            {
                if (tiffBase + 8 > stream.Length) return null;
                stream.Seek(tiffBase, SeekOrigin.Begin);
                byte[] tiffHdr = new byte[8];
                if (stream.Read(tiffHdr, 0, 8) < 8) return null;

                bool isLittleEndian;
                if (tiffHdr[0] == 0x49 && tiffHdr[1] == 0x49)
                    isLittleEndian = true;
                else if (tiffHdr[0] == 0x4D && tiffHdr[1] == 0x4D)
                    isLittleEndian = false;
                else if (forceLittleEndian.HasValue)
                    isLittleEndian = forceLittleEndian.Value;
                else
                    return null;

                uint ifd0Offset = ReadUInt32(tiffHdr, 4, isLittleEndian);
                if (ifd0Offset == 0 || tiffBase + ifd0Offset + 2 > stream.Length) return null;

                return ReadExifFromIfd(stream, tiffBase, ifd0Offset, isLittleEndian);
            }
            catch
            {
                return null;
            }
        }

        private static ExifData? ReadExifFromIfd(Stream stream, long tiffBase, uint ifd0Offset, bool isLittleEndian)
        {
            try
            {
                if (tiffBase + ifd0Offset + 2 > stream.Length) return null;
                stream.Seek(tiffBase + ifd0Offset, SeekOrigin.Begin);
                byte[] countBuf = new byte[2];
                if (stream.Read(countBuf, 0, 2) < 2) return null;

                ushort entryCount = ReadUInt16(countBuf, 0, isLittleEndian);
                if (entryCount == 0 || entryCount > 2000) return null;

                int entriesBytes = entryCount * 12;
                if (tiffBase + ifd0Offset + 2 + entriesBytes > stream.Length) return null;

                byte[] entries = new byte[entriesBytes];
                if (stream.Read(entries, 0, entries.Length) < entries.Length) return null;

                string? lens = null;
                string? aperture = null;
                string? shutterSpeed = null;
                string? iso = null;
                string? focalLength = null;
                string? dateTimeOriginal = null;
                string? orientation = null;
                uint exifIfdOffset = 0;

                for (int i = 0; i < entryCount; i++)
                {
                    int offset = i * 12;
                    ushort tag = ReadUInt16(entries, offset, isLittleEndian);
                    ushort type = ReadUInt16(entries, offset + 2, isLittleEndian);
                    uint count = ReadUInt32(entries, offset + 4, isLittleEndian);
                    uint valueOrOffset = ReadUInt32(entries, offset + 8, isLittleEndian);

                    switch (tag)
                    {
                        case 0x0112: // Orientation
                            ushort orientVal = ReadUInt16(entries, offset + 8, isLittleEndian);
                            orientation = FormatOrientation(orientVal);
                            break;

                        case 0x8769: // Exif SubIFD
                            exifIfdOffset = valueOrOffset;
                            break;

                        case 0x0132: // ModifyDate / DateTime
                            if (dateTimeOriginal == null)
                                dateTimeOriginal = ReadTiffString(stream, tiffBase, type, count, valueOrOffset, entries, offset + 8);
                            break;

                        case 0x829A: // ExposureTime
                            shutterSpeed ??= ReadRationalShutterSpeed(stream, tiffBase, type, count, valueOrOffset, isLittleEndian);
                            break;

                        case 0x829D: // FNumber
                            aperture ??= ReadRationalFNumber(stream, tiffBase, type, count, valueOrOffset, isLittleEndian);
                            break;

                        case 0x8827: // PhotographicSensitivity / ISO
                        case 0x8833: // RecommendedExposureIndex
                            iso ??= ReadIsoValue(stream, tiffBase, type, count, valueOrOffset, entries, offset + 8, isLittleEndian);
                            break;

                        case 0x920A: // FocalLength
                            focalLength ??= ReadRationalFocalLength(stream, tiffBase, type, count, valueOrOffset, isLittleEndian);
                            break;

                        case 0xA434: // LensModel
                            lens ??= ReadTiffString(stream, tiffBase, type, count, valueOrOffset, entries, offset + 8);
                            break;
                    }
                }

                if (exifIfdOffset > 0 && tiffBase + exifIfdOffset + 2 <= stream.Length)
                {
                    stream.Seek(tiffBase + exifIfdOffset, SeekOrigin.Begin);
                    if (stream.Read(countBuf, 0, 2) == 2)
                    {
                        ushort subCount = ReadUInt16(countBuf, 0, isLittleEndian);
                        if (subCount > 0 && subCount <= 2000 && tiffBase + exifIfdOffset + 2 + subCount * 12 <= stream.Length)
                        {
                            byte[] subEntries = new byte[subCount * 12];
                            if (stream.Read(subEntries, 0, subEntries.Length) == subEntries.Length)
                            {
                                for (int i = 0; i < subCount; i++)
                                {
                                    int offset = i * 12;
                                    ushort tag = ReadUInt16(subEntries, offset, isLittleEndian);
                                    ushort type = ReadUInt16(subEntries, offset + 2, isLittleEndian);
                                    uint count = ReadUInt32(subEntries, offset + 4, isLittleEndian);
                                    uint valueOrOffset = ReadUInt32(subEntries, offset + 8, isLittleEndian);

                                    switch (tag)
                                    {
                                        case 0xA434: // LensModel
                                            string? lm = ReadTiffString(stream, tiffBase, type, count, valueOrOffset, subEntries, offset + 8);
                                            if (!string.IsNullOrWhiteSpace(lm)) lens = lm;
                                            break;

                                        case 0xA432: // LensSpecification
                                            if (lens == null)
                                                lens = ReadLensSpecification(stream, tiffBase, type, count, valueOrOffset, isLittleEndian);
                                            break;

                                        case 0x829D: // FNumber
                                            string? fn = ReadRationalFNumber(stream, tiffBase, type, count, valueOrOffset, isLittleEndian);
                                            if (!string.IsNullOrWhiteSpace(fn)) aperture = fn;
                                            break;

                                        case 0x9202: // ApertureValue (APEX fallback)
                                            if (aperture == null)
                                                aperture = ReadApexAperture(stream, tiffBase, type, count, valueOrOffset, isLittleEndian);
                                            break;

                                        case 0x829A: // ExposureTime
                                            string? ss = ReadRationalShutterSpeed(stream, tiffBase, type, count, valueOrOffset, isLittleEndian);
                                            if (!string.IsNullOrWhiteSpace(ss)) shutterSpeed = ss;
                                            break;

                                        case 0x9201: // ShutterSpeedValue (APEX fallback)
                                            if (shutterSpeed == null)
                                                shutterSpeed = ReadApexShutterSpeed(stream, tiffBase, type, count, valueOrOffset, isLittleEndian);
                                            break;

                                        case 0x8827: // PhotographicSensitivity / ISO
                                            string? isoVal = ReadIsoValue(stream, tiffBase, type, count, valueOrOffset, subEntries, offset + 8, isLittleEndian);
                                            if (!string.IsNullOrWhiteSpace(isoVal)) iso = isoVal;
                                            break;

                                        case 0x8833: // RecommendedExposureIndex
                                            if (iso == null)
                                                iso = ReadIsoValue(stream, tiffBase, type, count, valueOrOffset, subEntries, offset + 8, isLittleEndian);
                                            break;

                                        case 0x920A: // FocalLength
                                            string? fl = ReadRationalFocalLength(stream, tiffBase, type, count, valueOrOffset, isLittleEndian);
                                            if (!string.IsNullOrWhiteSpace(fl)) focalLength = fl;
                                            break;

                                        case 0x9003: // DateTimeOriginal
                                            string? dto = ReadTiffString(stream, tiffBase, type, count, valueOrOffset, subEntries, offset + 8);
                                            if (!string.IsNullOrWhiteSpace(dto)) dateTimeOriginal = dto;
                                            break;

                                        case 0x9004: // DateTimeDigitized
                                            if (dateTimeOriginal == null)
                                                dateTimeOriginal = ReadTiffString(stream, tiffBase, type, count, valueOrOffset, subEntries, offset + 8);
                                            break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (lens == null && aperture == null && shutterSpeed == null && iso == null &&
                    focalLength == null && dateTimeOriginal == null && orientation == null)
                {
                    return null;
                }

                return new ExifData(lens, aperture, shutterSpeed, iso, focalLength, dateTimeOriginal, orientation);
            }
            catch
            {
                return null;
            }
        }

        private static string? FormatOrientation(ushort val)
        {
            return val switch
            {
                1 => "Horizontal (normal)",
                2 => "Mirror horizontal",
                3 => "Rotate 180",
                4 => "Mirror vertical",
                5 => "Mirror horizontal and rotate 270 CW",
                6 => "Rotate 90 CW",
                7 => "Mirror horizontal and rotate 90 CW",
                8 => "Rotate 270 CW",
                _ => null
            };
        }

        private static string? ReadTiffString(Stream stream, long tiffBase, ushort type, uint count, uint valueOrOffset, byte[] entries, int inlineOffset)
        {
            if (type != 2 || count == 0 || count > 2048) return null;

            byte[] strBytes = new byte[count];
            if (count <= 4)
            {
                Array.Copy(entries, inlineOffset, strBytes, 0, (int)count);
            }
            else
            {
                if (tiffBase + valueOrOffset + count > stream.Length) return null;
                long savedPos = stream.Position;
                stream.Seek(tiffBase + valueOrOffset, SeekOrigin.Begin);
                int bytesRead = stream.Read(strBytes, 0, (int)count);
                stream.Seek(savedPos, SeekOrigin.Begin);
                if (bytesRead < count) return null;
            }

            string str = Encoding.ASCII.GetString(strBytes).Trim('\0', ' ', '\r', '\n', '\t');
            return string.IsNullOrWhiteSpace(str) ? null : str;
        }

        private static string? ReadRationalShutterSpeed(Stream stream, long tiffBase, ushort type, uint count, uint valueOrOffset, bool isLittleEndian)
        {
            if (type != 5 && type != 10) return null;
            if (tiffBase + valueOrOffset + 8 > stream.Length) return null;

            long savedPos = stream.Position;
            stream.Seek(tiffBase + valueOrOffset, SeekOrigin.Begin);
            byte[] buf = new byte[8];
            int read = stream.Read(buf, 0, 8);
            stream.Seek(savedPos, SeekOrigin.Begin);
            if (read < 8) return null;

            uint num = ReadUInt32(buf, 0, isLittleEndian);
            uint den = ReadUInt32(buf, 4, isLittleEndian);
            return FormatShutterSpeed(num, den);
        }

        private static string? FormatShutterSpeed(uint numerator, uint denominator)
        {
            if (denominator == 0 || numerator == 0) return null;

            if (numerator == 1)
            {
                return $"1/{denominator}";
            }

            if (numerator < denominator)
            {
                double exactDen = (double)denominator / numerator;
                double roundedDen = Math.Round(exactDen);
                if (Math.Abs(exactDen - roundedDen) < 0.05)
                {
                    return $"1/{(long)roundedDen}";
                }
                double sec = (double)numerator / denominator;
                return sec.ToString("0.###", CultureInfo.InvariantCulture);
            }
            else
            {
                if (numerator % denominator == 0)
                {
                    return $"{numerator / denominator}";
                }
                double sec = (double)numerator / denominator;
                return sec.ToString("0.#", CultureInfo.InvariantCulture);
            }
        }

        private static string? ReadApexShutterSpeed(Stream stream, long tiffBase, ushort type, uint count, uint valueOrOffset, bool isLittleEndian)
        {
            if (type != 10 && type != 5) return null;
            if (tiffBase + valueOrOffset + 8 > stream.Length) return null;

            long savedPos = stream.Position;
            stream.Seek(tiffBase + valueOrOffset, SeekOrigin.Begin);
            byte[] buf = new byte[8];
            int read = stream.Read(buf, 0, 8);
            stream.Seek(savedPos, SeekOrigin.Begin);
            if (read < 8) return null;

            int num = (int)ReadUInt32(buf, 0, isLittleEndian);
            int den = (int)ReadUInt32(buf, 4, isLittleEndian);
            if (den == 0) return null;

            double apex = (double)num / den;
            double expTime = Math.Pow(2, -apex);
            if (expTime <= 0) return null;

            if (expTime < 1.0)
            {
                double denApprox = Math.Round(1.0 / expTime);
                return $"1/{(long)denApprox}";
            }
            return expTime.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string? ReadRationalFNumber(Stream stream, long tiffBase, ushort type, uint count, uint valueOrOffset, bool isLittleEndian)
        {
            if (type != 5) return null;
            if (tiffBase + valueOrOffset + 8 > stream.Length) return null;

            long savedPos = stream.Position;
            stream.Seek(tiffBase + valueOrOffset, SeekOrigin.Begin);
            byte[] buf = new byte[8];
            int read = stream.Read(buf, 0, 8);
            stream.Seek(savedPos, SeekOrigin.Begin);
            if (read < 8) return null;

            uint num = ReadUInt32(buf, 0, isLittleEndian);
            uint den = ReadUInt32(buf, 4, isLittleEndian);
            if (den == 0 || num == 0) return null;

            double fn = (double)num / den;
            return fn.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string? ReadApexAperture(Stream stream, long tiffBase, ushort type, uint count, uint valueOrOffset, bool isLittleEndian)
        {
            if (type != 5 && type != 10) return null;
            if (tiffBase + valueOrOffset + 8 > stream.Length) return null;

            long savedPos = stream.Position;
            stream.Seek(tiffBase + valueOrOffset, SeekOrigin.Begin);
            byte[] buf = new byte[8];
            int read = stream.Read(buf, 0, 8);
            stream.Seek(savedPos, SeekOrigin.Begin);
            if (read < 8) return null;

            uint num = ReadUInt32(buf, 0, isLittleEndian);
            uint den = ReadUInt32(buf, 4, isLittleEndian);
            if (den == 0) return null;

            double apex = (double)num / den;
            double fn = Math.Pow(2, apex / 2.0);
            return fn.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string? ReadRationalFocalLength(Stream stream, long tiffBase, ushort type, uint count, uint valueOrOffset, bool isLittleEndian)
        {
            if (type != 5) return null;
            if (tiffBase + valueOrOffset + 8 > stream.Length) return null;

            long savedPos = stream.Position;
            stream.Seek(tiffBase + valueOrOffset, SeekOrigin.Begin);
            byte[] buf = new byte[8];
            int read = stream.Read(buf, 0, 8);
            stream.Seek(savedPos, SeekOrigin.Begin);
            if (read < 8) return null;

            uint num = ReadUInt32(buf, 0, isLittleEndian);
            uint den = ReadUInt32(buf, 4, isLittleEndian);
            if (den == 0 || num == 0) return null;

            double fl = (double)num / den;
            return $"{fl.ToString("0.#", CultureInfo.InvariantCulture)} mm";
        }

        private static string? ReadLensSpecification(Stream stream, long tiffBase, ushort type, uint count, uint valueOrOffset, bool isLittleEndian)
        {
            if (type != 5 || count < 4) return null;
            if (tiffBase + valueOrOffset + 32 > stream.Length) return null;

            long savedPos = stream.Position;
            stream.Seek(tiffBase + valueOrOffset, SeekOrigin.Begin);
            byte[] buf = new byte[32];
            int read = stream.Read(buf, 0, 32);
            stream.Seek(savedPos, SeekOrigin.Begin);
            if (read < 32) return null;

            uint minFocalNum = ReadUInt32(buf, 0, isLittleEndian);
            uint minFocalDen = ReadUInt32(buf, 4, isLittleEndian);
            uint maxFocalNum = ReadUInt32(buf, 8, isLittleEndian);
            uint maxFocalDen = ReadUInt32(buf, 12, isLittleEndian);
            uint minFNum = ReadUInt32(buf, 16, isLittleEndian);
            uint minFDen = ReadUInt32(buf, 20, isLittleEndian);
            uint maxFNum = ReadUInt32(buf, 24, isLittleEndian);
            uint maxFDen = ReadUInt32(buf, 28, isLittleEndian);

            if (minFocalDen == 0 || maxFocalDen == 0) return null;
            double minFocal = (double)minFocalNum / minFocalDen;
            double maxFocal = (double)maxFocalNum / maxFocalDen;
            if (minFocal <= 0) return null;

            string focalStr = Math.Abs(minFocal - maxFocal) < 0.1
                ? $"{minFocal:0.#}mm"
                : $"{minFocal:0.#}-{maxFocal:0.#}mm";

            string apStr = "";
            if (minFDen > 0 && maxFDen > 0)
            {
                double minF = (double)minFNum / minFDen;
                double maxF = (double)maxFNum / maxFDen;
                if (minF > 0)
                {
                    apStr = Math.Abs(minF - maxF) < 0.05
                        ? $" f/{minF:0.#}"
                        : $" f/{minF:0.#}-{maxF:0.#}";
                }
            }

            return $"{focalStr}{apStr}".Trim();
        }

        private static string? ReadIsoValue(Stream stream, long tiffBase, ushort type, uint count, uint valueOrOffset, byte[] entries, int inlineOffset, bool isLittleEndian)
        {
            if (count == 0) return null;

            if (type == 3) // SHORT (16-bit)
            {
                if (count == 1)
                {
                    ushort val = ReadUInt16(entries, inlineOffset, isLittleEndian);
                    if (val > 0) return val.ToString();
                }
                else
                {
                    int totalBytes = (int)count * 2;
                    byte[] buf;
                    if (totalBytes <= 4)
                    {
                        buf = new byte[totalBytes];
                        Array.Copy(entries, inlineOffset, buf, 0, totalBytes);
                    }
                    else
                    {
                        if (tiffBase + valueOrOffset + totalBytes > stream.Length) return null;
                        long savedPos = stream.Position;
                        stream.Seek(tiffBase + valueOrOffset, SeekOrigin.Begin);
                        buf = new byte[totalBytes];
                        int read = stream.Read(buf, 0, totalBytes);
                        stream.Seek(savedPos, SeekOrigin.Begin);
                        if (read < totalBytes) return null;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        ushort val = ReadUInt16(buf, i * 2, isLittleEndian);
                        if (val > 0) return val.ToString();
                    }
                }
            }
            else if (type == 4) // LONG (32-bit)
            {
                if (count == 1)
                {
                    uint val = ReadUInt32(entries, inlineOffset, isLittleEndian);
                    if (val > 0) return val.ToString();
                }
                else
                {
                    int totalBytes = (int)count * 4;
                    if (tiffBase + valueOrOffset + totalBytes > stream.Length) return null;
                    long savedPos = stream.Position;
                    stream.Seek(tiffBase + valueOrOffset, SeekOrigin.Begin);
                    byte[] buf = new byte[totalBytes];
                    int read = stream.Read(buf, 0, totalBytes);
                    stream.Seek(savedPos, SeekOrigin.Begin);
                    if (read < totalBytes) return null;

                    for (int i = 0; i < count; i++)
                    {
                        uint val = ReadUInt32(buf, i * 4, isLittleEndian);
                        if (val > 0) return val.ToString();
                    }
                }
            }

            return null;
        }
    }
}
