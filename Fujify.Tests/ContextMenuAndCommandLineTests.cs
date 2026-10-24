using System;
using System.IO;
using System.Runtime.InteropServices;
using Fujify.Common;
using Fujify.Services;
using Microsoft.Win32;
using Xunit;

namespace Fujify.Tests
{
    [Collection("Registry")]
    public class ContextMenuAndCommandLineTests
    {
        [Theory]
        [InlineData(new[] { "--process", "C:\\test\\photo.arw" }, LaunchMode.Process, "C:\\test\\photo.arw")]
        [InlineData(new[] { "-p", "C:\\test\\photo.arw" }, LaunchMode.Process, "C:\\test\\photo.arw")]
        [InlineData(new[] { "--process-to", "C:\\test\\photo.arw" }, LaunchMode.ProcessTo, "C:\\test\\photo.arw")]
        [InlineData(new[] { "-pt", "C:\\test\\photo.arw" }, LaunchMode.ProcessTo, "C:\\test\\photo.arw")]
        [InlineData(new[] { "--queue", "C:\\test\\photo.arw" }, LaunchMode.Queue, "C:\\test\\photo.arw")]
        [InlineData(new[] { "-q", "C:\\test\\photo.arw" }, LaunchMode.Queue, "C:\\test\\photo.arw")]
        [InlineData(new[] { "C:\\test\\photo.arw" }, LaunchMode.Queue, "C:\\test\\photo.arw")]
        public void CommandLineArgs_ShouldParseModesAndFiles(string[] args, LaunchMode expectedMode, string expectedFile)
        {
            var parsed = CommandLineArgs.Parse(args);
            Assert.Equal(expectedMode, parsed.Mode);
            Assert.Single(parsed.Files);
            Assert.Equal(expectedFile, parsed.Files[0]);
        }

        [Fact]
        public void CommandLineArgs_ShouldHandleEmptyAndMultipleFiles()
        {
            var empty = CommandLineArgs.Parse(Array.Empty<string>());
            Assert.Equal(LaunchMode.Queue, empty.Mode);
            Assert.Empty(empty.Files);
            Assert.False(empty.IsQuickProcess);

            var multi = CommandLineArgs.Parse(new[] { "--process", "file1.arw", "file2.cr3", "file3.nef" });
            Assert.Equal(LaunchMode.Process, multi.Mode);
            Assert.True(multi.IsQuickProcess);
            Assert.Equal(3, multi.Files.Count);
        }

        [Fact]
        public void WindowsContextMenu_Registration_Lifecycle()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // Windows-only test
            }

            try
            {
                // Register cascading menu
                bool regResult = WindowsContextMenuService.RegisterContextMenu();
                Assert.True(regResult, "RegisterContextMenu should return true on Windows");
                Assert.True(WindowsContextMenuService.IsContextMenuRegistered(), "IsContextMenuRegistered should be true");

                // Verify parent key
                using var parentKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\.arw\shell\Fujifier");
                Assert.NotNull(parentKey);
                Assert.Equal("Fujifier", parentKey.GetValue("MUIVerb")?.ToString());
                Assert.Equal(string.Empty, parentKey.GetValue("SubCommands")?.ToString());

                // Verify 01_queue
                using var qCmd = Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\.arw\shell\Fujifier\shell\01_queue\command");
                Assert.NotNull(qCmd);
                string? qVal = qCmd.GetValue(null)?.ToString();
                Assert.Contains("--queue", qVal);

                // Verify 02_process
                using var pCmd = Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\.arw\shell\Fujifier\shell\02_process\command");
                Assert.NotNull(pCmd);
                string? pVal = pCmd.GetValue(null)?.ToString();
                Assert.Contains("--process", pVal);

                // Verify 03_processto
                using var ptCmd = Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\.arw\shell\Fujifier\shell\03_processto\command");
                Assert.NotNull(ptCmd);
                string? ptVal = ptCmd.GetValue(null)?.ToString();
                Assert.Contains("--process-to", ptVal);

                // Unregister and verify removal
                bool unregResult = WindowsContextMenuService.UnregisterContextMenu();
                Assert.True(unregResult, "UnregisterContextMenu should succeed");
                Assert.False(WindowsContextMenuService.IsContextMenuRegistered(), "IsContextMenuRegistered should be false after unregistration");
            }
            finally
            {
                // Clean up in case of failure
                WindowsContextMenuService.UnregisterContextMenu();
            }
        }
    }
}
