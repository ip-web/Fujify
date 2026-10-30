using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Fujify.Localization;
using Fujify.Models;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    public class LocExtensionLiveBindingTests
    {
        private class FakeSettingsService : ISettingsService
        {
            public AppSettings Settings { get; set; } = new AppSettings();
            public bool IsLoaded => true;
            public void Load() { }
            public void Save() { }
        }

        private class MockDialogService : IDialogService
        {
            public bool ConfirmationResult { get; set; } = true;
            public int ConfirmationCallCount { get; private set; }

            public Task<bool> ShowConfirmationAsync(Window? owner, string title, string message, string? confirmText = null, string? cancelText = null)
            {
                ConfirmationCallCount++;
                return Task.FromResult(ConfirmationResult);
            }

            public Task ShowMessageAsync(Window? owner, string title, string message, bool isSuccess = true) => Task.CompletedTask;
            public Task<FileConflictResolution> PromptFileConflictAsync(Window? owner, string destinationFilePath) => Task.FromResult(new FileConflictResolution(ConflictChoice.Skip, false));
            public Task<IReadOnlyList<string>> ShowOpenFileDialogAsync(Window? owner, string title, bool allowMultiple = true) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            public Task<string?> ShowFolderBrowserDialogAsync(Window? owner, string title) => Task.FromResult<string?>(null);
            public Task ShowPreferencesDialogAsync(Window? owner) => Task.CompletedTask;
            public Task ShowAboutDialogAsync(Window? owner) => Task.CompletedTask;
        }

        [Fact]
        public void LocExtension_StepByStep()
        {
            LocalizationManager.Instance.SetLanguage("en");
            Assert.Equal("Settings", LocalizationManager.Instance["Settings"]);

            var textBlock = new TextBlock();
            var binding = new Binding("[Settings]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay
            };
            textBlock.Bind(TextBlock.TextProperty, binding);

            Assert.Equal("Settings", textBlock.Text);

            LocalizationManager.Instance.SetLanguage("fr");
            Assert.Equal("Paramètres", LocalizationManager.Instance["Settings"]);
            Assert.Equal("Paramètres", textBlock.Text);

            LocalizationManager.Instance.SetLanguage("es");
            Assert.Equal("Ajustes", textBlock.Text);

            LocalizationManager.Instance.SetLanguage("de");
            Assert.Equal("Einstellungen", textBlock.Text);

            LocalizationManager.Instance.SetLanguage("en");
            Assert.Equal("Settings", textBlock.Text);
        }

        [Fact]
        public void MainViewModel_DynamicLocalization_UpdatesPropertiesOnLanguageChange()
        {
            var settings = new FakeSettingsService();
            var license = new LicenseService(settings);
            var vm = new MainViewModel(settingsService: settings, licenseService: license);

            LocalizationManager.Instance.SetLanguage("en");
            Assert.Equal("Ready", vm.OverallStatusText);
            Assert.Equal("Same folder as originals", vm.OutputDestinationSummary);
            Assert.Equal("(Optional)", vm.FormattedOutputFolderPath);
            Assert.Equal("Process", vm.ProcessButtonContent);

            LocalizationManager.Instance.SetLanguage("fr");
            Assert.Equal("Prêt", vm.OverallStatusText);
            Assert.Equal("Même dossier que les originaux", vm.OutputDestinationSummary);
            Assert.Equal("(Optionnel)", vm.FormattedOutputFolderPath);
            Assert.Equal("Convertir", vm.ProcessButtonContent);

            LocalizationManager.Instance.SetLanguage("es");
            Assert.Equal("Listo", vm.OverallStatusText);
            Assert.Equal("Misma carpeta que los originales", vm.OutputDestinationSummary);
            Assert.Equal("(Opcional)", vm.FormattedOutputFolderPath);
            Assert.Equal("Procesar", vm.ProcessButtonContent);

            LocalizationManager.Instance.SetLanguage("de");
            Assert.Equal("Bereit", vm.OverallStatusText);
            Assert.Equal("Gleicher Ordner wie Originale", vm.OutputDestinationSummary);
            Assert.Equal("(Optional)", vm.FormattedOutputFolderPath);
            Assert.Equal("Konvertieren", vm.ProcessButtonContent);

            // Reset back to English
            LocalizationManager.Instance.SetLanguage("en");
        }

        [Fact]
        public async Task MainViewModel_ConfirmationModal_ResolveConfirmAndCancel()
        {
            var settings = new FakeSettingsService();
            var license = new LicenseService(settings);
            var vm = new MainViewModel(settingsService: settings, licenseService: license);

            // Test confirm
            var confirmTask = vm.ShowConfirmationModalAsync("Test Title", "Test Warning", "Confirm", "Cancel");
            Assert.True(vm.IsConfirmationModal);
            Assert.True(vm.IsBottomSheetOpen);
            Assert.Equal("Test Title", vm.ModalTitle);
            Assert.Equal("Test Warning", vm.ModalMessage);
            Assert.Equal("Confirm", vm.ConfirmationConfirmText);
            Assert.Equal("Cancel", vm.ConfirmationCancelText);

            vm.ConfirmModalActionCommand.Execute(null);
            bool confirmResult = await confirmTask;
            Assert.True(confirmResult);
            Assert.False(vm.IsConfirmationModal);

            // Test cancel
            var cancelTask = vm.ShowConfirmationModalAsync("Test Title 2", "Test Warning 2");
            Assert.True(vm.IsConfirmationModal);
            vm.CancelModalActionCommand.Execute(null);
            bool cancelResult = await cancelTask;
            Assert.False(cancelResult);
            Assert.False(vm.IsConfirmationModal);

            // Test CloseModal resolves with false
            var closeTask = vm.ShowConfirmationModalAsync("Test Title 3", "Test Warning 3");
            Assert.True(vm.IsConfirmationModal);
            vm.CloseModal();
            bool closeResult = await closeTask;
            Assert.False(closeResult);
            Assert.False(vm.IsConfirmationModal);
        }

        [Fact]
        public void LocalizationManager_ContainsAllRequiredKeysForAllLanguages()
        {
            string[] requiredKeys = new[]
            {
                "Ready", "SameFolderAsOriginals", "SubfolderPrefix", "ImportingPhotos",
                "ProcessingPhotoCount", "AllPhotosConvertedSuccess", "PhotosInQueue",
                "PhotoInQueueSingle", "Stopping", "Optional", "DisableBackupTitle",
                "DisableBackupWarning", "DisableBackupConfirm", "DisableBackupCancel"
            };

            const string fallback = "___FALLBACK___";

            foreach (var lang in new[] { "en", "fr", "es", "de" })
            {
                LocalizationManager.Instance.SetLanguage(lang);
                foreach (var key in requiredKeys)
                {
                    string tr = LocalizationManager.Instance.Get(key, fallback);
                    Assert.NotEqual(fallback, tr);
                    Assert.False(string.IsNullOrWhiteSpace(tr));
                }
            }

            LocalizationManager.Instance.SetLanguage("en");
        }
    }
}
