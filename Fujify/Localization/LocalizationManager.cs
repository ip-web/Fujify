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
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using Avalonia;

namespace Fujify.Localization
{
    public record LanguageItem(string Code, string DisplayName, string NativeName);

    public class LocalizationManager : INotifyPropertyChanged
    {
        private static readonly Lazy<LocalizationManager> _lazyInstance = new(() => new LocalizationManager());
        public static LocalizationManager Instance => _lazyInstance.Value;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? LanguageChanged;

        public static readonly IReadOnlyList<LanguageItem> AvailableLanguages = new List<LanguageItem>
        {
            new("en", "English", "English"),
            new("fr", "French", "Français"),
            new("es", "Spanish", "Español"),
            new("de", "German", "Deutsch")
        };

        private string _currentLanguage = "en";
        public string CurrentLanguage
        {
            get => _currentLanguage;
            private set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    OnLanguageChanged();
                }
            }
        }

        public string this[string key] => Get(key);

        private readonly Dictionary<string, Dictionary<string, string>> _translations = new(StringComparer.OrdinalIgnoreCase);

        private LocalizationManager()
        {
            RegisterTranslations();
            SetLanguage("en");
        }

        public static string Tr(string key) => Instance.Get(key);

        public static string TrFormat(string key, params object[] args) => Instance.Format(key, args);

        public string Get(string key, string? defaultValue = null)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            if (_translations.TryGetValue(_currentLanguage, out var langDict) && langDict.TryGetValue(key, out var val))
            {
                return val;
            }

            // Fallback to English
            if (_translations.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
            {
                return enVal;
            }

            return defaultValue ?? key;
        }

        public string Format(string key, params object[] args)
        {
            string template = Get(key);
            try
            {
                return string.Format(template, args);
            }
            catch
            {
                return template;
            }
        }

        public void SetLanguage(string? languageCode)
        {
            string code = NormalizeLanguageCode(languageCode);
            if (!_translations.ContainsKey(code))
            {
                code = "en";
            }

            CurrentLanguage = code;
        }

        public static string NormalizeLanguageCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "en";
            string lower = code.Trim().ToLowerInvariant();
            if (lower.StartsWith("fr")) return "fr";
            if (lower.StartsWith("es")) return "es";
            if (lower.StartsWith("de")) return "de";
            return "en";
        }

        private void OnLanguageChanged()
        {
            try
            {
                var culture = new CultureInfo(_currentLanguage);
                Thread.CurrentThread.CurrentUICulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch { }

            // Sync with Avalonia Application resources for DynamicResource bindings
            if (Application.Current != null)
            {
                if (_translations.TryGetValue(_currentLanguage, out var currentDict))
                {
                    foreach (var kvp in currentDict)
                    {
                        Application.Current.Resources[$"Loc_{kvp.Key}"] = kvp.Value;
                    }
                }
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RegisterTranslations()
        {
            // English
            var en = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // App Header & Navigation
                ["AppTitle"] = "Fujifier",
                ["AppSubtitle"] = "Fujifilm Film Simulation Profiles for Any RAW Photo",
                ["Settings"] = "Settings",
                ["About"] = "About",
                ["Website"] = "Website",
                ["GitHub"] = "GitHub",
                ["Releases"] = "Releases",
                ["Pro"] = "PRO",
                ["FreeTier"] = "Free Tier",
                ["SelectSimulationProfile"] = "Select Fujifilm Simulation Camera",

                // View Modes & Actions
                ["TableView"] = "Table View",
                ["GridView"] = "Grid View",
                ["AddFiles"] = "Add Files",
                ["AddFolder"] = "Add Folder",
                ["ClearQueue"] = "Clear Queue",
                ["Process"] = "Process",
                ["ProcessAll"] = "Process All",
                ["Processing"] = "Processing...",
                ["Cancel"] = "Cancel",
                ["Stop"] = "Stop",

                // Dropzone & Empty State
                ["DropPhotosHere"] = "Drop RAW or DNG photos here",
                ["DropzoneSubtitle"] = "Supports ARW, CR2, CR3, NEF, RAF, RW2, DNG, PEF, ORF and more",
                ["ChooseFiles"] = "Choose Files",
                ["ChooseFolder"] = "Choose Folder",

                // Photo Table Headers & Columns
                ["HeaderPhoto"] = "Photo",
                ["HeaderFileName"] = "File Name",
                ["HeaderCameraModel"] = "Camera Model",
                ["HeaderSize"] = "Size",
                ["HeaderStatus"] = "Status",
                ["HeaderActions"] = "Actions",
                ["Remove"] = "Remove",
                ["Pending"] = "Pending",
                ["Success"] = "Completed",
                ["Error"] = "Error",

                // Quick Settings & Side Panel
                ["QuickSettings"] = "Quick Settings",
                ["FilmSimulation"] = "Film Simulation",
                ["OutputFolder"] = "Output Folder",
                ["OutputFolderPlaceholder"] = "Save to original folder (default)",
                ["Browse"] = "Browse",
                ["Clear"] = "Clear",
                ["BackupOriginal"] = "Backup Original Files",
                ["LosslessCompression"] = "Lossless Compression",
                ["EmbedRaw"] = "Embed Original RAW into DNG",

                // Inspector Sidebar
                ["InspectorTitle"] = "Inspector",
                ["TabSettings"] = "Settings",
                ["TabPhotoInfo"] = "Photo Info",
                ["NoPhotoSelected"] = "No photo selected",
                ["SelectPhotoHint"] = "Select a photo from the queue to inspect details",
                ["PropertyFileName"] = "File Name",
                ["PropertyPath"] = "File Path",
                ["PropertySize"] = "File Size",
                ["PropertyDimensions"] = "Dimensions",
                ["PropertyCamera"] = "Camera",
                ["PropertyLens"] = "Lens",
                ["PropertyFocalLength"] = "Focal Length",
                ["PropertyAperture"] = "Aperture",
                ["PropertyShutterSpeed"] = "Shutter Speed",
                ["PropertyIso"] = "ISO",
                ["PropertyDate"] = "Date Taken",

                // Batch Summary Modal
                ["BatchCompleteTitle"] = "Batch Conversion Complete",
                ["AllPhotosConverted"] = "✔ All Photos Converted",
                ["CompletedWithWarnings"] = "⚠ Completed with Warnings",
                ["PhotosConvertedCount"] = "{0} of {1} photos converted",
                ["PhotosConvertedWithFailures"] = "{0} of {1} photos converted ({2} failed)",
                ["Duration"] = "Duration",
                ["Throughput"] = "Throughput",
                ["VolumeConverted"] = "Volume Converted",
                ["Simulation"] = "Simulation",
                ["BatchNoticeSuccess"] = "Fujifilm film simulations & sidecar XMP metadata merged.",
                ["BatchNoticeWarnings"] = "Some files could not be converted. Check the file list for details.",
                ["OpenOutputFolder"] = "📁 Open Output Folder",
                ["Done"] = "Done",

                // Preferences / Settings Categories
                ["Appearance"] = "Appearance",
                ["Language"] = "Language",
                ["SelectLanguage"] = "Application Language",
                ["LanguageHint"] = "Select the display language for Fujifier interface.",
                ["Theme"] = "Theme",
                ["AppearanceMode"] = "Appearance Mode",
                ["ThemeAuto"] = "Automatic",
                ["ThemeDark"] = "Dark",
                ["ThemeAmoled"] = "AMOLED",
                ["ThemeLight"] = "Light",
                ["ThemeDescription"] = "Automatic adapts to system mode. Dark features refined slate tones. AMOLED provides true pitch-black contrast.",
                ["Performance"] = "Performance",
                ["ConcurrentConversions"] = "Concurrent Conversions",
                ["ImportInstantly"] = "Import Pictures Instantly",
                ["ImportInstantlyHint"] = "Skips thumbnail generation for performance, importing photos into the queue instantly",
                ["FileHandling"] = "File Handling",
                ["AddSuffix"] = "Add Suffix to Converted File Name",
                ["AddSuffixHint"] = "Appends custom text identifier before .dng extension",
                ["SuffixLabel"] = "Suffix:",
                ["SaveInSubfolder"] = "Save Converted Photos in Subfolder",
                ["SaveInSubfolderHint"] = "Creates a subfolder for converted DNGs instead of saving directly in source folder",
                ["SubfolderName"] = "Subfolder Name:",
                ["DesktopIntegration"] = "Desktop Integration",
                ["ContextMenuWindows"] = "Add Fujifier to Windows Context Menu",
                ["AutoOpenFolder"] = "Open Output Folder Automatically",
                ["AutoOpenLightroom"] = "Open in Adobe Lightroom Automatically",
                ["CheckUpdatesStartup"] = "Check for Updates on Startup",
                ["CheckUpdatesNow"] = "Check for Updates Now",
                ["License"] = "License",
                ["BuyPro"] = "⭐ Buy Pro",
                ["Activate"] = "Activate",
                ["Login"] = "Login",
                ["LicenseKey"] = "License Key",

                // Common Dialogs & Messages
                ["Warning"] = "Warning",
                ["Notice"] = "Notice",
                ["Close"] = "Close",
                ["GotIt"] = "Got it",
                ["NoFilesSelected"] = "No files to process.",
                ["NoSupportedPhotos"] = "No Supported Photos Found",
                ["NoSupportedPhotosMsg"] = "No supported RAW or DNG photos could be loaded from the selection."
            };
            _translations["en"] = en;

            // French (Français)
            var fr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // App Header & Navigation
                ["AppTitle"] = "Fujifier",
                ["AppSubtitle"] = "Simulations de films Fujifilm pour toutes vos photos RAW",
                ["Settings"] = "Paramètres",
                ["About"] = "À propos",
                ["Website"] = "Site Web",
                ["GitHub"] = "GitHub",
                ["Releases"] = "Versions",
                ["Pro"] = "PRO",
                ["FreeTier"] = "Version Gratuite",
                ["SelectSimulationProfile"] = "Sélectionner l'appareil simulation Fujifilm",

                // View Modes & Actions
                ["TableView"] = "Vue Tableau",
                ["GridView"] = "Vue Grille",
                ["AddFiles"] = "Ajouter des fichiers",
                ["AddFolder"] = "Ajouter un dossier",
                ["ClearQueue"] = "Vider la file",
                ["Process"] = "Traiter",
                ["ProcessAll"] = "Tout traiter",
                ["Processing"] = "Traitement en cours...",
                ["Cancel"] = "Annuler",
                ["Stop"] = "Arrêter",

                // Dropzone & Empty State
                ["DropPhotosHere"] = "Déposez vos photos RAW ou DNG ici",
                ["DropzoneSubtitle"] = "Prend en charge ARW, CR2, CR3, NEF, RAF, RW2, DNG, PEF, ORF et plus",
                ["ChooseFiles"] = "Choisir des fichiers",
                ["ChooseFolder"] = "Choisir un dossier",

                // Photo Table Headers & Columns
                ["HeaderPhoto"] = "Photo",
                ["HeaderFileName"] = "Nom du fichier",
                ["HeaderCameraModel"] = "Modèle d'appareil",
                ["HeaderSize"] = "Taille",
                ["HeaderStatus"] = "Statut",
                ["HeaderActions"] = "Actions",
                ["Remove"] = "Supprimer",
                ["Pending"] = "En attente",
                ["Success"] = "Converti",
                ["Error"] = "Erreur",

                // Quick Settings & Side Panel
                ["QuickSettings"] = "Paramètres rapides",
                ["FilmSimulation"] = "Simulation de film",
                ["OutputFolder"] = "Dossier de sortie",
                ["OutputFolderPlaceholder"] = "Enregistrer dans le dossier source (défaut)",
                ["Browse"] = "Parcourir",
                ["Clear"] = "Effacer",
                ["BackupOriginal"] = "Sauvegarder les originaux",
                ["LosslessCompression"] = "Compression sans perte",
                ["EmbedRaw"] = "Intégrer le RAW original au DNG",

                // Inspector Sidebar
                ["InspectorTitle"] = "Inspecteur",
                ["TabSettings"] = "Paramètres",
                ["TabPhotoInfo"] = "Infos photo",
                ["NoPhotoSelected"] = "Aucune photo sélectionnée",
                ["SelectPhotoHint"] = "Sélectionnez une photo dans la file pour afficher ses détails",
                ["PropertyFileName"] = "Nom du fichier",
                ["PropertyPath"] = "Chemin",
                ["PropertySize"] = "Taille",
                ["PropertyDimensions"] = "Dimensions",
                ["PropertyCamera"] = "Appareil photo",
                ["PropertyLens"] = "Objectif",
                ["PropertyFocalLength"] = "Focale",
                ["PropertyAperture"] = "Ouverture",
                ["PropertyShutterSpeed"] = "Vitesse d'obturation",
                ["PropertyIso"] = "ISO",
                ["PropertyDate"] = "Date de prise de vue",

                // Batch Summary Modal
                ["BatchCompleteTitle"] = "Conversion par lot terminée",
                ["AllPhotosConverted"] = "✔ Toutes les photos converties",
                ["CompletedWithWarnings"] = "⚠ Terminé avec des avertissements",
                ["PhotosConvertedCount"] = "{0} sur {1} photos converties",
                ["PhotosConvertedWithFailures"] = "{0} sur {1} photos converties ({2} échouées)",
                ["Duration"] = "Durée",
                ["Throughput"] = "Débit",
                ["VolumeConverted"] = "Volume converti",
                ["Simulation"] = "Simulation",
                ["BatchNoticeSuccess"] = "Simulations Fujifilm et métadonnées XMP sidecar intégrées.",
                ["BatchNoticeWarnings"] = "Certains fichiers n'ont pas pu être convertis. Vérifiez la liste.",
                ["OpenOutputFolder"] = "📁 Ouvrir le dossier de sortie",
                ["Done"] = "Terminé",

                // Preferences / Settings Categories
                ["Appearance"] = "Apparence",
                ["Language"] = "Langue",
                ["SelectLanguage"] = "Langue de l'application",
                ["LanguageHint"] = "Choisissez la langue d'affichage de l'interface.",
                ["Theme"] = "Thème",
                ["AppearanceMode"] = "Mode d'apparence",
                ["ThemeAuto"] = "Automatique",
                ["ThemeDark"] = "Sombre",
                ["ThemeAmoled"] = "AMOLED",
                ["ThemeLight"] = "Clair",
                ["ThemeDescription"] = "Automatique s'adapte au mode système. Sombre offre des tons ardoise élégants. AMOLED procure un contraste noir pur.",
                ["Performance"] = "Performances",
                ["ConcurrentConversions"] = "Conversions simultanées",
                ["ImportInstantly"] = "Importer les images instantanément",
                ["ImportInstantlyHint"] = "Ignore la création de miniatures pour importer les photos instantanément",
                ["FileHandling"] = "Gestion des fichiers",
                ["AddSuffix"] = "Ajouter un suffixe au nom de fichier",
                ["AddSuffixHint"] = "Ajoute un texte personnalisé avant l'extension .dng",
                ["SuffixLabel"] = "Suffixe :",
                ["SaveInSubfolder"] = "Enregistrer dans un sous-dossier",
                ["SaveInSubfolderHint"] = "Crée un sous-dossier dédié pour les DNG convertis",
                ["SubfolderName"] = "Nom du sous-dossier :",
                ["DesktopIntegration"] = "Intégration au bureau",
                ["ContextMenuWindows"] = "Ajouter Fujifier au menu contextuel Windows",
                ["AutoOpenFolder"] = "Ouvrir le dossier de sortie automatiquement",
                ["AutoOpenLightroom"] = "Ouvrir dans Adobe Lightroom automatiquement",
                ["CheckUpdatesStartup"] = "Rechercher les mises à jour au démarrage",
                ["CheckUpdatesNow"] = "Rechercher des mises à jour maintenant",
                ["License"] = "Licence",
                ["BuyPro"] = "⭐ Acheter Pro",
                ["Activate"] = "Activer",
                ["Login"] = "Connexion",
                ["LicenseKey"] = "Clé de licence",

                // Common Dialogs & Messages
                ["Warning"] = "Avertissement",
                ["Notice"] = "Information",
                ["Close"] = "Fermer",
                ["GotIt"] = "Compris",
                ["NoFilesSelected"] = "Aucun fichier à traiter.",
                ["NoSupportedPhotos"] = "Aucune photo prise en charge",
                ["NoSupportedPhotosMsg"] = "Aucune photo RAW ou DNG prise en charge n'a été trouvée dans la sélection."
            };
            _translations["fr"] = fr;

            // Spanish (Español)
            var es = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // App Header & Navigation
                ["AppTitle"] = "Fujifier",
                ["AppSubtitle"] = "Perfiles de simulación de película Fujifilm para cualquier RAW",
                ["Settings"] = "Ajustes",
                ["About"] = "Acerca de",
                ["Website"] = "Sitio Web",
                ["GitHub"] = "GitHub",
                ["Releases"] = "Versiones",
                ["Pro"] = "PRO",
                ["FreeTier"] = "Versión Gratuita",
                ["SelectSimulationProfile"] = "Seleccionar cámara de simulación Fujifilm",

                // View Modes & Actions
                ["TableView"] = "Vista Tabla",
                ["GridView"] = "Vista Cuadrícula",
                ["AddFiles"] = "Añadir archivos",
                ["AddFolder"] = "Añadir carpeta",
                ["ClearQueue"] = "Limpiar cola",
                ["Process"] = "Procesar",
                ["ProcessAll"] = "Procesar todo",
                ["Processing"] = "Procesando...",
                ["Cancel"] = "Cancelar",
                ["Stop"] = "Detener",

                // Dropzone & Empty State
                ["DropPhotosHere"] = "Arrastra fotos RAW o DNG aquí",
                ["DropzoneSubtitle"] = "Compatible con ARW, CR2, CR3, NEF, RAF, RW2, DNG, PEF, ORF y más",
                ["ChooseFiles"] = "Elegir archivos",
                ["ChooseFolder"] = "Elegir carpeta",

                // Photo Table Headers & Columns
                ["HeaderPhoto"] = "Foto",
                ["HeaderFileName"] = "Nombre de archivo",
                ["HeaderCameraModel"] = "Modelo de cámara",
                ["HeaderSize"] = "Tamaño",
                ["HeaderStatus"] = "Estado",
                ["HeaderActions"] = "Acciones",
                ["Remove"] = "Eliminar",
                ["Pending"] = "Pendiente",
                ["Success"] = "Completado",
                ["Error"] = "Error",

                // Quick Settings & Side Panel
                ["QuickSettings"] = "Ajustes rápidos",
                ["FilmSimulation"] = "Simulación de película",
                ["OutputFolder"] = "Carpeta de destino",
                ["OutputFolderPlaceholder"] = "Guardar en carpeta original (predeterminado)",
                ["Browse"] = "Examinar",
                ["Clear"] = "Limpiar",
                ["BackupOriginal"] = "Hacer copia de originales",
                ["LosslessCompression"] = "Compresión sin pérdida",
                ["EmbedRaw"] = "Incrustar RAW original en DNG",

                // Inspector Sidebar
                ["InspectorTitle"] = "Inspector",
                ["TabSettings"] = "Ajustes",
                ["TabPhotoInfo"] = "Info de foto",
                ["NoPhotoSelected"] = "Ninguna foto seleccionada",
                ["SelectPhotoHint"] = "Selecciona una foto de la cola para ver detalles",
                ["PropertyFileName"] = "Nombre de archivo",
                ["PropertyPath"] = "Ruta",
                ["PropertySize"] = "Tamaño",
                ["PropertyDimensions"] = "Dimensiones",
                ["PropertyCamera"] = "Cámara",
                ["PropertyLens"] = "Objetivo",
                ["PropertyFocalLength"] = "Distancia focal",
                ["PropertyAperture"] = "Apertura",
                ["PropertyShutterSpeed"] = "Velocidad",
                ["PropertyIso"] = "ISO",
                ["PropertyDate"] = "Fecha de captura",

                // Batch Summary Modal
                ["BatchCompleteTitle"] = "Conversión por lotes completada",
                ["AllPhotosConverted"] = "✔ Todas las fotos convertidas",
                ["CompletedWithWarnings"] = "⚠ Completado con advertencias",
                ["PhotosConvertedCount"] = "{0} de {1} fotos convertidas",
                ["PhotosConvertedWithFailures"] = "{0} de {1} fotos convertidas ({2} fallidas)",
                ["Duration"] = "Duración",
                ["Throughput"] = "Velocidad",
                ["VolumeConverted"] = "Volumen procesado",
                ["Simulation"] = "Simulación",
                ["BatchNoticeSuccess"] = "Simulaciones Fujifilm y metadatos XMP sidecar integrados.",
                ["BatchNoticeWarnings"] = "No se pudieron convertir algunos archivos. Revisa la lista.",
                ["OpenOutputFolder"] = "📁 Abrir carpeta de destino",
                ["Done"] = "Listo",

                // Preferences / Settings Categories
                ["Appearance"] = "Apariencia",
                ["Language"] = "Idioma",
                ["SelectLanguage"] = "Idioma de la aplicación",
                ["LanguageHint"] = "Selecciona el idioma de la interfaz de usuario.",
                ["Theme"] = "Tema",
                ["AppearanceMode"] = "Modo de apariencia",
                ["ThemeAuto"] = "Automático",
                ["ThemeDark"] = "Oscuro",
                ["ThemeAmoled"] = "AMOLED",
                ["ThemeLight"] = "Claro",
                ["ThemeDescription"] = "Automático se adapta al sistema. Oscuro ofrece tonos elegantes. AMOLED brinda contraste negro puro.",
                ["Performance"] = "Rendimiento",
                ["ConcurrentConversions"] = "Conversiones simultáneas",
                ["ImportInstantly"] = "Importar fotos al instante",
                ["ImportInstantlyHint"] = "Omite la generación de miniaturas para importar fotos inmediatamente",
                ["FileHandling"] = "Gestión de archivos",
                ["AddSuffix"] = "Añadir sufijo al nombre de archivo",
                ["AddSuffixHint"] = "Añade texto personalizado antes de la extensión .dng",
                ["SuffixLabel"] = "Sufijo:",
                ["SaveInSubfolder"] = "Guardar fotos en subcarpeta",
                ["SaveInSubfolderHint"] = "Crea una subcarpeta dedicada para los DNG convertidos",
                ["SubfolderName"] = "Nombre de subcarpeta:",
                ["DesktopIntegration"] = "Integración con escritorio",
                ["ContextMenuWindows"] = "Añadir Fujifier al menú contextual de Windows",
                ["AutoOpenFolder"] = "Abrir carpeta de salida automáticamente",
                ["AutoOpenLightroom"] = "Abrir en Adobe Lightroom automáticamente",
                ["CheckUpdatesStartup"] = "Buscar actualizaciones al inicio",
                ["CheckUpdatesNow"] = "Buscar actualizaciones ahora",
                ["License"] = "Licencia",
                ["BuyPro"] = "⭐ Comprar Pro",
                ["Activate"] = "Activar",
                ["Login"] = "Iniciar sesión",
                ["LicenseKey"] = "Clave de licencia",

                // Common Dialogs & Messages
                ["Warning"] = "Advertencia",
                ["Notice"] = "Información",
                ["Close"] = "Cerrar",
                ["GotIt"] = "Entendido",
                ["NoFilesSelected"] = "No hay archivos para procesar.",
                ["NoSupportedPhotos"] = "No se encontraron fotos compatibles",
                ["NoSupportedPhotosMsg"] = "No se encontraron fotos RAW o DNG compatibles en la selección."
            };
            _translations["es"] = es;

            // German (Deutsch)
            var de = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // App Header & Navigation
                ["AppTitle"] = "Fujifier",
                ["AppSubtitle"] = "Fujifilm Filmsimulationen für jedes RAW-Foto",
                ["Settings"] = "Einstellungen",
                ["About"] = "Über",
                ["Website"] = "Webseite",
                ["GitHub"] = "GitHub",
                ["Releases"] = "Versionen",
                ["Pro"] = "PRO",
                ["FreeTier"] = "Kostenlose Version",
                ["SelectSimulationProfile"] = "Fujifilm-Simulationskamera auswählen",

                // View Modes & Actions
                ["TableView"] = "Tabellenansicht",
                ["GridView"] = "Rasteransicht",
                ["AddFiles"] = "Dateien hinzufügen",
                ["AddFolder"] = "Ordner hinzufügen",
                ["ClearQueue"] = "Warteschlange leeren",
                ["Process"] = "Konvertieren",
                ["ProcessAll"] = "Alle konvertieren",
                ["Processing"] = "Wird verarbeitet...",
                ["Cancel"] = "Abbrechen",
                ["Stop"] = "Anhalten",

                // Dropzone & Empty State
                ["DropPhotosHere"] = "RAW- oder DNG-Fotos hier ablegen",
                ["DropzoneSubtitle"] = "Unterstützt ARW, CR2, CR3, NEF, RAF, RW2, DNG, PEF, ORF und mehr",
                ["ChooseFiles"] = "Dateien auswählen",
                ["ChooseFolder"] = "Ordner auswählen",

                // Photo Table Headers & Columns
                ["HeaderPhoto"] = "Foto",
                ["HeaderFileName"] = "Dateiname",
                ["HeaderCameraModel"] = "Kameramodell",
                ["HeaderSize"] = "Größe",
                ["HeaderStatus"] = "Status",
                ["HeaderActions"] = "Aktionen",
                ["Remove"] = "Entfernen",
                ["Pending"] = "Ausstehend",
                ["Success"] = "Abgeschlossen",
                ["Error"] = "Fehler",

                // Quick Settings & Side Panel
                ["QuickSettings"] = "Schnelleinstellungen",
                ["FilmSimulation"] = "Filmsimulation",
                ["OutputFolder"] = "Ausgabeordner",
                ["OutputFolderPlaceholder"] = "Im Originalordner speichern (Standard)",
                ["Browse"] = "Durchsuchen",
                ["Clear"] = "Löschen",
                ["BackupOriginal"] = "Originaldateien sichern",
                ["LosslessCompression"] = "Verlustfreie Komprimierung",
                ["EmbedRaw"] = "Original-RAW in DNG einbetten",

                // Inspector Sidebar
                ["InspectorTitle"] = "Inspektor",
                ["TabSettings"] = "Einstellungen",
                ["TabPhotoInfo"] = "Foto-Info",
                ["NoPhotoSelected"] = "Kein Foto ausgewählt",
                ["SelectPhotoHint"] = "Wählen Sie ein Foto aus, um Details anzuzeigen",
                ["PropertyFileName"] = "Dateiname",
                ["PropertyPath"] = "Dateipfad",
                ["PropertySize"] = "Dateigröße",
                ["PropertyDimensions"] = "Abmessungen",
                ["PropertyCamera"] = "Kamera",
                ["PropertyLens"] = "Objektiv",
                ["PropertyFocalLength"] = "Brennweite",
                ["PropertyAperture"] = "Blende",
                ["PropertyShutterSpeed"] = "Belichtungszeit",
                ["PropertyIso"] = "ISO",
                ["PropertyDate"] = "Aufnahmedatum",

                // Batch Summary Modal
                ["BatchCompleteTitle"] = "Stapelkonvertierung abgeschlossen",
                ["AllPhotosConverted"] = "✔ Alle Fotos erfolgreich konvertiert",
                ["CompletedWithWarnings"] = "⚠ Mit Warnungen abgeschlossen",
                ["PhotosConvertedCount"] = "{0} von {1} Fotos konvertiert",
                ["PhotosConvertedWithFailures"] = "{0} von {1} Fotos konvertiert ({2} fehlgeschlagen)",
                ["Duration"] = "Dauer",
                ["Throughput"] = "Geschwindigkeit",
                ["VolumeConverted"] = "Konvertiertes Volumen",
                ["Simulation"] = "Simulation",
                ["BatchNoticeSuccess"] = "Fujifilm-Simulationen und XMP-Sidecar-Metadaten zusammengeführt.",
                ["BatchNoticeWarnings"] = "Einige Dateien konnten nicht konvertiert werden. Details in Liste.",
                ["OpenOutputFolder"] = "📁 Ausgabeordner öffnen",
                ["Done"] = "Fertig",

                // Preferences / Settings Categories
                ["Appearance"] = "Erscheinungsbild",
                ["Language"] = "Sprache",
                ["SelectLanguage"] = "Anwendungssprache",
                ["LanguageHint"] = "Wählen Sie die Anzeigesprache für die Benutzeroberfläche.",
                ["Theme"] = "Thema",
                ["AppearanceMode"] = "Design-Modus",
                ["ThemeAuto"] = "Automatisch",
                ["ThemeDark"] = "Dunkel",
                ["ThemeAmoled"] = "AMOLED",
                ["ThemeLight"] = "Hell",
                ["ThemeDescription"] = "Automatisch passt sich dem System an. Dunkel bietet elegante Schiefer-Farbtöne. AMOLED liefert tiefes Schwarz.",
                ["Performance"] = "Leistung",
                ["ConcurrentConversions"] = "Gleichzeitige Konvertierungen",
                ["ImportInstantly"] = "Bilder sofort importieren",
                ["ImportInstantlyHint"] = "Überspringt Vorschaubilder, um Fotos sofort in die Warteschlange zu laden",
                ["FileHandling"] = "Dateiverwaltung",
                ["AddSuffix"] = "Suffix an Dateinamen anhängen",
                ["AddSuffixHint"] = "Fügt benutzerdefinierten Text vor der .dng-Erweiterung ein",
                ["SuffixLabel"] = "Suffix:",
                ["SaveInSubfolder"] = "Fotos in Unterordner speichern",
                ["SaveInSubfolderHint"] = "Erstellt einen Unterordner für konvertierte DNG-Dateien",
                ["SubfolderName"] = "Unterordnername:",
                ["DesktopIntegration"] = "Desktop-Integration",
                ["ContextMenuWindows"] = "Fujifier zum Windows-Kontextmenü hinzufügen",
                ["AutoOpenFolder"] = "Ausgabeordner automatisch öffnen",
                ["AutoOpenLightroom"] = "Automatisch in Adobe Lightroom öffnen",
                ["CheckUpdatesStartup"] = "Beim Start nach Updates suchen",
                ["CheckUpdatesNow"] = "Jetzt nach Updates suchen",
                ["License"] = "Lizenz",
                ["BuyPro"] = "⭐ Pro kaufen",
                ["Activate"] = "Aktivieren",
                ["Login"] = "Anmelden",
                ["LicenseKey"] = "Lizenzschlüssel",

                // Common Dialogs & Messages
                ["Warning"] = "Warnung",
                ["Notice"] = "Hinweis",
                ["Close"] = "Schließen",
                ["GotIt"] = "Verstanden",
                ["NoFilesSelected"] = "Keine Dateien zur Verarbeitung.",
                ["NoSupportedPhotos"] = "Keine unterstützten Fotos gefunden",
                ["NoSupportedPhotosMsg"] = "In der Auswahl wurden keine unterstützten RAW- oder DNG-Fotos gefunden."
            };
            _translations["de"] = de;
        }
    }
}

