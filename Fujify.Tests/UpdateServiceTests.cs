using System;
using System.Collections.Generic;
using Fujify.Common;
using Fujify.Models;
using Fujify.Services;
using Xunit;

namespace Fujify.Tests
{
    public class UpdateServiceTests
    {
        private class MockSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new();
            public void Save() { }
            public void Load() { }
        }

        [Theory]
        [InlineData("v2.1.0", 2, 1, 0)]
        [InlineData("2.1.0", 2, 1, 0)]
        [InlineData("V2.1.0", 2, 1, 0)]
        [InlineData("v1.1", 1, 1, 0)]
        [InlineData("1.1", 1, 1, 0)]
        [InlineData("3", 3, 0, 0)]
        [InlineData("v2.2.0-beta", 2, 2, 0)]
        [InlineData("v2.2.0-preview.1+commit123", 2, 2, 0)]
        [InlineData("v2.2.0+build456", 2, 2, 0)]
        [InlineData("  v2.5.1  ", 2, 5, 1)]
        [InlineData("v2.1.0.4", 2, 1, 0)]
        public void ParseVersion_ValidSemVerStrings_ParsesCorrectly(string input, int expectedMajor, int expectedMinor, int expectedBuild)
        {
            var version = UpdateService.ParseVersion(input);

            Assert.NotNull(version);
            Assert.Equal(expectedMajor, version.Major);
            Assert.Equal(expectedMinor, version.Minor);
            Assert.Equal(expectedBuild, version.Build);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-version")]
        [InlineData("vABC")]
        [InlineData("release")]
        public void ParseVersion_InvalidOrEmptyStrings_ReturnsNull(string? input)
        {
            var version = UpdateService.ParseVersion(input);
            Assert.Null(version);
        }

        [Fact]
        public void VersionComparison_DeterminesNewerVersionAccurately()
        {
            var currentVersion = new Version(2, 1, 0);

            var olderRelease = UpdateService.ParseVersion("v1.1.0");
            var sameRelease = UpdateService.ParseVersion("v2.1.0");
            var newerPatch = UpdateService.ParseVersion("v2.1.1");
            var newerMinor = UpdateService.ParseVersion("v2.2.0");
            var newerMajor = UpdateService.ParseVersion("v3.0.0");

            Assert.False(olderRelease > currentVersion, "1.1.0 should not be newer than 2.1.0");
            Assert.False(sameRelease > currentVersion, "2.1.0 should not be newer than 2.1.0");
            Assert.True(newerPatch > currentVersion, "2.1.1 should be newer than 2.1.0");
            Assert.True(newerMinor > currentVersion, "2.2.0 should be newer than 2.1.0");
            Assert.True(newerMajor > currentVersion, "3.0.0 should be newer than 2.1.0");
        }

        [Fact]
        public void ParseReleaseJson_ValidGitHubPayload_ParsesReleaseAndAssets()
        {
            string json = @"{
                ""tag_name"": ""v2.2.0"",
                ""name"": ""Fujifier 2.2.0 - Supercharged Performance"",
                ""body"": ""### What's Changed\n* Added autoupdater\n* Improved DNG conversion speed"",
                ""html_url"": ""https://github.com/ip-web/Fujifier/releases/tag/v2.2.0"",
                ""published_at"": ""2026-10-24T12:00:00Z"",
                ""draft"": false,
                ""assets"": [
                    {
                        ""name"": ""Fujifier-v2.2.0-Setup.exe"",
                        ""browser_download_url"": ""https://github.com/ip-web/Fujifier/releases/download/v2.2.0/Fujifier-v2.2.0-Setup.exe"",
                        ""size"": 45000000,
                        ""content_type"": ""application/vnd.microsoft.portable-executable""
                    },
                    {
                        ""name"": ""Fujifier-v2.2.0-linux-x64.tar.gz"",
                        ""browser_download_url"": ""https://github.com/ip-web/Fujifier/releases/download/v2.2.0/Fujifier-v2.2.0-linux-x64.tar.gz"",
                        ""size"": 38000000,
                        ""content_type"": ""application/gzip""
                    }
                ]
            }";

            var updateInfo = UpdateService.ParseReleaseJson(json);

            Assert.NotNull(updateInfo);
            Assert.Equal("v2.2.0", updateInfo.TagName);
            Assert.Equal(new Version(2, 2, 0), updateInfo.Version);
            Assert.Equal("2.2.0", updateInfo.VersionString);
            Assert.Equal("Fujifier 2.2.0 - Supercharged Performance", updateInfo.Title);
            Assert.Contains("Added autoupdater", updateInfo.ReleaseNotes);
            Assert.Equal("https://github.com/ip-web/Fujifier/releases/tag/v2.2.0", updateInfo.ReleaseUrl);
            Assert.NotNull(updateInfo.PublishedAt);
            Assert.Equal(2, updateInfo.Assets.Count);

            if (OperatingSystem.IsWindows())
            {
                Assert.NotNull(updateInfo.MatchingAsset);
                Assert.Equal("Fujifier-v2.2.0-Setup.exe", updateInfo.MatchingAsset.Name);
                Assert.True(updateInfo.IsInstallerAvailable);
                Assert.Equal("42.9 MB", updateInfo.MatchingAsset.FormattedSize);
            }
        }

