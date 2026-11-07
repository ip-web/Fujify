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
using System.Threading;
using System.Threading.Tasks;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    public class CaptureOneIntegrationTests
    {
        private class MockSettingsService : ISettingsService
        {
            public AppSettings Settings { get; set; } = new();
            public bool WasSaved { get; private set; }
            public void Save() => WasSaved = true;
            public void Load() { }
        }

        private class MockProcessRunner : IProcessRunner
        {
            public string? LastFileName { get; private set; }
            public string? LastArguments { get; private set; }
            public int ReturnExitCode { get; set; } = 0;
            public string ReturnOutput { get; set; } = "[INFO] repacked into RAF container";
            public string ReturnError { get; set; } = string.Empty;
            public Action<string, string>? OnRun { get; set; }

            public (int ExitCode, string Output, string Error) Run(string fileName, string arguments)
            {
                LastFileName = fileName;
                LastArguments = arguments;
                OnRun?.Invoke(fileName, arguments);
                return (ReturnExitCode, ReturnOutput, ReturnError);
            }

            public Func<string, string, Task>? OnRunAsync { get; set; }

            public async Task<(int ExitCode, string Output, string Error)> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
            {
                LastFileName = fileName;
                LastArguments = arguments;
                if (OnRunAsync != null)
                {
                    await OnRunAsync(fileName, arguments);
                }
                else
                {
                    OnRun?.Invoke(fileName, arguments);
                }
                return (ReturnExitCode, ReturnOutput, ReturnError);
            }
        }

        [Fact]
        public void AppSettings_CaptureOneDefaults_AreCorrect()
        {
            var settings = new AppSettings();
            Assert.False(settings.EnableCaptureOneMode);
            Assert.Null(settings.RafContainerPath);
            Assert.Equal("Lightroom", settings.ConversionTargetMode);
        }

        [Fact]
        public void OutputLocationHelper_ComputesRafDestination()
        {
            var settings = new AppSettings
            {
                AppendSuffix = true,
                Suffix = "-fuji"
            };

            string input = Path.Combine(Path.GetTempPath(), "test_photo.arw");
            string output = OutputLocationHelper.ComputeInitialDestinationFilePath(input, null, settings, ".raf");

            Assert.EndsWith("-fuji.raf", output, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RafConverterService_LocatesBinary_OnSupportedPlatforms()
        {
            if (!OperatingSystem.IsWindows()) return;

            string? path = RafConverterService.FindRaw2RafPath();
            Assert.NotNull(path);
            Assert.True(File.Exists(path), $"raw2raf.exe should exist at: {path}");
        }

        [Fact]
        public void RafConverterService_Fails_WhenContainerArgEmpty()
        {
            var runner = new MockProcessRunner();
            var service = new RafConverterService(runner);

            var res = service.ConvertToRaf("in.arw", "out.raf", "");
            Assert.False(res.Success);
            Assert.Contains("No reference Fujifilm RAF container provided", res.ErrorMessage);
        }

        [Fact]
        public async Task RafConverterService_PassesCorrectArguments_AndSucceeds()
        {
            string tempOut = Path.Combine(Path.GetTempPath(), $"test_raf_{Guid.NewGuid():N}.raf");
            string tempExifTool = Path.GetTempFileName();
            try
            {
                var runner = new MockProcessRunner
                {
                    OnRun = (fn, args) =>
                    {
                        // Simulate writing output file
                        File.WriteAllText(tempOut, "dummy raf");
                    }
                };

                var service = new RafConverterService(runner);
                var res = await service.ConvertToRafAsync("sample.arw", tempOut, "C:\\donor.raf", tempExifTool);

                Assert.True(res.Success);
                Assert.NotNull(runner.LastArguments);
                Assert.Contains("--container \"C:\\donor.raf\"", runner.LastArguments);
                Assert.Contains("--auto-container", runner.LastArguments);
                Assert.Contains("--no-metadata", runner.LastArguments);
                Assert.DoesNotContain("--exiftool", runner.LastArguments);
                Assert.Contains("\"sample.arw\"", runner.LastArguments);
                Assert.Contains($"\"{tempOut}\"", runner.LastArguments);
            }
            finally
            {
                if (File.Exists(tempOut)) File.Delete(tempOut);
                if (File.Exists(tempExifTool)) File.Delete(tempExifTool);
            }
        }

        [Fact]
        public async Task RafConverterService_Fails_WhenExitCodeNonZero()
        {
            string tempOut = Path.Combine(Path.GetTempPath(), $"test_raf_{Guid.NewGuid():N}.raf");
            var runner = new MockProcessRunner
            {
                ReturnExitCode = 1,
                ReturnError = "No valid Bayer container matching dimensions"
            };

            var service = new RafConverterService(runner);
            var res = await service.ConvertToRafAsync("sample.arw", tempOut, "C:\\donor.raf");

            Assert.False(res.Success);
            Assert.Contains("No valid Bayer container", res.ErrorMessage);
        }

        [Fact]
        public async Task RafConverterService_Conversions_CanRunConcurrently()
        {
            int concurrentCount = 0;
            int maxConcurrentObserved = 0;
            var lockObj = new object();

            var runner = new MockProcessRunner
            {
                OnRunAsync = async (fn, args) =>
                {
                    int current;
                    lock (lockObj)
                    {
                        current = Interlocked.Increment(ref concurrentCount);
                        if (current > maxConcurrentObserved)
                        {
                            maxConcurrentObserved = current;
                        }
                    }

                    // Simulate conversion processing time
                    await Task.Delay(60);

                    // Simulate output file creation
                    string outArg = args.Split('\"')[^2];
                    if (!File.Exists(outArg))
                    {
                        File.WriteAllText(outArg, "dummy converted raf");
                    }

                    Interlocked.Decrement(ref concurrentCount);
                }
            };

            var service = new RafConverterService(runner);
            var tempFiles = new System.Collections.Generic.List<string>();
            var tasks = new System.Collections.Generic.List<Task<RafConversionResult>>();

            try
            {
                for (int i = 0; i < 4; i++)
                {
                    string tempOut = Path.Combine(Path.GetTempPath(), $"concurrent_test_{i}_{Guid.NewGuid():N}.raf");
                    tempFiles.Add(tempOut);
                    tasks.Add(service.ConvertToRafAsync($"in_{i}.raw", tempOut, "C:\\donor.raf"));
                }

                var results = await Task.WhenAll(tasks);
                Assert.All(results, r => Assert.True(r.Success));
                Assert.True(maxConcurrentObserved > 1,
                    $"Conversions should run concurrently in parallel without being blocked by a serial lock. Max concurrent observed: {maxConcurrentObserved}");
            }
            finally
            {
                foreach (var f in tempFiles)
                {
                    if (File.Exists(f)) File.Delete(f);
                }
            }
        }

        [Fact]
        public void PreferencesViewModel_HandlesCaptureOneSettings()
        {
            var mockSettings = new MockSettingsService();
            var vm = new PreferencesViewModel(mockSettings);

            Assert.False(vm.EnableCaptureOneMode);
            Assert.False(vm.ShowCaptureOneContainerSettings);
            Assert.False(vm.HasRafContainerPath);

            // Enable Capture One mode
            vm.EnableCaptureOneMode = true;
            Assert.True(vm.ShowCaptureOneContainerSettings);
            Assert.True(mockSettings.Settings.EnableCaptureOneMode);

            // Set Container Path
            string tempFile = Path.GetTempFileName();
            try
            {
                vm.RafContainerPath = tempFile;
                Assert.True(vm.HasRafContainerPath);
                Assert.Contains(Path.GetFileName(tempFile), vm.RafContainerStatusText);

                // Clear command
                vm.ClearRafContainerCommand.Execute(null);
                Assert.False(vm.HasRafContainerPath);
                Assert.Empty(vm.RafContainerPath);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void MainViewModel_ModeSelector_SwitchesModesAndBadges()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings.EnableCaptureOneMode = true;
            mockSettings.Settings.ConversionTargetMode = "Lightroom";

            var vm = new MainViewModel(settingsService: mockSettings);

            Assert.True(vm.IsCaptureOneModeUnlocked);
            Assert.True(vm.IsLightroomModeSelected);
            Assert.False(vm.IsCaptureOneModeSelected);
            Assert.Equal(".DNG", vm.OutputExtensionBadge);
            Assert.EndsWith(".dng", vm.SuffixPreview);

            // Select Capture One mode
            vm.SelectCaptureOneModeCommand.Execute(null);

            Assert.False(vm.IsLightroomModeSelected);
            Assert.True(vm.IsCaptureOneModeSelected);
            Assert.Equal(".RAF", vm.OutputExtensionBadge);
            Assert.EndsWith(".raf", vm.SuffixPreview);
            Assert.Equal("CaptureOne", mockSettings.Settings.ConversionTargetMode);

            // Select Lightroom mode back
            vm.SelectLightroomModeCommand.Execute(null);

            Assert.True(vm.IsLightroomModeSelected);
            Assert.False(vm.IsCaptureOneModeSelected);
            Assert.Equal(".DNG", vm.OutputExtensionBadge);
            Assert.EndsWith(".dng", vm.SuffixPreview);
        }

        private static void CreateDummyRaf(string path, string model)
        {
            byte[] bytes = new byte[148];
            byte[] fuji = System.Text.Encoding.ASCII.GetBytes("FUJIFILM");
            Array.Copy(fuji, bytes, fuji.Length);
            byte[] modelBytes = System.Text.Encoding.ASCII.GetBytes(model);
            Array.Copy(modelBytes, 0, bytes, 0x1C, Math.Min(modelBytes.Length, 32));
            File.WriteAllBytes(path, bytes);
        }

        [Fact]
        public void RafConverterService_IsBayerSensor_IdentifiesBayerAndXTransModels()
        {
            var service = new RafConverterService(new MockProcessRunner());
            string tempDir = Path.Combine(Path.GetTempPath(), $"raf_bayer_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string gfxFile = Path.Combine(tempDir, "gfx50s.raf");
                CreateDummyRaf(gfxFile, "GFX 50S II");
                Assert.True(service.IsBayerSensor(gfxFile));

                string xt200File = Path.Combine(tempDir, "xt200.raf");
                CreateDummyRaf(xt200File, "X-T200");
                Assert.True(service.IsBayerSensor(xt200File));

                string xa7File = Path.Combine(tempDir, "xa7.raf");
                CreateDummyRaf(xa7File, "X-A7");
                Assert.True(service.IsBayerSensor(xa7File));

                string xf10File = Path.Combine(tempDir, "xf10.raf");
                CreateDummyRaf(xf10File, "XF10");
                Assert.True(service.IsBayerSensor(xf10File));

                string xt5File = Path.Combine(tempDir, "xt5.raf");
                CreateDummyRaf(xt5File, "X-T5");
                Assert.False(service.IsBayerSensor(xt5File));

                string xt3File = Path.Combine(tempDir, "xt3.raf");
                CreateDummyRaf(xt3File, "X-T3");
                Assert.False(service.IsBayerSensor(xt3File));

                string xpro3File = Path.Combine(tempDir, "xpro3.raf");
                CreateDummyRaf(xpro3File, "X-Pro3");
                Assert.False(service.IsBayerSensor(xpro3File));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task RafConverterService_PatchModelAsync_PatchesHeaderCorrectly()
        {
            var runner = new MockProcessRunner();
            var service = new RafConverterService(runner);
            string inPath = Path.Combine(Path.GetTempPath(), $"patch_in_{Guid.NewGuid():N}.raf");
            string outPath = Path.Combine(Path.GetTempPath(), $"patch_out_{Guid.NewGuid():N}.raf");

            try
            {
                CreateDummyRaf(inPath, "X-T3");
                var result = await service.PatchModelAsync(inPath, outPath, "X-T5");

                Assert.True(result.Success);
                Assert.Equal(outPath, result.OutputRafPath);

                string? detectedModel = RawMetadataReader.TryGetCameraMakeAndModel(outPath);
                Assert.NotNull(detectedModel);
                Assert.Contains("X-T5", detectedModel);
            }
            finally
            {
                if (File.Exists(inPath)) File.Delete(inPath);
                if (File.Exists(outPath)) File.Delete(outPath);
            }
        }

        [Fact]
        public void PreferencesViewModel_RafContainers_LoadsAndFormatsWithCameraModel()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"raf_prefs_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string gfxFile = Path.Combine(tempDir, "container_gfx.raf");
                string xt5File = Path.Combine(tempDir, "container_xt5.raf");
                CreateDummyRaf(gfxFile, "GFX 50S II");
                CreateDummyRaf(xt5File, "X-T5");

                var mockSettings = new MockSettingsService();
                mockSettings.Settings.EnableCaptureOneMode = true;
                mockSettings.Settings.RafContainerPath = $"{gfxFile};{xt5File}";

                var vm = new PreferencesViewModel(mockSettings);

                Assert.Equal(2, vm.RafContainers.Count);
                Assert.Equal(gfxFile, vm.RafContainers[0].FilePath);
                Assert.Contains("container_gfx.raf - FUJIFILM GFX 50S II", vm.RafContainers[0].DisplayText);
                Assert.Equal(xt5File, vm.RafContainers[1].FilePath);
                Assert.Contains("container_xt5.raf - FUJIFILM X-T5", vm.RafContainers[1].DisplayText);

                // Test removal of first item
                vm.RafContainers[0].RemoveCommand.Execute(null);

                Assert.Single(vm.RafContainers);
                Assert.Equal(xt5File, vm.RafContainers[0].FilePath);
                Assert.Equal(xt5File, mockSettings.Settings.RafContainerPath);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void RafConverterService_HasValidContainer_ValidatesSemicolonDelimitedContainers()
        {
            var service = new RafConverterService(new MockProcessRunner());
            string tempDir = Path.Combine(Path.GetTempPath(), $"raf_val_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string realFile1 = Path.Combine(tempDir, "real1.raf");
                string realFile2 = Path.Combine(tempDir, "real2.raf");
                File.WriteAllText(realFile1, "dummy");
                File.WriteAllText(realFile2, "dummy");

                // Empty / null
                Assert.False(service.HasValidContainer(null));
                Assert.False(service.HasValidContainer("   "));

                // Single non-existent file
                Assert.False(service.HasValidContainer("C:\\does_not_exist_12345.raf"));

                // Single real file
                Assert.True(service.HasValidContainer(realFile1));

                // Real directory
                Assert.True(service.HasValidContainer(tempDir));

                // Semicolon-delimited: multiple files (the exact case that was failing with File.Exists!)
                string multiSetting = $"{realFile1};{realFile2}";
                Assert.True(service.HasValidContainer(multiSetting));

                // Delimited with one non-existent and one real file
                string mixedSetting = $"C:\\bogus.raf;\"{realFile1}\"";
                Assert.True(service.HasValidContainer(mixedSetting));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void RafMetadataModifier_InjectMetadata_TransfersMetadataFromRawToRaf()
        {
            string containerRaf = Path.GetFullPath(@"..\..\..\..\test_files\Fujifilm-X-T4.RAF");
            if (!File.Exists(containerRaf)) containerRaf = Path.GetFullPath(@"test_files\Fujifilm-X-T4.RAF");
            string sourceRaw = Path.GetFullPath(@"..\..\..\..\test_files\Sony-a7III.ARW");
            if (!File.Exists(sourceRaw)) sourceRaw = Path.GetFullPath(@"test_files\Sony-a7III.ARW");

            string tempRaf = Path.Combine(Path.GetTempPath(), $"inject_test_{Guid.NewGuid():N}.raf");
            try
            {
                File.Copy(containerRaf, tempRaf, overwrite: true);

                // Verify before injection: container metadata is present
                var beforeExif = RawMetadataReader.TryGetExifData(tempRaf);
                Assert.NotNull(beforeExif);
                Assert.Equal("XF16-55mmF2.8 R LM WR", beforeExif.Lens);
                Assert.Equal("160", beforeExif.Iso);

                // Inject Sony A7 III metadata into RAF container
                bool success = RafMetadataModifier.InjectMetadata(tempRaf, sourceRaw, "GFX50S II");
                Assert.True(success);

                // Verify after injection: Sony metadata is now active in RAF
                var afterExif = RawMetadataReader.TryGetExifData(tempRaf);
                Assert.NotNull(afterExif);
                Assert.Equal("FE 16-35mm F2.8 GM", afterExif.Lens);
                Assert.Equal("4", afterExif.Aperture);
                Assert.Equal("1/50", afterExif.ShutterSpeed);
                Assert.Equal("400", afterExif.Iso);
                Assert.Equal("32 mm", afterExif.FocalLength);
                Assert.Equal("2018:03:13 16:38:13", afterExif.DateTimeOriginal);

                string? cameraModel = RawMetadataReader.TryGetCameraMakeAndModel(tempRaf);
                Assert.Equal("FUJIFILM GFX50S II", cameraModel);
            }
            finally
            {
                if (File.Exists(tempRaf)) File.Delete(tempRaf);
            }
        }

        [Fact]
        public void RafMetadataModifier_PatchModelOnly_PreservesExistingShootingMetadata()
        {
            string containerRaf = Path.GetFullPath(@"..\..\..\..\test_files\Fujifilm-X-T4.RAF");
            if (!File.Exists(containerRaf)) containerRaf = Path.GetFullPath(@"test_files\Fujifilm-X-T4.RAF");

            string tempRaf = Path.Combine(Path.GetTempPath(), $"patch_model_test_{Guid.NewGuid():N}.raf");
            try
            {
                File.Copy(containerRaf, tempRaf, overwrite: true);

                // Patch model to X-T5
                bool success = RafMetadataModifier.PatchModelOnly(tempRaf, "X-T5");
                Assert.True(success);

                // Model is updated
                string? cameraModel = RawMetadataReader.TryGetCameraMakeAndModel(tempRaf);
                Assert.Equal("FUJIFILM X-T5", cameraModel);

                // Shooting metadata is preserved intact
                var exif = RawMetadataReader.TryGetExifData(tempRaf);
                Assert.NotNull(exif);
                Assert.Equal("XF16-55mmF2.8 R LM WR", exif.Lens);
                Assert.Equal("3.6", exif.Aperture);
                Assert.Equal("1/125", exif.ShutterSpeed);
                Assert.Equal("160", exif.Iso);
                Assert.Equal("16 mm", exif.FocalLength);
                Assert.Equal("2020:05:08 11:19:04", exif.DateTimeOriginal);
            }
            finally
            {
                if (File.Exists(tempRaf)) File.Delete(tempRaf);
            }
        }

        [Fact]
        public async Task RafConverterService_PatchModelAsync_UsesInProcessModifier()
        {
            string containerRaf = Path.GetFullPath(@"..\..\..\..\test_files\Fujifilm-X-T4.RAF");
            if (!File.Exists(containerRaf)) containerRaf = Path.GetFullPath(@"test_files\Fujifilm-X-T4.RAF");

            string tempRaf = Path.Combine(Path.GetTempPath(), $"async_patch_{Guid.NewGuid():N}.raf");
            try
            {
                var service = new RafConverterService(new MockProcessRunner());
                var result = await service.PatchModelAsync(containerRaf, tempRaf, "GFX50S II");

                Assert.True(result.Success);
                Assert.Equal(tempRaf, result.OutputRafPath);

                string? cameraModel = RawMetadataReader.TryGetCameraMakeAndModel(tempRaf);
                Assert.Equal("FUJIFILM GFX50S II", cameraModel);
            }
            finally
            {
                if (File.Exists(tempRaf)) File.Delete(tempRaf);
            }
        }

        [Fact]
        public void RafMetadataModifier_ExtractExifInfo_ExtractsAccuratelyFromVariousRaws()
        {
            string testDir = Path.GetFullPath(@"..\..\..\..\test_files");
            if (!Directory.Exists(testDir)) testDir = Path.GetFullPath(@"test_files");

            // 1. Sony ARW
            string arwPath = Path.Combine(testDir, "Sony-a7III.ARW");
            if (File.Exists(arwPath))
            {
                var info = RafMetadataModifier.ExtractExifInfo(arwPath);
                Assert.Equal((1u, 50u), info.ExposureTime);
                Assert.Equal((40u, 10u), info.FNumber);
                Assert.Equal((ushort)400, info.Iso);
                Assert.Equal((320u, 10u), info.FocalLength);
                Assert.Equal("FE 16-35mm F2.8 GM", info.LensModel);
                Assert.Equal("2018:03:13 16:38:13", info.DateTimeOriginal);
            }

            // 2. Canon CR3
            string cr3Path = Path.Combine(testDir, "Canon-EOS-R.CR3");
            if (File.Exists(cr3Path))
            {
                var info = RafMetadataModifier.ExtractExifInfo(cr3Path);
                Assert.NotNull(info.ExposureTime);
                Assert.NotNull(info.FNumber);
                Assert.Equal((ushort)100, info.Iso);
                Assert.Equal("RF85mm F2 MACRO IS STM", info.LensModel);
            }

            // 3. Nikon NEF
            string nefPath = Path.Combine(testDir, "Nikon-Z6.NEF");
            if (File.Exists(nefPath))
            {
                var info = RafMetadataModifier.ExtractExifInfo(nefPath);
                Assert.NotNull(info.ExposureTime);
                Assert.NotNull(info.FNumber);
                Assert.Equal((ushort)100, info.Iso);
                Assert.Equal("NIKKOR Z 24-70mm f/4 S", info.LensModel);
            }
        }

        [Fact]
        public void ExtractExifInfo_ExtractsLensesAndShootingMetadata_ForDiverseRawFormats()
        {
            string testDir = @"D:\Users\isisk\Documents\Dev\FujifyNET\test_files";
            if (!Directory.Exists(testDir)) return;

            // 1. Panasonic RW2 (embedded JPEG MakerNotes)
            string panasonicPath = Path.Combine(testDir, "Panasonic-G9.RW2");
            if (File.Exists(panasonicPath))
            {
                var info = RafMetadataModifier.ExtractExifInfo(panasonicPath);
                Assert.Equal("LEICA DG 12-60/F2.8-4.0", info.LensModel);
                Assert.Equal((ushort)200, info.Iso);
                Assert.NotNull(info.ExposureTime);
                Assert.NotNull(info.FNumber);
            }

            // 2. Ricoh GR III (fixed-lens camera detection)
            string ricohPath = Path.Combine(testDir, "Ricoh-GR-III.DNG");
            if (File.Exists(ricohPath))
            {
                var info = RafMetadataModifier.ExtractExifInfo(ricohPath);
                Assert.Equal("GR LENS 18.3mm F2.8", info.LensModel);
                Assert.Equal((ushort)100, info.Iso);
            }

            // 3. Nikon Coolpix P1000 (fixed-lens bridge camera)
            string p1000Path = Path.Combine(testDir, "Nikon-Coolpix-P1000.NRW");
            if (File.Exists(p1000Path))
            {
                var info = RafMetadataModifier.ExtractExifInfo(p1000Path);
                Assert.Equal("NIKKOR 4.3-539mm f/2.8-8", info.LensModel);
            }

            // 4. Apple iPhone 12 Pro (long lens model name > 40 chars)
            string iphonePath = Path.Combine(testDir, "Apple-iPhone-12-Pro.DNG");
            if (File.Exists(iphonePath))
            {
                var info = RafMetadataModifier.ExtractExifInfo(iphonePath);
                Assert.Equal("iPhone 12 Pro back triple camera 4.2mm f/1.6", info.LensModel);
            }
        }

        [Fact]
        public void InjectMetadata_WithXT200Container_TransfersMetadataAndOverwritesDonorLens()
        {
            string xt200 = @"D:\Users\isisk\Documents\Dev\RAWtoRAF\container\X-T200_ISO_200.RAF";
            string testDir = @"D:\Users\isisk\Documents\Dev\FujifyNET\test_files";
            if (!File.Exists(xt200) || !Directory.Exists(testDir)) return;

            string tempDir = Path.Combine(Path.GetTempPath(), "Fujify_XT200_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Test 1: Apple iPhone 12 Pro (Lens model > 25 bytes, requires safe buffer relocation)
                string iphoneSrc = Path.Combine(testDir, "Apple-iPhone-12-Pro.DNG");
                if (File.Exists(iphoneSrc))
                {
                    string tempRaf = Path.Combine(tempDir, "iphone_test.RAF");
                    File.Copy(xt200, tempRaf, true);

                    bool injected = RafMetadataModifier.InjectMetadata(tempRaf, iphoneSrc, "GFX50S II");
                    Assert.True(injected);

                    var readBack = RafMetadataModifier.ExtractExifInfo(tempRaf);
                    Assert.Equal("iPhone 12 Pro back triple camera 4.2mm f/1.6", readBack.LensModel);
                    Assert.NotEqual("XC15-45mmF3.5-5.6 OIS PZ", readBack.LensModel);
                }

                // Test 2: Panasonic G9 (RW2 format, Leica lens)
                string panasonicSrc = Path.Combine(testDir, "Panasonic-G9.RW2");
                if (File.Exists(panasonicSrc))
                {
                    string tempRaf = Path.Combine(tempDir, "panasonic_test.RAF");
                    File.Copy(xt200, tempRaf, true);

                    bool injected = RafMetadataModifier.InjectMetadata(tempRaf, panasonicSrc, "GFX50S II");
                    Assert.True(injected);

                    var readBack = RafMetadataModifier.ExtractExifInfo(tempRaf);
                    Assert.Equal("LEICA DG 12-60/F2.8-4.0", readBack.LensModel);
                    Assert.NotEqual("XC15-45mmF3.5-5.6 OIS PZ", readBack.LensModel);
                }

                // Test 3: Ricoh GR III (fixed lens)
                string ricohSrc = Path.Combine(testDir, "Ricoh-GR-III.DNG");
                if (File.Exists(ricohSrc))
                {
                    string tempRaf = Path.Combine(tempDir, "ricoh_test.RAF");
                    File.Copy(xt200, tempRaf, true);

                    bool injected = RafMetadataModifier.InjectMetadata(tempRaf, ricohSrc, "GFX50S II");
                    Assert.True(injected);

                    var readBack = RafMetadataModifier.ExtractExifInfo(tempRaf);
                    Assert.Equal("GR LENS 18.3mm F2.8", readBack.LensModel);
                    Assert.NotEqual("XC15-45mmF3.5-5.6 OIS PZ", readBack.LensModel);
                }

                // Test 4: Canon EOS D60 (camera with no lens info in RAW - donor lens MUST be cleared)
                string d60Src = Path.Combine(testDir, "Canon-EOS-D60.CRW");
                if (File.Exists(d60Src))
                {
                    string tempRaf = Path.Combine(tempDir, "d60_test.RAF");
                    File.Copy(xt200, tempRaf, true);

                    bool injected = RafMetadataModifier.InjectMetadata(tempRaf, d60Src, "GFX50S II");
                    Assert.True(injected);

                    var readBack = RafMetadataModifier.ExtractExifInfo(tempRaf);
                    Assert.NotEqual("XC15-45mmF3.5-5.6 OIS PZ", readBack.LensModel);
                    Assert.True(string.IsNullOrWhiteSpace(readBack.LensModel));
                }

                // Batch test across all RAW files in test_files: none should have the donor XC kit lens
                foreach (var sampleFile in Directory.GetFiles(testDir))
                {
                    string ext = Path.GetExtension(sampleFile).ToLowerInvariant();
                    if (ext == ".raf" && sampleFile.Contains("X-T4", StringComparison.OrdinalIgnoreCase)) continue; // skip native Fujifilm RAF
                    if (ext != ".dng" && ext != ".arw" && ext != ".cr2" && ext != ".cr3" && ext != ".crw" &&
                        ext != ".nef" && ext != ".nrw" && ext != ".orf" && ext != ".rw2" && ext != ".srf" && ext != ".sr2") continue;

                    string outRaf = Path.Combine(tempDir, $"batch_{Path.GetFileNameWithoutExtension(sampleFile)}.RAF");
                    File.Copy(xt200, outRaf, true);

                    bool ok = RafMetadataModifier.InjectMetadata(outRaf, sampleFile, "GFX50S II");
                    Assert.True(ok, $"Injection failed for {Path.GetFileName(sampleFile)}");

                    var rb = RafMetadataModifier.ExtractExifInfo(outRaf);
                    Assert.NotEqual("XC15-45mmF3.5-5.6 OIS PZ", rb.LensModel);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Canon5DMarkIV_LensAndShootingMetadata_ResolvedAndInjectedCorrectly()
        {
            string testDir = @"D:\Users\isisk\Documents\Dev\FujifyNET\test_files";
            string cr2Path = Path.Combine(testDir, "Canon-5D-Mark-IV.CR2");
            string xt200 = @"D:\Users\isisk\Documents\Dev\RAWtoRAF\container\X-T200_ISO_200.RAF";
            string gfx50 = @"D:\Users\isisk\Documents\Dev\RAWtoRAF\container\Fuji_GFX50S_II_-_Compressed.RAF";
            if (!File.Exists(cr2Path) || !Directory.Exists(testDir)) return;

            // 1. Verify ExtractExifInfo on Canon 5D Mark IV
            var info = RafMetadataModifier.ExtractExifInfo(cr2Path);
            Assert.Equal("Sigma 24-70mm F2.8 IF EX DG HSM (Canon EF)", info.LensModel);
            Assert.Equal("SIGMA", info.LensMake);
            Assert.Equal("0000000000", info.LensSerialNumber);
            Assert.Equal((ushort)100, info.Iso);
            Assert.NotNull(info.FocalLength);
            Assert.Equal(40.0, (double)info.FocalLength.Value.Num / info.FocalLength.Value.Den);
            Assert.NotNull(info.FNumber);
            Assert.Equal(2.8, (double)info.FNumber.Value.Num / info.FNumber.Value.Den);
            Assert.True(info.LensSpecification.HasValue);
            var spec = info.LensSpecification.Value;
            Assert.Equal(24.0, (double)spec.MinFocal.Num / spec.MinFocal.Den);
            Assert.Equal(70.0, (double)spec.MaxFocal.Num / spec.MaxFocal.Den);

            // 2. Test injection with GFX50S II donor container
            if (File.Exists(gfx50))
            {
                string tempRaf = Path.GetTempFileName() + ".RAF";
                try
                {
                    File.Copy(gfx50, tempRaf, true);
                    bool ok = RafMetadataModifier.InjectMetadata(tempRaf, cr2Path, "GFX50S II");
                    Assert.True(ok);

                    var readBack = RafMetadataModifier.ExtractExifInfo(tempRaf);
                    Assert.Equal("Sigma 24-70mm F2.8 IF EX DG HSM (Canon EF)", readBack.LensModel);
                    Assert.Equal("SIGMA", readBack.LensMake);
                    Assert.Null(readBack.LensSerialNumber); // Serial numbers are erased
                    Assert.True(readBack.LensSpecification.HasValue);
                    Assert.Equal(24.0, (double)readBack.LensSpecification.Value.MinFocal.Num / readBack.LensSpecification.Value.MinFocal.Den);
                    Assert.Equal(70.0, (double)readBack.LensSpecification.Value.MaxFocal.Num / readBack.LensSpecification.Value.MaxFocal.Den);
                }
                finally
                {
                    try { File.Delete(tempRaf); } catch { }
                }
            }

            // 3. Test injection with X-T200 donor container
            if (File.Exists(xt200))
            {
                string tempRaf = Path.GetTempFileName() + ".RAF";
                try
                {
                    File.Copy(xt200, tempRaf, true);
                    bool ok = RafMetadataModifier.InjectMetadata(tempRaf, cr2Path, "GFX50S II");
                    Assert.True(ok);

                    var readBack = RafMetadataModifier.ExtractExifInfo(tempRaf);
                    Assert.Equal("Sigma 24-70mm F2.8 IF EX DG HSM (Canon EF)", readBack.LensModel);
                    Assert.Equal("SIGMA", readBack.LensMake);
                    Assert.Null(readBack.LensSerialNumber); // Serial numbers are erased
                    Assert.True(readBack.LensSpecification.HasValue);
                    Assert.Equal(24.0, (double)readBack.LensSpecification.Value.MinFocal.Num / readBack.LensSpecification.Value.MinFocal.Den);
                    Assert.Equal(70.0, (double)readBack.LensSpecification.Value.MaxFocal.Num / readBack.LensSpecification.Value.MaxFocal.Den);
                }
                finally
                {
                    try { File.Delete(tempRaf); } catch { }
                }
            }
        }

        [Fact]
        public void SonyA7III_LensAndShootingMetadata_ResolvedAndInjectedCorrectly()
        {
            string sonyPath = @"D:\Users\isisk\Documents\Dev\FujifyNET\test_files\Sony-a7III.ARW";
            string xt200 = @"D:\Users\isisk\Documents\Dev\RAWtoRAF\container\X-T200_ISO_200.RAF";
            if (!File.Exists(sonyPath) || !File.Exists(xt200)) return;

            string tempRaf = Path.GetTempFileName() + ".RAF";
            try
            {
                File.Copy(xt200, tempRaf, true);
                bool ok = RafMetadataModifier.InjectMetadata(tempRaf, sonyPath, "GFX50S II");
                Assert.True(ok);

                var readBack = RafMetadataModifier.ExtractExifInfo(tempRaf);
                Assert.Equal("FE 16-35mm F2.8 GM", readBack.LensModel);
                Assert.Equal("SONY", readBack.LensMake);
                Assert.True(readBack.LensSpecification.HasValue);
                Assert.Equal(16.0, (double)readBack.LensSpecification.Value.MinFocal.Num / readBack.LensSpecification.Value.MinFocal.Den);
                Assert.Equal(35.0, (double)readBack.LensSpecification.Value.MaxFocal.Num / readBack.LensSpecification.Value.MaxFocal.Den);
                Assert.True(readBack.LensSpecification.Value.MinFNum.HasValue);
                Assert.Equal(2.8, (double)readBack.LensSpecification.Value.MinFNum.Value.Num / readBack.LensSpecification.Value.MinFNum.Value.Den);
                Assert.True(readBack.LensSpecification.Value.MaxFNum.HasValue);
                Assert.Equal(2.8, (double)readBack.LensSpecification.Value.MaxFNum.Value.Num / readBack.LensSpecification.Value.MaxFNum.Value.Den);
                Assert.NotNull(readBack.FocalLength);
                Assert.Equal(32.0, (double)readBack.FocalLength.Value.Num / readBack.FocalLength.Value.Den);
                Assert.NotNull(readBack.FNumber);
                Assert.Equal(4.0, (double)readBack.FNumber.Value.Num / readBack.FNumber.Value.Den);
            }
            finally
            {
                try { File.Delete(tempRaf); } catch { }
            }
        }

        [Fact]
        public void RafMetadataModifier_Erases_OwnerAuthor_Software_And_SerialNumbers()
        {
            string testDir = @"D:\Users\isisk\Documents\Dev\FujifyNET\test_files";
            string cr2Path = Path.Combine(testDir, "Canon-5D-Mark-IV.CR2");
            string gfx50 = @"D:\Users\isisk\Documents\Dev\RAWtoRAF\container\Fuji_GFX50S_II_-_Compressed.RAF";
            if (!File.Exists(cr2Path) || !File.Exists(gfx50)) return;

            string tempRaf = Path.GetTempFileName() + ".RAF";
            try
            {
                File.Copy(gfx50, tempRaf, true);
                bool ok = RafMetadataModifier.InjectMetadata(tempRaf, cr2Path, "GFX50S II");
                Assert.True(ok);

                var readBack = RafMetadataModifier.ExtractExifInfo(tempRaf);
                Assert.Null(readBack.LensSerialNumber);

                // Verify directly in the TIFF structure of the RAF that Software, Artist, Copyright,
                // and SerialNumber tags have been wiped (count == 0).
                using var fs = new FileStream(tempRaf, FileMode.Open, FileAccess.Read);
                byte[] header = new byte[148];
                fs.Read(header, 0, 148);
                uint jpegOff = (uint)((header[0x54] << 24) | (header[0x55] << 16) | (header[0x56] << 8) | header[0x57]);

                // Scan APP1 to find TIFF base
                fs.Seek(jpegOff, SeekOrigin.Begin);
                long tiffBase = -1;
                while (fs.Position < fs.Length - 4)
                {
                    int b = fs.ReadByte();
                    if (b != 0xFF) continue;
                    int marker = fs.ReadByte();
                    if (marker == 0xE1) // APP1
                    {
                        byte[] lenBuf = new byte[2];
                        fs.Read(lenBuf, 0, 2);
                        int mLen = (lenBuf[0] << 8) | lenBuf[1];
                        byte[] exSig = new byte[6];
                        if (fs.Read(exSig, 0, 6) == 6 && exSig[0] == 'E' && exSig[1] == 'x' && exSig[2] == 'i' && exSig[3] == 'f')
                        {
                            tiffBase = fs.Position;
                            break;
                        }
                    }
                    else if (marker == 0xDA) break;
                }

                Assert.True(tiffBase > 0);
                fs.Seek(tiffBase, SeekOrigin.Begin);
                byte[] tiffHdr = new byte[8];
                fs.Read(tiffHdr, 0, 8);
                bool isLittleEndian = tiffHdr[0] == 0x49;
                uint ifd0Offset = isLittleEndian
                    ? (uint)(tiffHdr[4] | (tiffHdr[5] << 8) | (tiffHdr[6] << 16) | (tiffHdr[7] << 24))
                    : (uint)((tiffHdr[4] << 24) | (tiffHdr[5] << 16) | (tiffHdr[6] << 8) | tiffHdr[7]);

                fs.Seek(tiffBase + ifd0Offset, SeekOrigin.Begin);
                byte[] cntBuf = new byte[2];
                fs.Read(cntBuf, 0, 2);
                ushort count = isLittleEndian ? (ushort)(cntBuf[0] | (cntBuf[1] << 8)) : (ushort)((cntBuf[0] << 8) | cntBuf[1]);

                uint exifIfdOffset = 0;
                for (int i = 0; i < count; i++)
                {
                    byte[] entry = new byte[12];
                    fs.Read(entry, 0, 12);
                    ushort tag = isLittleEndian ? (ushort)(entry[0] | (entry[1] << 8)) : (ushort)((entry[0] << 8) | entry[1]);
                    uint entryCount = isLittleEndian
                        ? (uint)(entry[4] | (entry[5] << 8) | (entry[6] << 16) | (entry[7] << 24))
                        : (uint)((entry[4] << 24) | (entry[5] << 16) | (entry[6] << 8) | entry[7]);

                    // Software (0x0131), Artist (0x013B), Copyright (0x8298) must have count == 0
                    if (tag == 0x0131 || tag == 0x013B || tag == 0x8298)
                    {
                        Assert.Equal(0u, entryCount);
                    }
                    else if (tag == 0x8769)
                    {
                        exifIfdOffset = isLittleEndian
                            ? (uint)(entry[8] | (entry[9] << 8) | (entry[10] << 16) | (entry[11] << 24))
                            : (uint)((entry[8] << 24) | (entry[9] << 16) | (entry[10] << 8) | entry[11]);
                    }
                }

                // Check ExifSubIFD: BodySerialNumber (0xA431) and LensSerialNumber (0xA435)
                if (exifIfdOffset > 0)
                {
                    fs.Seek(tiffBase + exifIfdOffset, SeekOrigin.Begin);
                    fs.Read(cntBuf, 0, 2);
                    ushort subCount = isLittleEndian ? (ushort)(cntBuf[0] | (cntBuf[1] << 8)) : (ushort)((cntBuf[0] << 8) | cntBuf[1]);
                    for (int j = 0; j < subCount; j++)
                    {
                        byte[] entry = new byte[12];
                        fs.Read(entry, 0, 12);
                        ushort tag = isLittleEndian ? (ushort)(entry[0] | (entry[1] << 8)) : (ushort)((entry[0] << 8) | entry[1]);
                        uint entryCount = isLittleEndian
                            ? (uint)(entry[4] | (entry[5] << 8) | (entry[6] << 16) | (entry[7] << 24))
                            : (uint)((entry[4] << 24) | (entry[5] << 16) | (entry[6] << 8) | entry[7]);

                        if (tag == 0xA420 || tag == 0xA431 || tag == 0xA435)
                        {
                            Assert.Equal(0u, entryCount);
                        }
                    }
                }
            }
            finally
            {
                try { File.Delete(tempRaf); } catch { }
            }
        }

        [Fact]
        public void PatchDsc0922()
        {
            string dng = @"d:\Users\isisk\Documents\test sony\test files\free raws\tag @signatureeditsco DSC_0922.dng";
            string raf = @"d:\Users\isisk\Documents\test sony\test files\free raws\free raws fujifiedc1\tag @signatureeditsco DSC_0922.raf";
            if (File.Exists(dng) && File.Exists(raf))
            {
                bool ok = RafMetadataModifier.InjectMetadata(raf, dng, "GFX50S II");
                Assert.True(ok);
            }
        }

        [Fact]
        public void ReprocessTestFilesFujified_Safe()
        {
            string testDir = @"D:\Users\isisk\Documents\Dev\FujifyNET\test_files";
            string fujifiedDir = Path.Combine(testDir, "test_files fujified");
            if (!Directory.Exists(testDir) || !Directory.Exists(fujifiedDir)) return;

            foreach (var srcFile in Directory.GetFiles(testDir))
            {
                string ext = Path.GetExtension(srcFile).ToLowerInvariant();
                if (ext != ".dng" && ext != ".arw" && ext != ".cr2" && ext != ".cr3" && ext != ".crw" &&
                    ext != ".nef" && ext != ".nrw" && ext != ".orf" && ext != ".rw2" && ext != ".srf" && ext != ".sr2" &&
                    !(ext == ".raf" && !srcFile.Contains("X-T4", StringComparison.OrdinalIgnoreCase))) continue;

                string destRaf = Path.Combine(fujifiedDir, Path.GetFileNameWithoutExtension(srcFile) + ".raf");
                if (File.Exists(destRaf))
                {
                    try
                    {
                        RafMetadataModifier.InjectMetadata(destRaf, srcFile, "GFX50S II");
                    }
                    catch { }
                }
            }
        }

        [Fact]
        public void RafConverterService_InspectContainerStatic_DetectsXTransSensorFromBinaryCfa()
        {
            string xt4Raf = Path.GetFullPath(@"..\..\..\..\test_files\Fujifilm-X-T4.RAF");
            if (!File.Exists(xt4Raf)) xt4Raf = Path.GetFullPath(@"test_files\Fujifilm-X-T4.RAF");

            if (File.Exists(xt4Raf))
            {
                var inspection = RafConverterService.InspectContainerStatic(xt4Raf);
                Assert.Equal(RafSensorType.XTrans, inspection.SensorType);
                Assert.False(inspection.IsValid);
                Assert.Contains("X-Trans", inspection.ErrorMessage);
                Assert.Contains("X-T4", inspection.CameraModel);
            }
        }

        [Fact]
        public void RafConverterService_InspectContainerStatic_DetectsBayerSensorFromBinaryCfa()
        {
            string xt200 = @"D:\Users\isisk\Documents\Dev\RAWtoRAF\container\X-T200_ISO_200.RAF";
            string gfx50 = @"D:\Users\isisk\Documents\Dev\RAWtoRAF\container\Fuji_GFX50S_II_-_Compressed.RAF";

            if (File.Exists(xt200))
            {
                var xt200Inspection = RafConverterService.InspectContainerStatic(xt200);
                Assert.Equal(RafSensorType.Bayer, xt200Inspection.SensorType);
                Assert.True(xt200Inspection.IsValid);
                Assert.Equal(6016, xt200Inspection.Width);
                Assert.Equal(4014, xt200Inspection.Height);
                Assert.Contains("X-T200", xt200Inspection.CameraModel);
            }

            if (File.Exists(gfx50))
            {
                var gfxInspection = RafConverterService.InspectContainerStatic(gfx50);
                Assert.Equal(RafSensorType.Bayer, gfxInspection.SensorType);
                Assert.True(gfxInspection.IsValid);
                Assert.Equal(8472, gfxInspection.Width);
                Assert.Equal(6210, gfxInspection.Height);
                Assert.Contains("GFX50S II", gfxInspection.CameraModel);
            }
        }

        [Fact]
        public void RafConverterService_InspectContainerStatic_HandlesNonRafOrCorruptFile()
        {
            string tempInvalid = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempInvalid, "Not a RAF file at all");
                var inspection = RafConverterService.InspectContainerStatic(tempInvalid);
                Assert.Equal(RafSensorType.Invalid, inspection.SensorType);
                Assert.False(inspection.IsValid);
                Assert.NotNull(inspection.ErrorMessage);
            }
            finally
            {
                try { File.Delete(tempInvalid); } catch { }
            }
        }

        [Fact]
        public void PreferencesViewModel_ExternalContainerLinks_AreInitialized()
        {
            var mockSettings = new MockSettingsService();
            var vm = new PreferencesViewModel(mockSettings);

            Assert.NotNull(vm.OpenPixlsUsCommand);
            Assert.NotNull(vm.OpenDngLabDbCommand);
        }

        [Fact]
        public void PreferencesViewModel_RafContainerCountText_FormatsSingularAndPlural()
        {
            var mockSettings = new MockSettingsService();
            var vm = new PreferencesViewModel(mockSettings);

            // Initially empty
            Assert.False(vm.HasRafContainers);
            Assert.Equal(string.Empty, vm.RafContainerCountText);

            // 1 container
            vm.RafContainers.Add(new RafContainerItemViewModel(@"C:\c1.raf", "X-T200", _ => { }));
            Assert.True(vm.HasRafContainers);
            Assert.Contains("1", vm.RafContainerCountText);

            // 2 containers
            vm.RafContainers.Add(new RafContainerItemViewModel(@"C:\c2.raf", "GFX50S II", _ => { }));
            Assert.Contains("2", vm.RafContainerCountText);
        }

        #region Raw2Raf Native (C-ABI) In-Process Tests

        [Fact]
        public void Raw2RafNative_IsAvailable_AndReturnsValidVersion()
        {
            Assert.True(Raw2RafNative.IsAvailable, "Native raw2raf_ffi library should be available and loadable on Windows x64.");
            string? version = Raw2RafNative.GetVersion();
            Assert.NotNull(version);
            Assert.Contains("1.0.1", version);
        }

        [Fact]
        public void Raw2RafNative_ConvertFile_HandlesInvalidOrNonexistentInputSafely()
        {
            var (success, error) = Raw2RafNative.ConvertFile(
                @"C:\nonexistent_path\fake_raw.arw",
                @"C:\nonexistent_path\output.raf",
                @"C:\nonexistent_path\container.raf");

            Assert.False(success);
            Assert.NotNull(error);
            Assert.Contains("does not exist", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RafConverterService_IsAvailable_ReturnsTrueWhenNativeOrCliAvailable()
        {
            var service = new RafConverterService();
            Assert.True(service.IsAvailable);
            string? pathOrMode = service.GetRaw2RafPath();
            Assert.NotNull(pathOrMode);
            Assert.Contains("raw2raf", pathOrMode, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Raw2RafNative_ConvertFile_ConvertsBayerRawInProcessSuccessfully()
        {
            string container = @"D:\Users\isisk\Documents\Dev\RAWtoRAF\container\X-T200_ISO_200.RAF";
            string sourceRaw = @"D:\Users\isisk\Documents\Dev\FujifyNET\test_files\Sony-a7III.ARW";

            if (File.Exists(container) && File.Exists(sourceRaw))
            {
                string tempOutput = Path.Combine(Path.GetTempPath(), $"native_test_{Guid.NewGuid():N}.raf");
                try
                {
                    var (success, error) = Raw2RafNative.ConvertFile(
                        sourceRaw,
                        tempOutput,
                        container,
                        nonGfxModel: "GFX50S II",
                        autoContainer: true);

                    Assert.True(success, $"In-process raw2raf conversion failed: {error}");
                    Assert.True(File.Exists(tempOutput));
                    var fi = new FileInfo(tempOutput);
                    Assert.True(fi.Length > 1024 * 1024);

                    // Verify inspection
                    var inspection = RafConverterService.InspectContainerStatic(tempOutput);
                    Assert.True(inspection.IsValid);
                    Assert.Equal(RafSensorType.Bayer, inspection.SensorType);
                }
                finally
                {
                    try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }
                }
            }
        }

        #endregion
    }
}
