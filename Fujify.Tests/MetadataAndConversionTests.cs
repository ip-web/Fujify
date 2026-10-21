using System;
using System.IO;
using System.Threading.Tasks;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
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
    }
}
