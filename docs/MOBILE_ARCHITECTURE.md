# Fujify Mobile Architecture & Multiplatform Maintenance Guide

This document describes the architecture, design principles, and maintenance workflows for the **Android** and **iOS** versions of **Fujify**, running alongside Desktop (Windows, macOS, Linux).

---

## 1. High-Level Architecture

Fujify uses a single shared cross-platform codebase built with **Avalonia UI 11** and **.NET 10**:

```
                         ┌────────────────────────┐
                         │   FujifyNET.sln        │
                         └───────────┬────────────┘
                                     │
      ┌──────────────────┬───────────┴───────────┬──────────────────┐
      │                  │                       │                  │
┌─────▼──────────┐ ┌─────▼─────────────┐   ┌─────▼────────────┐ ┌───▼────────────┐
│ Fujify (Core)  │ │ Fujify.Android    │   │ Fujify.iOS       │ │ Fujify.Tests   │
│ - ViewModels   │ │ - MainActivity    │   │ - AppDelegate    │ │ - XUnit Suite  │
│ - Views        │ │ - AndroidManifest │   │ - Info.plist     │ │                │
│ - Services     │ │ - Resources       │   │ - Entitlements   │ │                │
│ - Models       │ └───────────────────┘   └──────────────────┘ └────────────────┘
└────────────────┘
```

### Component Responsibility:
1. **`Fujify` (Shared Core Library & Desktop Executable)**:
   - Multi-targeted to build as an executable on desktop (`WinExe` when no target platform is set) and as a shared library when referenced by `Fujify.Android` or `Fujify.iOS`.
   - Contains all business logic: RAW conversion pipeline, Exif & DNG metadata injection (`DngMetadataEditor`), thumbnail extraction, and adaptive ViewModels.
2. **`Fujify.Android` (`net10.0-android`)**:
   - Android runner project configuring `AvaloniaMainActivity<App>`.
   - Permissions for photo library and external storage (`READ_MEDIA_IMAGES`, `MANAGE_EXTERNAL_STORAGE`).
3. **`Fujify.iOS` (`net10.0-ios`)**:
   - iOS runner project configuring `AvaloniaAppDelegate<App>`.
   - Supports both iPhone (`UIDeviceFamily=1`) and iPad (`UIDeviceFamily=2`).
   - Enables `UIFileSharingEnabled` (iTunes/Finder file sharing) and `LSSupportsOpeningDocumentsInPlace` (Files app integration).

---

## 2. Adaptive UI / UX Design Directives

The UI dynamically transitions between screen form factors:

### Phone Layout (`width < 650dp`):
- **Adheres to Material 3 & Apple HIG**:
  - Touch targets minimum 44–48dp for all buttons and list items.
  - Tonal card backgrounds with 12–16dp rounded corners.
  - **Quick Header**: Brand title, camera target selector pill (`📷 Fujifilm X-T5 ▾`), Preferences, and About buttons.
  - **Action Toolbar**: Large `+ Photos`, `+ Folder`, and `Clear` buttons.
  - **Touch Card List**:
    - High-density 60x60 thumbnail preview.
    - File name and detected camera model with ellipsis trimming.
    - Color-coded status badge (Pending, Processing, Done ✔, Error).
    - Quick-tap remove button (`✕`).
  - **Sticky Bottom Action Bar**:
    - Backup DNG toggle switch.
    - Real-time progress bar.
    - Full-width 48dp high primary action button (`Process Photos` / `Stop`).

### Tablet & Desktop Layout (`width >= 650dp`):
- **2-Column Master-Detail Layout**:
  - **Left Column (Master)**: Complete queue management, file selection, and processing toolbar.
  - **Right Column (Inspector / Detail Panel)**:
    - High-resolution preview of the currently selected photo.
    - Detailed EXIF breakdown: file name, detected camera make and model, file size, status.
    - **Simulation Target Selector**: Interactive camera picker chip.
    - **Unlocked Film Simulations Grid**: Displays authentic color recipes (Provia, Velvia, Astia, Classic Chrome, Classic Neg., Nostalgic Neg., Reala Ace, Eterna, Acros).

---

## 3. Mobile Dialog & In-App Modal System

Mobile platforms (Android and iOS) run under Avalonia's `ISingleViewApplicationLifetime`. Attempting to instantiate desktop `Window` dialogs on mobile throws a runtime exception.

Fujify solves this transparently through `IDialogService` and `MainViewModel`:

1. **Detection**: `DialogService` inspects `Application.Current.ApplicationLifetime`:
   - If `IClassicDesktopStyleApplicationLifetime`, standard modal child windows are shown.
   - If `ISingleViewApplicationLifetime`, dialog calls route to `MainViewModel.Instance`.
2. **In-App Modal Sheets**:
   - `MainView.axaml` renders a sheet with semi-transparent backdrop and drag handle.
   - Handles:
     - **Camera Profile Picker**: Quick selection of Fujifilm camera model.
     - **File Conflict**: Skip, Keep Both (unique name), Overwrite, Apply to All.
     - **Preferences**: Concurrency threads, raw embedding, dark mode.
     - **About**: Version, credits, GPL v3 license.
     - **Alert Messages**: Status notifications.

---

## 4. Cross-Platform File & Photo Picker

File picking on Android and iOS requires native system providers:
- `DialogService.GetStorageProvider(owner)` retrieves the `IStorageProvider` via:
  `TopLevel.GetTopLevel(singleView.MainView)?.StorageProvider`
- On Android, this opens the system **Photo Picker / SAF (Storage Access Framework)**.
- On iOS, this opens the native **Document Picker / Photo Library**.

---

## 5. Build, Test, and CI/CD Workflow

### Local Development:
- **Run Tests**:
  ```powershell
  dotnet test
  ```
- **Build Mobile Artifacts**:
  ```powershell
  .\build_mobile.ps1
  ```

### Automated CI/CD (`.github/workflows/multiplatform.yml`):
Every push and release tag automatically builds:
1. **Windows x64** desktop executable and installer.
2. **Linux x64** binary package.
3. **macOS arm64** binary package.
4. **Android APK** signed package using Ubuntu runner with Android SDK 34 and JDK 17.
5. **iOS App** simulator and release bundle using macOS-14 runner with Xcode.
