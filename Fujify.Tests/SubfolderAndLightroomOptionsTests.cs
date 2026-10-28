using System;
using System.IO;
using System.Text.Json;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    public class SubfolderAndLightroomOptionsTests
    {
        [Fact]
        public void ComputeTargetDirectory_WhenSubfolderDisabled_ReturnsBaseDirectory()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            string expectedBase = Path.Combine("C:", "Photos", "Holiday");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFile,
                customOutputFolder: null,
                createSubfolder: false,
                subfolderUseOriginalName: true,
                subfolderSuffix: "-fujified",
                subfolderCustomName: "Fujified");

            Assert.Equal(expectedBase, result);
        }

        [Fact]
        public void ComputeTargetDirectory_WhenSubfolderEnabled_AndKeepOriginalNameWithSuffix()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            string expected = Path.Combine("C:", "Photos", "Holiday", "Holiday-fujified");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFile,
                customOutputFolder: null,
                createSubfolder: true,
                subfolderUseOriginalName: true,
                subfolderSuffix: "-fujified",
                subfolderCustomName: "Fujified");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ComputeTargetDirectory_WhenSubfolderEnabled_AndKeepOriginalNameWithoutSuffix()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            string expected = Path.Combine("C:", "Photos", "Holiday", "Holiday");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFile,
                customOutputFolder: null,
                createSubfolder: true,
                subfolderUseOriginalName: true,
                subfolderSuffix: "",
                subfolderCustomName: "Fujified");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ComputeTargetDirectory_WhenSubfolderEnabled_AndCustomSubfolderName()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            string expected = Path.Combine("C:", "Photos", "Holiday", "ConvertedDNG");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFile,
                customOutputFolder: null,
                createSubfolder: true,
                subfolderUseOriginalName: false,
                subfolderSuffix: "-fujified",
                subfolderCustomName: "ConvertedDNG");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ComputeTargetDirectory_WithCustomOutputFolder_CreatesSubfolderUnderCustomOutput()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            string customOutput = Path.Combine("D:", "ExportFolder");
            string expected = Path.Combine("D:", "ExportFolder", "Holiday-fujified");

            string result = OutputLocationHelper.ComputeTargetDirectory(
                sourceFile,
                customOutputFolder: customOutput,
                createSubfolder: true,
                subfolderUseOriginalName: true,
                subfolderSuffix: "-fujified",
                subfolderCustomName: "Fujified");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void ComputeInitialDestinationFilePath_CombinesSubfolderAndFileSuffix()
        {
            string sourceFile = Path.Combine("C:", "Photos", "Holiday", "DSC001.ARW");
            var settings = new AppSettings
            {
                CreateSubfolder = true,
                SubfolderUseOriginalName = true,
                SubfolderSuffix = "-fujified",
                AppendSuffix = true,
                Suffix = "-fujified"
            };

            string result = OutputLocationHelper.ComputeInitialDestinationFilePath(
                sourceFile,
                customOutputFolder: null,
                settings);

            string expected = Path.Combine("C:", "Photos", "Holiday", "Holiday-fujified", "DSC001-fujified.dng");
            Assert.Equal(expected, result);
        }

        [Fact]
        public void AppSettings_SubfolderProperties_SerializeAndDeserialize()
        {
            var original = new AppSettings
            {
                CreateSubfolder = true,
                SubfolderUseOriginalName = false,
                SubfolderSuffix = "_processed",
                SubfolderCustomName = "DNG_Exports",
                AutoOpenOutputFolder = true
            };

            string json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

            Assert.NotNull(deserialized);
            Assert.True(deserialized.CreateSubfolder);
            Assert.False(deserialized.SubfolderUseOriginalName);
            Assert.Equal("_processed", deserialized.SubfolderSuffix);
            Assert.Equal("DNG_Exports", deserialized.SubfolderCustomName);
            Assert.True(deserialized.AutoOpenOutputFolder);
        }

        [Fact]
        public void FileItem_ErrorHighlightProperties_WhenStatusIsError()
        {
            var item = new Models.FileItem
            {
                FilePath = "C:\\test\\photo.arw",
                Status = "Error: Native conversion failed"
            };

            Assert.True(item.IsError);
            Assert.Equal("#E74C3C", item.StatusColor);
            Assert.Equal("#22E74C3C", item.ErrorBackground);
            Assert.Equal("#80E74C3C", item.ErrorBorderBrush);
            Assert.Equal(1.0, item.ErrorBorderThickness);
            Assert.Equal("✕", item.StatusIcon);
        }

        [Fact]
        public void FileItem_ErrorHighlightProperties_WhenStatusIsSuccessOrPending()
        {
            var pendingItem = new Models.FileItem
            {
                FilePath = "C:\\test\\photo1.arw",
                Status = "Pending"
            };

            Assert.False(pendingItem.IsError);
            Assert.Equal("#A0A0AC", pendingItem.StatusColor);
            Assert.Equal("Transparent", pendingItem.ErrorBackground);
            Assert.Equal("Transparent", pendingItem.ErrorBorderBrush);
            Assert.Equal(0.0, pendingItem.ErrorBorderThickness);
            Assert.Equal("⏳", pendingItem.StatusIcon);

            var successItem = new Models.FileItem
            {
                FilePath = "C:\\test\\photo2.arw",
                Status = "✔"
            };

            Assert.False(successItem.IsError);
            Assert.True(successItem.IsCompleted);
            Assert.Equal("#01D701", successItem.StatusColor);
            Assert.Equal("Transparent", successItem.ErrorBackground);
            Assert.Equal("Transparent", successItem.ErrorBorderBrush);
            Assert.Equal(0.0, successItem.ErrorBorderThickness);
            Assert.Equal("✓", successItem.StatusIcon);
        }

        [Fact]
        public void SortFilesByStatus_MovesErrorFilesToTop()
        {
            var settingsService = new FakeSettingsService();
            var licenseService = new LicenseService(settingsService);
            var vm = new MainViewModel(settingsService: settingsService, licenseService: licenseService);

            var file1 = new Models.FileItem { FilePath = "C:\\photos\\img1.arw", Status = "✔" };
            var file2 = new Models.FileItem { FilePath = "C:\\photos\\img2.arw", Status = "Error: Corrupted file" };
            var file3 = new Models.FileItem { FilePath = "C:\\photos\\img3.arw", Status = "Pending" };
            var file4 = new Models.FileItem { FilePath = "C:\\photos\\img4.arw", Status = "Error: Cannot write" };
            var file5 = new Models.FileItem { FilePath = "C:\\photos\\img5.arw", Status = "✔" };

            vm.Files.Add(file1);
            vm.Files.Add(file2);
            vm.Files.Add(file3);
            vm.Files.Add(file4);
            vm.Files.Add(file5);

            vm.SortFilesByStatus();

            // Error items should be first
            Assert.Equal("C:\\photos\\img2.arw", vm.Files[0].FilePath);
            Assert.Equal("C:\\photos\\img4.arw", vm.Files[1].FilePath);
            // Then pending / other
            Assert.Equal("C:\\photos\\img3.arw", vm.Files[2].FilePath);
            // Then completed
            Assert.Equal("C:\\photos\\img1.arw", vm.Files[3].FilePath);
            Assert.Equal("C:\\photos\\img5.arw", vm.Files[4].FilePath);

            // First error item should be automatically selected
            Assert.Equal(vm.Files[0], vm.SelectedFile);
        }

        [Fact]
        public void PreferencesViewModel_AutoOpenOutputFolder_SavesToSettings()
        {
            var settingsService = new FakeSettingsService();
            var licenseService = new LicenseService(settingsService);
            var prefs = new PreferencesViewModel(settingsService, licenseService);

            Assert.False(prefs.AutoOpenOutputFolder);
            prefs.AutoOpenOutputFolder = true;

            prefs.SaveCommand.Execute(null);

            Assert.True(settingsService.Settings.AutoOpenOutputFolder);
        }

        [Fact]
        public void LightroomOption_IsDisabledOnDesktopPlatforms()
        {
            bool isMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

            var settingsService = new FakeSettingsService();
            var licenseService = new LicenseService(settingsService);
            var prefs = new PreferencesViewModel(settingsService, licenseService);
            Assert.Equal(isMobile, prefs.IsOpenInLightroomSupported);

            if (!isMobile)
            {
                Assert.False(prefs.IsOpenInLightroomSupported);
            }
        }

        private class FakeSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new AppSettings();
            public void Save() { }
            public void Load() { }
        }
    }
}
