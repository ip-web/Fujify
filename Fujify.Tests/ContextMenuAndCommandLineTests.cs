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

        [Fact]
        public void ContextMenuCommands_NullOrEmptyHandling_ShouldNotThrow()
        {
            var ex1 = Record.Exception(() => CrossPlatformLauncher.ShowFileProperties(""));
            Assert.Null(ex1);

            var ex2 = Record.Exception(() => CrossPlatformLauncher.OpenWith(""));
            Assert.Null(ex2);
        }

        private class MockSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new();
            public void Save() { }
            public void Load() { }
        }

        [Fact]
        public void FileItem_IsSelected_ShouldRaisePropertyChanged()
        {
            var item = new Models.FileItem { FilePath = "C:\\test\\photo.arw" };
            bool raised = false;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Models.FileItem.IsSelected))
                {
                    raised = true;
                }
            };

            item.IsSelected = true;
            Assert.True(raised);
            Assert.True(item.IsSelected);
        }

        [Fact]
        public void MainViewModel_SelectedFile_ShouldSynchronizeIsSelected()
        {
            var settings = new MockSettingsService();
            var vm = new ViewModels.MainViewModel(settingsService: settings);
            var file1 = new Models.FileItem { FilePath = "C:\\test\\photo1.arw" };
            var file2 = new Models.FileItem { FilePath = "C:\\test\\photo2.arw" };

            vm.Files.Add(file1);
            vm.Files.Add(file2);

            vm.SelectedFile = file1;
            Assert.True(file1.IsSelected);
            Assert.False(file2.IsSelected);

            vm.SelectedFile = file2;
            Assert.False(file1.IsSelected);
            Assert.True(file2.IsSelected);
        }

        [Fact]
        public void MainViewModel_ContextMenuActions_WithSingleFileItem_ShouldExecuteCorrectly()
        {
            var settings = new MockSettingsService();
            var vm = new ViewModels.MainViewModel(settingsService: settings);
            var file1 = new Models.FileItem { FilePath = "C:\\test\\photo1.arw", Status = "Done" };
            var file2 = new Models.FileItem { FilePath = "C:\\test\\photo2.arw", Status = "Error" };

            vm.Files.Add(file1);
            vm.Files.Add(file2);

            // Reset to pending via single item command parameter (as in Grid View context menu)
            vm.ResetStatusCommand.Execute(file1);
            Assert.Equal("Pending", file1.Status);

            // Remove single item via command parameter (as in Grid View context menu)
            vm.RemoveFileCommand.Execute(file2);
            Assert.Single(vm.Files);
            Assert.Equal(file1, vm.Files[0]);
        }

        [Theory]
        // 100% DPI: width=1180, buttons width=138 (starts at 1042), height=34
        [InlineData(1100, 5, 1180, 1.0, true)]    // Top part of buttons (previously blocked by DWM)
        [InlineData(1179, 1, 1180, 1.0, true)]    // Top edge of close button
        [InlineData(1179, 30, 1180, 1.0, true)]   // Bottom part of close button
        [InlineData(1043, 10, 1180, 1.0, true)]   // Minimize button
        [InlineData(500, 10, 1180, 1.0, false)]   // Titlebar spacer (should remain native HTCAPTION)
        [InlineData(50, 10, 1180, 1.0, false)]    // Studio logo area
        [InlineData(1100, 50, 1180, 1.0, false)]  // Below titlebar
        // 150% High DPI: width=1770, buttons width=207 (starts at 1563), height=51
        [InlineData(1600, 8, 1770, 1.5, true)]    // Top part of buttons under high-DPI scaling
        [InlineData(1769, 45, 1770, 1.5, true)]   // Bottom part under high-DPI scaling
        [InlineData(800, 15, 1770, 1.5, false)]   // Titlebar spacer under high-DPI scaling
        public void CaptionButtons_HitTest_ShouldRoutePointerToClient(
            int clientX, int clientY, int clientWidth, double scaling, bool expectedIsOverButtons)
        {
            bool isOver = Views.MainWindow.IsPointerOverCaptionButtons(clientX, clientY, clientWidth, scaling);
            Assert.Equal(expectedIsOverButtons, isOver);
        }

        [Fact]
        public void MainViewModel_MobileSidebar_ToggleAndClose_ShouldWorkProperly()
        {
            var settings = new MockSettingsService();
            var vm = new ViewModels.MainViewModel(settingsService: settings);

            // Default state
            Assert.False(vm.IsMobileSidebarOpen);
            Assert.Equal("▲", vm.MobileSidebarChevron);

            // Toggle open
            vm.ToggleMobileSidebarCommand.Execute(null);
            Assert.True(vm.IsMobileSidebarOpen);
            Assert.Equal("▼", vm.MobileSidebarChevron);

            // Toggle close
            vm.ToggleMobileSidebarCommand.Execute(null);
            Assert.False(vm.IsMobileSidebarOpen);
            Assert.Equal("▲", vm.MobileSidebarChevron);

            // Explicit open & close commands
            vm.OpenMobileSidebarCommand.Execute(null);
            Assert.True(vm.IsMobileSidebarOpen);
            Assert.Equal("▼", vm.MobileSidebarChevron);

            vm.CloseMobileSidebarCommand.Execute(null);
            Assert.False(vm.IsMobileSidebarOpen);
            Assert.Equal("▲", vm.MobileSidebarChevron);
        }

        [Fact]
        public void MainViewModel_MobileSidebar_ShouldCloseWhenSwitchingToDesktop()
        {
            var settings = new MockSettingsService();
            var vm = new ViewModels.MainViewModel(settingsService: settings);

            vm.UpdateLayoutMode(400, 700);
            Assert.True(vm.IsPhoneLayout);

            vm.OpenMobileSidebarCommand.Execute(null);
            Assert.True(vm.IsMobileSidebarOpen);

            // Switch to desktop width
            vm.UpdateLayoutMode(1000, 700);
            Assert.False(vm.IsPhoneLayout);
            Assert.False(vm.IsMobileSidebarOpen);
        }

        [Fact]
        public void MainViewModel_SelectInspectorInfoTab_ShouldAutoSelectFirstFileIfNoneSelected()
        {
            var settings = new MockSettingsService();
            var vm = new ViewModels.MainViewModel(settingsService: settings);
            var file = new Models.FileItem { FilePath = "C:\\test\\photo.arw" };

            // Start with empty files
            vm.SelectInspectorSettingsTabCommand.Execute(null);
            Assert.True(vm.IsInspectorSettingsTab);

            // Add file (auto-selected by collection changed)
            vm.Files.Add(file);
            Assert.Equal(file, vm.SelectedFile);

            // Switch to info tab
            vm.SelectInspectorInfoTabCommand.Execute(null);
            Assert.True(vm.IsInspectorInfoTab);
            Assert.Equal(file, vm.SelectedFile);
        }

        [Fact]
        public async System.Threading.Tasks.Task MainViewModel_OpenPreferences_InPhoneLayout_ShouldShowPreferencesModal()
        {
            var settings = new MockSettingsService();
            var vm = new ViewModels.MainViewModel(settingsService: settings);
            vm.UpdateLayoutMode(450, 700);
            Assert.True(vm.IsPhoneLayout);

            if (vm.OpenPreferencesCommand is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCmd)
            {
                await asyncCmd.ExecuteAsync(null);
            }
            else
            {
                vm.OpenPreferencesCommand.Execute(null);
            }

            Assert.True(vm.IsPreferencesModal);
            Assert.True(vm.IsModalOpen);
        }
    }
}
