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
using System.Text;

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

                // Check for Fujifilm RAF: starts with "FUJIFILM"
                if (header[0] == 0x46 && header[1] == 0x55 && header[2] == 0x4A && header[3] == 0x49 &&
                    header[4] == 0x46 && header[5] == 0x49 && header[6] == 0x4C && header[7] == 0x4D)
                {
                    if (fs.Length >= 92)
                    {
                        fs.Seek(0x3C, SeekOrigin.Begin);
                        byte[] modelBytes = new byte[32];
                        int modelRead = fs.Read(modelBytes, 0, 32);
                        if (modelRead > 0)
                        {
                            string rafModel = Encoding.ASCII.GetString(modelBytes, 0, modelRead).Trim('\0', ' ');
                            return string.IsNullOrEmpty(rafModel) ? "FUJIFILM" : $"FUJIFILM {rafModel}".Trim();
                        }
                    }
                    return "FUJIFILM";
                }

                // Check for TIFF-based RAW (DNG, ARW, CR2, NEF, ORF, PEF, etc.)
                bool isLittleEndian;
                if (header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
                {
                    isLittleEndian = true;
                }
                else if (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)
                {
                    isLittleEndian = false;
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

                // 3. TIFF-based RAW (DNG, ARW, CR2, NEF, ORF, PEF, RW2, etc.)
                bool isLittleEndian;
                if (header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
                {
                    isLittleEndian = true;
                }
                else if (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)
                {
                    isLittleEndian = false;
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
    }
}
