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
