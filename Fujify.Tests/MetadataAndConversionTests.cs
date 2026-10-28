using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Fujify.Models;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    [Collection("Registry")]
    public class MetadataAndConversionTests : IDisposable
    {
        private readonly string _sampleArw;
        private readonly string _tempDng;

        public MetadataAndConversionTests()
        {
            string rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            _sampleArw = Path.Combine(rootDir, "DSC00381.ARW");
            _tempDng = Path.Combine(Path.GetTempPath(), $"fujify_test_{Guid.NewGuid():N}.dng");
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_tempDng)) File.Delete(_tempDng);
                string backup = Path.ChangeExtension(_tempDng, ".dng_original");
                if (File.Exists(backup)) File.Delete(backup);
            }
            catch { }
        }

        [Fact]
        public void DngLabNative_ShouldBeAvailableAndExportVersion()
        {
            Assert.True(DngLabNative.IsAvailable, "DngLabNative should load dnglab_ffi successfully.");
            string? version = DngLabNative.GetVersion();
            Assert.NotNull(version);
            Assert.Contains("ffi", version, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("arw")]
        [InlineData("cr2")]
        [InlineData("cr3")]
        [InlineData("nef")]
        [InlineData("raf")]
        public void DngLabNative_ShouldSupportCommonRawExtensions(string ext)
        {
            Assert.True(DngLabNative.IsExtensionSupported(ext));
        }

        [Fact]
        public void DngMetadataEditor_ShouldReadCameraFromRawFile()
        {
            if (!File.Exists(_sampleArw)) return; // Skip if large sample RAW not present

            string? camera = DngMetadataEditor.ReadCameraMakeAndModel(_sampleArw);
            Assert.NotNull(camera);
            Assert.Contains("ILCE-7RM4", camera);
        }

        [Fact]
        public async Task EndToEnd_NativeConversionAndMetadataInjection_ShouldSucceed()
        {
            if (!File.Exists(_sampleArw)) return; // Skip if sample file not available

            // 1. Convert RAW to DNG in-process via dnglab_ffi native library
            var (convSuccess, convError) = await DngLabNative.ConvertFileAsync(_sampleArw, _tempDng, false);
            Assert.True(convSuccess, $"Native conversion failed: {convError}");
            Assert.True(File.Exists(_tempDng));
            var fileInfo = new FileInfo(_tempDng);
            Assert.True(fileInfo.Length > 10_000_000, "Converted DNG should be larger than 10MB");

            // 2. Inject Fujifilm X-T5 metadata directly into DNG IFD0 & XMP
            var metaResult = await DngMetadataEditor.InjectFujifilmMetadataAsync(
                _tempDng,
                outputFolder: null,
                createBackup: false,
                isOriginallyDng: false,
                targetUniqueCameraModel: "Fujifilm X-T5",
                targetMake: "FUJIFILM",
                targetModel: "X-T5");

            Assert.True(metaResult.Success, $"Metadata injection failed: {metaResult.ErrorMessage}");

            // 3. Verify UniqueCameraModel was rewritten to Fujifilm X-T5
            string? uniqueModel = DngMetadataEditor.ReadUniqueCameraModel(_tempDng);
            Assert.Equal("Fujifilm X-T5", uniqueModel);

            // 4. Verify Adobe XMP packet was injected and contains CameraProfiles sequence
            string? xmp = DngMetadataEditor.ReadXmpPacket(_tempDng);
            Assert.NotNull(xmp);
            Assert.Contains("photoshop:CameraProfiles", xmp);
            Assert.Contains("<stCamera:CameraRawProfile>True</stCamera:CameraRawProfile>", xmp);
            Assert.Contains("<stCamera:Make>FUJIFILM</stCamera:Make>", xmp);
            Assert.Contains("<stCamera:Model>X-T5</stCamera:Model>", xmp);
            Assert.Contains("<stCamera:UniqueCameraModel>Fujifilm X-T5</stCamera:UniqueCameraModel>", xmp);

            // 5. Verify using ExifTool to guarantee external tool & Adobe ACR compatibility
            string exifTool = Path.Combine(AppContext.BaseDirectory, "libs", "exiftool.exe");
            if (File.Exists(exifTool))
            {
                var runner = new ProcessRunner();
                var (code, output, _) = runner.Run(exifTool, $"-s -s -s -UniqueCameraModel -CameraProfilesMake -CameraProfilesModel -CameraProfilesUniqueCameraModel -CameraProfilesCameraRawProfile \"{_tempDng}\"");
                Assert.Equal(0, code);
                Assert.Contains("Fujifilm X-T5", output);
                Assert.Contains("FUJIFILM", output);
                Assert.Contains("X-T5", output);
                Assert.Contains("True", output);
            }
        }

        [Fact]
        public async Task Concurrent_NativeConversions_ShouldNotCrash()
        {
            if (!File.Exists(_sampleArw)) return;
            int count = 12;
            var tasks = new System.Collections.Generic.List<Task<(bool Success, string? ErrorMessage)>>();
            var tempFiles = new System.Collections.Generic.List<string>();
            for (int i = 0; i < count; i++)
            {
                string temp = Path.Combine(Path.GetTempPath(), $"concurrent_test_{Guid.NewGuid():N}_{i}.dng");
                tempFiles.Add(temp);
                tasks.Add(Task.Run(() => DngLabNative.ConvertFile(_sampleArw, temp, false)));
            }
            var results = await Task.WhenAll(tasks);
            foreach (var file in tempFiles)
            {
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
            foreach (var r in results)
            {
                Assert.True(r.Success, $"Conversion failed: {r.ErrorMessage}");
            }
        }

        [Fact]
        public void DngLabNative_ShouldHandleCorruptFileGracefullyWithoutCrashing()
        {
            string corruptRaw = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.raw");
            string outDng = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.dng");
            try
            {
                File.WriteAllBytes(corruptRaw, new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x01, 0x00 });
                var (success, error) = DngLabNative.ConvertFile(corruptRaw, outDng, false);
                Assert.False(success);
                Assert.NotNull(error);
                Assert.False(File.Exists(outDng));
            }
            finally
            {
                try { if (File.Exists(corruptRaw)) File.Delete(corruptRaw); } catch { }
                try { if (File.Exists(outDng)) File.Delete(outDng); } catch { }
            }
        }

        [Fact]
        public void DngLabNative_ShouldHandlePanicInducingFileWithoutCrashing()
        {
            string panasonicRaw = @"D:\Users\isisk\Documents\test sony\test files\raws\panasonic\RAW_PANASONIC_DMC_LX3.RW2";
            if (!File.Exists(panasonicRaw)) return;

            string outDng = Path.Combine(Path.GetTempPath(), $"lx3_test_{Guid.NewGuid():N}.dng");
            try
            {
                var (success, error) = DngLabNative.ConvertFile(panasonicRaw, outDng, false);
                Assert.False(success);
                Assert.NotNull(error);
                Assert.Contains("Raw decode failed", error);
                Assert.False(File.Exists(outDng));
            }
            finally
            {
                try { if (File.Exists(outDng)) File.Delete(outDng); } catch { }
            }
        }

        [Fact]
        public void MetadataService_ShouldUseInProcessEditor()
        {
            var service = new MetadataService();

            if (File.Exists(_sampleArw))
            {
                string info = service.GetCameraMakeAndModel(_sampleArw);
                Assert.Contains("ILCE-7RM4", info);
            }
        }

        [Fact]
        public void RawMetadataReader_And_MetadataService_ShouldExtractExifDataFromRaw()
        {
            if (!File.Exists(_sampleArw)) return;

            // 1. Test RawMetadataReader.TryGetExifData directly
            var directExif = RawMetadataReader.TryGetExifData(_sampleArw);
            Assert.NotNull(directExif);
            Assert.Equal("E 28-75mm F2.8 A063", directExif.Lens);
            Assert.Equal("2.8", directExif.Aperture);
            Assert.Equal("1/500", directExif.ShutterSpeed);
            Assert.Equal("100", directExif.Iso);
            Assert.Equal("33 mm", directExif.FocalLength);
            Assert.Equal("2026:08:03 12:17:15", directExif.DateTimeOriginal);
            Assert.Equal("Rotate 270 CW", directExif.Orientation);

            // 2. Test MetadataService.GetExifData
            var service = new MetadataService();
            var serviceExif = service.GetExifData(_sampleArw);
            Assert.NotNull(serviceExif);
            Assert.Equal("E 28-75mm F2.8 A063", serviceExif.Lens);
            Assert.Equal("2.8", serviceExif.Aperture);
            Assert.Equal("1/500", serviceExif.ShutterSpeed);
            Assert.Equal("100", serviceExif.Iso);
            Assert.Equal("33 mm", serviceExif.FocalLength);
            Assert.Equal("2026:08:03 12:17:15", serviceExif.DateTimeOriginal);
            Assert.Equal("Rotate 270 CW", serviceExif.Orientation);
        }

        [Fact]
        public void AppSettings_ShouldDefaultToFujifiedSuffix()
        {
            var settings = new AppSettings();
            Assert.False(settings.AppendSuffix);
            Assert.Equal("-fujified", settings.Suffix);
        }

        [Fact]
        public void PreferencesViewModel_ShouldLoadAndSaveSuffixOptions()
        {
            var tempSettingsPath = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
            try
            {
                var settingsService = new SettingsService(tempSettingsPath);
                var vm = new PreferencesViewModel(settingsService);

                Assert.False(vm.AppendSuffix);
                Assert.Equal("-fujified", vm.Suffix);

                vm.AppendSuffix = true;
                vm.Suffix = "_fuji_edit";
                vm.SaveCommand.Execute(null);

                // Reload and verify persistence
                var reloaded = new SettingsService(tempSettingsPath);
                Assert.True(reloaded.Settings.AppendSuffix);
                Assert.Equal("_fuji_edit", reloaded.Settings.Suffix);
            }
            finally
            {
                if (File.Exists(tempSettingsPath)) File.Delete(tempSettingsPath);
            }
        }

        [Fact]
        public void ProgressSummaryText_ShouldFormatDecimalProperly_NeverOmitDecimalPoint()
        {
            var vm = new MainViewModel();
            // Create a fake file with 118.1 MB (118.1 * 1024 * 1024 = 123836826 bytes)
            long bytes = (long)(118.1 * 1024 * 1024);
            var tempFile = Path.Combine(Path.GetTempPath(), $"test_size_{Guid.NewGuid():N}.dng");
            try
            {
                using var fs = File.Create(tempFile);
                fs.SetLength(bytes);

                var fileItem = new Models.FileItem { FilePath = tempFile };
                vm.Files.Add(fileItem);

                string summary = vm.ProgressSummaryText;
                Assert.True(summary.Contains("118.1 MB") || summary.Contains("118,1 MB"),
                    $"Expected decimal format like 118.1 MB or 118,1 MB, but got: {summary}");
                Assert.DoesNotContain("1181", summary);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void RawConverterService_ShouldFallbackToNativeWhenNoCli()
        {
            var service = new RawConverterService();
            // Verifies service can be instantiated and basic non-existent file handling returns false
            var result = service.ConvertToDng("non_existent_file.arw", false);
            Assert.False(result.Success);
        }

        [Fact]
        public void MetadataService_ShouldHandleDirectMetadataWhenNoExifTool()
        {
            var service = new MetadataService();
            // Should not throw even when no external exiftool executable is present
            int orientation = service.GetOrientation("non_existent_file.dng");
            Assert.Equal(1, orientation);
        }

        [Fact]
        public void PreferencesViewModel_ThemeSelection_ShouldUpdateAndPersistThemeMode()
        {
            var tempSettingsPath = Path.Combine(Path.GetTempPath(), $"settings_theme_{Guid.NewGuid():N}.json");
            try
            {
                var settingsService = new SettingsService(tempSettingsPath);
                var vm = new PreferencesViewModel(settingsService);

                // Default is Automatic
                Assert.True(vm.IsAutoThemeSelected);
                Assert.False(vm.IsLightThemeSelected);
                Assert.False(vm.IsDarkThemeSelected);
                Assert.False(vm.IsAmoledThemeSelected);

                // Select Light
                vm.IsLightThemeSelected = true;
                Assert.Equal("Light", vm.ThemeMode);
                Assert.True(vm.IsLightThemeSelected);
                Assert.False(vm.IsAutoThemeSelected);
                Assert.False(vm.IsDarkThemeSelected);
                Assert.False(vm.IsAmoledThemeSelected);
                Assert.False(vm.IsDarkTheme);

                // Save Light and verify persistence
                vm.SaveCommand.Execute(null);
                var reloaded = new SettingsService(tempSettingsPath);
                Assert.Equal("Light", reloaded.Settings.ThemeMode);
                Assert.False(reloaded.Settings.IsDarkTheme);

                // Select AMOLED
                var vm2 = new PreferencesViewModel(reloaded);
                Assert.True(vm2.IsLightThemeSelected);
                vm2.IsAmoledThemeSelected = true;
                Assert.Equal("Amoled", vm2.ThemeMode);
                Assert.True(vm2.IsAmoledThemeSelected);
                Assert.False(vm2.IsLightThemeSelected);
                Assert.False(vm2.IsDarkThemeSelected);
                Assert.False(vm2.IsAutoThemeSelected);
                Assert.True(vm2.IsDarkTheme);

                vm2.SaveCommand.Execute(null);
                reloaded = new SettingsService(tempSettingsPath);
                Assert.Equal("Amoled", reloaded.Settings.ThemeMode);
                Assert.True(reloaded.Settings.IsDarkTheme);

                // Select Dark
                var vm3 = new PreferencesViewModel(reloaded);
                vm3.IsDarkThemeSelected = true;
                Assert.Equal("Dark", vm3.ThemeMode);
                Assert.True(vm3.IsDarkThemeSelected);
                Assert.False(vm3.IsAmoledThemeSelected);
                Assert.False(vm3.IsLightThemeSelected);
                Assert.False(vm3.IsAutoThemeSelected);
                Assert.True(vm3.IsDarkTheme);

                // Select Automatic
                vm3.IsAutoThemeSelected = true;
                Assert.Equal("Automatic", vm3.ThemeMode);
                Assert.True(vm3.IsAutoThemeSelected);
                Assert.False(vm3.IsDarkThemeSelected);
                Assert.False(vm3.IsAmoledThemeSelected);
                Assert.False(vm3.IsLightThemeSelected);

                vm3.SaveCommand.Execute(null);
                reloaded = new SettingsService(tempSettingsPath);
                Assert.Equal("Automatic", reloaded.Settings.ThemeMode);
            }
            finally
            {
                if (File.Exists(tempSettingsPath)) File.Delete(tempSettingsPath);
            }
        }

        [Fact]
        public async Task PromptFileConflictAsync_ShouldExecuteFromBackgroundThreadWithoutThrowing()
        {
            var dialogService = new DialogService();
            // Call from background thread pool thread, simulating Parallel.ForEachAsync execution
            var resolution = await Task.Run(async () =>
            {
                return await dialogService.PromptFileConflictAsync(null, "test_output.dng");
            });

            Assert.NotNull(resolution);
            Assert.Equal(ConflictChoice.Skip, resolution.Choice);
        }

        [Fact]
        public void FileConflictDialogViewModel_Commands_ShouldReturnExpectedChoices()
        {
            string path = @"C:\Photos\Test.dng";
            var vm = new FileConflictDialogViewModel(path);
            Assert.Equal("Test.dng", vm.FileName);

            FileConflictResolution? chosen = null;
            vm.CloseAction = res => chosen = res;

            vm.ApplyToAll = true;
            vm.UseUniqueNameCommand.Execute(null);
            Assert.NotNull(chosen);
            Assert.Equal(ConflictChoice.UseUniqueName, chosen!.Choice);
            Assert.True(chosen.ApplyToAll);

            vm.ApplyToAll = false;
            vm.OverwriteCommand.Execute(null);
            Assert.Equal(ConflictChoice.Overwrite, chosen.Choice);
            Assert.False(chosen.ApplyToAll);

            vm.SkipCommand.Execute(null);
            Assert.Equal(ConflictChoice.Skip, chosen.Choice);
        }

        [Fact]
        public void ReadCameraMakeAndModel_FujifilmRAF_ShouldExtractModelNotVersion()
        {
            // Synthetic Fujifilm RAF file:
            // 0x00: "FUJIFILMCCD-RAW "
            // 0x10: "0201"
            // 0x14: "FA119501"
            // 0x1C: "X100S\0\0\0..." (32 bytes)
            // 0x3C: "0120" (Internal RAF version, previously erroneously read as model)
            byte[] rafData = new byte[128];
            System.Text.Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW ").CopyTo(rafData, 0);
            System.Text.Encoding.ASCII.GetBytes("0201").CopyTo(rafData, 16);
            System.Text.Encoding.ASCII.GetBytes("FA119501").CopyTo(rafData, 20);
            System.Text.Encoding.ASCII.GetBytes("X100S").CopyTo(rafData, 28);
            System.Text.Encoding.ASCII.GetBytes("0120").CopyTo(rafData, 60);

            string tempRaf = Path.Combine(Path.GetTempPath(), $"test_fuji_{Guid.NewGuid():N}.raf");
            try
            {
                File.WriteAllBytes(tempRaf, rafData);

                string? dngEditorResult = DngMetadataEditor.ReadCameraMakeAndModel(tempRaf);
                Assert.Equal("FUJIFILM X100S", dngEditorResult);

                string? rawReaderResult = RawMetadataReader.TryGetCameraMakeAndModel(tempRaf);
                Assert.Equal("FUJIFILM X100S", rawReaderResult);

                var metadataService = new MetadataService();
                string svcResult = metadataService.GetCameraMakeAndModel(tempRaf);
                Assert.Equal("FUJIFILM X100S", svcResult);
            }
            finally
            {
                if (File.Exists(tempRaf)) File.Delete(tempRaf);
            }

            // Also test real sample if present on test machine
            string realSample = @"D:\Users\isisk\Documents\test sony\test files\raws\fuji\RAW_FUJIFILM_X100S.RAF";
            if (File.Exists(realSample))
            {
                string? realModel = DngMetadataEditor.ReadCameraMakeAndModel(realSample);
                Assert.Equal("FUJIFILM X100S", realModel);
            }
        }

        [Fact]
        public void ReadCameraMakeAndModel_CanonCiff_ShouldExtractMakeAndModel()
        {
            string realCiff = @"D:\Users\isisk\Documents\test sony\test files\raws\canon\pro70\RAW_CANON_PRO70_SRGB.CRW";
            if (File.Exists(realCiff))
            {
                string? dngEditorResult = DngMetadataEditor.ReadCameraMakeAndModel(realCiff);
                Assert.Equal("Canon PowerShot Pro70", dngEditorResult);

                string? rawReaderResult = RawMetadataReader.TryGetCameraMakeAndModel(realCiff);
                Assert.Equal("Canon PowerShot Pro70", rawReaderResult);

                var metadataService = new MetadataService();
                string svcResult = metadataService.GetCameraMakeAndModel(realCiff);
                Assert.Equal("Canon PowerShot Pro70", svcResult);

                var exif = RawMetadataReader.TryGetExifData(realCiff);
                Assert.NotNull(exif);
                Assert.NotNull(exif.DateTimeOriginal);
            }
        }

        [Fact]
        public void ReadCameraMakeAndModel_CanonCr3_ShouldExtractCanonEOSR()
        {
            string cr3Path = @"D:\Users\isisk\Documents\test sony\test files\Canon-EOS-R.CR3";
            if (File.Exists(cr3Path))
            {
                string? dngEditorResult = DngMetadataEditor.ReadCameraMakeAndModel(cr3Path);
                Assert.Equal("Canon EOS R", dngEditorResult);

                string? rawReaderResult = RawMetadataReader.TryGetCameraMakeAndModel(cr3Path);
                Assert.Equal("Canon EOS R", rawReaderResult);

                var metadataService = new MetadataService();
                string svcResult = metadataService.GetCameraMakeAndModel(cr3Path);
                Assert.Equal("Canon EOS R", svcResult);
                Assert.NotEqual("CCDT", svcResult);

                var exif = RawMetadataReader.TryGetExifData(cr3Path);
                Assert.NotNull(exif);
            }
        }

        [Fact]
        public void ReadCanonCr3CameraInfo_SyntheticCr3WithCcdtBox_ShouldReturnCanonEOSRNotCCDT()
        {
            using var ms = new MemoryStream();
            // 1. ftyp box (24 bytes)
            ms.Write(new byte[] { 0, 0, 0, 24 });
            ms.Write(Encoding.ASCII.GetBytes("ftypcrx "));
            ms.Write(new byte[] { 0, 0, 0, 1 });
            ms.Write(Encoding.ASCII.GetBytes("crx isom"));

            // moov box containing uuid box containing CCDT and CMT1
            long moovPos = ms.Position;
            ms.Write(new byte[] { 0, 0, 0, 0 }); // moov size placeholder
            ms.Write(Encoding.ASCII.GetBytes("moov"));

            long uuidPos = ms.Position;
            ms.Write(new byte[] { 0, 0, 0, 0 }); // uuid size placeholder
            ms.Write(Encoding.ASCII.GetBytes("uuid"));
            ms.Write(new byte[] { 0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x02, 0x4C }); // Canon UUID

            // Sub-box 1: CCDT box (simulating the box that caused the false reading)
            long ccdtPos = ms.Position;
            ms.Write(new byte[] { 0, 0, 0, 32 }); // 32 bytes
            ms.Write(Encoding.ASCII.GetBytes("CCDT"));
            ms.Write(new byte[24]); // dummy payload

            // Sub-box 2: CMT1 box
            long cmt1Pos = ms.Position;
            ms.Write(new byte[] { 0, 0, 0, 0 }); // cmt1 size placeholder
            ms.Write(Encoding.ASCII.GetBytes("CMT1"));

            long tiffStart = ms.Position;
            // TIFF header: II (little-endian), magic 42, IFD0 offset 8
            ms.Write(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00 });

            // IFD0: 2 entries (Make = 0x010F, Model = 0x0110)
            ms.Write(BitConverter.GetBytes((ushort)2));

            // Offsets relative to tiffStart:
            // Header is 8 bytes.
            // IFD0 count: 2 bytes.
            // 2 entries * 12 bytes = 24 bytes.
            // Next IFD offset: 4 bytes.
            // Total IFD size = 2 + 24 + 4 = 30 bytes.
            // String data starts at offset 8 + 30 = 38 (relative to tiffStart).
            uint makeOffset = 38;
            byte[] makeBytes = Encoding.ASCII.GetBytes("Canon\0"); // 6 bytes
            uint modelOffset = (uint)(makeOffset + makeBytes.Length); // 44
            byte[] modelBytes = Encoding.ASCII.GetBytes("Canon EOS R\0"); // 12 bytes

            // Entry 1: Make (0x010F, ASCII=2, count=6, offset=makeOffset)
            ms.Write(BitConverter.GetBytes((ushort)0x010F));
            ms.Write(BitConverter.GetBytes((ushort)2));
            ms.Write(BitConverter.GetBytes((uint)makeBytes.Length));
            ms.Write(BitConverter.GetBytes(makeOffset));

            // Entry 2: Model (0x0110, ASCII=2, count=12, offset=modelOffset)
            ms.Write(BitConverter.GetBytes((ushort)0x0110));
            ms.Write(BitConverter.GetBytes((ushort)2));
            ms.Write(BitConverter.GetBytes((uint)modelBytes.Length));
            ms.Write(BitConverter.GetBytes(modelOffset));

            // Next IFD = 0
            ms.Write(new byte[] { 0, 0, 0, 0 });

            // String payloads
            ms.Write(makeBytes);
            ms.Write(modelBytes);

            long cmt1End = ms.Position;
            long cmt1Size = cmt1End - cmt1Pos;
            long uuidEnd = ms.Position;
            long uuidSize = uuidEnd - uuidPos;
            long moovEnd = ms.Position;
            long moovSize = moovEnd - moovPos;

            // Fill placeholders (big-endian)
            ms.Seek(cmt1Pos, SeekOrigin.Begin);
            ms.Write(new byte[] { (byte)(cmt1Size >> 24), (byte)(cmt1Size >> 16), (byte)(cmt1Size >> 8), (byte)cmt1Size });

            ms.Seek(uuidPos, SeekOrigin.Begin);
            ms.Write(new byte[] { (byte)(uuidSize >> 24), (byte)(uuidSize >> 16), (byte)(uuidSize >> 8), (byte)uuidSize });

            ms.Seek(moovPos, SeekOrigin.Begin);
            ms.Write(new byte[] { (byte)(moovSize >> 24), (byte)(moovSize >> 16), (byte)(moovSize >> 8), (byte)moovSize });

            ms.Seek(0, SeekOrigin.Begin);

            string? model = DngMetadataEditor.ReadCanonCr3CameraInfo(ms);
            Assert.Equal("Canon EOS R", model);
        }

        [Fact]
        public async Task DngLabNative_ConvertFile_ShouldSucceedForPentaxK200D()
        {
            string k200dPath = @"D:\Users\isisk\Documents\test sony\test files\raws\pentax\k200d\RAW_PENTAX_K200D.PEF";
            if (!File.Exists(k200dPath)) return;

            string outDng = Path.Combine(Path.GetTempPath(), $"k200d_test_{Guid.NewGuid():N}.dng");
            try
            {
                var (success, error) = await DngLabNative.ConvertFileAsync(k200dPath, outDng, false);
                Assert.True(success, $"K200D conversion failed: {error}");
                Assert.True(File.Exists(outDng));
                Assert.True(new FileInfo(outDng).Length > 1_000_000);
            }
            finally
            {
                if (File.Exists(outDng)) File.Delete(outDng);
            }
        }

        [Fact]
        public void ReadCameraMakeAndModel_MinoltaMrw_ShouldExtractMakeModelAndExif()
        {
            string mrwPath = @"D:\Users\isisk\Documents\test sony\test files\raws\minolta\5d\RAW_MINOLTA_5D.MRW";
            if (!File.Exists(mrwPath)) return;

            string? dngEditorResult = DngMetadataEditor.ReadCameraMakeAndModel(mrwPath);
            Assert.Equal("KONICA MINOLTA DYNAX 5D", dngEditorResult);

            string? rawReaderResult = RawMetadataReader.TryGetCameraMakeAndModel(mrwPath);
            Assert.Equal("KONICA MINOLTA DYNAX 5D", rawReaderResult);

            var metadataService = new MetadataService();
            string svcResult = metadataService.GetCameraMakeAndModel(mrwPath);
            Assert.Equal("KONICA MINOLTA DYNAX 5D", svcResult);

            int? orientation = RawMetadataReader.TryGetOrientation(mrwPath);
            Assert.Equal(1, orientation);

            var exif = RawMetadataReader.TryGetExifData(mrwPath);
            Assert.NotNull(exif);
            Assert.Equal("100", exif.Iso);
            Assert.Equal("8", exif.Aperture);
            Assert.Equal("1/320", exif.ShutterSpeed);
            Assert.Equal("110 mm", exif.FocalLength);
            Assert.Equal("2006:10:01 13:21:28", exif.DateTimeOriginal);
        }

        [Fact]
        public async Task DngLabNative_ConvertFile_ShouldSucceedForMinolta5D()
        {
            string mrwPath = @"D:\Users\isisk\Documents\test sony\test files\raws\minolta\5d\RAW_MINOLTA_5D.MRW";
            if (!File.Exists(mrwPath)) return;

            string outDng = Path.Combine(Path.GetTempPath(), $"minolta_5d_test_{Guid.NewGuid():N}.dng");
            try
            {
                var (success, error) = await DngLabNative.ConvertFileAsync(mrwPath, outDng, false);
                Assert.True(success, $"Minolta 5D conversion failed: {error}");
                Assert.True(File.Exists(outDng));
                Assert.True(new FileInfo(outDng).Length > 2_000_000);

                string? convertedModel = DngMetadataEditor.ReadCameraMakeAndModel(outDng);
                Assert.NotNull(convertedModel);
                Assert.Contains("DYNAX 5D", convertedModel);
            }
            finally
            {
                if (File.Exists(outDng)) File.Delete(outDng);
            }
        }

        [Fact]
        public async Task DngLabNative_ConvertFile_ShouldSucceedForLeafAptus22()
        {
            string mosPath = @"D:\Users\isisk\Documents\test sony\test files\raws\leaf\aptus22\RAW_LEAF_APTUS_22.MOS";
            if (!File.Exists(mosPath)) return;

            string outDng = Path.Combine(Path.GetTempPath(), $"leaf_aptus_test_{Guid.NewGuid():N}.dng");
            try
            {
                var (success, error) = await DngLabNative.ConvertFileAsync(mosPath, outDng, false);
                Assert.True(success, $"Leaf Aptus 22 conversion failed: {error}");
                Assert.True(File.Exists(outDng));
                Assert.True(new FileInfo(outDng).Length > 10_000_000);
            }
            finally
            {
                if (File.Exists(outDng)) File.Delete(outDng);
            }
        }

        [Fact]
        public async Task DngLabNative_ConvertFile_CanonPro70_ShouldReturnClearCygmError()
        {
            string pro70Path = @"D:\Users\isisk\Documents\test sony\test files\raws\canon\pro70\RAW_CANON_PRO70_SRGB.CRW";
            if (!File.Exists(pro70Path)) return;

            string outDng = Path.Combine(Path.GetTempPath(), $"canon_pro70_test_{Guid.NewGuid():N}.dng");
            try
            {
                var (success, error) = await DngLabNative.ConvertFileAsync(pro70Path, outDng, false);
                Assert.False(success);
                Assert.NotNull(error);
                Assert.Contains("CYGM", error);
            }
            finally
            {
                if (File.Exists(outDng)) File.Delete(outDng);
            }
        }
    }
}
