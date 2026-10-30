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
using System.Text.Json;
using Fujify.Localization;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    public class MultilingualLocalizationTests : IDisposable
    {
        public MultilingualLocalizationTests()
        {
            // Reset to English before each test
            LocalizationManager.Instance.SetLanguage("en");
        }

        public void Dispose()
        {
            // Reset to English after each test
            LocalizationManager.Instance.SetLanguage("en");
        }

        #region 1. LocalizationManager Core Tests

        [Fact]
        public void AvailableLanguages_ContainsEnglishFrenchSpanishGerman()
        {
            var languages = LocalizationManager.AvailableLanguages;
            Assert.Contains(languages, l => l.Code == "en");
            Assert.Contains(languages, l => l.Code == "fr");
            Assert.Contains(languages, l => l.Code == "es");
            Assert.Contains(languages, l => l.Code == "de");
        }

        [Theory]
        [InlineData("fr", "fr")]
        [InlineData("fr-FR", "fr")]
        [InlineData("FR", "fr")]
        [InlineData("es", "es")]
        [InlineData("es-ES", "es")]
        [InlineData("de", "de")]
        [InlineData("de-DE", "de")]
        [InlineData("en", "en")]
        [InlineData("en-US", "en")]
        [InlineData("unknown", "en")]
        [InlineData(null, "en")]
        public void NormalizeLanguageCode_NormalizesCorrectly(string? input, string expected)
        {
            string normalized = LocalizationManager.NormalizeLanguageCode(input);
            Assert.Equal(expected, normalized);
        }

        [Fact]
        public void Translations_EnglishReturnsExpectedStrings()
        {
            LocalizationManager.Instance.SetLanguage("en");

            Assert.Equal("Settings", LocalizationManager.Tr("Settings"));
            Assert.Equal("Add Files", LocalizationManager.Tr("AddFiles"));
            Assert.Equal("Clear Queue", LocalizationManager.Tr("ClearQueue"));
            Assert.Equal("Duration", LocalizationManager.Tr("Duration"));
            Assert.Equal("Throughput", LocalizationManager.Tr("Throughput"));
            Assert.Equal("Volume Converted", LocalizationManager.Tr("VolumeConverted"));
        }

        [Fact]
        public void Translations_FrenchReturnsExpectedStrings()
        {
            LocalizationManager.Instance.SetLanguage("fr");

            Assert.Equal("Paramètres", LocalizationManager.Tr("Settings"));
            Assert.Equal("Ajouter des fichiers", LocalizationManager.Tr("AddFiles"));
            Assert.Equal("Vider la file", LocalizationManager.Tr("ClearQueue"));
            Assert.Equal("Durée", LocalizationManager.Tr("Duration"));
            Assert.Equal("Débit", LocalizationManager.Tr("Throughput"));
            Assert.Equal("Volume converti", LocalizationManager.Tr("VolumeConverted"));
            Assert.Equal("Déposez vos photos RAW ou DNG ici", LocalizationManager.Tr("DropPhotosHere"));
        }

        [Fact]
        public void Translations_SpanishReturnsExpectedStrings()
        {
            LocalizationManager.Instance.SetLanguage("es");

            Assert.Equal("Ajustes", LocalizationManager.Tr("Settings"));
            Assert.Equal("Añadir archivos", LocalizationManager.Tr("AddFiles"));
            Assert.Equal("Limpiar cola", LocalizationManager.Tr("ClearQueue"));
            Assert.Equal("Duración", LocalizationManager.Tr("Duration"));
            Assert.Equal("Velocidad", LocalizationManager.Tr("Throughput"));
            Assert.Equal("Volumen procesado", LocalizationManager.Tr("VolumeConverted"));
            Assert.Equal("Arrastra fotos RAW o DNG aquí", LocalizationManager.Tr("DropPhotosHere"));
        }

        [Fact]
        public void Translations_GermanReturnsExpectedStrings()
        {
            LocalizationManager.Instance.SetLanguage("de");

            Assert.Equal("Einstellungen", LocalizationManager.Tr("Settings"));
            Assert.Equal("Dateien hinzufügen", LocalizationManager.Tr("AddFiles"));
            Assert.Equal("Warteschlange leeren", LocalizationManager.Tr("ClearQueue"));
            Assert.Equal("Dauer", LocalizationManager.Tr("Duration"));
            Assert.Equal("Geschwindigkeit", LocalizationManager.Tr("Throughput"));
            Assert.Equal("Konvertiertes Volumen", LocalizationManager.Tr("VolumeConverted"));
            Assert.Equal("RAW- oder DNG-Fotos hier ablegen", LocalizationManager.Tr("DropPhotosHere"));
        }

        [Fact]
        public void Format_FormatsPlaceholdersAcrossLanguages()
        {
            LocalizationManager.Instance.SetLanguage("en");
            string enFormatted = LocalizationManager.TrFormat("PhotosConvertedCount", 5, 10);
            Assert.Equal("5 of 10 photos converted", enFormatted);

            LocalizationManager.Instance.SetLanguage("fr");
            string frFormatted = LocalizationManager.TrFormat("PhotosConvertedCount", 5, 10);
            Assert.Equal("5 sur 10 photos converties", frFormatted);

            LocalizationManager.Instance.SetLanguage("es");
            string esFormatted = LocalizationManager.TrFormat("PhotosConvertedCount", 5, 10);
            Assert.Equal("5 de 10 fotos convertidas", esFormatted);

            LocalizationManager.Instance.SetLanguage("de");
            string deFormatted = LocalizationManager.TrFormat("PhotosConvertedCount", 5, 10);
            Assert.Equal("5 von 10 Fotos konvertiert", deFormatted);
        }

        [Fact]
        public void Fallback_ReturnsEnglishForMissingKeyInTargetLanguage()
        {
            LocalizationManager.Instance.SetLanguage("fr");
            // If key is not in fr, it falls back to en
            string res = LocalizationManager.Tr("AppTitle");
            Assert.Equal("Fujifier", res);
        }

        [Fact]
        public void LanguageChangedEvent_FiresOnLanguageChange()
        {
            bool eventFired = false;
            void Handler(object? s, EventArgs e) => eventFired = true;

            LocalizationManager.Instance.LanguageChanged += Handler;
            try
            {
                LocalizationManager.Instance.SetLanguage("es");
                Assert.True(eventFired);
                Assert.Equal("es", LocalizationManager.Instance.CurrentLanguage);
            }
            finally
            {
                LocalizationManager.Instance.LanguageChanged -= Handler;
            }
        }

        #endregion

        #region 2. Settings & PreferencesViewModel Integration Tests

        [Fact]
        public void AppSettings_LanguageSerializesAndDeserializes()
        {
            var settings = new AppSettings { Language = "es" };
            string json = JsonSerializer.Serialize(settings);
            var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("es", deserialized.Language);
        }

        [Fact]
        public void PreferencesViewModel_LanguageRadioSelectionUpdatesCorrectly()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FujifyTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string settingsFile = Path.Combine(tempDir, "settings.json");
                var settingsService = new SettingsService(settingsFile);
                var licenseService = new LicenseService(settingsService);

                var vm = new PreferencesViewModel(settingsService, licenseService);

                Assert.True(vm.IsEnglishSelected);
                Assert.False(vm.IsFrenchSelected);
                Assert.False(vm.IsSpanishSelected);
                Assert.False(vm.IsGermanSelected);

                // Switch to French
                vm.IsFrenchSelected = true;
                Assert.Equal("fr", vm.Language);
                Assert.True(vm.IsFrenchSelected);
                Assert.False(vm.IsEnglishSelected);
                Assert.Equal("fr", LocalizationManager.Instance.CurrentLanguage);

                // Switch to German
                vm.IsGermanSelected = true;
                Assert.Equal("de", vm.Language);
                Assert.True(vm.IsGermanSelected);
                Assert.False(vm.IsFrenchSelected);
                Assert.Equal("de", LocalizationManager.Instance.CurrentLanguage);

                // Switch to Spanish
                vm.IsSpanishSelected = true;
                Assert.Equal("es", vm.Language);
                Assert.True(vm.IsSpanishSelected);
                Assert.Equal("es", LocalizationManager.Instance.CurrentLanguage);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void PreferencesViewModel_SavePersistsLanguage()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FujifyTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string settingsFile = Path.Combine(tempDir, "settings.json");
                var settingsService = new SettingsService(settingsFile);
                var licenseService = new LicenseService(settingsService);

                var vm = new PreferencesViewModel(settingsService, licenseService)
                {
                    IsFrenchSelected = true
                };

                vm.SaveCommand.Execute(null);

                // Reload settings from disk
                var reloadedSettings = new SettingsService(settingsFile);
                Assert.Equal("fr", reloadedSettings.Settings.Language);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void PreferencesViewModel_CancelRestoresOriginalLanguage()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FujifyTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string settingsFile = Path.Combine(tempDir, "settings.json");
                var settingsService = new SettingsService(settingsFile);
                settingsService.Settings.Language = "en";
                settingsService.Save();

                var licenseService = new LicenseService(settingsService);
                var vm = new PreferencesViewModel(settingsService, licenseService)
                {
                    IsSpanishSelected = true
                };
                Assert.Equal("es", LocalizationManager.Instance.CurrentLanguage);

                vm.CancelCommand.Execute(null);
                Assert.Equal("en", LocalizationManager.Instance.CurrentLanguage);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        #endregion
    }
}

