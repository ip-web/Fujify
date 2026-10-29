using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fujify.Common;
using Fujify.Models;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    public class BackupAndTrashOptionTests : IDisposable
    {
        private readonly string _testDir;

        public BackupAndTrashOptionTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"FujifyBackupTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, recursive: true);
                }
            }
            catch { }
        }

        [Fact]
        public void AppSettings_BackupOriginalFile_DefaultsToTrue()
        {
            var settings = new AppSettings();
            Assert.True(settings.BackupOriginalFile);
        }

        [Fact]
        public void PreferencesViewModel_ImportPicturesInstantly_AliasesSkipThumbnailGeneration()
        {
            var settingsService = new FakeSettingsService();
            var licenseService = new LicenseService(settingsService);
            var prefs = new PreferencesViewModel(settingsService, licenseService);

            Assert.False(prefs.ImportPicturesInstantly);
            Assert.False(prefs.SkipThumbnailGeneration);

            prefs.ImportPicturesInstantly = true;
            Assert.True(prefs.ImportPicturesInstantly);
            Assert.True(prefs.SkipThumbnailGeneration);

            prefs.SkipThumbnailGeneration = false;
            Assert.False(prefs.ImportPicturesInstantly);
            Assert.False(prefs.SkipThumbnailGeneration);

            prefs.ImportPicturesInstantly = true;
            prefs.SaveCommand.Execute(null);
            Assert.True(settingsService.Settings.SkipThumbnailGeneration);
        }

        [Fact]
        public void PreferencesViewModel_BackupOriginalFile_LoadsAndSavesCorrectly()
        {
            var settingsService = new FakeSettingsService();
            settingsService.Settings.BackupOriginalFile = true;
            var licenseService = new LicenseService(settingsService);
            var prefs = new PreferencesViewModel(settingsService, licenseService);

            Assert.True(prefs.BackupOriginalFile);

            prefs.BackupOriginalFile = false;
            prefs.SaveCommand.Execute(null);
            Assert.False(settingsService.Settings.BackupOriginalFile);
        }

        [Fact]
        public void MainViewModel_CreateBackup_ReflectsAndUpdatesSettings()
        {
            var settingsService = new FakeSettingsService();
            settingsService.Settings.BackupOriginalFile = true;
            var licenseService = new LicenseService(settingsService);
            var vm = new MainViewModel(settingsService: settingsService, licenseService: licenseService);

            Assert.True(vm.CreateBackup);

            vm.CreateBackup = false;
            Assert.False(settingsService.Settings.BackupOriginalFile);

            vm.CreateBackup = true;
            Assert.True(settingsService.Settings.BackupOriginalFile);
        }

        [Fact]
        public void FileTrashHelper_SendToTrash_NonExistentFile_ReturnsFalseSafely()
        {
            string nonExistent = Path.Combine(_testDir, "non_existent.dng");
            bool result = FileTrashHelper.SendToTrash(nonExistent);
            Assert.False(result);
        }

        [Fact]
        public void FileTrashHelper_SendToTrash_ExistingFile_RemovesFileFromPath()
        {
            string testFile = Path.Combine(_testDir, "test_to_trash.txt");
            File.WriteAllText(testFile, "Hello Trash");
            Assert.True(File.Exists(testFile));

            bool result = FileTrashHelper.SendToTrash(testFile);
            Assert.True(result);
            Assert.False(File.Exists(testFile));
        }

        [Fact]
        public void InPlaceDngBackupNaming_ComputesCorrectOrigDngPath()
        {
            string dngPath = Path.Combine(_testDir, "DSC_0001.DNG");
            string expectedBackupPath = Path.Combine(_testDir, "DSC_0001ORIG.DNG");

            string origBaseName = Path.GetFileNameWithoutExtension(dngPath);
            string backupPath = Path.Combine(Path.GetDirectoryName(dngPath) ?? string.Empty, $"{origBaseName}ORIG.DNG");

            Assert.Equal(expectedBackupPath, backupPath);
        }

        [Fact]
        public void NonDngFile_TargetDngPath_DoesNotOverwriteOriginalFile()
        {
            string rawFile = Path.Combine(_testDir, "DSC_0002.ARW");
            File.WriteAllText(rawFile, "fake raw content");

            var settings = new AppSettings { CreateSubfolder = false, AppendSuffix = false };
            string initialDest = OutputLocationHelper.ComputeInitialDestinationFilePath(rawFile, null, settings);

            Assert.Equal(Path.Combine(_testDir, "DSC_0002.dng"), initialDest, ignoreCase: true);
            Assert.NotEqual(rawFile, initialDest);
            Assert.True(File.Exists(rawFile));
        }

        private class FakeSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new AppSettings();
            public void Save() { }
            public void Load() { }
        }
    }
}
