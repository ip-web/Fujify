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

namespace Fujify.Services
{
    /// <summary>
    /// Pure C# cross-platform Fujifilm RAF metadata modifier and injector.
    /// Modifies the RAF header (offset 0x1C) and embedded JPEG EXIF APP1 segment
    /// entirely in-process without any external process dependencies (no ExifTool or Perl),
    /// making it fully functional on Windows, macOS, Linux, Android, and iOS.
    /// </summary>
    public static class RafMetadataModifier
    {
        public class RawExifInfo
        {
            public (uint Num, uint Den)? ExposureTime { get; set; }
            public (uint Num, uint Den)? FNumber { get; set; }
            public ushort? Iso { get; set; }
            public (uint Num, uint Den)? FocalLength { get; set; }
            public ushort? FocalLengthIn35mmFormat { get; set; }
            public string? DateTimeOriginal { get; set; }
            public string? DateTimeDigitized { get; set; }
            public string? LensModel { get; set; }
            public string? LensMake { get; set; }
            public string? LensSerialNumber { get; set; }
            public ((uint Num, uint Den) MinFocal, (uint Num, uint Den) MaxFocal, (uint Num, uint Den)? MinFNum, (uint Num, uint Den)? MaxFNum)? LensSpecification { get; set; }
            public (uint Num, uint Den)? MaxApertureValue { get; set; }
            public ushort? Orientation { get; set; }
            public string? Make { get; set; }
            public string? Model { get; set; }
        }

        /// <summary>
        /// Injects shooting metadata (shutter speed, aperture, ISO, focal length, lens, date taken, orientation)
        /// from the source RAW file into the repacked RAF file, and updates the model to the target Fujifilm model.
        /// </summary>
        public static bool InjectMetadata(string rafFilePath, string sourceRawFilePath, string targetModel = "GFX50S II")
        {
            if (!File.Exists(rafFilePath) || !File.Exists(sourceRawFilePath))
            {
                return false;
            }

            var exif = ExtractExifInfo(sourceRawFilePath);
            return ApplyMetadataToRaf(rafFilePath, exif, targetModel);
        }

        /// <summary>
        /// Updates only the camera model in the RAF header and embedded EXIF IFD0,
        /// preserving all original shooting metadata of the file.
        /// Used for native Fujifilm RAF files in Capture One mode.
        /// </summary>
        public static bool PatchModelOnly(string rafFilePath, string targetModel)
        {
            if (!File.Exists(rafFilePath))
            {
                return false;
            }

            return ApplyMetadataToRaf(rafFilePath, new RawExifInfo(), targetModel, patchModelOnly: true);
        }

