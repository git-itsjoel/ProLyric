# ProLyric

<p align="center">
  <strong>Fast, elegant lyric generator for Renewed Vision ProPresenter 7+</strong><br>
  Single & Dual-Language presentations, offline Malayalam-to-Manglish transliteration, SongDB search, and built-in song library.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8.0" />
  <img src="https://img.shields.io/badge/Platform-Windows%20x64-0078D6?logo=windows&logoColor=white" alt="Platform" />
  <img src="https://img.shields.io/badge/UI-WPF%20XAML-00599C" alt="WPF" />
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License" />
</p>

---

## Overview

**ProLyric** is a modern Windows desktop application built with .NET 8 and WPF designed to streamline lyric slide creation for church worship presentations in Renewed Vision ProPresenter 7+.

Whether preparing traditional Malayalam hymns, modern bilingual choruses, or dual-language slides (Malayalam + phonetic Manglish), ProLyric eliminates tedious slide formatting and manual typing by automating file generation directly into native `.pro` presentation files.

### Key Features

* **ProPresenter 7+ Native Generation**: Generates official `.pro` files with internal Protobuf binary structures, fully compatible with ProPresenter 7.0 through latest versions.
* **Single & Dual-Language Modes**:
  * **Single Mode**: Clean, single-text primary slides.
  * **Dual Mode**: Seamless stacked dual-language slides (`Lang1` Primary + `Lang2` Secondary).
* **High-Accuracy Offline Transliteration**:
  * Instant Malayalam-to-Manglish transliteration for entire presentations with one click (`Ctrl + Shift + T` or button).
  * Runs 100% locally and offline—no internet access or cloud API keys required.
* **SongDB (VerseVIEW Integration)**:
  * Connect any SQLite (`.db`, `.sqlite`) or XML hymn database.
  * Lightning-fast search across thousands of hymns by title, Malayalam/English lyrics, or song number.
  * Preview primary and secondary lyrics and import directly with customizable paragraph or line-count splitting.
* **Local Song Library**:
  * Built-in SQLite database to save, search, reopen, and duplicate song projects.
* **Smart Slide Editing**:
  * **Ctrl + Enter**: Split slide at cursor position.
  * **Ctrl + Left Click**: Split slide at clicked text location.
  * **Bulk Paste**: Import unstructured lyrics with automatic stanza splitting (blank lines or fixed line count).
* **Theme-Proof RTF Formatting**:
  * ProLyric injects exact paragraph control words (`\pard`, `\fN`, `\fsN`, `\cfN`, `\qc`) for every line. When you apply a custom ProPresenter Theme in church, all lines uniformly inherit the theme's font size and style without shrinking or font degradation.

---

## ProPresenter Template Setup

ProLyric includes an **internal default template** that automatically formats centered, high-visibility 54pt text. No template configuration is strictly required to get started.

If you wish to design a custom template in ProPresenter to match your church's stage screens or stream overlays:

1. Open **ProPresenter 7+**.
2. Create a new presentation (e.g. `Template - Dual Lang.pro`).
3. In the slide editor, add your text boxes and **rename the layers**:
   * **`Lang1`** (case-insensitive): Layer for Primary lyrics (Malayalam / Main language).
   * **`Lang2`** (case-insensitive): Layer for Secondary lyrics (Manglish / English).
4. Customize fonts, font sizes, colors, text alignment, margins, text shadows, or backgrounds on each layer.
5. Export the presentation as a `.pro` file.

ProLyric clones the geometry, positions, styles, and layer hierarchy of `Lang1` and `Lang2` to generate every slide in the presentation.

---

## Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| <kbd>Ctrl</kbd> + <kbd>Enter</kbd> | Split current slide at cursor position into two slides |
| <kbd>Ctrl</kbd> + <kbd>Click</kbd> | Split slide at mouse click location |
| <kbd>Ctrl</kbd> + <kbd>N</kbd> | New presentation |
| <kbd>Ctrl</kbd> + <kbd>S</kbd> | Save current presentation to local library |
| <kbd>Ctrl</kbd> + <kbd>E</kbd> | Export `.pro` presentation file |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>T</kbd> | Transliterate all Malayalam slides to Manglish |

---

## Getting Started

### Prerequisites

* **Operating System**: Windows 10 (Build 19041+) or Windows 11 (64-bit)
* **Runtime**: [.NET 8.0 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)
* **SDK** *(for building from source)*: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## Build & Development

### 1. Run from Source
```powershell
# Clone the repository
git clone https://github.com/JoelBoban/ProLyric.git
cd ProLyric

# Build and run
dotnet run --project ProLyric
```

### 2. Run Unit Tests
ProLyric includes an extensive test suite covering Protobuf serialization, RTF Unicode multi-line formatting, transliteration phonetic rules, SQLite library operations, and SongDB parser compatibility:

```powershell
dotnet test
```

### 3. Build Windows Installer (`.exe`)
ProLyric uses [Inno Setup 6](https://jrsoftware.org/isinfo.php) to build a streamlined, single-file Windows installer:

```powershell
# Publishes Release win-x64 binaries and compiles installer
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

The compiled installer will be located at:
```text
dist\ProLyric-Setup.exe
```

---

## Project Structure

```text
ProLyric/
├── ProLyric/                 # WPF Application Project
│   ├── Assets/               # Logo and application icons (.ico, .png)
│   ├── Models/               # Core data models (SlideModel, SongProject)
│   ├── Protos/               # ProPresenter 7+ Protocol Buffer definitions
│   ├── Services/             # Business logic
│   │   ├── ProPresenterService.cs     # .pro protobuf builder & template cloner
│   │   ├── RtfUnicodeEncoder.cs       # RTF Unicode & paragraph formatting engine
│   │   ├── TransliterationService.cs  # Malayalam -> Manglish phonetic engine
│   │   ├── VerseViewDbService.cs      # SongDB SQLite/XML database provider
│   │   ├── LibraryDbService.cs        # SQLite song library repository
│   │   └── SettingsService.cs         # App configuration management
│   ├── ViewModels/           # MVVM view models (CommunityToolkit.Mvvm)
│   └── Views/                # XAML windows and dialogs
├── ProLyric.Tests/           # Unit & integration test suite (xUnit)
├── build-installer.ps1       # Automated publish & installer script
├── installer.iss             # Inno Setup 6 compiler configuration
└── README.md
```

---

## Acknowledgments & Credits

ProLyric relies on several open-source projects and linguistic algorithms:

* **[greyshirtguy/ProPresenter7-Proto](https://github.com/greyshirtguy/ProPresenter7-Proto)**: For the reverse-engineered Protocol Buffer schemas defining Renewed Vision ProPresenter 7+ presentations, cues, actions, and slide graphics.
* **[knadh/ml2en](https://github.com/knadh/ml2en)** by Kailash Nadh: For the foundational phonetic vowel/consonant character mapping and transliteration structure from Malayalam to Latin script.
* **[Ajishkbn/aksharam](https://github.com/Ajishkbn/aksharam)** by Ajish: For Malayalam phonetic syllable parsing, chillu letters, conjunct ligatures, and contextual transliteration rules.

---

## License

This project is licensed under the [MIT License](LICENSE).

<p align="center">
  <sub>made with 🤍 by joe!</sub>
</p>
