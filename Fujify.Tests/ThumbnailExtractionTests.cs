using System;
using System.IO;
using System.Text;
using Fujify.Services;
using SkiaSharp;
using Xunit;

namespace Fujify.Tests
{
    public class ThumbnailExtractionTests : IDisposable
    {
        private readonly string _testDir;

        public ThumbnailExtractionTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"fujify_thumb_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        private static byte[] CreateValidJpegBytes(int width = 120, int height = 80)
        {
            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.SeaGreen);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            return data.ToArray();
        }

        [Fact]
        public void RealSonyArw_ShouldExtractEmbeddedThumbnail()
        {
            string rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string arwPath = Path.Combine(rootDir, "DSC00381.ARW");

            if (File.Exists(arwPath))
            {
                var jpegBytes = ThumbnailService.TryExtractEmbeddedJpeg(arwPath);
                Assert.NotNull(jpegBytes);
                Assert.True(jpegBytes.Length > 1000);

                using var bmp = SKBitmap.Decode(jpegBytes);
                Assert.NotNull(bmp);
                Assert.True(bmp.Width > 0);
                Assert.True(bmp.Height > 0);
            }
        }

        [Fact]
        public void CanonCr3_ShouldExtractPreviewFromPrvwBox()
        {
            byte[] jpeg = CreateValidJpegBytes();
            string cr3Path = Path.Combine(_testDir, "test_canon.cr3");

            using (var fs = new FileStream(cr3Path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // 1. ftyp box
                bw.Write(new byte[] { 0, 0, 0, 16 }); // size 16
                bw.Write(Encoding.ASCII.GetBytes("ftyp"));
                bw.Write(Encoding.ASCII.GetBytes("crx "));
                bw.Write(new byte[] { 0, 0, 0, 0 }); // minor version

                // 2. PRVW box
                int prvwSize = 8 + 4 + 4 + 4 + jpeg.Length;
                bw.Write(new byte[] { (byte)(prvwSize >> 24), (byte)(prvwSize >> 16), (byte)(prvwSize >> 8), (byte)prvwSize });
                bw.Write(Encoding.ASCII.GetBytes("PRVW"));
                bw.Write(new byte[] { 0, 0, 0, 0 }); // version/flags
                bw.Write(new byte[] { 0, 120, 0, 80 }); // 120x80
                bw.Write(new byte[] { (byte)(jpeg.Length >> 24), (byte)(jpeg.Length >> 16), (byte)(jpeg.Length >> 8), (byte)jpeg.Length });
                bw.Write(jpeg);
            }

            var jpegBytes = ThumbnailService.TryExtractEmbeddedJpeg(cr3Path);
            Assert.NotNull(jpegBytes);
            Assert.True(jpegBytes.Length >= jpeg.Length);

            using var bmp = SKBitmap.Decode(jpegBytes);
            Assert.NotNull(bmp);
            Assert.Equal(120, bmp.Width);
            Assert.Equal(80, bmp.Height);
        }

        [Fact]
        public void NikonNef_ShouldExtractPreviewFromSubIfd()
        {
            byte[] jpeg = CreateValidJpegBytes();
            string nefPath = Path.Combine(_testDir, "test_nikon.nef");

            using (var fs = new FileStream(nefPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // Header: Little-endian TIFF, IFD0 offset = 8
                bw.Write(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00 });

                // IFD0: 1 entry (SubIFDs tag 0x014A) pointing to SubIFD at offset 26
                bw.Write((ushort)1); // count = 1
                bw.Write((ushort)0x014A); // tag SubIFDs
                bw.Write((ushort)4); // type LONG
                bw.Write((uint)1); // count = 1
                bw.Write((uint)26); // offset of SubIFD = 26
                bw.Write((uint)0); // next IFD = 0

                // SubIFD at offset 26:
                // 2 entries: 0x0201 (jpegOffset) and 0x0202 (jpegLength)
                uint jpegOffset = 26 + 2 + (2 * 12) + 4; // = 56
                bw.Write((ushort)2); // count = 2
                // Entry 1: 0x0201
                bw.Write((ushort)0x0201);
                bw.Write((ushort)4);
                bw.Write((uint)1);
                bw.Write((uint)jpegOffset);
                // Entry 2: 0x0202
                bw.Write((ushort)0x0202);
                bw.Write((ushort)4);
                bw.Write((uint)1);
                bw.Write((uint)jpeg.Length);
                bw.Write((uint)0); // next IFD = 0

                // JPEG payload
                bw.Write(jpeg);
            }

            var jpegBytes = ThumbnailService.TryExtractEmbeddedJpeg(nefPath);
            Assert.NotNull(jpegBytes);
            Assert.Equal(jpeg.Length, jpegBytes.Length);

            using var bmp = SKBitmap.Decode(jpegBytes);
            Assert.NotNull(bmp);
            Assert.Equal(120, bmp.Width);
            Assert.Equal(80, bmp.Height);
        }

        [Fact]
        public void OlympusOrf_ShouldExtractPreviewFromIiroMagicHeader()
        {
            byte[] jpeg = CreateValidJpegBytes();
            string orfPath = Path.Combine(_testDir, "test_olympus.orf");

            using (var fs = new FileStream(orfPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // Header: Little-endian Olympus "IIRO" (0x49 0x49 0x52 0x4F), IFD0 offset = 8
                bw.Write(new byte[] { 0x49, 0x49, 0x52, 0x4F, 0x08, 0x00, 0x00, 0x00 });

                // IFD0: 1 dummy entry, with next IFD pointing to IFD1 at offset 26
                bw.Write((ushort)1);
                bw.Write((ushort)0x0100); // ImageWidth
                bw.Write((ushort)3); // SHORT
                bw.Write((uint)1);
                bw.Write((uint)120);
                bw.Write((uint)26); // next IFD = IFD1 at 26

                // IFD1 at offset 26:
                uint jpegOffset = 26 + 2 + (2 * 12) + 4; // = 56
                bw.Write((ushort)2);
                bw.Write((ushort)0x0201);
                bw.Write((ushort)4);
                bw.Write((uint)1);
                bw.Write((uint)jpegOffset);
                bw.Write((ushort)0x0202);
                bw.Write((ushort)4);
                bw.Write((uint)1);
                bw.Write((uint)jpeg.Length);
                bw.Write((uint)0); // next IFD = 0

                // JPEG payload
                bw.Write(jpeg);
            }

            var jpegBytes = ThumbnailService.TryExtractEmbeddedJpeg(orfPath);
            Assert.NotNull(jpegBytes);
            Assert.Equal(jpeg.Length, jpegBytes.Length);

            using var bmp = SKBitmap.Decode(jpegBytes);
            Assert.NotNull(bmp);
            Assert.Equal(120, bmp.Width);
            Assert.Equal(80, bmp.Height);
        }

        [Fact]
        public void ScannerFallback_ShouldExtractEmbeddedJpegFromArbitraryContainer()
        {
            byte[] jpeg = CreateValidJpegBytes();
            string rawPath = Path.Combine(_testDir, "test_fallback.raw");

            using (var fs = new FileStream(rawPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // 1024 bytes of dummy header
                byte[] dummyHeader = new byte[1024];
                new Random(42).NextBytes(dummyHeader);
                bw.Write(dummyHeader);

                // Embedded JPEG
                bw.Write(jpeg);

                // 2048 bytes of trailing raw data
                byte[] trailing = new byte[2048];
                bw.Write(trailing);
            }

            var jpegBytes = ThumbnailService.TryExtractEmbeddedJpeg(rawPath);
            Assert.NotNull(jpegBytes);
            Assert.True(jpegBytes.Length > 0);

            using var bmp = SKBitmap.Decode(jpegBytes);
            Assert.NotNull(bmp);
            Assert.Equal(120, bmp.Width);
            Assert.Equal(80, bmp.Height);
        }

        [Fact]
        public void CanonCiff_ShouldExtractPreviewFromMockCrw()
        {
            byte[] jpeg = CreateValidJpegBytes(120, 80);
            string ciffPath = Path.Combine(_testDir, "test_canon.crw");

            using (var fs = new FileStream(ciffPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // 1. CIFF Header (26 bytes)
                bw.Write(new byte[] { 0x49, 0x49 }); // "II" (little-endian)
                bw.Write((uint)26); // header length = 26
                bw.Write(Encoding.ASCII.GetBytes("HEAPCCDR")); // CIFF raw heap signature
                bw.Write(new byte[12]); // padding up to 26 bytes

                // 2. JPEG payload (at offset 0 relative to blockBase = 26)
                bw.Write(jpeg);

                // 3. Root directory (starts at offset = jpeg.Length relative to blockBase = 26)
                uint dirOffset = (uint)jpeg.Length;
                bw.Write((ushort)1); // 1 record
                // Record 0: tag 0x2007 (JPEG Image), length, offset
                bw.Write((ushort)0x2007);
                bw.Write((uint)jpeg.Length);
                bw.Write((uint)0); // offset = 0 relative to blockBase = 26

                // 4. Last 4 bytes of file: directory offset relative to blockBase = 26
                bw.Write(dirOffset);
            }

            var jpegBytes = ThumbnailService.TryExtractEmbeddedJpeg(ciffPath);
            Assert.NotNull(jpegBytes);
            Assert.Equal(jpeg.Length, jpegBytes.Length);

            using var bmp = SKBitmap.Decode(jpegBytes);
            Assert.NotNull(bmp);
            Assert.Equal(120, bmp.Width);
            Assert.Equal(80, bmp.Height);
        }

        [Fact]
        public void RealCanonD60_ShouldExtractEmbeddedJpegPreview()
        {
            string d60Path = @"D:\Users\isisk\Documents\test sony\test files\Canon-EOS-D60.CRW";
            if (!File.Exists(d60Path))
            {
                string altPath = @"D:\Users\isisk\Documents\test sony\test files\raws\canon\d60\RAW_CANON_D60_ARGB.CRW";
                if (File.Exists(altPath))
                {
                    d60Path = altPath;
                }
                else
                {
                    return; // Skip if file not found in test environment
                }
            }

            var jpegBytes = ThumbnailService.TryExtractEmbeddedJpeg(d60Path);
            Assert.NotNull(jpegBytes);
            Assert.True(jpegBytes.Length > 100_000, $"Expected large embedded JPEG preview, got {jpegBytes.Length} bytes");

            using var bmp = SKBitmap.Decode(jpegBytes);
            Assert.NotNull(bmp);
            Assert.True(bmp.Width > 500, $"Expected preview width > 500, got {bmp.Width}");
            Assert.True(bmp.Height > 500, $"Expected preview height > 500, got {bmp.Height}");
        }
    }
}