        /// <summary>
        /// Extracts EXIF metadata directly from the source RAW file.
        /// Tries direct TIFF parsing first for exact rational precision,
        /// with fallback to RawMetadataReader.TryGetExifData for formats like CR3.
        /// </summary>
        public static RawExifInfo ExtractExifInfo(string sourceFilePath)
        {
            var info = new RawExifInfo();

            try
            {
                // 1. If source is a TIFF-based RAW (ARW, CR2, NEF, ORF, PEF, DNG, etc.), read exact rationals
                TryExtractFromTiffStream(sourceFilePath, info);
            }
            catch { }

            // 2. If any fields are still missing, fall back to RawMetadataReader
            try
            {
                var directExif = RawMetadataReader.TryGetExifData(sourceFilePath);
                if (directExif != null)
                {
                    if (!info.ExposureTime.HasValue && TryParseRational(directExif.ShutterSpeed, out uint ssNum, out uint ssDen))
                    {
                        info.ExposureTime = (ssNum, ssDen);
                    }

                    if (!info.FNumber.HasValue && TryParseFNumber(directExif.Aperture, out uint fnNum, out uint fnDen))
                    {
                        info.FNumber = (fnNum, fnDen);
                    }

                    if (!info.Iso.HasValue && TryParseIso(directExif.Iso, out ushort isoVal))
                    {
                        info.Iso = isoVal;
                    }

                    if (!info.FocalLength.HasValue && TryParseFocalLength(directExif.FocalLength, out uint flNum, out uint flDen))
                    {
                        info.FocalLength = (flNum, flDen);
                    }

                    if (string.IsNullOrWhiteSpace(info.DateTimeOriginal) && !string.IsNullOrWhiteSpace(directExif.DateTimeOriginal))
                    {
                        info.DateTimeOriginal = directExif.DateTimeOriginal;
                    }

                    if (string.IsNullOrWhiteSpace(info.DateTimeDigitized) && !string.IsNullOrWhiteSpace(directExif.DateTimeOriginal))
                    {
                        info.DateTimeDigitized = directExif.DateTimeOriginal;
                    }

                    if (string.IsNullOrWhiteSpace(info.LensModel) && !string.IsNullOrWhiteSpace(directExif.Lens))
                    {
                        info.LensModel = directExif.Lens;
                    }

                    if (!info.Orientation.HasValue && !string.IsNullOrWhiteSpace(directExif.Orientation))
                    {
                        info.Orientation = ParseOrientation(directExif.Orientation);
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(info.Model))
            {
                try
                {
                    string? makeModel = RawMetadataReader.TryGetCameraMakeAndModel(sourceFilePath);
                    if (!string.IsNullOrWhiteSpace(makeModel))
                    {
                        info.Model = makeModel;
                    }
                }
                catch { }
            }

            // 3. Resolve fixed-lens camera models or synthetic lens descriptors if missing
            if (string.IsNullOrWhiteSpace(info.LensModel))
            {
                info.LensModel = ResolveFixedLensModel(info.Make, info.Model, info.FocalLength, info.FNumber);
            }

            if (string.IsNullOrWhiteSpace(info.LensMake) && !string.IsNullOrWhiteSpace(info.LensModel))
            {
                info.LensMake = GetLensMake(info.Make, info.LensModel);
            }

            var resolvedSpec = ResolveLensSpecification(info);
            if (!info.LensSpecification.HasValue && resolvedSpec.MinFocal > 0)
            {
                info.LensSpecification = (
                    ToRational(resolvedSpec.MinFocal),
                    ToRational(resolvedSpec.MaxFocal),
                    ToRational(resolvedSpec.MinFNum),
                    ToRational(resolvedSpec.MaxFNum)
                );
            }

            if (!info.MaxApertureValue.HasValue && resolvedSpec.MinFNum > 0)
            {
                double apex = 2.0 * Math.Log2(resolvedSpec.MinFNum);
                info.MaxApertureValue = ((uint)Math.Max(0, Math.Round(apex * 100)), 100);
            }

            return info;
        }

        private static void TryExtractFromTiffStream(string sourceFilePath, RawExifInfo info)
        {
            using var fs = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192);
            if (fs.Length < 16) return;

            byte[] header = new byte[8];
            if (fs.Read(header, 0, 8) < 8) return;

            long tiffBase = 0;
            if (header[0] == 0x46 && header[1] == 0x55 && header[2] == 0x4A && header[3] == 0x49) // FUJIFILM RAF
            {
                if (fs.Length < 0x5C) return;
                fs.Seek(0x54, SeekOrigin.Begin);
                byte[] offBuf = new byte[8];
                if (fs.Read(offBuf, 0, 8) < 8) return;
                uint jpegOff = (uint)((offBuf[0] << 24) | (offBuf[1] << 16) | (offBuf[2] << 8) | offBuf[3]);
                uint jpegLen = (uint)((offBuf[4] << 24) | (offBuf[5] << 16) | (offBuf[6] << 8) | offBuf[7]);
                if (jpegOff == 0 || jpegOff + jpegLen > fs.Length) return;

                fs.Seek(jpegOff, SeekOrigin.Begin);
                byte[] soi = new byte[2];
                if (fs.Read(soi, 0, 2) < 2 || soi[0] != 0xFF || soi[1] != 0xD8) return;

                byte[] markerBuf = new byte[4];
                while (fs.Position < jpegOff + jpegLen - 4)
                {
                    if (fs.Read(markerBuf, 0, 4) < 4) break;
                    if (markerBuf[0] != 0xFF) break;
                    byte marker = markerBuf[1];
                    int mLen = (markerBuf[2] << 8) | markerBuf[3];
                    if (mLen < 2) break;

                    if (marker == 0xE1) // APP1
                    {
                        byte[] exifSig = new byte[6];
                        if (fs.Read(exifSig, 0, 6) == 6 &&
                            exifSig[0] == 'E' && exifSig[1] == 'x' && exifSig[2] == 'i' && exifSig[3] == 'f' &&
                            exifSig[4] == 0 && exifSig[5] == 0)
                        {
                            tiffBase = fs.Position;
                            break;
                        }
                        else
                        {
                            fs.Seek(mLen - 8, SeekOrigin.Current);
                        }
                    }
                    else if (marker == 0xDA) break;
                    else fs.Seek(mLen - 2, SeekOrigin.Current);
                }

                if (tiffBase == 0 || tiffBase + 8 > fs.Length) return;
                fs.Seek(tiffBase, SeekOrigin.Begin);
                if (fs.Read(header, 0, 8) < 8) return;
            }

            bool isLittleEndian;
            if (header[0] == 0x49 && header[1] == 0x49)
            {
                isLittleEndian = true;
                ushort magic = (ushort)(header[2] | (header[3] << 8));
                if (magic != 42 && magic != 85 && magic != 0x4F52 && magic != 0x5352) return;
            }
            else if (header[0] == 0x4D && header[1] == 0x4D)
            {
                isLittleEndian = false;
                ushort magic = (ushort)((header[2] << 8) | header[3]);
                if (magic != 42 && magic != 0x4F52 && magic != 0x524F) return;
            }
            else
            {
                return;
            }

            uint ifd0Offset = ReadUInt32(header, 4, isLittleEndian);
            if (ifd0Offset == 0 || tiffBase + ifd0Offset + 2 > fs.Length) return;

            fs.Seek(tiffBase + ifd0Offset, SeekOrigin.Begin);
            byte[] countBuf = new byte[2];
            if (fs.Read(countBuf, 0, 2) < 2) return;
            ushort ifd0Count = ReadUInt16(countBuf, 0, isLittleEndian);
            if (ifd0Count > 500) return;

            uint exifIfdOffset = 0;
            for (int i = 0; i < ifd0Count; i++)
            {
                long entryPos = tiffBase + ifd0Offset + 2 + i * 12;
                fs.Seek(entryPos, SeekOrigin.Begin);
                byte[] entry = new byte[12];
                if (fs.Read(entry, 0, 12) < 12) break;

                ushort tag = ReadUInt16(entry, 0, isLittleEndian);
                ushort type = ReadUInt16(entry, 2, isLittleEndian);
                uint count = ReadUInt32(entry, 4, isLittleEndian);
                uint val = ReadUInt32(entry, 8, isLittleEndian);

                switch (tag)
                {
                    case 0x010F: // Make
                        info.Make ??= ReadTiffString(fs, tiffBase, type, count, val, entry, 8);
                        break;
                    case 0x0110: // Model
                        info.Model ??= ReadTiffString(fs, tiffBase, type, count, val, entry, 8);
                        break;
                    case 0x0017: // Panasonic RW2 ISO
                        info.Iso ??= (ushort)val;
                        break;
                    case 0x002E: // Panasonic RW2 JpgFromRaw
                        TryExtractFromPanasonicJpegFromRaw(fs, val, count, info);
                        break;
                    case 0x0112: // Orientation
                        info.Orientation ??= (ushort)val;
                        break;
                    case 0x0132: // DateTime
                        if (string.IsNullOrWhiteSpace(info.DateTimeOriginal))
                        {
                            info.DateTimeOriginal = ReadTiffString(fs, tiffBase, type, count, val, entry, 8);
                        }
                        break;
                    case 0x8769: // ExifSubIFD
                        exifIfdOffset = val;
                        break;
                }
            }

            if (exifIfdOffset > 0 && tiffBase + exifIfdOffset + 2 <= fs.Length)
            {
                fs.Seek(tiffBase + exifIfdOffset, SeekOrigin.Begin);
                if (fs.Read(countBuf, 0, 2) == 2)
                {
                    ushort subCount = ReadUInt16(countBuf, 0, isLittleEndian);
                    if (subCount <= 500)
                    {
                        uint makerNoteOffset = 0;
                        for (int j = 0; j < subCount; j++)
                        {
                            long entryPos = tiffBase + exifIfdOffset + 2 + j * 12;
                            fs.Seek(entryPos, SeekOrigin.Begin);
                            byte[] entry = new byte[12];
                            if (fs.Read(entry, 0, 12) < 12) break;

                            ushort tag = ReadUInt16(entry, 0, isLittleEndian);
                            ushort type = ReadUInt16(entry, 2, isLittleEndian);
                            uint count = ReadUInt32(entry, 4, isLittleEndian);
                            uint val = ReadUInt32(entry, 8, isLittleEndian);

                            switch (tag)
                            {
                                case 0x829A: // ExposureTime (RATIONAL)
                                    if (tiffBase + val + 8 <= fs.Length)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        byte[] rBuf = new byte[8];
                                        if (fs.Read(rBuf, 0, 8) == 8)
                                        {
                                            uint num = ReadUInt32(rBuf, 0, isLittleEndian);
                                            uint den = ReadUInt32(rBuf, 4, isLittleEndian);
                                            if (den > 0) info.ExposureTime = (num, den);
                                        }
                                    }
                                    break;

                                case 0x829D: // FNumber (RATIONAL)
                                    if (tiffBase + val + 8 <= fs.Length)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        byte[] rBuf = new byte[8];
                                        if (fs.Read(rBuf, 0, 8) == 8)
                                        {
                                            uint num = ReadUInt32(rBuf, 0, isLittleEndian);
                                            uint den = ReadUInt32(rBuf, 4, isLittleEndian);
                                            if (den > 0) info.FNumber = (num, den);
                                        }
                                    }
                                    break;

                                case 0x8827: // ISO (SHORT)
                                    info.Iso ??= (ushort)val;
                                    break;

                                case 0x8832: // RecommendedExposureIndex
                                    info.Iso ??= (ushort)val;
                                    break;

                                case 0x9003: // DateTimeOriginal
                                    string? dto = ReadTiffString(fs, tiffBase, type, count, val, entry, 8);
                                    if (!string.IsNullOrWhiteSpace(dto)) info.DateTimeOriginal = dto;
                                    break;

                                case 0x9004: // DateTimeDigitized
                                    string? dtd = ReadTiffString(fs, tiffBase, type, count, val, entry, 8);
                                    if (!string.IsNullOrWhiteSpace(dtd)) info.DateTimeDigitized = dtd;
                                    break;

                                case 0x9205: // MaxApertureValue (RATIONAL)
                                    if (tiffBase + val + 8 <= fs.Length)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        byte[] rBuf = new byte[8];
                                        if (fs.Read(rBuf, 0, 8) == 8)
                                        {
                                            uint num = ReadUInt32(rBuf, 0, isLittleEndian);
                                            uint den = ReadUInt32(rBuf, 4, isLittleEndian);
                                            if (den > 0) info.MaxApertureValue = (num, den);
                                        }
                                    }
                                    break;

                                case 0x920A: // FocalLength (RATIONAL)
                                    if (tiffBase + val + 8 <= fs.Length)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        byte[] rBuf = new byte[8];
                                        if (fs.Read(rBuf, 0, 8) == 8)
                                        {
                                            uint num = ReadUInt32(rBuf, 0, isLittleEndian);
                                            uint den = ReadUInt32(rBuf, 4, isLittleEndian);
                                            if (den > 0) info.FocalLength = (num, den);
                                        }
                                    }
                                    break;

                                case 0x927C: // MakerNotes
                                    makerNoteOffset = val;
                                    break;

                                case 0xA405: // FocalLengthIn35mmFormat (SHORT)
                                    info.FocalLengthIn35mmFormat ??= (ushort)val;
                                    break;

                                case 0xA432: // LensSpecification (RATIONAL[4])
                                    if (count >= 4 && tiffBase + val + 32 <= fs.Length)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        byte[] specBuf = new byte[32];
                                        if (fs.Read(specBuf, 0, 32) == 32)
                                        {
                                            uint minFNum = ReadUInt32(specBuf, 0, isLittleEndian);
                                            uint minFDen = ReadUInt32(specBuf, 4, isLittleEndian);
                                            uint maxFNum = ReadUInt32(specBuf, 8, isLittleEndian);
                                            uint maxFDen = ReadUInt32(specBuf, 12, isLittleEndian);
                                            uint minFnNum = ReadUInt32(specBuf, 16, isLittleEndian);
                                            uint minFnDen = ReadUInt32(specBuf, 20, isLittleEndian);
                                            uint maxFnNum = ReadUInt32(specBuf, 24, isLittleEndian);
                                            uint maxFnDen = ReadUInt32(specBuf, 28, isLittleEndian);
                                            if (minFDen > 0 && maxFDen > 0)
                                            {
                                                info.LensSpecification = (
                                                    (minFNum, minFDen),
                                                    (maxFNum, maxFDen),
                                                    minFnDen > 0 && minFnNum > 0 ? (minFnNum, minFnDen) : null,
                                                    maxFnDen > 0 && maxFnNum > 0 ? (maxFnNum, maxFnDen) : null
                                                );
                                            }
                                        }
                                    }
                                    break;

                                case 0xA433: // LensMake (ASCII)
                                    string? lmk = ReadTiffString(fs, tiffBase, type, count, val, entry, 8);
                                    if (!string.IsNullOrWhiteSpace(lmk)) info.LensMake = lmk;
                                    break;

                                case 0xA434: // LensModel (ASCII)
                                    string? lm = ReadTiffString(fs, tiffBase, type, count, val, entry, 8);
                                    if (!string.IsNullOrWhiteSpace(lm)) info.LensModel = lm;
                                    break;

                                case 0xA435: // LensSerialNumber (ASCII)
                                    string? lsn = ReadTiffString(fs, tiffBase, type, count, val, entry, 8);
                                    if (!string.IsNullOrWhiteSpace(lsn)) info.LensSerialNumber = lsn;
                                    break;
                            }
                        }