        [Fact]
        public void ParseReleaseJson_DraftRelease_IsIgnoredAndReturnsNull()
        {
            string draftJson = @"{
                ""tag_name"": ""v2.3.0"",
                ""draft"": true,
                ""assets"": []
            }";

            var updateInfo = UpdateService.ParseReleaseJson(draftJson);
            Assert.Null(updateInfo);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{ invalid json }")]
        public void ParseReleaseJson_MalformedOrEmpty_ReturnsNull(string invalidJson)
        {
            var updateInfo = UpdateService.ParseReleaseJson(invalidJson);
            Assert.Null(updateInfo);
        }

        [Fact]
        public void SelectMatchingAsset_OnWindows_PrefersSetupInstallerExe()
        {
            var assets = new List<UpdateAsset>
            {
                new UpdateAsset { Name = "source-code.zip", DownloadUrl = "https://example.com/src.zip", Size = 1000 },
                new UpdateAsset { Name = "Fujifier-v2.2.0-linux.tar.gz", DownloadUrl = "https://example.com/linux.tar.gz", Size = 2000 },
                new UpdateAsset { Name = "Fujifier-v2.2.0-portable.zip", DownloadUrl = "https://example.com/win.zip", Size = 3000 },
                new UpdateAsset { Name = "Fujifier-v2.2.0-Setup.exe", DownloadUrl = "https://example.com/setup.exe", Size = 4000 }
            };

            var match = UpdateService.SelectMatchingAsset(assets);

            if (OperatingSystem.IsWindows())
            {
                Assert.NotNull(match);
                Assert.Equal("Fujifier-v2.2.0-Setup.exe", match.Name);
            }
            else if (OperatingSystem.IsLinux())
            {
                Assert.NotNull(match);
                Assert.Contains("linux", match.Name);
            }
        }

        [Fact]
        public void UpdateCheckResult_ConstructorsAndHelpers_SetPropertiesProperly()
        {
            var info = new UpdateInfo { TagName = "v2.5.0", Version = new Version(2, 5, 0) };

            var available = UpdateCheckResult.UpdateAvailable(info, "2.1.0");
            Assert.True(available.Success);
            Assert.True(available.IsUpdateAvailable);
            Assert.Same(info, available.UpdateInfo);
            Assert.Equal("2.1.0", available.CurrentVersion);
            Assert.Null(available.ErrorMessage);

            var upToDate = UpdateCheckResult.UpToDate("2.1.0", info);
            Assert.True(upToDate.Success);
            Assert.False(upToDate.IsUpdateAvailable);
            Assert.Same(info, upToDate.UpdateInfo);

            var failure = UpdateCheckResult.Failure("Connection timed out", "2.1.0");
            Assert.False(failure.Success);
            Assert.False(failure.IsUpdateAvailable);
            Assert.Null(failure.UpdateInfo);
            Assert.Equal("Connection timed out", failure.ErrorMessage);
        }

        [Fact]
        public void UpdateService_FallbackVersion_MatchesProductRelease()
        {
            var settings = new MockSettingsService();
            var service = new UpdateService(settings);

            Assert.NotNull(service.CurrentVersion);
            Assert.True(service.CurrentVersion >= new Version(2, 1, 0));
            Assert.False(string.IsNullOrWhiteSpace(service.CurrentVersionString));
        }

        [Fact]
        public void SettingsService_UpdateSettingsProperties_ArePersisted()
        {
            var settings = new MockSettingsService();
            Assert.True(settings.Settings.CheckForUpdatesOnStartup);
            Assert.Null(settings.Settings.SkippedVersion);
            Assert.Null(settings.Settings.LastUpdateCheck);

            settings.Settings.CheckForUpdatesOnStartup = false;
            settings.Settings.SkippedVersion = "2.2.0";
            var now = DateTime.UtcNow;
            settings.Settings.LastUpdateCheck = now;

            Assert.False(settings.Settings.CheckForUpdatesOnStartup);
            Assert.Equal("2.2.0", settings.Settings.SkippedVersion);
            Assert.Equal(now, settings.Settings.LastUpdateCheck);
        }
    }
}
