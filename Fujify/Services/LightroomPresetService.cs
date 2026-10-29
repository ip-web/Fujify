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
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Fujify.Services
{
    public class LightroomPresetService : ILightroomPresetService
    {
        private readonly ILicenseService _licenseService;
        public const string PresetGroupName = "Fujifilm Film Simulations (Fujifier)";

        public static ILightroomPresetService? Instance { get; set; }

        public LightroomPresetService(ILicenseService? licenseService = null)
        {
            _licenseService = licenseService ?? LicenseService.Instance ?? new LicenseService();
        }

        public IReadOnlyList<LightroomPresetInfo> Presets { get; } = new List<LightroomPresetInfo>
        {
            new(
                Id: "provia",
                DisplayName: "Fujifilm PROVIA - Standard",
                CameraProfile: "Camera PROVIA/Standard",
                FileName: "Fujifilm PROVIA - Standard.xmp",
                Description: "Authentic Fujifilm Provia/Standard simulation with neutral natural colors.",
                Uuid: "F0010001000040008000000000000001"
            ),
            new(
                Id: "velvia",
                DisplayName: "Fujifilm Velvia - Vivid",
                CameraProfile: "Camera Velvia/Vivid",
                FileName: "Fujifilm Velvia - Vivid.xmp",
                Description: "Rich saturation, punchy contrast, and deep blues and greens for landscapes.",
                Uuid: "F0010001000040008000000000000002"
            ),
            new(
                Id: "astia",
                DisplayName: "Fujifilm ASTIA - Soft",
                CameraProfile: "Camera ASTIA/Soft",
                FileName: "Fujifilm ASTIA - Soft.xmp",
                Description: "Soft tones with vivid colors, ideal for portraits with flattering skin tones.",
                Uuid: "F0010001000040008000000000000003"
            ),
            new(
                Id: "classic_chrome",
                DisplayName: "Fujifilm CLASSIC CHROME",
                CameraProfile: "Camera CLASSIC CHROME",
                FileName: "Fujifilm CLASSIC CHROME.xmp",
                Description: "Subdued saturation and deep shadows for a documentary film magazine aesthetic.",
                Uuid: "F0010001000040008000000000000004"
            ),
            new(
                Id: "pro_neg_hi",
                DisplayName: "Fujifilm PRO Neg. Hi",
                CameraProfile: "Camera PRO Neg. Hi",
                FileName: "Fujifilm PRO Neg. Hi.xmp",
                Description: "Professional portrait negative film with slightly harder contrast.",
                Uuid: "F0010001000040008000000000000005"
            ),
            new(
                Id: "pro_neg_std",
                DisplayName: "Fujifilm PRO Neg. Std",
                CameraProfile: "Camera PRO Neg. Std",
                FileName: "Fujifilm PRO Neg. Std.xmp",
                Description: "Professional portrait negative film with soft contrast and delicate skin tones.",
                Uuid: "F0010001000040008000000000000006"
            ),
            new(
                Id: "classic_neg",
                DisplayName: "Fujifilm Classic Neg.",
                CameraProfile: "Camera PROVIA/Standard",
                FileName: "Fujifilm Classic Neg..xmp",
                Description: "Nostalgic color negative film look (Superia) with hard contrast and rich chromatic shadows.",
                Uuid: "F0010001000040008000000000000007",
                LookName: "Camera CLASSIC Neg",
                LookUuid: "20be890a36db4dbd990dd984231d15db"
            ),
            new(
                Id: "nostalgic_neg",
                DisplayName: "Fujifilm Nostalgic Neg.",
                CameraProfile: "Camera PROVIA/Standard",
                FileName: "Fujifilm Nostalgic Neg..xmp",
                Description: "Warm golden highlights, soft tones, and 1970s New American Color aesthetic.",
                Uuid: "F0010001000040008000000000000008",
                LookName: "Camera NOSTALGIC Neg",
                LookUuid: "eb139fd741f44978b0f05060770dcbe0"
            ),
            new(
                Id: "eterna",
                DisplayName: "Fujifilm ETERNA - Cinema",
                CameraProfile: "Camera ETERNA/Cinema",
                FileName: "Fujifilm ETERNA - Cinema.xmp",
                Description: "Motion picture film simulation with understated saturation and rich shadow detail.",
                Uuid: "F0010001000040008000000000000009"
            ),
            new(
                Id: "eterna_bleach_bypass",
                DisplayName: "Fujifilm ETERNA BLEACH BYPASS",
                CameraProfile: "Camera PROVIA/Standard",
                FileName: "Fujifilm ETERNA BLEACH BYPASS.xmp",
                Description: "High contrast with dramatic desaturation emulating traditional bleach bypass processing.",
                Uuid: "F0010001000040008000000000000010",
                LookName: "Camera BLEACH BYPASS",
                LookUuid: "24c5377485d8445092f11c94abb3938c"
            ),
            new(
                Id: "reala_ace",
                DisplayName: "Fujifilm REALA ACE",
                CameraProfile: "Camera REALA ACE",
                FileName: "Fujifilm REALA ACE.xmp",
                Description: "Faithful color reproduction with hard tonality and gentle highlight roll-off.",
                Uuid: "F0010001000040008000000000000011"
            ),
            new(
                Id: "acros",
                DisplayName: "Fujifilm ACROS",
                CameraProfile: "Camera ACROS",
                FileName: "Fujifilm ACROS.xmp",
                Description: "Legendary rich monochrome with smooth gradation and deep blacks.",
                Uuid: "F0010001000040008000000000000012",
                IsMonochrome: true
            ),
            new(
                Id: "acros_ye",
                DisplayName: "Fujifilm ACROS + Ye",
                CameraProfile: "Camera ACROS+Ye",
                FileName: "Fujifilm ACROS + Ye.xmp",
                Description: "ACROS black & white with yellow filter, slightly boosting contrast and darkening skies.",
                Uuid: "F0010001000040008000000000000013",
                IsMonochrome: true
            ),
            new(
                Id: "acros_r",
                DisplayName: "Fujifilm ACROS + R",
                CameraProfile: "Camera ACROS+R",
                FileName: "Fujifilm ACROS + R.xmp",
                Description: "ACROS black & white with red filter, delivering dramatic sky darkening and high contrast.",
                Uuid: "F0010001000040008000000000000014",
                IsMonochrome: true
            ),
            new(
                Id: "acros_g",
                DisplayName: "Fujifilm ACROS + G",
                CameraProfile: "Camera ACROS+G",
                FileName: "Fujifilm ACROS + G.xmp",
                Description: "ACROS black & white with green filter, enhancing portrait skin tones and lips.",
                Uuid: "F0010001000040008000000000000015",
                IsMonochrome: true
            ),
            new(
                Id: "monochrome",
                DisplayName: "Fujifilm MONOCHROME",
                CameraProfile: "Camera MONOCHROME",
                FileName: "Fujifilm MONOCHROME.xmp",
                Description: "Standard monochrome black and white simulation.",
                Uuid: "F0010001000040008000000000000016",
                IsMonochrome: true
            ),
            new(
                Id: "monochrome_ye",
                DisplayName: "Fujifilm MONOCHROME + Ye",
                CameraProfile: "Camera MONOCHROME+Ye",
                FileName: "Fujifilm MONOCHROME + Ye.xmp",
                Description: "Monochrome simulation with yellow filter.",
                Uuid: "F0010001000040008000000000000017",
                IsMonochrome: true
            ),
            new(
                Id: "monochrome_r",
                DisplayName: "Fujifilm MONOCHROME + R",
                CameraProfile: "Camera MONOCHROME+R",
                FileName: "Fujifilm MONOCHROME + R.xmp",
                Description: "Monochrome simulation with red filter.",
                Uuid: "F0010001000040008000000000000018",
                IsMonochrome: true
            ),
            new(
                Id: "monochrome_g",
                DisplayName: "Fujifilm MONOCHROME + G",
                CameraProfile: "Camera MONOCHROME+G",
                FileName: "Fujifilm MONOCHROME + G.xmp",
                Description: "Monochrome simulation with green filter.",
                Uuid: "F0010001000040008000000000000019",
                IsMonochrome: true
            ),
            new(
                Id: "sepia",
                DisplayName: "Fujifilm SEPIA",
                CameraProfile: "Camera PROVIA/Standard",
                FileName: "Fujifilm SEPIA.xmp",
                Description: "Warm antique monochrome look.",
                Uuid: "F0010001000040008000000000000020",
                LookName: "Camera Sepia",
                LookUuid: "0031528dc4d74079ad965ab497659642",
                IsMonochrome: true
            )
        };

        public string GeneratePresetXml(LightroomPresetInfo preset)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" x:xmptk=\"Adobe XMP Core 7.0-c000 1.000000, 0000/00/00-00:00:00        \">");
            sb.AppendLine(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
            sb.AppendLine("  <rdf:Description rdf:about=\"\"");
            sb.AppendLine("    xmlns:crs=\"http://ns.adobe.com/camera-raw-settings/1.0/\"");
            sb.AppendLine("   crs:PresetType=\"Normal\"");
            sb.AppendLine("   crs:Cluster=\"Fujifier\"");
            sb.AppendLine($"   crs:UUID=\"{preset.Uuid}\"");
            sb.AppendLine("   crs:SupportsAmount2=\"False\"");
            sb.AppendLine("   crs:SupportsAmount=\"False\"");
            sb.AppendLine("   crs:SupportsColor=\"True\"");
            sb.AppendLine("   crs:SupportsMonochrome=\"True\"");
            sb.AppendLine("   crs:SupportsHighDynamicRange=\"True\"");
            sb.AppendLine("   crs:SupportsNormalDynamicRange=\"True\"");
            sb.AppendLine("   crs:SupportsSceneReferred=\"True\"");
            sb.AppendLine("   crs:SupportsOutputReferred=\"True\"");
            sb.AppendLine("   crs:CameraModelRestriction=\"\"");
            sb.AppendLine("   crs:Copyright=\"Fujifier\"");
            sb.AppendLine("   crs:ContactInfo=\"https://fujifier.cc\"");
            sb.AppendLine("   crs:Version=\"16.0\"");
            sb.AppendLine("   crs:ProcessVersion=\"15.4\"");
            sb.AppendLine($"   crs:CameraProfile=\"{preset.CameraProfile}\"");
            sb.AppendLine("   crs:HasSettings=\"True\">");
            sb.AppendLine("   <crs:Name>");
            sb.AppendLine("    <rdf:Alt>");
            sb.AppendLine($"     <rdf:li xml:lang=\"x-default\">{preset.DisplayName}</rdf:li>");
            sb.AppendLine("    </rdf:Alt>");
            sb.AppendLine("   </crs:Name>");
            sb.AppendLine("   <crs:ShortName>");
            sb.AppendLine("    <rdf:Alt>");
            sb.AppendLine("     <rdf:li xml:lang=\"x-default\"/>");
            sb.AppendLine("    </rdf:Alt>");
            sb.AppendLine("   </crs:ShortName>");
            sb.AppendLine("   <crs:SortName>");
            sb.AppendLine("    <rdf:Alt>");
            sb.AppendLine("     <rdf:li xml:lang=\"x-default\"/>");
            sb.AppendLine("    </rdf:Alt>");
            sb.AppendLine("   </crs:SortName>");
            sb.AppendLine("   <crs:Group>");
            sb.AppendLine("    <rdf:Alt>");
            sb.AppendLine($"     <rdf:li xml:lang=\"x-default\">{PresetGroupName}</rdf:li>");
            sb.AppendLine("    </rdf:Alt>");
            sb.AppendLine("   </crs:Group>");
            sb.AppendLine("   <crs:Description>");
            sb.AppendLine("    <rdf:Alt>");
            sb.AppendLine($"     <rdf:li xml:lang=\"x-default\">Applies {preset.DisplayName} profile. Note: Photos must be converted with Fujifier beforehand.</rdf:li>");
            sb.AppendLine("    </rdf:Alt>");
            sb.AppendLine("   </crs:Description>");

            if (!string.IsNullOrEmpty(preset.LookName) && !string.IsNullOrEmpty(preset.LookUuid))
            {
                sb.AppendLine("   <crs:Look>");
                sb.AppendLine("    <rdf:Description");
                sb.AppendLine($"     crs:Name=\"{preset.LookName}\"");
                sb.AppendLine("     crs:Amount=\"1\"");
                sb.AppendLine($"     crs:UUID=\"{preset.LookUuid}\"");
                sb.AppendLine("     crs:SupportsAmount=\"False\"");
                sb.AppendLine("     crs:Stubbed=\"true\">");
                sb.AppendLine("     <crs:Group>");
                sb.AppendLine("      <rdf:Alt>");
                sb.AppendLine("       <rdf:li xml:lang=\"x-default\">Profiles</rdf:li>");
                sb.AppendLine("      </rdf:Alt>");
                sb.AppendLine("     </crs:Group>");
                sb.AppendLine("    </rdf:Description>");
                sb.AppendLine("   </crs:Look>");
            }
            else
            {
                sb.AppendLine("   <crs:Look/>");
            }

            sb.AppendLine("  </rdf:Description>");
            sb.AppendLine(" </rdf:RDF>");
            sb.AppendLine("</x:xmpmeta>");
            return sb.ToString();
        }

        public string? DetectLightroomSettingsDirectory()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string candidate = Path.Combine(appData, "Adobe", "CameraRaw", "Settings");
                    return candidate;
                }
                else if (OperatingSystem.IsMacOS())
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string candidate = Path.Combine(home, "Library", "Application Support", "Adobe", "CameraRaw", "Settings");
                    return candidate;
                }
                else if (OperatingSystem.IsLinux())
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string wineCandidate = Path.Combine(home, ".wine", "drive_c", "users", Environment.UserName, "AppData", "Roaming", "Adobe", "CameraRaw", "Settings");
                    if (Directory.Exists(wineCandidate))
                    {
                        return wineCandidate;
                    }
                    string nativeCandidate = Path.Combine(home, ".adobe", "CameraRaw", "Settings");
                    return nativeCandidate;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LightroomPresetService] Error detecting Lightroom dir: {ex.Message}");
            }

            return null;
        }

        public bool IsLightroomDirectoryDetected
        {
            get
            {
                string? dir = DetectLightroomSettingsDirectory();
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir);
            }
        }

        public PresetInstallResult InstallPresetsToLightroom()
        {
            if (!_licenseService.IsPro)
            {
                return new PresetInstallResult(
                    Success: false,
                    Count: 0,
                    TargetPath: null,
                    ErrorMessage: "Automatic Lightroom preset installation is exclusive to Fujifier Pro.",
                    RequiresPro: true
                );
            }

            string? baseDir = DetectLightroomSettingsDirectory();
            if (string.IsNullOrEmpty(baseDir))
            {
                return new PresetInstallResult(
                    Success: false,
                    Count: 0,
                    TargetPath: null,
                    ErrorMessage: "Adobe Camera Raw / Lightroom settings directory could not be located on this system. You can use 'Export Presets (.zip)' to save the preset pack manually.",
                    RequiresPro: false
                );
            }

            try
            {
                string targetDir = Path.Combine(baseDir, "Fujifier Film Simulations");
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                int count = 0;
                foreach (var preset in Presets)
                {
                    string xml = GeneratePresetXml(preset);
                    string path = Path.Combine(targetDir, preset.FileName);
                    File.WriteAllText(path, xml, Encoding.UTF8);
                    count++;
                }

                return new PresetInstallResult(
                    Success: true,
                    Count: count,
                    TargetPath: targetDir,
                    ErrorMessage: null,
                    RequiresPro: false
                );
            }
            catch (Exception ex)
            {
                return new PresetInstallResult(
                    Success: false,
                    Count: 0,
                    TargetPath: null,
                    ErrorMessage: $"Failed to install Lightroom presets: {ex.Message}",
                    RequiresPro: false
                );
            }
        }

        public PresetInstallResult ExportPresetsToDirectory(string targetDirectory)
        {
            if (!_licenseService.IsPro)
            {
                return new PresetInstallResult(
                    Success: false,
                    Count: 0,
                    TargetPath: null,
                    ErrorMessage: "Exporting Lightroom presets is exclusive to Fujifier Pro.",
                    RequiresPro: true
                );
            }

            try
            {
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                int count = 0;
                foreach (var preset in Presets)
                {
                    string xml = GeneratePresetXml(preset);
                    string path = Path.Combine(targetDirectory, preset.FileName);
                    File.WriteAllText(path, xml, Encoding.UTF8);
                    count++;
                }

                return new PresetInstallResult(true, count, targetDirectory, null);
            }
            catch (Exception ex)
            {
                return new PresetInstallResult(false, 0, null, $"Failed to export presets: {ex.Message}");
            }
        }

        public byte[] GeneratePresetsZip()
        {
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var preset in Presets)
                {
                    var entry = archive.CreateEntry($"Fujifilm Film Simulations (Fujifier)/{preset.FileName}", CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                    writer.Write(GeneratePresetXml(preset));
                }

                var readmeEntry = archive.CreateEntry("Fujifilm Film Simulations (Fujifier)/README.txt", CompressionLevel.Optimal);
                using var readmeStream = readmeEntry.Open();
                using var readmeWriter = new StreamWriter(readmeStream, Encoding.UTF8);
                readmeWriter.WriteLine("Fujifilm Film Simulation Develop Presets by Fujifier");
                readmeWriter.WriteLine("===================================================");
                readmeWriter.WriteLine();
                readmeWriter.WriteLine("IMPORTANT REQUIREMENT:");
                readmeWriter.WriteLine("Photos MUST be converted to DNG using Fujifier beforehand.");
                readmeWriter.WriteLine("Fujifilm camera profiles only apply to Fujified photos or genuine Fujifilm cameras.");
                readmeWriter.WriteLine("These presets switch exclusively the camera profile without altering your exposure, white balance, or any other develop sliders.");
                readmeWriter.WriteLine();
                readmeWriter.WriteLine("HOW TO INSTALL:");
                readmeWriter.WriteLine("1. Desktop (Lightroom Classic / Adobe Camera Raw):");
                readmeWriter.WriteLine("   - Open Lightroom, go to File > Import Develop Profiles and Presets (or click '+' in the Presets panel).");
                readmeWriter.WriteLine("   - Select this .zip file or the individual .xmp files.");
                readmeWriter.WriteLine("2. Mobile (Lightroom Mobile on iOS / Android):");
                readmeWriter.WriteLine("   - Open Lightroom Mobile, open an image, tap 'Presets' > '...' (three dots) > 'Import Presets'.");
                readmeWriter.WriteLine("   - Select this .zip file.");
                readmeWriter.WriteLine();
                readmeWriter.WriteLine("Website: https://fujifier.cc");
            }
            return ms.ToArray();
        }
    }
}

