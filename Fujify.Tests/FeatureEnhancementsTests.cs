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
    }
}
