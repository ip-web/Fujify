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
using System.Threading.Tasks;
using System.Xml.Linq;
using Fujify.Common;
using Fujify.Models;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    public class FeatureEnhancementsTests
    {
        public FeatureEnhancementsTests()
        {
            Localization.LocalizationManager.Instance.SetLanguage("en");
        }

        #region 1. Sidecar XMP Preservation Tests

        [Fact]
        public void FindSidecarXmp_FindsStandardXmp()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FujifyTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string rawPath = Path.Combine(tempDir, "photo1.ARW");
                string xmpPath = Path.Combine(tempDir, "photo1.xmp");
                File.WriteAllText(rawPath, "raw bytes");
                File.WriteAllText(xmpPath, "<x:xmpmeta/>");

                string? found = DngMetadataEditor.FindSidecarXmp(rawPath);
                Assert.NotNull(found);
                Assert.Equal(xmpPath, found);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FindSidecarXmp_FindsDoubleExtensionXmp()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FujifyTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string rawPath = Path.Combine(tempDir, "photo2.CR2");
                string xmpPath = Path.Combine(tempDir, "photo2.CR2.xmp");
                File.WriteAllText(rawPath, "raw bytes");
                File.WriteAllText(xmpPath, "<x:xmpmeta/>");

                string? found = DngMetadataEditor.FindSidecarXmp(rawPath);
                Assert.NotNull(found);
                Assert.Equal(xmpPath, found);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void MergeCameraProfilesIntoXmp_MergesRatingLabelAndDevelopSettings()
        {
            string sidecarXmp = @"<?xpacket begin='﻿' id='W5M0MpCehiHzreSzNTczkc9d'?>
<x:xmpmeta xmlns:x='adobe:ns:meta/'>
 <rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>
  <rdf:Description rdf:about=''
    xmlns:xmp='http://ns.adobe.com/xap/1.0/'
    xmlns:crs='http://ns.adobe.com/camera-raw-settings/1.0/'
    xmlns:dc='http://purl.org/dc/elements/1.1/'
    xmp:Rating='5'
    xmp:Label='Red'
    crs:HasCrop='True'
    crs:CropTop='0.15'>
   <dc:subject>
    <rdf:Bag>
     <rdf:li>Landscape</rdf:li>
     <rdf:li>Travel</rdf:li>
    </rdf:Bag>
   </dc:subject>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end='w'?>";

            string resultXmp = DngMetadataEditor.MergeCameraProfilesIntoXmp(
                existingXmp: null,
                make: "FUJIFILM",
                model: "X-T5",
                uniqueModel: "Fujifilm X-T5",
                sidecarXmp: sidecarXmp);

            Assert.NotNull(resultXmp);

            // Assert CameraProfiles are properly injected
            Assert.Contains("UniqueCameraModel>Fujifilm X-T5</", resultXmp);
            Assert.Contains("CameraProfiles", resultXmp);

            // Assert sidecar metadata (rating, label, crop, keywords) was seamlessly merged
            Assert.Contains("Rating=\"5\"", resultXmp);
            Assert.Contains("Label=\"Red\"", resultXmp);
            Assert.Contains("CropTop=\"0.15\"", resultXmp);
            Assert.Contains("Landscape", resultXmp);
            Assert.Contains("Travel", resultXmp);
        }

        #endregion

        #region 2. Storage Auto-Detection & Drive Performance Helper Tests

        [Fact]
        public void GetOptimalIngestionConcurrency_ReturnsSafeRange()
        {
            // For standard paths (or null), concurrency is clamped to [2, 8]
            int concurrency = DrivePerformanceHelper.GetOptimalIngestionConcurrency(null);
            Assert.InRange(concurrency, 2, 8);
        }

        [Fact]
        public void GetOptimalConversionConcurrency_FreeTier_AlwaysReturns1()
        {
            int concurrency = DrivePerformanceHelper.GetOptimalConversionConcurrency(
                sampleFilePath: "C:\\test\\photo.dng",
                configuredConcurrency: 8,
                isPro: false);

            Assert.Equal(1, concurrency);
        }

        [Fact]
        public void GetOptimalConversionConcurrency_ProTier_RespectsConfigured()
        {
            int configured = Math.Clamp(Environment.ProcessorCount, 1, 4);
            int concurrency = DrivePerformanceHelper.GetOptimalConversionConcurrency(
                sampleFilePath: "C:\\test\\photo.dng",
                configuredConcurrency: configured,
                isPro: true);

            Assert.Equal(configured, concurrency);
        }

        [Fact]
        public void GetStorageDescription_ReturnsMeaningfulString()
        {
            string desc = DrivePerformanceHelper.GetStorageDescription("C:\\test\\file.dng");
            Assert.False(string.IsNullOrWhiteSpace(desc));
        }

        #endregion

        #region 3. Batch Summary Card Modal Tests

        [Fact]
        public void ShowBatchSummaryModal_AllSuccess_FormatsDurationSpeedAndVolume()
        {
            var vm = new MainViewModel();

            TimeSpan duration = TimeSpan.FromSeconds(5.0);
            long bytes = 100 * 1024 * 1024; // 100 MB

            vm.ShowBatchSummaryModal(totalCount: 10, successCount: 10, duration: duration, totalBytes: bytes, hasErrors: false);

            Assert.True(vm.IsBatchSummaryModal);
            Assert.True(vm.IsBottomSheetOpen);
            Assert.Equal("10 of 10 photos converted", vm.BatchSummaryPhotosCountText);
            Assert.Equal("✔ All Photos Converted", vm.BatchSummaryBadgeText);
            Assert.Equal("5.0s", vm.BatchSummaryDurationText);
            Assert.Equal("2.0 photos/s", vm.BatchSummarySpeedText);
            Assert.Equal("100.0 MB", vm.BatchSummaryVolumeText);
            Assert.Contains("Fujifilm", vm.BatchSummaryNoticeText);
        }

        [Fact]
        public void ShowBatchSummaryModal_WithErrors_ShowsWarningBadge()
        {
            var vm = new MainViewModel();

            TimeSpan duration = TimeSpan.FromSeconds(75.0); // 1m 15s
            long bytes = 2L * 1024 * 1024 * 1024; // 2 GB

            vm.ShowBatchSummaryModal(totalCount: 20, successCount: 18, duration: duration, totalBytes: bytes, hasErrors: true);

            Assert.True(vm.IsBatchSummaryModal);
            Assert.True(vm.IsBottomSheetOpen);
            Assert.Equal("18 of 20 photos converted (2 failed)", vm.BatchSummaryPhotosCountText);
            Assert.Equal("⚠ Completed with Warnings", vm.BatchSummaryBadgeText);
            Assert.Equal("1m 15s", vm.BatchSummaryDurationText);
            Assert.Equal("2.00 GB", vm.BatchSummaryVolumeText);
        }

        [Fact]
        public void ClearQueueAndCloseModal_ClosesModalAndClearsQueue()
        {
            var vm = new MainViewModel();

            vm.Files.Add(new FileItem { FilePath = "C:\\test\\img.dng" });
            vm.ShowBatchSummaryModal(totalCount: 1, successCount: 1, duration: TimeSpan.FromSeconds(1), totalBytes: 1024, hasErrors: false);

            Assert.True(vm.IsModalOpen);
            Assert.True(vm.IsBatchSummaryModal);
            Assert.Single(vm.Files);

            // Execute ClearQueueAndCloseModalCommand
            vm.ClearQueueAndCloseModalCommand.Execute(null);

            Assert.False(vm.IsModalOpen);
            Assert.Empty(vm.Files);
        }

        #endregion

        #region 5. Mobile Multi-Selection Mode Tests

        [Fact]
        public void MultiSelectionMode_FullWorkflowTests()
        {
            var vm = new MainViewModel();
            var item1 = new FileItem { FilePath = "C:\\test\\photo1.dng" };
            var item2 = new FileItem { FilePath = "C:\\test\\photo2.dng" };
            var item3 = new FileItem { FilePath = "C:\\test\\photo3.dng" };
            vm.Files.Add(item1);
            vm.Files.Add(item2);
            vm.Files.Add(item3);

            Assert.False(vm.IsMultiSelectMode);
            // In single-selection mode, the first file is automatically the active SelectedFile
            Assert.Equal(1, vm.SelectedFilesCount);
            Assert.True(vm.HasSelectedFiles);
            Assert.True(item1.IsSelected);
            Assert.False(item2.IsSelected);

            // 1. Enter multi-selection mode on item2
            vm.EnterMultiSelectMode(item2);
            Assert.True(vm.IsMultiSelectMode);
            Assert.False(item1.IsSelected);
            Assert.True(item2.IsSelected);
            Assert.Equal(1, vm.SelectedFilesCount);
            Assert.True(vm.HasSelectedFiles);
            Assert.Equal("1 selected", vm.MultiSelectCountText);
            Assert.Equal("Select All", vm.SelectAllButtonText);

            // 2. Toggle item1
            vm.ToggleFileSelection(item1);
            Assert.True(item1.IsSelected);
            Assert.True(item2.IsSelected);
            Assert.Equal(2, vm.SelectedFilesCount);
            Assert.Equal("2 selected", vm.MultiSelectCountText);

            // 3. Toggle select all -> all 3 selected
            vm.ToggleSelectAll();
            Assert.Equal(3, vm.SelectedFilesCount);
            Assert.Equal("Deselect All", vm.SelectAllButtonText);

            // 4. Delete selected files -> removes all 3, exits multi-select
            vm.DeleteSelectedMultiSelect();
            Assert.Empty(vm.Files);
            Assert.False(vm.IsMultiSelectMode);
        }

        [Fact]
        public void MultiSelectionMode_Exit_RestoresSingleSelection()
        {
            var vm = new MainViewModel();
            var item1 = new FileItem { FilePath = "C:\\test\\photo1.dng" };
            var item2 = new FileItem { FilePath = "C:\\test\\photo2.dng" };
            vm.Files.Add(item1);
            vm.Files.Add(item2);

            vm.EnterMultiSelectMode(item1);
            vm.ToggleFileSelection(item2);
            Assert.True(vm.IsMultiSelectMode);
            Assert.Equal(2, vm.SelectedFilesCount);

            vm.ExitMultiSelectMode();
            Assert.False(vm.IsMultiSelectMode);
            Assert.True(item1.IsSelected);
            Assert.False(item2.IsSelected);
            Assert.Equal(1, vm.SelectedFilesCount);
        }

        #endregion

        #region 5. Conversion Failure Progress Indicator & Logging Tests

        [Fact]
        public void OverallStatusText_WhenAllPhotosSucceed_DisplaysAllConvertedSuccessfully()
        {
            var vm = new MainViewModel();
            vm.Files.Add(new FileItem { FilePath = "C:\\test\\photo1.dng", Status = "✔" });
            vm.Files.Add(new FileItem { FilePath = "C:\\test\\photo2.dng", Status = "✔" });
            vm.Files.Add(new FileItem { FilePath = "C:\\test\\photo3.dng", Status = "✔" });

            Assert.Contains("All 3 photos converted successfully", vm.OverallStatusText);
            Assert.Equal("#01D701", vm.OverallStatusDotColor);
            Assert.Equal("3 / 3 completed", vm.ProgressSummaryText);
        }

        [Fact]
        public void OverallStatusText_WhenSomePhotosFail_DisplaysActualSuccessAndFailureCount()
        {
            var vm = new MainViewModel();
            vm.Files.Add(new FileItem { FilePath = "C:\\test\\photo1.dng", Status = "✔" });
            vm.Files.Add(new FileItem { FilePath = "C:\\test\\photo2.dng", Status = "✔" });
            vm.Files.Add(new FileItem { FilePath = "C:\\test\\photo3.dng", Status = "Error: Invalid header" });

            Assert.Contains("2 of 3 photos converted (1 failed)", vm.OverallStatusText);
            Assert.Equal("#F39C12", vm.OverallStatusDotColor);
            Assert.Contains("2 / 3 completed (1 failed)", vm.ProgressSummaryText);
        }

        [Fact]
        public void OverallStatusText_WhenAllPhotosFail_DisplaysAllFailed()
        {
            var vm = new MainViewModel();
            vm.Files.Add(new FileItem { FilePath = "C:\\test\\photo1.dng", Status = "Error: Timeout" });
            vm.Files.Add(new FileItem { FilePath = "C:\\test\\photo2.dng", Status = "Error: Corrupted" });

            Assert.Contains("Conversion failed (2 failed)", vm.OverallStatusText);
            Assert.Equal("#E74C3C", vm.OverallStatusDotColor);
            Assert.Contains("0 / 2 completed (2 failed)", vm.ProgressSummaryText);
        }

        [Fact]
        public void ConversionLogService_GenerateLogContent_ContainsFileCameraAndErrorMessage()
        {
            var failures = new System.Collections.Generic.List<FailedConversionEntry>
            {
                new FailedConversionEntry(
                    "D:\\Photos\\IMG_0042.CR3",
                    "Canon EOS R5",
                    "RF 24-70mm F2.8L IS USM",
                    "Capture One (RAF)",
                    "Fujifilm X-T5",
                    "No valid donor container matching dimensions (8192x5464)",
                    "   at Fujify.Services.RafConverterService.ConvertToRafAsync()",
                    DateTime.Now),
                new FailedConversionEntry(
                    "D:\\Photos\\DSC_0987.ARW",
                    "Sony ILCE-7M3",
                    "FE 50mm F1.8",
                    "Lightroom (DNG)",
                    "Fujifilm X-T5",
                    "Corrupt RAW payload",
                    null,
                    DateTime.Now)
            };

            string log = ConversionLogService.GenerateLogContent(failures, 10, 8, TimeSpan.FromSeconds(15.2));

            Assert.Contains("FUJIFY - FAILED CONVERSIONS REPORT", log);
            Assert.Contains("Total Processed    : 10", log);
            Assert.Contains("Succeeded          : 8", log);
            Assert.Contains("Failed             : 2", log);
            Assert.Contains("D:\\Photos\\IMG_0042.CR3", log);
            Assert.Contains("Canon EOS R5", log);
            Assert.Contains("RF 24-70mm F2.8L IS USM", log);
            Assert.Contains("No valid donor container matching dimensions (8192x5464)", log);
            Assert.Contains("D:\\Photos\\DSC_0987.ARW", log);
            Assert.Contains("Sony ILCE-7M3", log);
            Assert.Contains("Corrupt RAW payload", log);
        }

        [Fact]
        public void ConversionLogService_WriteFailureLog_WritesToOutputDirectoryWhenEnabled()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FujifyLogTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var failures = new System.Collections.Generic.List<FailedConversionEntry>
                {
                    new FailedConversionEntry(
                        "C:\\raw\\sample.nef",
                        "Nikon Z 6",
                        "NIKKOR Z 24-70mm f/4 S",
                        "Lightroom (DNG)",
                        "Fujifilm X-T5",
                        "Decoding failed",
                        null,
                        DateTime.Now)
                };

                string? writtenPath = ConversionLogService.WriteFailureLog(
                    failures,
                    1,
                    0,
                    TimeSpan.FromSeconds(2.0),
                    tempDir,
                    exportToDirectory: true);

                Assert.NotNull(writtenPath);
                Assert.True(File.Exists(writtenPath));
                Assert.StartsWith(tempDir, writtenPath);

                string content = File.ReadAllText(writtenPath);
                Assert.Contains("sample.nef", content);
                Assert.Contains("Nikon Z 6", content);
                Assert.Contains("Decoding failed", content);

                // Latest log in app data should also exist
                Assert.True(ConversionLogService.HasLatestFailureLog());
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
                ConversionLogService.ClearLatestFailureLog();
            }
        }

        [Fact]
        public void MainViewModel_HasFailureLog_IsFalseWhenNoErrorsOccur()
        {
            var mockSettings = new MockSettingsService();
            var vm = new ViewModels.MainViewModel(settingsService: mockSettings);

            // Initially no log
            Assert.False(vm.HasFailureLog);

            // Setting LastFailureLogPath to a non-existent or null file keeps HasFailureLog false
            vm.LastFailureLogPath = null;
            Assert.False(vm.HasFailureLog);

            // Even if an old stale log file exists in AppData, if the current batch had no failure, HasFailureLog must be false
            string tempLog = Path.Combine(Path.GetTempPath(), $"failure_test_{Guid.NewGuid():N}.log");
            try
            {
                File.WriteAllText(tempLog, "dummy log");
                vm.LastFailureLogPath = tempLog;
                Assert.True(vm.HasFailureLog);

                // When cleared upon successful batch completion
                vm.LastFailureLogPath = null;
                Assert.False(vm.HasFailureLog);
            }
            finally
            {
                try { File.Delete(tempLog); } catch { }
            }
        }

        [Fact]
        public void PreferencesViewModel_ExportFailedConversionsLog_PersistsToSettings()
        {
            var mockSettings = new MockSettingsService();
            var vm = new PreferencesViewModel(mockSettings);

            Assert.False(vm.ExportFailedConversionsLog);
            vm.ExportFailedConversionsLog = true;

            Assert.True(mockSettings.Settings.ExportFailedConversionsLog);
            Assert.True(mockSettings.WasSaved);
            Assert.NotNull(vm.OpenLatestFailureLogCommand);
        }

        #endregion

        #region 8. Process and Stop Button Dynamic Transformation Tests

        [Fact]
        public void ProcessButton_InitialState_IsProcessWithSparkleIcon()
        {
            var mockSettings = new MockSettingsService();
            var vm = new MainViewModel(settingsService: mockSettings);

            Assert.Equal("Process", vm.ProcessButtonContent);
            Assert.Equal(MainViewModel.SparkleIconData, vm.ProcessButtonIcon);
            Assert.False(vm.IsProcessing);
            Assert.False(vm.CanProcessOrStop);
            Assert.NotNull(vm.ProcessCommand);
            Assert.NotNull(vm.StopCommand);
        }

        [Fact]
        public void ProcessButton_WhenPendingFilesPresent_CanProcessOrStopIsTrue()
        {
            var mockSettings = new MockSettingsService();
            var vm = new MainViewModel(settingsService: mockSettings);

            vm.Files.Add(new FileItem { FilePath = "dummy.arw", Status = "Pending" });

            Assert.True(vm.CanProcessOrStop);
            Assert.Equal("Process", vm.ProcessButtonContent);
            Assert.Equal(MainViewModel.SparkleIconData, vm.ProcessButtonIcon);
            Assert.Equal("Process batch with Fujifilm simulation profiles", vm.ProcessButtonTooltip);
        }

        [Fact]
        public void ExecuteStop_SignalsCancellationAndTransformsButton()
        {
            var mockSettings = new MockSettingsService();
            var vm = new MainViewModel(settingsService: mockSettings);

            vm.Files.Add(new FileItem { FilePath = "test.arw", Status = "Pending" });

            // Set IsProcessing via reflection
            var isProcessingProp = typeof(MainViewModel).GetProperty("IsProcessing");
            isProcessingProp?.SetValue(vm, true);

            Assert.True(vm.IsProcessing);
            Assert.Equal("Stop", vm.ProcessButtonContent);
            Assert.Equal(MainViewModel.StopIconData, vm.ProcessButtonIcon);
            Assert.Equal("Stop processing batch", vm.ProcessButtonTooltip);

            // Execute stop
            vm.ExecuteStop();

            Assert.Equal("Stopping...", vm.ProcessButtonContent);
            Assert.False(vm.CanProcessOrStop);
        }

        #endregion

        private class MockSettingsService : ISettingsService
        {
            public AppSettings Settings { get; set; } = new();
            public bool WasSaved { get; private set; }
            public void Save() => WasSaved = true;
            public void Load() { }
        }
    }
}
