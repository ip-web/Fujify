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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Fujify.Services
{
    /// <summary>
    /// Pure C# high-performance TIFF 6.0 / DNG EXIF &amp; XMP reader and editor.
    /// Replaces external tools (like ExifTool) with in-process binary IFD manipulation,
    /// enabling sub-millisecond metadata reading and Fujifilm camera profile injection
    /// without spawning child processes.
    /// </summary>
    public static class DngMetadataEditor
    {
        // TIFF Tag Constants
        public const ushort TagImageDescription = 0x010E;
        public const ushort TagMake = 0x010F;
        public const ushort TagModel = 0x0110;
        public const ushort TagSoftware = 0x0131;
        public const ushort TagDateTime = 0x0132;
        public const ushort TagXmp = 0x02BC; // 700 - ApplicationNotes / Adobe XMP
        public const ushort TagExifIfdPointer = 0x8769; // 34665
        public const ushort TagDngVersion = 0xC612; // 50706
        public const ushort TagDngBackwardVersion = 0xC613; // 50707
        public const ushort TagUniqueCameraModel = 0xC614; // 50708

        // TIFF Type Constants
        public const ushort TypeByte = 1;
        public const ushort TypeAscii = 2;
        public const ushort TypeShort = 3;
        public const ushort TypeLong = 4;
        public const ushort TypeRational = 5;
        public const ushort TypeUndefined = 7;

        public class TiffEntry
        {
            public ushort Tag { get; set; }
            public ushort Type { get; set; }
            public uint Count { get; set; }
            public uint ValueOrOffset { get; set; }

            public int DataSize => (int)(Count * GetTypeSize(Type));
            public bool IsInline => DataSize <= 4;
        }

        public static int GetTypeSize(ushort type) => type switch
        {
            1 => 1, // BYTE
            2 => 1, // ASCII
            3 => 2, // SHORT
            4 => 4, // LONG
            5 => 8, // RATIONAL
            6 => 1, // SBYTE
            7 => 1, // UNDEFINED
            8 => 2, // SSHORT
            9 => 4, // SLONG
            10 => 8, // SRATIONAL
            11 => 4, // FLOAT
            12 => 8, // DOUBLE
            _ => 1
        };

        #region Metadata Reading

        /// <summary>
        /// Reads the camera make and model string from any supported RAW, DNG, RAF, or JPEG file.
        /// </summary>
        public static string? ReadCameraMakeAndModel(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
                if (fs.Length < 16) return null;

                byte[] header = new byte[16];
                int read = fs.Read(header, 0, 16);
                if (read < 16) return null;

                // 1. Fujifilm RAF check: starts with "FUJIFILM"
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

                // 2. Canon CIFF (.CRW) check: "II" at 0..1 and "HEAPCCDR" / "HEAPCCDB" at 6..13
                if (header[0] == 0x49 && header[1] == 0x49 && header.Length >= 14 &&
                    header[6] == 0x48 && header[7] == 0x45 && header[8] == 0x41 && header[9] == 0x50 &&
                    header[10] == 0x43 && header[11] == 0x43 && header[12] == 0x44 &&
                    (header[13] == 0x52 || header[13] == 0x42))
                {
                    string? ciffInfo = ReadCanonCiffCameraInfo(fs);
                    if (!string.IsNullOrEmpty(ciffInfo)) return ciffInfo;
                }

                // 3. Canon CR3 check: starts with ftyp
                if (header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
                {
                    string? cr3Info = ReadCanonCr3CameraInfo(fs);
                    if (!string.IsNullOrEmpty(cr3Info)) return cr3Info;
                }

                // 4. JPEG check: 0xFF 0xD8
                if (header[0] == 0xFF && header[1] == 0xD8)
                {
                    string? jpegInfo = ReadJpegExifCameraInfo(fs);
                    if (!string.IsNullOrEmpty(jpegInfo)) return jpegInfo;
                }

                // 5. Minolta MRW check: starts with \0MRM (0x00, 0x4D, 0x52, 0x4D)
                if (header[0] == 0x00 && header[1] == 0x4D && header[2] == 0x52 && header[3] == 0x4D)
                {
                    string? mrwInfo = ReadMinoltaMrwCameraInfo(fs);
                    if (!string.IsNullOrEmpty(mrwInfo)) return mrwInfo;
                }

                // 6. TIFF-based RAW (DNG, Sony ARW, Nikon NEF, Canon CR2, Olympus ORF, Panasonic RW2, Pentax PEF, etc.)
                bool isLittleEndian;
                if (header[0] == 0x49 && header[1] == 0x49)
                {
                    isLittleEndian = true;
                    ushort magic = (ushort)(header[2] | (header[3] << 8));
                    if (magic != 42 && magic != 85 && magic != 0x4F52 && magic != 0x5352)
                    {
                        return null;
                    }
                }
                else if (header[0] == 0x4D && header[1] == 0x4D)
                {
                    isLittleEndian = false;
                    ushort magic = (ushort)((header[2] << 8) | header[3]);
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

                var entries = ReadIfdEntries(fs, ifdOffset, isLittleEndian);
                string? make = ReadTagString(fs, entries, TagMake, isLittleEndian);
                string? model = ReadTagString(fs, entries, TagModel, isLittleEndian);

                return FormatMakeAndModel(make, model);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reads UniqueCameraModel (Tag 0xC614) from a DNG file.
        /// </summary>
        public static string? ReadUniqueCameraModel(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
                if (!TryReadTiffHeader(fs, out bool isLittleEndian, out uint ifd0Offset)) return null;

                var entries = ReadIfdEntries(fs, ifd0Offset, isLittleEndian);
                return ReadTagString(fs, entries, TagUniqueCameraModel, isLittleEndian);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reads the Adobe XMP packet (Tag 0x02BC) from a DNG file.
        /// </summary>
        public static string? ReadXmpPacket(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
                if (!TryReadTiffHeader(fs, out bool isLittleEndian, out uint ifd0Offset)) return null;

                var entries = ReadIfdEntries(fs, ifd0Offset, isLittleEndian);
                var xmpEntry = entries.FirstOrDefault(e => e.Tag == TagXmp);
                if (xmpEntry == null || xmpEntry.Count == 0) return null;

                byte[] xmpBytes = new byte[xmpEntry.Count];
                fs.Seek(xmpEntry.ValueOrOffset, SeekOrigin.Begin);
                int read = fs.Read(xmpBytes, 0, (int)xmpEntry.Count);
                if (read < xmpBytes.Length) return null;

                return Encoding.UTF8.GetString(xmpBytes);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Metadata Injection & Editing

        /// <summary>
        /// Injects Fujifilm profile simulation metadata (UniqueCameraModel 0xC614 and XMP CameraProfiles 0x02BC)
        /// into the target DNG file.
        /// </summary>
        public static MetadataResult InjectFujifilmMetadata(
            string dngFilePath,
            string? outputFolder = null,
            bool createBackup = false,
            bool isOriginallyDng = false,
            string? customOutputFilePath = null,
            string targetUniqueCameraModel = "Fujifilm X-T5",
            string targetMake = "FUJIFILM",
            string targetModel = "X-T5")
        {
            if (!File.Exists(dngFilePath))
            {
                return new MetadataResult(false, $"Source file does not exist: {dngFilePath}");
            }

            bool useOutputPath = !string.IsNullOrEmpty(outputFolder) || !string.IsNullOrEmpty(customOutputFilePath);
            string targetFilePath;

            if (useOutputPath)
            {
                targetFilePath = !string.IsNullOrEmpty(customOutputFilePath)
                    ? customOutputFilePath
                    : Path.Combine(outputFolder!, Path.GetFileNameWithoutExtension(dngFilePath) + ".dng");

                try
                {
                    string? dir = Path.GetDirectoryName(targetFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (!string.Equals(dngFilePath, targetFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(dngFilePath, targetFilePath, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    return new MetadataResult(false, $"Failed to prepare output file: {ex.Message}");
                }
            }
            else
            {
                targetFilePath = dngFilePath;

                if (isOriginallyDng && createBackup)
                {
                    string backupPath = Path.Combine(Path.GetDirectoryName(dngFilePath) ?? string.Empty, $"{Path.GetFileNameWithoutExtension(dngFilePath)}ORIG.DNG");
                    if (!File.Exists(backupPath))
                    {
                        try
                        {
                            File.Copy(dngFilePath, backupPath, overwrite: false);
                        }
                        catch (Exception ex)
                        {
                            return new MetadataResult(false, $"Failed to create backup file: {ex.Message}");
                        }
                    }
                }
            }

            try
            {
                using var fs = new FileStream(targetFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                if (!TryReadTiffHeader(fs, out bool isLittleEndian, out uint ifd0Offset))
                {
                    return new MetadataResult(false, "Invalid TIFF/DNG header");
                }

                // 1. Read existing IFD0 entries
                uint nextIfdOffset;
                var entries = ReadIfdEntriesWithNext(fs, ifd0Offset, isLittleEndian, out nextIfdOffset);

                // 2. Read existing XMP packet if present
                string? existingXmp = null;
                var existingXmpEntry = entries.FirstOrDefault(e => e.Tag == TagXmp);
                if (existingXmpEntry != null && existingXmpEntry.Count > 0)
                {
                    byte[] oldXmpBytes = new byte[existingXmpEntry.Count];
                    fs.Seek(existingXmpEntry.ValueOrOffset, SeekOrigin.Begin);
                    int xmpRead = fs.Read(oldXmpBytes, 0, (int)existingXmpEntry.Count);
                    if (xmpRead == existingXmpEntry.Count)
                    {
                        existingXmp = Encoding.UTF8.GetString(oldXmpBytes);
                    }
                }

                // 3. Prepare updated XMP and UniqueCameraModel payloads
                string newXmpString = MergeCameraProfilesIntoXmp(existingXmp, targetMake, targetModel, targetUniqueCameraModel);
                byte[] newXmpBytes = Encoding.UTF8.GetBytes(newXmpString);

                string modelStr = targetUniqueCameraModel + "\0";
                byte[] modelBytes = Encoding.ASCII.GetBytes(modelStr);

                // 4. Seek to end of file to append new tag payloads
                long appendPos = fs.Length;
                // Align to 4 bytes
                appendPos = (appendPos + 3) & ~3;
                fs.Seek(appendPos, SeekOrigin.Begin);

                // Write UniqueCameraModel payload
                uint modelPayloadOffset = (uint)fs.Position;
                fs.Write(modelBytes, 0, modelBytes.Length);

                // Align to 4 bytes
                AlignStream4(fs);

                // Write XMP payload
                uint xmpPayloadOffset = (uint)fs.Position;
                fs.Write(newXmpBytes, 0, newXmpBytes.Length);

                // Align to 4 bytes
                AlignStream4(fs);

                // 5. Build new IFD0 entry list:
                // Remove existing UniqueCameraModel and Xmp entries
                entries.RemoveAll(e => e.Tag == TagUniqueCameraModel || e.Tag == TagXmp);

                // Add updated UniqueCameraModel entry
                entries.Add(new TiffEntry
                {
                    Tag = TagUniqueCameraModel,
                    Type = TypeAscii,
                    Count = (uint)modelBytes.Length,
                    ValueOrOffset = modelPayloadOffset
                });

                // Add updated XMP entry
                entries.Add(new TiffEntry
                {
                    Tag = TagXmp,
                    Type = TypeByte,
                    Count = (uint)newXmpBytes.Length,
                    ValueOrOffset = xmpPayloadOffset
                });

                // TIFF 6.0 requirement: IFD entries MUST be sorted by Tag ID ascending
                entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));

                // 6. Write new IFD0 table at end of file
                uint newIfd0Offset = (uint)fs.Position;
                WriteUInt16(fs, (ushort)entries.Count, isLittleEndian);

                foreach (var entry in entries)
                {
                    WriteUInt16(fs, entry.Tag, isLittleEndian);
                    WriteUInt16(fs, entry.Type, isLittleEndian);
                    WriteUInt32(fs, entry.Count, isLittleEndian);
                    WriteUInt32(fs, entry.ValueOrOffset, isLittleEndian);
                }

                // Write next IFD offset (preserves original chain)
                WriteUInt32(fs, nextIfdOffset, isLittleEndian);

                // 7. Update the IFD0 pointer in the TIFF header (offset 4)
                fs.Seek(4, SeekOrigin.Begin);
                WriteUInt32(fs, newIfd0Offset, isLittleEndian);

                fs.Flush();
            }
            catch (Exception ex)
            {
                return new MetadataResult(false, $"Failed to inject metadata: {ex.Message}");
            }

            // Cleanup intermediate DNG if it was converted from another RAW format and output path was used
            if (useOutputPath && !isOriginallyDng && !string.Equals(dngFilePath, targetFilePath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(dngFilePath))
                    {
                        File.Delete(dngFilePath);
                    }
                }
                catch { }
            }

            return new MetadataResult(true, null);
        }

        /// <summary>
        /// Injects Fujifilm profile simulation metadata asynchronously.
        /// </summary>
        public static Task<MetadataResult> InjectFujifilmMetadataAsync(
            string dngFilePath,
            string? outputFolder = null,
            bool createBackup = false,
            bool isOriginallyDng = false,
            string? customOutputFilePath = null,
            string targetUniqueCameraModel = "Fujifilm X-T5",
            string targetMake = "FUJIFILM",
            string targetModel = "X-T5",
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return InjectFujifilmMetadata(
                    dngFilePath,
                    outputFolder,
                    createBackup,
                    isOriginallyDng,
                    customOutputFilePath,
                    targetUniqueCameraModel,
                    targetMake,
                    targetModel);
            }, cancellationToken);
        }

        #endregion

        #region Helper Methods

        private static bool TryReadTiffHeader(Stream stream, out bool isLittleEndian, out uint ifdOffset)
        {
            isLittleEndian = true;
            ifdOffset = 0;

            if (stream.Length < 8) return false;

            byte[] header = new byte[8];
            stream.Seek(0, SeekOrigin.Begin);
            if (stream.Read(header, 0, 8) < 8) return false;

            if (header[0] == 0x49 && header[1] == 0x49)
            {
                isLittleEndian = true;
                ushort magic = (ushort)(header[2] | (header[3] << 8));
                if (magic != 42 && magic != 85 && magic != 0x4F52 && magic != 0x5352) return false;
            }
            else if (header[0] == 0x4D && header[1] == 0x4D)
            {
                isLittleEndian = false;
                ushort magic = (ushort)((header[2] << 8) | header[3]);
                if (magic != 42 && magic != 0x4F52 && magic != 0x524F) return false;
            }
            else
            {
                return false;
            }

            ifdOffset = ReadUInt32(header, 4, isLittleEndian);
            return ifdOffset > 0 && ifdOffset + 2 <= stream.Length;
        }

        private static List<TiffEntry> ReadIfdEntries(Stream stream, uint ifdOffset, bool isLittleEndian)
        {
            return ReadIfdEntriesWithNext(stream, ifdOffset, isLittleEndian, out _);
        }

        private static List<TiffEntry> ReadIfdEntriesWithNext(Stream stream, uint ifdOffset, bool isLittleEndian, out uint nextIfdOffset)
        {
            nextIfdOffset = 0;
            var list = new List<TiffEntry>();

            stream.Seek(ifdOffset, SeekOrigin.Begin);
            byte[] countBuf = new byte[2];
            if (stream.Read(countBuf, 0, 2) < 2) return list;

            ushort count = ReadUInt16(countBuf, 0, isLittleEndian);
            if (count > 2000) return list; // Safety limit

            byte[] entriesBuf = new byte[count * 12];
            int read = stream.Read(entriesBuf, 0, entriesBuf.Length);
            if (read < entriesBuf.Length) return list;

            for (int i = 0; i < count; i++)
            {
                int offset = i * 12;
                list.Add(new TiffEntry
                {
                    Tag = ReadUInt16(entriesBuf, offset, isLittleEndian),
                    Type = ReadUInt16(entriesBuf, offset + 2, isLittleEndian),
                    Count = ReadUInt32(entriesBuf, offset + 4, isLittleEndian),
                    ValueOrOffset = ReadUInt32(entriesBuf, offset + 8, isLittleEndian)
                });
            }

            byte[] nextBuf = new byte[4];
            if (stream.Read(nextBuf, 0, 4) == 4)
            {
                nextIfdOffset = ReadUInt32(nextBuf, 0, isLittleEndian);
            }

            return list;
        }

        private static string? ReadTagString(Stream stream, List<TiffEntry> entries, ushort tag, bool isLittleEndian, long baseOffset = 0)
        {
            var entry = entries.FirstOrDefault(e => e.Tag == tag);
            if (entry == null || entry.Count == 0 || entry.Count > 1024) return null;

            byte[] bytes = new byte[entry.Count];
            if (entry.IsInline)
            {
                byte[] inlineBytes = BitConverter.GetBytes(entry.ValueOrOffset);
                if (!isLittleEndian && BitConverter.IsLittleEndian)
                {
                    Array.Reverse(inlineBytes);
                }
                Array.Copy(inlineBytes, 0, bytes, 0, Math.Min(bytes.Length, 4));
            }
            else
            {
                long targetOffset = baseOffset + entry.ValueOrOffset;
                if (targetOffset < 0 || targetOffset + entry.Count > stream.Length) return null;
                long saved = stream.Position;
                stream.Seek(targetOffset, SeekOrigin.Begin);
                int read = stream.Read(bytes, 0, (int)entry.Count);
                stream.Seek(saved, SeekOrigin.Begin);
                if (read < entry.Count) return null;
            }

            return Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
        }

        private static string? FormatMakeAndModel(string? make, string? model)
        {
            if (string.IsNullOrWhiteSpace(make) && string.IsNullOrWhiteSpace(model))
                return null;

            if (make != null)
            {
                if (make.Equals("NIKON CORPORATION", StringComparison.OrdinalIgnoreCase))
                    make = "NIKON";
                else if (make.Equals("Minolta Co., Ltd.", StringComparison.OrdinalIgnoreCase))
                    make = "MINOLTA";
                else if (make.Equals("Konica Minolta Camera, Inc.", StringComparison.OrdinalIgnoreCase))
                    make = "KONICA MINOLTA";
                else if (make.Equals("PENTAX Corporation", StringComparison.OrdinalIgnoreCase))
                    make = "PENTAX";
            }

            if (make != null && model != null && model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
            {
                return model.Trim();
            }

            return $"{make} {model}".Trim();
        }

        private static void AlignStream4(Stream stream)
        {
            while ((stream.Position % 4) != 0)
            {
                stream.WriteByte(0);
            }
        }

        private static void WriteUInt16(Stream stream, ushort value, bool isLittleEndian)
        {
            if (isLittleEndian)
            {
                stream.WriteByte((byte)(value & 0xFF));
                stream.WriteByte((byte)((value >> 8) & 0xFF));
            }
            else
            {
                stream.WriteByte((byte)((value >> 8) & 0xFF));
                stream.WriteByte((byte)(value & 0xFF));
            }
        }

        private static void WriteUInt32(Stream stream, uint value, bool isLittleEndian)
        {
            if (isLittleEndian)
            {
                stream.WriteByte((byte)(value & 0xFF));
                stream.WriteByte((byte)((value >> 8) & 0xFF));
                stream.WriteByte((byte)((value >> 16) & 0xFF));
                stream.WriteByte((byte)((value >> 24) & 0xFF));
            }
            else
            {
                stream.WriteByte((byte)((value >> 24) & 0xFF));
                stream.WriteByte((byte)((value >> 16) & 0xFF));
                stream.WriteByte((byte)((value >> 8) & 0xFF));
                stream.WriteByte((byte)(value & 0xFF));
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

        private static string MergeCameraProfilesIntoXmp(string? existingXmp, string make, string model, string uniqueModel)
        {
            if (string.IsNullOrWhiteSpace(existingXmp))
            {
                return BuildDefaultXmp(make, model, uniqueModel);
            }

            try
            {
                int xmpStart = existingXmp.IndexOf("<x:xmpmeta", StringComparison.OrdinalIgnoreCase);
                int xmpEnd = existingXmp.LastIndexOf("</x:xmpmeta>", StringComparison.OrdinalIgnoreCase);

                if (xmpStart < 0 || xmpEnd <= xmpStart)
                {
                    return BuildDefaultXmp(make, model, uniqueModel);
                }

                string xmlContent = existingXmp.Substring(xmpStart, xmpEnd + "</x:xmpmeta>".Length - xmpStart);
                var doc = XDocument.Parse(xmlContent);

                XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
                XNamespace photoshop = "http://ns.adobe.com/photoshop/1.0/";
                XNamespace stCamera = "http://ns.adobe.com/photoshop/1.0/camera-profile";

                var rdfElem = doc.Descendants(rdf + "RDF").FirstOrDefault();
                if (rdfElem == null)
                {
                    return BuildDefaultXmp(make, model, uniqueModel);
                }

                var descElem = rdfElem.Elements(rdf + "Description").FirstOrDefault();
                if (descElem == null)
                {
                    descElem = new XElement(rdf + "Description", new XAttribute(rdf + "about", ""));
                    rdfElem.Add(descElem);
                }

                // Remove existing photoshop:CameraProfiles
                descElem.Elements(photoshop + "CameraProfiles").Remove();

                // Add updated CameraProfiles
                var profilesElem = new XElement(photoshop + "CameraProfiles",
                    new XElement(rdf + "Seq",
                        new XElement(rdf + "li",
                            new XAttribute(rdf + "parseType", "Resource"),
                            new XElement(stCamera + "CameraRawProfile", "True"),
                            new XElement(stCamera + "Make", make),
                            new XElement(stCamera + "Model", model),
                            new XElement(stCamera + "UniqueCameraModel", uniqueModel)
                        )
                    )
                );

                descElem.Add(profilesElem);

                string header = "<?xpacket begin='\uFEFF' id='W5M0MpCehiHzreSzNTczkc9d'?>\n";
                string trailer = "\n<?xpacket end='w'?>";
                return $"{header}{doc.ToString(SaveOptions.OmitDuplicateNamespaces)}{trailer}";
            }
            catch
            {
                return BuildDefaultXmp(make, model, uniqueModel);
            }
        }

        private static string BuildDefaultXmp(string make, string model, string uniqueModel)
        {
            return $@"<?xpacket begin='﻿' id='W5M0MpCehiHzreSzNTczkc9d'?>
<x:xmpmeta xmlns:x='adobe:ns:meta/' x:xmptk='Fujify Native'>
<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>
 <rdf:Description rdf:about=''
  xmlns:photoshop='http://ns.adobe.com/photoshop/1.0/'
  xmlns:stCamera='http://ns.adobe.com/photoshop/1.0/camera-profile'>
  <photoshop:CameraProfiles>
   <rdf:Seq>
    <rdf:li rdf:parseType='Resource'>
     <stCamera:CameraRawProfile>True</stCamera:CameraRawProfile>
     <stCamera:Make>{make}</stCamera:Make>
     <stCamera:Model>{model}</stCamera:Model>
     <stCamera:UniqueCameraModel>{uniqueModel}</stCamera:UniqueCameraModel>
    </rdf:li>
   </rdf:Seq>
  </photoshop:CameraProfiles>
 </rdf:Description>
</rdf:RDF>
</x:xmpmeta>
<?xpacket end='w'?>";
        }

        public static string? ReadCanonCr3CameraInfo(Stream stream)
        {
            try
            {
                // 1. Box traversal: locate CMT1 box
                long cmt1Offset = FindCanonCr3Cmt1Offset(stream);
                if (cmt1Offset > 0)
                {
                    string? res = ReadTiffInfoAt(stream, cmt1Offset);
                    if (!string.IsNullOrEmpty(res) && !res.Equals("CCDT", StringComparison.OrdinalIgnoreCase))
                    {
                        return res;
                    }
                }

                // 2. Buffer scan: scan first 512KB for "CMT1"
                int scanLen = (int)Math.Min(stream.Length, 524288);
                byte[] buf = new byte[scanLen];
                stream.Seek(0, SeekOrigin.Begin);
                int read = stream.Read(buf, 0, scanLen);

                for (int i = 0; i < read - 12; i++)
                {
                    if (buf[i] == 0x43 && buf[i + 1] == 0x4D && buf[i + 2] == 0x54 && buf[i + 3] == 0x31) // "CMT1"
                    {
                        long tiffOffset = i + 4;
                        string? res = ReadTiffInfoAt(stream, tiffOffset);
                        if (!string.IsNullOrEmpty(res) && !res.Equals("CCDT", StringComparison.OrdinalIgnoreCase))
                        {
                            return res;
                        }
                    }
                }

                // 3. Fallback buffer scan: standard TIFF header (II*\0 or MM\0*)
                for (int i = 0; i < read - 16; i++)
                {
                    if ((buf[i] == 0x49 && buf[i + 1] == 0x49 && buf[i + 2] == 0x2A && buf[i + 3] == 0x00) ||
                        (buf[i] == 0x4D && buf[i + 1] == 0x4D && buf[i + 2] == 0x00 && buf[i + 3] == 0x2A))
                    {
                        string? res = ReadTiffInfoAt(stream, i);
                        if (!string.IsNullOrEmpty(res) && !res.Equals("CCDT", StringComparison.OrdinalIgnoreCase))
                        {
                            return res;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static long FindCanonCr3Cmt1Offset(Stream stream)
        {
            try
            {
                long length = Math.Min(stream.Length, 15 * 1024 * 1024);
                byte[] hdr = new byte[8];
                stream.Seek(0, SeekOrigin.Begin);

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
                                    // Canon metadata UUID starts with 85 c0 b6 87
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
                                                return stream.Position;
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
            }
            catch { }
            return -1;
        }

        private static string? ReadTiffInfoAt(Stream stream, long tiffOffset)
        {
            try
            {
                if (tiffOffset <= 0 || tiffOffset + 8 > stream.Length) return null;
                stream.Seek(tiffOffset, SeekOrigin.Begin);
                byte[] header = new byte[8];
                if (stream.Read(header, 0, 8) < 8) return null;

                bool isLittle;
                if (header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
                {
                    isLittle = true;
                }
                else if (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)
                {
                    isLittle = false;
                }
                else
                {
                    return null;
                }

                uint relIfd = ReadUInt32(header, 4, isLittle);
                long absIfd = tiffOffset + relIfd;
                if (absIfd <= 0 || absIfd + 2 >= stream.Length) return null;

                var entries = ReadIfdEntries(stream, (uint)absIfd, isLittle);
                string? make = ReadTagString(stream, entries, TagMake, isLittle, tiffOffset);
                string? model = ReadTagString(stream, entries, TagModel, isLittle, tiffOffset);
                return FormatMakeAndModel(make, model);
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadJpegExifCameraInfo(Stream stream)
        {
            try
            {
                byte[] markerBuf = new byte[4];
                stream.Seek(2, SeekOrigin.Begin); // Skip 0xFF 0xD8

                while (stream.Position < stream.Length - 4)
                {
                    if (stream.Read(markerBuf, 0, 4) < 4) break;
                    if (markerBuf[0] != 0xFF) break;

                    byte marker = markerBuf[1];
                    int len = (markerBuf[2] << 8) | markerBuf[3];
                    if (len < 2) break;

                    // APP1 marker = 0xFF 0xE1
                    if (marker == 0xE1)
                    {
                        byte[] exifId = new byte[6];
                        if (stream.Read(exifId, 0, 6) == 6 &&
                            exifId[0] == 'E' && exifId[1] == 'x' && exifId[2] == 'i' && exifId[3] == 'f' &&
                            exifId[4] == 0 && exifId[5] == 0)
                        {
                            long tiffBase = stream.Position;
                            if (TryReadTiffHeader(stream, out bool isLittle, out uint ifdOffset))
                            {
                                var entries = ReadIfdEntries(stream, (uint)(tiffBase + ifdOffset), isLittle);
                                string? make = ReadTagString(stream, entries, TagMake, isLittle, tiffBase);
                                string? model = ReadTagString(stream, entries, TagModel, isLittle, tiffBase);
                                return FormatMakeAndModel(make, model);
                            }
                        }
                        break;
                    }
                    else if (marker == 0xDA) // Start of Scan (SOS) - stop reading headers
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

        internal static string? ReadCanonCiffCameraInfo(Stream stream)
        {
            try
            {
                long flen = stream.Length;
                if (flen < 30) return null;

                stream.Seek(flen - 4, SeekOrigin.Begin);
                byte[] dirOffsetBuf = new byte[4];
                if (stream.Read(dirOffsetBuf, 0, 4) < 4) return null;

                uint dirOffset = (uint)(dirOffsetBuf[0] | (dirOffsetBuf[1] << 8) | (dirOffsetBuf[2] << 16) | (dirOffsetBuf[3] << 24));
                return ParseCiffDirectory(stream, 26, dirOffset, (uint)(flen - 26), 0);
            }
            catch
            {
                return null;
            }
        }

        private static string? ParseCiffDirectory(Stream stream, long blockBase, uint dirOffset, uint blockLen, int depth)
        {
            if (depth > 10 || dirOffset + 2 > blockLen) return null;

            stream.Seek(blockBase + dirOffset, SeekOrigin.Begin);
            byte[] countBuf = new byte[2];
            if (stream.Read(countBuf, 0, 2) < 2) return null;

            ushort count = (ushort)(countBuf[0] | (countBuf[1] << 8));
            if (count > 256) return null;

            byte[] entriesBuf = new byte[count * 10];
            if (stream.Read(entriesBuf, 0, entriesBuf.Length) < entriesBuf.Length) return null;

            for (int i = 0; i < count; i++)
            {
                int entryOff = i * 10;
                ushort tag = (ushort)(entriesBuf[entryOff] | (entriesBuf[entryOff + 1] << 8));
                uint length = (uint)(entriesBuf[entryOff + 2] | (entriesBuf[entryOff + 3] << 8) | (entriesBuf[entryOff + 4] << 16) | (entriesBuf[entryOff + 5] << 24));
                uint offset = (uint)(entriesBuf[entryOff + 6] | (entriesBuf[entryOff + 7] << 8) | (entriesBuf[entryOff + 8] << 16) | (entriesBuf[entryOff + 9] << 24));

                ushort tagId = (ushort)(tag & 0x3FFF);
                ushort tagType = (ushort)(tag & 0x3800);

                if (tagId == 0x080A) // CanonRawMakeModel
                {
                    if (offset + length <= blockLen)
                    {
                        stream.Seek(blockBase + offset, SeekOrigin.Begin);
                        byte[] valBuf = new byte[length];
                        int read = stream.Read(valBuf, 0, (int)length);
                        if (read > 0)
                        {
                            string rawText = Encoding.ASCII.GetString(valBuf, 0, read);
                            string[] parts = rawText.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                string make = parts[0].Trim();
                                string model = parts[1].Trim();
                                if (model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
                                {
                                    return model;
                                }
                                return $"{make} {model}".Trim();
                            }
                            else if (parts.Length == 1)
                            {
                                return parts[0].Trim();
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
                            string? subResult = ParseCiffDirectory(stream, subBase, subDirOffset, length, depth + 1);
                            if (subResult != null) return subResult;
                        }
                    }
                }
            }

            return null;
        }

        internal static long? FindMinoltaTtwBase(Stream stream)
        {
            try
            {
                if (stream.Length < 16) return null;
                long origPos = stream.Position;
                stream.Seek(0, SeekOrigin.Begin);
                byte[] header = new byte[8];
                if (stream.Read(header, 0, 8) < 8) return null;

                // Check for \0MRM (0x00, 0x4D, 0x52, 0x4D)
                if (header[0] != 0x00 || header[1] != 0x4D || header[2] != 0x52 || header[3] != 0x4D)
                    return null;

                uint dataOffset = (uint)((header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7]);
                long headerEnd = Math.Min(dataOffset + 8, stream.Length);

                byte[] blockHdr = new byte[8];
                while (stream.Position + 8 <= headerEnd)
                {
                    if (stream.Read(blockHdr, 0, 8) < 8) break;
                    uint blockLen = (uint)((blockHdr[4] << 24) | (blockHdr[5] << 16) | (blockHdr[6] << 8) | blockHdr[7]);

                    // \0TTW (0x00, 0x54, 0x54, 0x57)
                    if (blockHdr[0] == 0x00 && blockHdr[1] == 0x54 && blockHdr[2] == 0x54 && blockHdr[3] == 0x57)
                    {
                        return stream.Position;
                    }

                    if (blockLen == 0 || stream.Position + blockLen > stream.Length) break;
                    stream.Seek(blockLen, SeekOrigin.Current);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        internal static string? ReadMinoltaMrwCameraInfo(Stream stream)
        {
            try
            {
                long? ttwBaseOpt = FindMinoltaTtwBase(stream);
                if (!ttwBaseOpt.HasValue) return null;
                long ttwBase = ttwBaseOpt.Value;

                if (ttwBase + 8 > stream.Length) return null;
                stream.Seek(ttwBase, SeekOrigin.Begin);
                byte[] tiffHdr = new byte[8];
                if (stream.Read(tiffHdr, 0, 8) < 8) return null;

                bool isLittleEndian;
                if (tiffHdr[0] == 0x49 && tiffHdr[1] == 0x49) isLittleEndian = true;
                else if (tiffHdr[0] == 0x4D && tiffHdr[1] == 0x4D) isLittleEndian = false;
                else return null;

                uint ifd0Offset = isLittleEndian
                    ? (uint)(tiffHdr[4] | (tiffHdr[5] << 8) | (tiffHdr[6] << 16) | (tiffHdr[7] << 24))
                    : (uint)((tiffHdr[4] << 24) | (tiffHdr[5] << 16) | (tiffHdr[6] << 8) | tiffHdr[7]);

                if (ifd0Offset == 0 || ttwBase + ifd0Offset + 2 > stream.Length) return null;
                stream.Seek(ttwBase + ifd0Offset, SeekOrigin.Begin);

                byte[] countBuf = new byte[2];
                if (stream.Read(countBuf, 0, 2) < 2) return null;
                ushort count = isLittleEndian
                    ? (ushort)(countBuf[0] | (countBuf[1] << 8))
                    : (ushort)((countBuf[0] << 8) | countBuf[1]);

                if (count == 0 || count > 500) return null;
                byte[] entriesBuf = new byte[count * 12];
                if (stream.Read(entriesBuf, 0, entriesBuf.Length) < entriesBuf.Length) return null;

                string? make = null;
                string? model = null;

                for (int i = 0; i < count; i++)
                {
                    int off = i * 12;
                    ushort tag = isLittleEndian
                        ? (ushort)(entriesBuf[off] | (entriesBuf[off + 1] << 8))
                        : (ushort)((entriesBuf[off] << 8) | entriesBuf[off + 1]);
                    uint len = isLittleEndian
                        ? (uint)(entriesBuf[off + 4] | (entriesBuf[off + 5] << 8) | (entriesBuf[off + 6] << 16) | (entriesBuf[off + 7] << 24))
                        : (uint)((entriesBuf[off + 4] << 24) | (entriesBuf[off + 5] << 16) | (entriesBuf[off + 6] << 8) | entriesBuf[off + 7]);
                    uint valOrOff = isLittleEndian
                        ? (uint)(entriesBuf[off + 8] | (entriesBuf[off + 9] << 8) | (entriesBuf[off + 10] << 16) | (entriesBuf[off + 11] << 24))
                        : (uint)((entriesBuf[off + 8] << 24) | (entriesBuf[off + 9] << 16) | (entriesBuf[off + 10] << 8) | entriesBuf[off + 11]);

                    if (tag == TagMake && len > 0 && len < 256 && ttwBase + valOrOff + len <= stream.Length)
                    {
                        stream.Seek(ttwBase + valOrOff, SeekOrigin.Begin);
                        byte[] strBuf = new byte[len];
                        if (stream.Read(strBuf, 0, (int)len) == len)
                        {
                            make = Encoding.ASCII.GetString(strBuf).Trim('\0', ' ');
                        }
                    }
                    else if (tag == TagModel && len > 0 && len < 256 && ttwBase + valOrOff + len <= stream.Length)
                    {
                        stream.Seek(ttwBase + valOrOff, SeekOrigin.Begin);
                        byte[] strBuf = new byte[len];
                        if (stream.Read(strBuf, 0, (int)len) == len)
                        {
                            model = Encoding.ASCII.GetString(strBuf).Trim('\0', ' ');
                        }
                    }
                }

                return FormatMakeAndModel(make, model);
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