                        if (makerNoteOffset > 0 && (info.Make?.Contains("Canon", StringComparison.OrdinalIgnoreCase) == true || sourceFilePath.EndsWith(".cr2", StringComparison.OrdinalIgnoreCase)))
                        {
                            TryExtractFromCanonMakerNotes(fs, (uint)(tiffBase + makerNoteOffset), isLittleEndian, info);
                        }
                    }
                }
            }
        }

        private static void TryExtractFromPanasonicJpegFromRaw(FileStream fs, uint jpegOffset, uint jpegLength, RawExifInfo info)
        {
            try
            {
                if (jpegOffset == 0 || jpegOffset + 12 > fs.Length) return;
                long savedPos = fs.Position;
                fs.Seek(jpegOffset, SeekOrigin.Begin);
                byte[] soi = new byte[2];
                if (fs.Read(soi, 0, 2) < 2 || soi[0] != 0xFF || soi[1] != 0xD8)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }

                // Scan markers for APP1
                byte[] markerBuf = new byte[4];
                long tiffBase = -1;
                while (fs.Position < jpegOffset + Math.Min(jpegLength, 150000) - 4)
                {
                    if (fs.Read(markerBuf, 0, 4) < 4) break;
                    if (markerBuf[0] != 0xFF) break;
                    byte marker = markerBuf[1];
                    int mLen = (markerBuf[2] << 8) | markerBuf[3];
                    if (mLen < 2) break;

                    if (marker == 0xE1) // APP1
                    {
                        byte[] exifSig = new byte[6];
                        if (fs.Read(exifSig, 0, 6) == 6 &&
                            exifSig[0] == 'E' && exifSig[1] == 'x' && exifSig[2] == 'i' && exifSig[3] == 'f' &&
                            exifSig[4] == 0 && exifSig[5] == 0)
                        {
                            tiffBase = fs.Position;
                            break;
                        }
                    }
                    else if (marker == 0xDA) // SOS
                    {
                        break;
                    }
                    else
                    {
                        fs.Seek(mLen - 2, SeekOrigin.Current);
                    }
                }

                if (tiffBase < 0 || tiffBase + 8 > fs.Length)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }

                fs.Seek(tiffBase, SeekOrigin.Begin);
                byte[] tiffHdr = new byte[8];
                if (fs.Read(tiffHdr, 0, 8) < 8)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }

                bool isLE = tiffHdr[0] == 0x49 && tiffHdr[1] == 0x49;
                uint ifd0Off = ReadUInt32(tiffHdr, 4, isLE);
                if (ifd0Off == 0 || tiffBase + ifd0Off + 2 > fs.Length)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }

                fs.Seek(tiffBase + ifd0Off, SeekOrigin.Begin);
                byte[] cntBuf = new byte[2];
                if (fs.Read(cntBuf, 0, 2) < 2)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }
                ushort ifd0Count = ReadUInt16(cntBuf, 0, isLE);
                uint subIfdOff = 0;
                for (int i = 0; i < ifd0Count; i++)
                {
                    long entryPos = tiffBase + ifd0Off + 2 + i * 12;
                    fs.Seek(entryPos, SeekOrigin.Begin);
                    byte[] entry = new byte[12];
                    if (fs.Read(entry, 0, 12) < 12) break;
                    ushort tag = ReadUInt16(entry, 0, isLE);
                    uint val = ReadUInt32(entry, 8, isLE);
                    if (tag == 0x8769) subIfdOff = val;
                }

                if (subIfdOff > 0 && tiffBase + subIfdOff + 2 <= fs.Length)
                {
                    fs.Seek(tiffBase + subIfdOff, SeekOrigin.Begin);
                    if (fs.Read(cntBuf, 0, 2) == 2)
                    {
                        ushort subCount = ReadUInt16(cntBuf, 0, isLE);
                        uint makerNoteOff = 0;
                        for (int j = 0; j < subCount; j++)
                        {
                            long entryPos = tiffBase + subIfdOff + 2 + j * 12;
                            fs.Seek(entryPos, SeekOrigin.Begin);
                            byte[] entry = new byte[12];
                            if (fs.Read(entry, 0, 12) < 12) break;

                            ushort tag = ReadUInt16(entry, 0, isLE);
                            ushort type = ReadUInt16(entry, 2, isLE);
                            uint count = ReadUInt32(entry, 4, isLE);
                            uint val = ReadUInt32(entry, 8, isLE);

                            switch (tag)
                            {
                                case 0x927C: // MakerNotes
                                    makerNoteOff = val;
                                    break;
                                case 0x8827: // ISO
                                    info.Iso ??= (ushort)val;
                                    break;
                                case 0xA434: // LensModel
                                    if (string.IsNullOrWhiteSpace(info.LensModel))
                                    {
                                        info.LensModel = ReadTiffString(fs, tiffBase, type, count, val, entry, 8);
                                    }
                                    break;
                            }
                        }

                        // Parse Panasonic MakerNotes if present
                        if (makerNoteOff > 0 && tiffBase + makerNoteOff + 14 <= fs.Length)
                        {
                            fs.Seek(tiffBase + makerNoteOff, SeekOrigin.Begin);
                            byte[] mnSig = new byte[12];
                            if (fs.Read(mnSig, 0, 12) == 12 &&
                                mnSig[0] == 'P' && mnSig[1] == 'a' && mnSig[2] == 'n' && mnSig[3] == 'a' && mnSig[4] == 's' && mnSig[5] == 'o' && mnSig[6] == 'n' && mnSig[7] == 'i' && mnSig[8] == 'c')
                            {
                                if (fs.Read(cntBuf, 0, 2) == 2)
                                {
                                    ushort mnCount = ReadUInt16(cntBuf, 0, isLE);
                                    for (int k = 0; k < mnCount; k++)
                                    {
                                        long entryPos = tiffBase + makerNoteOff + 14 + k * 12;
                                        fs.Seek(entryPos, SeekOrigin.Begin);
                                        byte[] entry = new byte[12];
                                        if (fs.Read(entry, 0, 12) < 12) break;

                                        ushort mnTag = ReadUInt16(entry, 0, isLE);
                                        ushort mnType = ReadUInt16(entry, 2, isLE);
                                        uint mnCountTag = ReadUInt32(entry, 4, isLE);
                                        uint mnVal = ReadUInt32(entry, 8, isLE);

                                        if (mnTag == 0x0051 && string.IsNullOrWhiteSpace(info.LensModel)) // LensType / LensModel
                                        {
                                            info.LensModel = ReadTiffString(fs, tiffBase, mnType, mnCountTag, mnVal, entry, 8);
                                        }
                                        else if (mnTag == 0x0052 && string.IsNullOrWhiteSpace(info.LensSerialNumber)) // LensSerialNumber
                                        {
                                            info.LensSerialNumber = ReadTiffString(fs, tiffBase, mnType, mnCountTag, mnVal, entry, 8);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                fs.Seek(savedPos, SeekOrigin.Begin);
            }
            catch { }
        }

        private static void TryExtractFromCanonMakerNotes(FileStream fs, uint makerNoteOffset, bool isLittleEndian, RawExifInfo info)
        {
            try
            {
                if (makerNoteOffset == 0 || makerNoteOffset + 2 > fs.Length) return;
                long savedPos = fs.Position;
                fs.Seek(makerNoteOffset, SeekOrigin.Begin);
                byte[] cntBuf = new byte[2];
                if (fs.Read(cntBuf, 0, 2) < 2)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }
                ushort mnCount = ReadUInt16(cntBuf, 0, isLittleEndian);
                if (mnCount > 300)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }

                ushort lensType = 0;
                ushort minFocal = 0;
                ushort maxFocal = 0;
                double maxAperture = 0;
                string? canonLensModel = null;

                for (int k = 0; k < mnCount; k++)
                {
                    long entryPos = makerNoteOffset + 2 + k * 12;
                    if (entryPos + 12 > fs.Length) break;
                    fs.Seek(entryPos, SeekOrigin.Begin);
                    byte[] entry = new byte[12];
                    if (fs.Read(entry, 0, 12) < 12) break;

                    ushort mnTag = ReadUInt16(entry, 0, isLittleEndian);
                    ushort mnType = ReadUInt16(entry, 2, isLittleEndian);
                    uint mnCountEntry = ReadUInt32(entry, 4, isLittleEndian);
                    uint mnVal = ReadUInt32(entry, 8, isLittleEndian);

                    if (mnTag == 0x0001) // CanonCameraSettings
                    {
                        if (mnVal + 56 <= fs.Length)
                        {
                            byte[] settingsBuf = new byte[56];
                            fs.Seek(mnVal, SeekOrigin.Begin);
                            if (fs.Read(settingsBuf, 0, 56) == 56)
                            {
                                lensType = ReadUInt16(settingsBuf, 22 * 2, isLittleEndian);
                                maxFocal = ReadUInt16(settingsBuf, 23 * 2, isLittleEndian);
                                minFocal = ReadUInt16(settingsBuf, 24 * 2, isLittleEndian);
                                short maxApCode = (short)ReadUInt16(settingsBuf, 26 * 2, isLittleEndian);
                                if (maxApCode > 0)
                                {
                                    maxAperture = Math.Pow(2.0, (maxApCode / 32.0) / 2.0);
                                }
                            }
                        }
                    }
                    else if (mnTag == 0x0095) // Canon LensModel tag
                    {
                        canonLensModel = ReadTiffString(fs, 0, mnType, mnCountEntry, mnVal, entry, 8);
                    }
                }

                if (lensType > 0)
                {
                    var resolved = ResolveCanonLens(lensType, minFocal, maxFocal, maxAperture, canonLensModel ?? info.LensModel);
                    if (resolved.HasValue)
                    {
                        info.LensModel = resolved.Value.LensModel;
                        info.LensMake = resolved.Value.LensMake;
                        info.LensSpecification = (
                            ToRational(resolved.Value.MinFocal),
                            ToRational(resolved.Value.MaxFocal),
                            ToRational(resolved.Value.MinFNum),
                            ToRational(resolved.Value.MaxFNum)
                        );
                        if (resolved.Value.MinFNum > 0)
                        {
                            double apex = 2.0 * Math.Log2(resolved.Value.MinFNum);
                            info.MaxApertureValue = ((uint)Math.Max(0, Math.Round(apex * 100)), 100);
                        }
                    }
                }

                fs.Seek(savedPos, SeekOrigin.Begin);
            }
            catch { }
        }

        private static (string LensModel, string LensMake, double MinFocal, double MaxFocal, double MinFNum, double MaxFNum)? ResolveCanonLens(
            ushort lensType,
            ushort minFocal,
            ushort maxFocal,
            double maxAperture,
            string? rawLensModel)
        {
            double minF = minFocal > 0 ? minFocal : 0;
            double maxF = maxFocal > 0 ? maxFocal : minF;
            double minFn = maxAperture > 0 ? Math.Round(maxAperture, 1) : 2.8;
            double maxFn = minFn;

            if (lensType == 137)
            {
                if (minFocal == 24 && maxFocal == 70)
                    return ("Sigma 24-70mm F2.8 IF EX DG HSM (Canon EF)", "SIGMA", 24, 70, 2.8, 2.8);
                if (minFocal == 18 && maxFocal == 50)
                    return ("Sigma 18-50mm f/2.8-4.5 DC OS HSM", "SIGMA", 18, 50, 2.8, 4.5);
                if (minFocal == 50 && maxFocal == 200)
                    return ("Sigma 50-200mm f/4-5.6 DC OS HSM", "SIGMA", 50, 200, 4.0, 5.6);
                if (minFocal == 18 && maxFocal == 250)
                    return ("Sigma 18-250mm f/3.5-6.3 DC OS HSM", "SIGMA", 18, 250, 3.5, 6.3);
                if (minFocal == 18 && maxFocal == 125)
                    return ("Sigma 18-125mm f/3.8-5.6 DC OS HSM", "SIGMA", 18, 125, 3.8, 5.6);
                if (minFocal == 17 && maxFocal == 70)
                    return ("Sigma 17-70mm f/2.8-4 DC Macro OS HSM | C", "SIGMA", 17, 70, 2.8, 4.0);
                if (minFocal == 17 && maxFocal == 50)
                    return ("Sigma 17-50mm f/2.8 OS HSM", "SIGMA", 17, 50, 2.8, 2.8);
                if (minFocal == 85 && maxFocal == 85)
                    return ("Canon EF 85mm f/1.2L USM", "Canon", 85, 85, 1.2, 1.2);
                if (minFocal == 8 && maxFocal == 16)
                    return ("Sigma 8-16mm f/4.5-5.6 DC HSM", "SIGMA", 8, 16, 4.5, 5.6);
                if (minFocal == 10 && maxFocal == 20)
                    return ("Sigma 10-20mm f/3.5 EX DC HSM", "SIGMA", 10, 20, 3.5, 3.5);
                if (minFocal == 18 && maxFocal == 35)
                    return ("Sigma 18-35mm f/1.8 DC HSM", "SIGMA", 18, 35, 1.8, 1.8);
            }
            else if (lensType == 161)
            {
                if (minFocal == 24 && maxFocal == 70)
                    return ("Sigma 24-70mm f/2.8 EX", "SIGMA", 24, 70, 2.8, 2.8);
                if (minFocal == 28 && maxFocal == 70)
                    return ("Canon EF 28-70mm f/2.8L USM", "Canon", 28, 70, 2.8, 2.8);
            }
            else if (lensType == 495)
            {
                if (minFocal == 24 && maxFocal == 70)
                    return ("Canon EF 24-70mm f/2.8L II USM", "Canon", 24, 70, 2.8, 2.8);
            }
            else if (lensType == 248)
            {
                return ("Canon EF 24-70mm f/4L IS USM", "Canon", 24, 70, 4.0, 4.0);
            }
            else if (lensType == 254)
            {
                return ("Canon EF 24-105mm f/4L IS II USM", "Canon", 24, 105, 4.0, 4.0);
            }
            else if (lensType == 237)
            {
                return ("Canon EF 24-105mm f/4L IS USM", "Canon", 24, 105, 4.0, 4.0);
            }
            else if (lensType == 251)
            {
                return ("Canon EF 16-35mm f/4L IS USM", "Canon", 16, 35, 4.0, 4.0);
            }
            else if (lensType == 256)
            {
                return ("Canon EF 16-35mm f/2.8L III USM", "Canon", 16, 35, 2.8, 2.8);
            }
            else if (lensType == 213)
            {
                return ("Canon EF 70-200mm f/2.8L IS II USM", "Canon", 70, 200, 2.8, 2.8);
            }
            else if (lensType == 257)
            {
                return ("Canon EF 70-200mm f/2.8L IS III USM", "Canon", 70, 200, 2.8, 2.8);
            }
            else if (lensType == 1)
            {
                return ("Canon EF 50mm f/1.8", "Canon", 50, 50, 1.8, 1.8);
            }
            else if (lensType == 29)
            {
                return ("Canon EF 50mm f/1.8 II", "Canon", 50, 50, 1.8, 1.8);
            }
            else if (lensType == 253)
            {
                return ("Canon EF 50mm f/1.8 STM", "Canon", 50, 50, 1.8, 1.8);
            }
            else if (lensType == 198)
            {
                return ("Canon EF 50mm f/1.2L USM", "Canon", 50, 50, 1.2, 1.2);
            }
            else if (lensType == 255)
            {
                return ("Canon EF 100-400mm f/4.5-5.6L IS II USM", "Canon", 100, 400, 4.5, 5.6);
            }
            else if (lensType == 250)
            {
                return ("Canon EF 100mm f/2.8L Macro IS USM", "Canon", 100, 100, 2.8, 2.8);
            }

            if (!string.IsNullOrWhiteSpace(rawLensModel) && rawLensModel.Trim() != "24-70mm")
            {
                string lm = rawLensModel.Trim();
                string make = GetLensMake("Canon", lm) ?? "Canon";
                return (lm, make, minF, maxF, minFn, maxFn);
            }

            return null;
        }

        public static (double MinFocal, double MaxFocal, double MinFNum, double MaxFNum) ResolveLensSpecification(RawExifInfo info)
        {
            double minFocal = 0;
            double maxFocal = 0;
            double minFNum = 0;
            double maxFNum = 0;

            // 1. From existing info.LensSpecification
            if (info.LensSpecification.HasValue)
            {
                var spec = info.LensSpecification.Value;
                if (spec.MinFocal.Den > 0) minFocal = (double)spec.MinFocal.Num / spec.MinFocal.Den;
                if (spec.MaxFocal.Den > 0) maxFocal = (double)spec.MaxFocal.Num / spec.MaxFocal.Den;
                if (spec.MinFNum.HasValue && spec.MinFNum.Value.Den > 0) minFNum = (double)spec.MinFNum.Value.Num / spec.MinFNum.Value.Den;
                if (spec.MaxFNum.HasValue && spec.MaxFNum.Value.Den > 0) maxFNum = (double)spec.MaxFNum.Value.Num / spec.MaxFNum.Value.Den;
            }

            // 2. Parse from LensModel if any fields are missing
            if (!string.IsNullOrWhiteSpace(info.LensModel))
            {
                string lm = info.LensModel;

                if (minFocal <= 0 || maxFocal <= 0)
                {
                    var matchZoom = System.Text.RegularExpressions.Regex.Match(lm, @"(?<![fF\/\d])(\d+(?:\.\d+)?)\s*-\s*(\d+(?:\.\d+)?)\s*(?:mm|\/|\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (matchZoom.Success)
                    {
                        if (double.TryParse(matchZoom.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double f1) &&
                            double.TryParse(matchZoom.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double f2))
                        {
                            minFocal = f1;
                            maxFocal = f2;
                        }
                    }
                    else
                    {
                        var matchPrime = System.Text.RegularExpressions.Regex.Match(lm, @"(?<![fF\/\d])(\d+(?:\.\d+)?)\s*mm", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (matchPrime.Success && double.TryParse(matchPrime.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double f1))
                        {
                            minFocal = f1;
                            maxFocal = f1;
                        }
                    }
                }

                if (minFNum <= 0 || maxFNum <= 0)
                {
                    var matchAperture = System.Text.RegularExpressions.Regex.Match(lm, @"(?:(?<=[0-9]mm|[0-9]\s*mm|\/|\s|^)[fF](?:\/|\s*)|1\s*:\s*)(\d+(?:\.\d+)?)(?:\s*-\s*(?:[fF]\/?)?(\d+(?:\.\d+)?))?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (matchAperture.Success)
                    {
                        if (double.TryParse(matchAperture.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double a1) && a1 <= 32)
                        {
                            minFNum = a1;
                            if (matchAperture.Groups[2].Success && double.TryParse(matchAperture.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double a2) && a2 <= 32)
                            {
                                maxFNum = a2;
                            }
                            else
                            {
                                maxFNum = minFNum;
                            }
                        }
                    }
                }
            }

            // 3. Fallbacks to shooting focal length and aperture
            if (minFocal <= 0 && info.FocalLength.HasValue && info.FocalLength.Value.Den > 0)
            {
                minFocal = (double)info.FocalLength.Value.Num / info.FocalLength.Value.Den;
                maxFocal = minFocal;
            }

            if (minFNum <= 0 && info.FNumber.HasValue && info.FNumber.Value.Den > 0)
            {
                minFNum = (double)info.FNumber.Value.Num / info.FNumber.Value.Den;
                maxFNum = minFNum;
            }

            if (maxFocal <= 0) maxFocal = minFocal;
            if (maxFNum <= 0) maxFNum = minFNum;

            return (minFocal, maxFocal, minFNum, maxFNum);
        }

        private static void UpdateFujifilmMakerNotes(FileStream fs, long makerNotePos, double minFocal, double maxFocal, double minFNum, double maxFNum, bool isLittleEndian)
        {
            try
            {
                if (makerNotePos + 14 > fs.Length) return;
                long savedPos = fs.Position;
                fs.Seek(makerNotePos, SeekOrigin.Begin);
                byte[] fujiSig = new byte[12];
                if (fs.Read(fujiSig, 0, 12) < 12)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }

                if (fujiSig[0] != 'F' || fujiSig[1] != 'U' || fujiSig[2] != 'J' || fujiSig[3] != 'I' ||
                    fujiSig[4] != 'F' || fujiSig[5] != 'I' || fujiSig[6] != 'L' || fujiSig[7] != 'M')
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }

                uint ifdRel = ReadUInt32(fujiSig, 8, isLittleEndian);
                long ifdPos = makerNotePos + ifdRel;
                if (ifdPos + 2 > fs.Length)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }

                fs.Seek(ifdPos, SeekOrigin.Begin);
                byte[] cntBuf = new byte[2];
                if (fs.Read(cntBuf, 0, 2) < 2)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }
                ushort count = ReadUInt16(cntBuf, 0, isLittleEndian);
                if (count > 200)
                {
                    fs.Seek(savedPos, SeekOrigin.Begin);
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    long entryPos = ifdPos + 2 + i * 12;
                    if (entryPos + 12 > fs.Length) break;
                    fs.Seek(entryPos, SeekOrigin.Begin);
                    byte[] entry = new byte[12];
                    if (fs.Read(entry, 0, 12) < 12) break;

                    ushort tag = ReadUInt16(entry, 0, isLittleEndian);
                    ushort type = ReadUInt16(entry, 2, isLittleEndian);
                    uint mnCount = ReadUInt32(entry, 4, isLittleEndian);
                    uint valOffset = ReadUInt32(entry, 8, isLittleEndian);

                    // Fuji MakerNote value offsets are relative to makerNotePos
                    long valPos = makerNotePos + valOffset;

                    if (tag == 0x0010 || tag == 0x0052)
                    {
                        // Wipe InternalSerialNumber (0x0010) and LensSerialNumber (0x0052)
                        if (mnCount <= 4)
                        {
                            fs.Seek(entryPos + 8, SeekOrigin.Begin);
                            WriteUInt32(fs, 0, isLittleEndian);
                            fs.Seek(entryPos + 4, SeekOrigin.Begin);
                            WriteUInt32(fs, 0, isLittleEndian);
                        }
                        else if (valPos + mnCount <= fs.Length)
                        {
                            fs.Seek(valPos, SeekOrigin.Begin);
                            byte[] empty = new byte[Math.Min(mnCount, 128)];
                            fs.Write(empty, 0, empty.Length);
                            fs.Seek(entryPos + 4, SeekOrigin.Begin);
                            WriteUInt32(fs, 0, isLittleEndian);
                        }
                        continue;
                    }

                    if (valPos + 8 > fs.Length) continue;

                    switch (tag)
                    {
                        case 0x1404: // MinFocalLength (RATIONAL)
                            fs.Seek(valPos, SeekOrigin.Begin);
                            if (minFocal > 0)
                            {
                                WriteUInt32(fs, (uint)Math.Round(minFocal * 100), isLittleEndian);
                                WriteUInt32(fs, 100, isLittleEndian);
                            }
                            else
                            {
                                WriteUInt32(fs, 0, isLittleEndian);
                                WriteUInt32(fs, 1, isLittleEndian);
                            }
                            break;

                        case 0x1405: // MaxFocalLength (RATIONAL)
                            fs.Seek(valPos, SeekOrigin.Begin);
                            if (maxFocal > 0)
                            {
                                WriteUInt32(fs, (uint)Math.Round(maxFocal * 100), isLittleEndian);
                                WriteUInt32(fs, 100, isLittleEndian);
                            }
                            else
                            {
                                WriteUInt32(fs, 0, isLittleEndian);
                                WriteUInt32(fs, 1, isLittleEndian);
                            }
                            break;

                        case 0x1406: // MaxApertureAtMinFocal (RATIONAL)
                            fs.Seek(valPos, SeekOrigin.Begin);
                            if (minFNum > 0)
                            {
                                WriteUInt32(fs, (uint)Math.Round(minFNum * 100), isLittleEndian);
                                WriteUInt32(fs, 100, isLittleEndian);
                            }
                            else
                            {
                                WriteUInt32(fs, 0, isLittleEndian);
                                WriteUInt32(fs, 1, isLittleEndian);
                            }
                            break;

                        case 0x1407: // MaxApertureAtMaxFocal (RATIONAL)
                            fs.Seek(valPos, SeekOrigin.Begin);
                            if (maxFNum > 0)
                            {
                                WriteUInt32(fs, (uint)Math.Round(maxFNum * 100), isLittleEndian);
                                WriteUInt32(fs, 100, isLittleEndian);
                            }
                            else
                            {
                                WriteUInt32(fs, 0, isLittleEndian);
                                WriteUInt32(fs, 1, isLittleEndian);
                            }
                            break;
                    }
                }

                fs.Seek(savedPos, SeekOrigin.Begin);
            }
            catch { }
        }

        public static (uint Num, uint Den) ToRational(double val)
        {
            if (val <= 0) return (0, 1);
            if (Math.Abs(val - Math.Round(val)) < 0.001)
            {
                return ((uint)Math.Round(val), 1);
            }
            if (Math.Abs(val * 10 - Math.Round(val * 10)) < 0.01)
            {
                return ((uint)Math.Round(val * 10), 10);
            }
            return ((uint)Math.Round(val * 100), 100);
        }

        private static string? ResolveFixedLensModel(string? make, string? model, (uint Num, uint Den)? focalLength, (uint Num, uint Den)? fNumber)
        {
            if (string.IsNullOrWhiteSpace(model)) return null;
            string m = model.Trim().ToUpperInvariant();
            string mk = (make ?? "").Trim().ToUpperInvariant();

            if (m.Contains("GR IIIX")) return "GR LENS 26.1mm F2.8";
            if (m.Contains("GR III") || m.Contains("GR II") || m.StartsWith("GR DIGITAL") || m == "RICOH GR") return "GR LENS 18.3mm F2.8";
            if (m.Contains("P1000")) return "NIKKOR 4.3-539mm f/2.8-8";
            if (m.Contains("P950") || m.Contains("P900")) return "NIKKOR 4.3-357mm f/2.8-6.5";
            if (m.Contains("COOLPIX A")) return "NIKKOR 18.5mm f/2.8";
            if (m.Contains("DSC-F828")) return "Carl Zeiss Vario-Sonnar 7.1-51mm F2.0-2.8";
            if (m.Contains("DSC-R1")) return "Carl Zeiss Vario-Sonnar 14.3-71.5mm F2.8-4.8";
            if (m.Contains("DSC-RX1R") || m.Contains("DSC-RX1")) return "ZEISS Sonnar T* 35mm F2";
            if (m.Contains("DSC-RX100")) return "ZEISS Vario-Sonnar T*";
            if (m.Contains("DSC-RX10")) return "ZEISS Vario-Sonnar T*";
            if (m.Contains("LEICA Q")) return "Summilux 28mm f/1.7 ASPH";
            if (m.Contains("X100")) return "Fujinon 23mm F2";
            if (m.Contains("FC4382") || m.Contains("MAVIC 3 PRO")) return "DJI 70mm f/2.8";
            if (m.Contains("FC3411") || m.Contains("MINI 3 PRO") || m.Contains("MINI 4 PRO")) return "DJI 24mm f/1.7";

            // If camera is iPhone or smartphone without lens model
            if (mk.Contains("APPLE") || m.Contains("IPHONE"))
            {
                if (focalLength.HasValue && focalLength.Value.Den > 0)
                {
                    double fl = (double)focalLength.Value.Num / focalLength.Value.Den;
                    return $"{model} camera {fl:0.#}mm";
                }
                return $"{model} back camera";
            }

            return null;
        }

        private static string? GetLensMake(string? cameraMake, string? lensModel)
        {
            if (string.IsNullOrWhiteSpace(lensModel)) return null;
            string lm = lensModel.Trim();
            if (lm.StartsWith("Canon", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("EF", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("RF", StringComparison.OrdinalIgnoreCase)) return "Canon";
            if (lm.StartsWith("Nikon", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("NIKKOR", StringComparison.OrdinalIgnoreCase)) return "NIKON";
            if (lm.StartsWith("Sony", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("FE ", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("E ", StringComparison.OrdinalIgnoreCase)) return "SONY";
            if (lm.StartsWith("Leica", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("LEICA", StringComparison.OrdinalIgnoreCase)) return "Leica";
            if (lm.StartsWith("Olympus", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("OLYMPUS", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("M.Zuiko", StringComparison.OrdinalIgnoreCase)) return "OLYMPUS";
            if (lm.StartsWith("Fujifilm", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("FUJINON", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("XF", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("XC", StringComparison.OrdinalIgnoreCase)) return "FUJIFILM";
            if (lm.StartsWith("Sigma", StringComparison.OrdinalIgnoreCase)) return "SIGMA";
            if (lm.StartsWith("Tamron", StringComparison.OrdinalIgnoreCase)) return "TAMRON";
            if (lm.StartsWith("Apple", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("iPhone", StringComparison.OrdinalIgnoreCase)) return "Apple";
            if (lm.StartsWith("Ricoh", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("GR", StringComparison.OrdinalIgnoreCase)) return "RICOH";
            if (lm.StartsWith("DJI", StringComparison.OrdinalIgnoreCase)) return "DJI";
            if (lm.StartsWith("Zeiss", StringComparison.OrdinalIgnoreCase) || lm.StartsWith("Carl Zeiss", StringComparison.OrdinalIgnoreCase)) return "ZEISS";
            if (!string.IsNullOrWhiteSpace(cameraMake)) return cameraMake;
            return null;
        }

        private static uint GetTagByteLength(ushort type, uint count)
        {
            uint typeSize = type switch
            {
                1 => 1, // BYTE
                2 => 1, // ASCII
                3 => 2, // SHORT
                4 => 4, // LONG
                5 => 8, // RATIONAL
                7 => 1, // UNDEFINED
                9 => 4, // SLONG
                10 => 8, // SRATIONAL
                _ => 1
            };
            return count * typeSize;
        }

        private static bool ApplyMetadataToRaf(string rafFilePath, RawExifInfo exif, string targetModel, bool patchModelOnly = false)
        {
            try
            {
                using var fs = new FileStream(rafFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                if (fs.Length < 148) return false;

                // 1. Check RAF magic
                byte[] magic = new byte[8];
                fs.Seek(0, SeekOrigin.Begin);
                if (fs.Read(magic, 0, 8) < 8) return false;
                if (magic[0] != 0x46 || magic[1] != 0x55 || magic[2] != 0x4A || magic[3] != 0x49 ||
                    magic[4] != 0x46 || magic[5] != 0x49 || magic[6] != 0x4C || magic[7] != 0x4D)
                {
                    return false;
                }

                // 2. Patch Model in RAF Header at offset 0x1C (32 bytes)
                fs.Seek(0x1C, SeekOrigin.Begin);
                byte[] modelHdr = new byte[32];
                byte[] modelAscii = Encoding.ASCII.GetBytes(targetModel);
                Array.Copy(modelAscii, modelHdr, Math.Min(modelAscii.Length, 32));
                fs.Write(modelHdr, 0, 32);

                // 3. Read jpeg_off and jpeg_len at offset 0x54 (uint32 BE)
                if (fs.Length < 0x5C)
                {
                    fs.Flush();
                    return true;
                }

                fs.Seek(0x54, SeekOrigin.Begin);
                byte[] offsetsBuf = new byte[8];
                if (fs.Read(offsetsBuf, 0, 8) < 8)
                {
                    fs.Flush();
                    return true;
                }
                uint jpegOff = (uint)((offsetsBuf[0] << 24) | (offsetsBuf[1] << 16) | (offsetsBuf[2] << 8) | offsetsBuf[3]);
                uint jpegLen = (uint)((offsetsBuf[4] << 24) | (offsetsBuf[5] << 16) | (offsetsBuf[6] << 8) | offsetsBuf[7]);

                if (jpegOff == 0 || jpegOff + jpegLen > fs.Length)
                {
                    fs.Flush();
                    return true;
                }

                // 4. Locate APP1 in embedded JPEG
                fs.Seek(jpegOff, SeekOrigin.Begin);
                byte[] soi = new byte[2];
                if (fs.Read(soi, 0, 2) < 2 || soi[0] != 0xFF || soi[1] != 0xD8)
                {
                    fs.Flush();
                    return true;
                }

                long tiffBase = -1;
                uint app1DataLen = 0;
                byte[] markerBuf = new byte[4];
                while (fs.Position < jpegOff + jpegLen - 4)
                {
                    if (fs.Read(markerBuf, 0, 4) < 4) break;
                    if (markerBuf[0] != 0xFF) break;
                    byte marker = markerBuf[1];
                    int mLen = (markerBuf[2] << 8) | markerBuf[3];
                    if (mLen < 2) break;

                    if (marker == 0xE1) // APP1
                    {
                        byte[] exifSig = new byte[6];
                        if (fs.Read(exifSig, 0, 6) == 6 &&
                            exifSig[0] == 'E' && exifSig[1] == 'x' && exifSig[2] == 'i' && exifSig[3] == 'f' &&
                            exifSig[4] == 0 && exifSig[5] == 0)
                        {
                            tiffBase = fs.Position;
                            app1DataLen = (uint)Math.Max(0, mLen - 8);
                            break;
                        }
                        else
                        {
                            fs.Seek(mLen - 8, SeekOrigin.Current);
                        }
                    }
                    else if (marker == 0xDA) // SOS
                    {
                        break;
                    }
                    else
                    {
                        fs.Seek(mLen - 2, SeekOrigin.Current);
                    }
                }

                if (tiffBase < 0)
                {
                    fs.Flush();
                    return true;
                }

                // 5. Read TIFF header
                fs.Seek(tiffBase, SeekOrigin.Begin);
                byte[] tiffHdr = new byte[8];
                if (fs.Read(tiffHdr, 0, 8) < 8) return false;

                bool isLittleEndian;
                if (tiffHdr[0] == 0x49 && tiffHdr[1] == 0x49)
                    isLittleEndian = true;
                else if (tiffHdr[0] == 0x4D && tiffHdr[1] == 0x4D)
                    isLittleEndian = false;
                else
                    return false;

                uint ifd0Offset = ReadUInt32(tiffHdr, 4, isLittleEndian);
                if (ifd0Offset == 0 || tiffBase + ifd0Offset + 2 > fs.Length) return false;

                // Find Copyright / Artist tag offsets in IFD0 to use as safe scratch buffers if needed,
                // or calculate free space in APP1 past all used tags and thumbnails
                fs.Seek(tiffBase + ifd0Offset, SeekOrigin.Begin);
                byte[] cntBuf = new byte[2];
                if (fs.Read(cntBuf, 0, 2) < 2) return false;
                ushort ifd0Count = ReadUInt16(cntBuf, 0, isLittleEndian);

                uint safeBufferOffset = 0;
                uint safeBufferSize = 0;
                uint exifIfdOffset = 0;
                uint maxUsedTiffOffset = ifd0Offset + 2 + (uint)ifd0Count * 12 + 4;

                for (int i = 0; i < ifd0Count; i++)
                {
                    long entryPos = tiffBase + ifd0Offset + 2 + i * 12;
                    fs.Seek(entryPos, SeekOrigin.Begin);
                    byte[] entry = new byte[12];
                    if (fs.Read(entry, 0, 12) < 12) break;

                    ushort tag = ReadUInt16(entry, 0, isLittleEndian);
                    ushort type = ReadUInt16(entry, 2, isLittleEndian);
                    uint count = ReadUInt32(entry, 4, isLittleEndian);
                    uint val = ReadUInt32(entry, 8, isLittleEndian);

                    if (tag == 0x8298 && count >= 64) // Copyright tag: plenty of blank spaces
                    {
                        safeBufferOffset = val;
                        safeBufferSize = count;
                        if (tiffBase + val + count <= fs.Length)
                        {
                            long curPos = fs.Position;
                            fs.Seek(tiffBase + val, SeekOrigin.Begin);
                            byte[] empty = new byte[Math.Min(count, 512)];
                            fs.Write(empty, 0, empty.Length);
                            fs.Seek(curPos, SeekOrigin.Begin);
                        }
                    }
                    else if (tag == 0x013B && count >= 64 && safeBufferOffset == 0) // Artist tag
                    {
                        safeBufferOffset = val;
                        safeBufferSize = count;
                        if (tiffBase + val + count <= fs.Length)
                        {
                            long curPos = fs.Position;
                            fs.Seek(tiffBase + val, SeekOrigin.Begin);
                            byte[] empty = new byte[Math.Min(count, 512)];
                            fs.Write(empty, 0, empty.Length);
                            fs.Seek(curPos, SeekOrigin.Begin);
                        }
                    }
                    else if (tag == 0x8769) // ExifSubIFD pointer
                    {
                        exifIfdOffset = val;
                    }

                    uint byteLen = GetTagByteLength(type, count);
                    if (byteLen > 4 && val + byteLen <= app1DataLen)
                    {
                        maxUsedTiffOffset = Math.Max(maxUsedTiffOffset, val + byteLen);
                    }
                }

                // Scan IFD1 (thumbnail IFD) to account for thumbnail bounds
                fs.Seek(tiffBase + ifd0Offset + 2 + (uint)ifd0Count * 12, SeekOrigin.Begin);
                byte[] nextIfdBuf = new byte[4];
                if (fs.Read(nextIfdBuf, 0, 4) == 4)
                {
                    uint ifd1Offset = ReadUInt32(nextIfdBuf, 0, isLittleEndian);
                    if (ifd1Offset > 0 && ifd1Offset + 2 <= app1DataLen)
                    {
                        fs.Seek(tiffBase + ifd1Offset, SeekOrigin.Begin);
                        if (fs.Read(cntBuf, 0, 2) == 2)
                        {
                            ushort ifd1Count = ReadUInt16(cntBuf, 0, isLittleEndian);
                            maxUsedTiffOffset = Math.Max(maxUsedTiffOffset, ifd1Offset + 2 + (uint)ifd1Count * 12 + 4);
                            uint thumbOff = 0;
                            uint thumbLen = 0;
                            for (int k = 0; k < ifd1Count; k++)
                            {
                                fs.Seek(tiffBase + ifd1Offset + 2 + k * 12, SeekOrigin.Begin);
                                byte[] entry = new byte[12];
                                if (fs.Read(entry, 0, 12) < 12) break;
                                ushort tag = ReadUInt16(entry, 0, isLittleEndian);
                                ushort type = ReadUInt16(entry, 2, isLittleEndian);
                                uint count = ReadUInt32(entry, 4, isLittleEndian);
                                uint val = ReadUInt32(entry, 8, isLittleEndian);

                                if (tag == 0x0201) thumbOff = val;
                                else if (tag == 0x0202) thumbLen = val;
                                else if (tag == 0x0131 || tag == 0x013B || tag == 0x8298 || tag == 0xC62F || tag == 0xA420 || tag == 0xA431 || tag == 0xA435)
                                {
                                    long ifd1EntryPos = tiffBase + ifd1Offset + 2 + k * 12;
                                    if (count <= 4)
                                    {
                                        fs.Seek(ifd1EntryPos + 8, SeekOrigin.Begin);
                                        WriteUInt32(fs, 0, isLittleEndian);
                                        fs.Seek(ifd1EntryPos + 4, SeekOrigin.Begin);
                                        WriteUInt32(fs, 0, isLittleEndian);
                                    }
                                    else if (tiffBase + val + count <= fs.Length)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        byte[] empty = new byte[Math.Min(count, 512)];
                                        fs.Write(empty, 0, empty.Length);
                                        fs.Seek(ifd1EntryPos + 4, SeekOrigin.Begin);
                                        WriteUInt32(fs, 0, isLittleEndian);
                                    }
                                }
                                else
                                {
                                    uint byteLen = GetTagByteLength(type, count);
                                    if (byteLen > 4 && val + byteLen <= app1DataLen)
                                        maxUsedTiffOffset = Math.Max(maxUsedTiffOffset, val + byteLen);
                                }
                            }
                            if (thumbOff > 0)
                            {
                                maxUsedTiffOffset = Math.Max(maxUsedTiffOffset, thumbOff + thumbLen);
                            }
                        }
                    }
                }

                // Scan ExifSubIFD bounds
                if (exifIfdOffset > 0 && exifIfdOffset + 2 <= app1DataLen)
                {
                    fs.Seek(tiffBase + exifIfdOffset, SeekOrigin.Begin);
                    if (fs.Read(cntBuf, 0, 2) == 2)
                    {
                        ushort subCount = ReadUInt16(cntBuf, 0, isLittleEndian);
                        maxUsedTiffOffset = Math.Max(maxUsedTiffOffset, exifIfdOffset + 2 + (uint)subCount * 12 + 4);
                        for (int j = 0; j < subCount; j++)
                        {
                            fs.Seek(tiffBase + exifIfdOffset + 2 + j * 12, SeekOrigin.Begin);
                            byte[] entry = new byte[12];
                            if (fs.Read(entry, 0, 12) < 12) break;
                            ushort type = ReadUInt16(entry, 2, isLittleEndian);
                            uint count = ReadUInt32(entry, 4, isLittleEndian);
                            uint val = ReadUInt32(entry, 8, isLittleEndian);
                            uint byteLen = GetTagByteLength(type, count);
                            if (byteLen > 4 && val + byteLen <= app1DataLen)
                                maxUsedTiffOffset = Math.Max(maxUsedTiffOffset, val + byteLen);
                        }
                    }
                }

                // Fallback safe buffer allocation in APP1 trailing space
                if (safeBufferOffset == 0 && app1DataLen > 0)
                {
                    uint aligned = (maxUsedTiffOffset + 15) & ~15u;
                    if (app1DataLen > aligned + 64)
                    {
                        safeBufferOffset = aligned;
                        safeBufferSize = app1DataLen - aligned;
                    }
                }

                uint safeCursor = safeBufferOffset;

                // 6. Update IFD0 tags
                for (int i = 0; i < ifd0Count; i++)
                {
                    long entryPos = tiffBase + ifd0Offset + 2 + i * 12;
                    fs.Seek(entryPos, SeekOrigin.Begin);
                    byte[] entry = new byte[12];
                    if (fs.Read(entry, 0, 12) < 12) break;

                    ushort tag = ReadUInt16(entry, 0, isLittleEndian);
                    ushort type = ReadUInt16(entry, 2, isLittleEndian);
                    uint count = ReadUInt32(entry, 4, isLittleEndian);
                    uint val = ReadUInt32(entry, 8, isLittleEndian);

                    if (tag == 0x0110) // Model
                    {
                        byte[] targetModelBytes = Encoding.ASCII.GetBytes(targetModel + "\0");
                        if (targetModelBytes.Length <= count)
                        {
                            fs.Seek(tiffBase + val, SeekOrigin.Begin);
                            byte[] padded = new byte[count];
                            Array.Copy(targetModelBytes, padded, targetModelBytes.Length);
                            fs.Write(padded, 0, (int)count);
                        }
                        else if (safeCursor > 0 && safeCursor + targetModelBytes.Length <= safeBufferOffset + safeBufferSize)
                        {
                            fs.Seek(tiffBase + safeCursor, SeekOrigin.Begin);
                            fs.Write(targetModelBytes, 0, targetModelBytes.Length);

                            // Update IFD entry: Count and ValueOrOffset
                            fs.Seek(entryPos + 4, SeekOrigin.Begin);
                            WriteUInt32(fs, (uint)targetModelBytes.Length, isLittleEndian);
                            WriteUInt32(fs, safeCursor, isLittleEndian);
                            safeCursor = (safeCursor + (uint)targetModelBytes.Length + 3) & ~3u;
                        }
                    }
                    else if (tag == 0x010F) // Make
                    {
                        byte[] fujiBytes = Encoding.ASCII.GetBytes("FUJIFILM\0");
                        if (fujiBytes.Length <= count)
                        {
                            fs.Seek(tiffBase + val, SeekOrigin.Begin);
                            fs.Write(fujiBytes, 0, fujiBytes.Length);
                        }
                    }
                    else if (!patchModelOnly && tag == 0x0112 && exif.Orientation.HasValue) // Orientation
                    {
                        fs.Seek(entryPos + 8, SeekOrigin.Begin);
                        WriteUInt16(fs, exif.Orientation.Value, isLittleEndian);
                    }
                    else if (!patchModelOnly && tag == 0x0132 && !string.IsNullOrWhiteSpace(exif.DateTimeOriginal)) // DateTime
                    {
                        byte[] dateBytes = FormatDateBytes(exif.DateTimeOriginal);
                        if (count >= 20)
                        {
                            fs.Seek(tiffBase + val, SeekOrigin.Begin);
                            fs.Write(dateBytes, 0, Math.Min(dateBytes.Length, (int)count));
                        }
                    }
                    else if (tag == 0x0131 || tag == 0x013B || tag == 0x8298 || tag == 0xC62F)
                    {
                        // Wipe Software (0x0131), Artist (0x013B), Copyright (0x8298), SerialNumber (0xC62F)
                        if (count <= 4)
                        {
                            fs.Seek(entryPos + 8, SeekOrigin.Begin);
                            WriteUInt32(fs, 0, isLittleEndian);
                            fs.Seek(entryPos + 4, SeekOrigin.Begin);
                            WriteUInt32(fs, 0, isLittleEndian);
                        }
                        else if (tiffBase + val + count <= fs.Length)
                        {
                            if (val != safeBufferOffset || safeCursor == safeBufferOffset)
                            {
                                fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                byte[] empty = new byte[Math.Min(count, 512)];
                                fs.Write(empty, 0, empty.Length);
                            }
                            fs.Seek(entryPos + 4, SeekOrigin.Begin);
                            WriteUInt32(fs, 0, isLittleEndian);
                        }
                    }
                }

                // 7. Update ExifSubIFD tags (wipes serial numbers and owner even when patchModelOnly is true)
                if (exifIfdOffset > 0 && tiffBase + exifIfdOffset + 2 <= fs.Length)
                {
                    (double minFocal, double maxFocal, double minFNum, double maxFNum) = !patchModelOnly
                        ? ResolveLensSpecification(exif)
                        : (0.0, 0.0, 0.0, 0.0);

                    fs.Seek(tiffBase + exifIfdOffset, SeekOrigin.Begin);
                    if (fs.Read(cntBuf, 0, 2) == 2)
                    {
                        ushort subCount = ReadUInt16(cntBuf, 0, isLittleEndian);
                        for (int j = 0; j < subCount; j++)
                        {
                            long entryPos = tiffBase + exifIfdOffset + 2 + j * 12;
                            fs.Seek(entryPos, SeekOrigin.Begin);
                            byte[] entry = new byte[12];
                            if (fs.Read(entry, 0, 12) < 12) break;

                            ushort tag = ReadUInt16(entry, 0, isLittleEndian);
                            ushort type = ReadUInt16(entry, 2, isLittleEndian);
                            uint count = ReadUInt32(entry, 4, isLittleEndian);
                            uint val = ReadUInt32(entry, 8, isLittleEndian);

                            if (patchModelOnly && tag != 0xA420 && tag != 0xA431 && tag != 0xA435 && tag != 0x927C)
                            {
                                continue;
                            }

                            switch (tag)
                            {
                                case 0x829A: // ExposureTime
                                    if (exif.ExposureTime.HasValue && exif.ExposureTime.Value.Den > 0)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        WriteUInt32(fs, exif.ExposureTime.Value.Num, isLittleEndian);
                                        WriteUInt32(fs, exif.ExposureTime.Value.Den, isLittleEndian);
                                    }
                                    break;

                                case 0x9201: // ShutterSpeedValue (APEX srational)
                                    if (exif.ExposureTime.HasValue && exif.ExposureTime.Value.Den > 0 && exif.ExposureTime.Value.Num > 0)
                                    {
                                        double expSec = (double)exif.ExposureTime.Value.Num / exif.ExposureTime.Value.Den;
                                        double apex = -Math.Log2(expSec);
                                        int apexNum = (int)Math.Round(apex * 100);
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        WriteInt32(fs, apexNum, isLittleEndian);
                                        WriteInt32(fs, 100, isLittleEndian);
                                    }
                                    break;

                                case 0x829D: // FNumber
                                    if (exif.FNumber.HasValue && exif.FNumber.Value.Den > 0)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        WriteUInt32(fs, exif.FNumber.Value.Num, isLittleEndian);
                                        WriteUInt32(fs, exif.FNumber.Value.Den, isLittleEndian);
                                    }
                                    break;

                                case 0x9202: // ApertureValue (APEX rational)
                                    if (exif.FNumber.HasValue && exif.FNumber.Value.Den > 0 && exif.FNumber.Value.Num > 0)
                                    {
                                        double fNum = (double)exif.FNumber.Value.Num / exif.FNumber.Value.Den;
                                        double apex = 2.0 * Math.Log2(fNum);
                                        uint apexNum = (uint)Math.Max(0, Math.Round(apex * 100));
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        WriteUInt32(fs, apexNum, isLittleEndian);
                                        WriteUInt32(fs, 100, isLittleEndian);
                                    }
                                    break;

                                case 0x9205: // MaxApertureValue (APEX rational)
                                    if (minFNum > 0 && val + 8 <= fs.Length)
                                    {
                                        double apex = 2.0 * Math.Log2(minFNum);
                                        uint apexNum = (uint)Math.Max(0, Math.Round(apex * 100));
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        WriteUInt32(fs, apexNum, isLittleEndian);
                                        WriteUInt32(fs, 100, isLittleEndian);
                                    }
                                    else if (minFNum <= 0 && string.IsNullOrWhiteSpace(exif.LensModel) && val + 8 <= fs.Length)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        WriteUInt32(fs, 0, isLittleEndian);
                                        WriteUInt32(fs, 1, isLittleEndian);
                                    }
                                    break;

                                case 0x8827: // ISO
                                    if (exif.Iso.HasValue)
                                    {
                                        fs.Seek(entryPos + 8, SeekOrigin.Begin);
                                        WriteUInt16(fs, exif.Iso.Value, isLittleEndian);
                                    }
                                    break;

                                case 0x8832: // RecommendedExposureIndex
                                    if (exif.Iso.HasValue)
                                    {
                                        fs.Seek(entryPos + 8, SeekOrigin.Begin);
                                        if (type == 3) // SHORT
                                             WriteUInt16(fs, exif.Iso.Value, isLittleEndian);
                                        else if (type == 4) // LONG
                                             WriteUInt32(fs, exif.Iso.Value, isLittleEndian);
                                    }
                                    break;

                                case 0x9003: // DateTimeOriginal
                                    if (!string.IsNullOrWhiteSpace(exif.DateTimeOriginal))
                                    {
                                        byte[] dateBytes = FormatDateBytes(exif.DateTimeOriginal);
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        fs.Write(dateBytes, 0, Math.Min(dateBytes.Length, (int)count));
                                    }
                                    break;

                                case 0x9004: // DateTimeDigitized
                                    {
                                        string? dt = exif.DateTimeDigitized ?? exif.DateTimeOriginal;
                                        if (!string.IsNullOrWhiteSpace(dt))
                                        {
                                            byte[] dateBytes = FormatDateBytes(dt);
                                            fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                            fs.Write(dateBytes, 0, Math.Min(dateBytes.Length, (int)count));
                                        }
                                    }
                                    break;

                                case 0x920A: // FocalLength
                                    if (exif.FocalLength.HasValue && exif.FocalLength.Value.Den > 0)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        WriteUInt32(fs, exif.FocalLength.Value.Num, isLittleEndian);
                                        WriteUInt32(fs, exif.FocalLength.Value.Den, isLittleEndian);
                                    }
                                    break;

                                case 0x927C: // MakerNotes (Fujifilm MakerNotes)
                                    UpdateFujifilmMakerNotes(fs, tiffBase + val, minFocal, maxFocal, minFNum, maxFNum, isLittleEndian);
                                    break;

                                case 0xA405: // FocalLengthIn35mmFormat
                                    if (exif.FocalLengthIn35mmFormat.HasValue)
                                    {
                                        fs.Seek(entryPos + 8, SeekOrigin.Begin);
                                        WriteUInt16(fs, exif.FocalLengthIn35mmFormat.Value, isLittleEndian);
                                    }
                                    break;

                                case 0xA434: // LensModel
                                    if (!string.IsNullOrWhiteSpace(exif.LensModel))
                                    {
                                        byte[] lensBytes = Encoding.ASCII.GetBytes(exif.LensModel + "\0");
                                        if (lensBytes.Length <= count)
                                        {
                                            fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                            byte[] paddedLens = new byte[count];
                                            Array.Copy(lensBytes, paddedLens, lensBytes.Length);
                                            fs.Write(paddedLens, 0, (int)count);
                                        }
                                        else if (safeCursor > 0 && safeCursor + lensBytes.Length <= safeBufferOffset + safeBufferSize)
                                        {
                                            fs.Seek(tiffBase + safeCursor, SeekOrigin.Begin);
                                            fs.Write(lensBytes, 0, lensBytes.Length);

                                            // Update IFD entry: Count and ValueOrOffset
                                            fs.Seek(entryPos + 4, SeekOrigin.Begin);
                                            WriteUInt32(fs, (uint)lensBytes.Length, isLittleEndian);
                                            WriteUInt32(fs, safeCursor, isLittleEndian);
                                            safeCursor = (safeCursor + (uint)lensBytes.Length + 3) & ~3u;
                                        }
                                    }
                                    else
                                    {
                                        // Clear donor lens model so donor kit lens does not falsely display
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        byte[] empty = new byte[Math.Min(count, 32)];
                                        fs.Write(empty, 0, empty.Length);
                                    }
                                    break;

                                case 0xA433: // LensMake
                                    string? lensMake = exif.LensMake ?? GetLensMake(exif.Make, exif.LensModel);
                                    if (!string.IsNullOrWhiteSpace(lensMake))
                                    {
                                        byte[] makeBytes = Encoding.ASCII.GetBytes(lensMake + "\0");
                                        if (makeBytes.Length <= count)
                                        {
                                            fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                            byte[] padded = new byte[count];
                                            Array.Copy(makeBytes, padded, makeBytes.Length);
                                            fs.Write(padded, 0, (int)count);
                                        }
                                        else if (safeCursor > 0 && safeCursor + makeBytes.Length <= safeBufferOffset + safeBufferSize)
                                        {
                                            fs.Seek(tiffBase + safeCursor, SeekOrigin.Begin);
                                            fs.Write(makeBytes, 0, makeBytes.Length);

                                            fs.Seek(entryPos + 4, SeekOrigin.Begin);
                                            WriteUInt32(fs, (uint)makeBytes.Length, isLittleEndian);
                                            WriteUInt32(fs, safeCursor, isLittleEndian);
                                            safeCursor = (safeCursor + (uint)makeBytes.Length + 3) & ~3u;
                                        }
                                    }
                                    else
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        byte[] empty = new byte[Math.Min(count, 32)];
                                        fs.Write(empty, 0, empty.Length);
                                    }
                                    break;

                                case 0xA435: // LensSerialNumber
                                case 0xA420: // OwnerName / CameraOwnerName
                                case 0xA431: // BodySerialNumber
                                    if (count <= 4)
                                    {
                                        fs.Seek(entryPos + 8, SeekOrigin.Begin);
                                        WriteUInt32(fs, 0, isLittleEndian);
                                        fs.Seek(entryPos + 4, SeekOrigin.Begin);
                                        WriteUInt32(fs, 0, isLittleEndian);
                                    }
                                    else if (tiffBase + val + count <= fs.Length)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        byte[] emptySerial = new byte[Math.Min(count, 128)];
                                        fs.Write(emptySerial, 0, emptySerial.Length);
                                        fs.Seek(entryPos + 4, SeekOrigin.Begin);
                                        WriteUInt32(fs, 0, isLittleEndian);
                                    }
                                    break;

                                case 0xA432: // LensSpecification
                                    if (minFocal > 0 && maxFocal > 0 && val + 32 <= fs.Length)
                                    {
                                        (uint fMinNum, uint fMinDen) = ToRational(minFocal);
                                        (uint fMaxNum, uint fMaxDen) = ToRational(maxFocal);
                                        (uint fnMinNum, uint fnMinDen) = ToRational(minFNum > 0 ? minFNum : (exif.FNumber.HasValue ? (double)exif.FNumber.Value.Num / exif.FNumber.Value.Den : 2.8));
                                        (uint fnMaxNum, uint fnMaxDen) = ToRational(maxFNum > 0 ? maxFNum : (fnMinNum / (double)fnMinDen));

                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        WriteUInt32(fs, fMinNum, isLittleEndian);
                                        WriteUInt32(fs, fMinDen, isLittleEndian);
                                        WriteUInt32(fs, fMaxNum, isLittleEndian);
                                        WriteUInt32(fs, fMaxDen, isLittleEndian);
                                        WriteUInt32(fs, fnMinNum, isLittleEndian);
                                        WriteUInt32(fs, fnMinDen, isLittleEndian);
                                        WriteUInt32(fs, fnMaxNum, isLittleEndian);
                                        WriteUInt32(fs, fnMaxDen, isLittleEndian);
                                    }
                                    else if (string.IsNullOrWhiteSpace(exif.LensModel) && val + 32 <= fs.Length)
                                    {
                                        fs.Seek(tiffBase + val, SeekOrigin.Begin);
                                        byte[] zeros = new byte[Math.Min(count * 8, 32)];
                                        fs.Write(zeros, 0, zeros.Length);
                                    }
                                    break;
                            }
                        }
                    }
                }

                fs.Flush();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Fujify] Error applying RAF metadata: {ex.Message}");
                return false;
            }
        }

        #region Helpers

        private static byte[] FormatDateBytes(string dateStr)
        {
            byte[] bytes = new byte[20];
            byte[] ascii = Encoding.ASCII.GetBytes(dateStr);
            Array.Copy(ascii, bytes, Math.Min(ascii.Length, 19));
            bytes[19] = 0;
            return bytes;
        }

        public static bool TryParseRational(string? text, out uint num, out uint den)
        {
            num = 0;
            den = 1;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim().TrimEnd('s', 'S', 'm', 'M');

            if (text.Contains('/'))
            {
                var parts = text.Split('/');
                if (parts.Length == 2 &&
                    uint.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out uint n) &&
                    uint.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out uint d) &&
                    d > 0)
                {
                    num = n;
                    den = d;
                    return true;
                }
            }
            else if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double val) && val > 0)
            {
                if (val < 1.0)
                {
                    double exactDen = 1.0 / val;
                    uint roundedDen = (uint)Math.Round(exactDen);
                    if (roundedDen > 0 && Math.Abs(exactDen - roundedDen) < 0.05)
                    {
                        num = 1;
                        den = roundedDen;
                        return true;
                    }
                }

                num = (uint)Math.Round(val * 10000);
                den = 10000;
                return true;
            }

            return false;
        }

        public static bool TryParseFNumber(string? text, out uint num, out uint den)
        {
            num = 0;
            den = 1;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim().TrimStart('f', 'F', '/').Trim();

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double val) && val > 0)
            {
                num = (uint)Math.Round(val * 10);
                den = 10;
                return true;
            }

            return false;
        }

        public static bool TryParseFocalLength(string? text, out uint num, out uint den)
        {
            num = 0;
            den = 1;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Replace("mm", "", StringComparison.OrdinalIgnoreCase).Trim();

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double val) && val > 0)
            {
                num = (uint)Math.Round(val * 10);
                den = 10;
                return true;
            }

            return false;
        }

        public static bool TryParseIso(string? text, out ushort iso)
        {
            iso = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Replace("ISO", "", StringComparison.OrdinalIgnoreCase).Trim();
            return ushort.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out iso);
        }

        public static ushort ParseOrientation(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 1;
            if (ushort.TryParse(text, out ushort val) && val >= 1 && val <= 8) return val;
            if (text.Contains("90", StringComparison.OrdinalIgnoreCase)) return 6;
            if (text.Contains("180", StringComparison.OrdinalIgnoreCase)) return 3;
            if (text.Contains("270", StringComparison.OrdinalIgnoreCase)) return 8;
            return 1;
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

        private static void WriteUInt16(Stream stream, ushort value, bool isLittleEndian)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian != isLittleEndian) Array.Reverse(bytes);
            stream.Write(bytes, 0, 2);
        }

        private static void WriteUInt32(Stream stream, uint value, bool isLittleEndian)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian != isLittleEndian) Array.Reverse(bytes);
            stream.Write(bytes, 0, 4);
        }

        private static void WriteInt32(Stream stream, int value, bool isLittleEndian)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian != isLittleEndian) Array.Reverse(bytes);
            stream.Write(bytes, 0, 4);
        }

        #endregion
    }
}

