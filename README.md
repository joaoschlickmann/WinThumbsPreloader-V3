# WinThumbsPreloader v3

**High-Performance Thumbnail Preloader & Thumbnail Cache Manager for Windows 10/11**

![Demo](https://raw.githubusercontent.com/Mfarooq360/WinThumbsPreloader/master/demo.gif)

**WinThumbsPreloader v3** is a high-throughput Windows utility that pre-generates file thumbnails into the native Windows Explorer thumbnail cache (`thumbcache_*.db`) and protects, backs up, repairs, and restores that cache so your folders open with instant thumbnails—even across massive local libraries, NAS/SMB network shares, and Windows updates.

> **Project Lineage & Credits**
> This project is a fork and architectural evolution of **[WinThumbsPreloader V2](https://github.com/Mfarooq360/WinThumbsPreloader-V2)** by **[Mutahar Farooq](https://github.com/Mfarooq360)**, which itself was forked from the original **[WinThumbsPreloader](https://github.com/Bruhov/WinThumbsPreloader)** created by **[Dmitry Bruhov](https://github.com/Bruhov)**. See [Credits & Acknowledgments](#credits--acknowledgments) below for full details.

---

## Table of Contents

- [Overview](#overview)
- [What's New in v3](#whats-new-in-v3)
- [Core Features](#core-features)
  - [1. Streaming Producer–Consumer Preloading Engine](#1-streaming-producerconsumer-preloading-engine)
  - [2. Thumbnail Cache Protection, Backup, Binary Sanitization & Restore](#2-thumbnail-cache-protection-backup-binary-sanitization--restore)
  - [3. Automatic Bucket Spillover & 2 GB Offset-Limit Guard](#3-automatic-bucket-spillover--2-gb-offset-limit-guard)
  - [4. Network Drive (SMB/UNC) & Heavy-Media Isolation](#4-network-drive-smbunc--heavy-media-isolation)
  - [5. Bulk Preloader & Task Scheduler Automation](#5-bulk-preloader--task-scheduler-automation)
  - [6. Built-In Windows Optimizer Module (`WindowsOptimizerForm`)](#6-built-in-windows-optimizer-module-windowsoptimizerform)
  - [7. Built-In Open-Source Thumbnail Codecs (`CodecsForm`)](#7-built-in-open-source-thumbnail-codecs-codecsform)
- [Installation](#installation)
- [Usage Guide](#usage-guide)
  - [Windows Explorer Context Menu](#windows-explorer-context-menu)
  - [Graphical Interface (GUI)](#graphical-interface-gui)
  - [Command-Line Interface (CLI)](#command-line-interface-cli)
- [Recommended Windows Shell Extensions](#recommended-windows-shell-extensions)
- [Building from Source & Running Tests](#building-from-source--running-tests)
- [Credits & Acknowledgments](#credits--acknowledgments)
- [License](#license)

---

## Overview

Windows Explorer generates thumbnails lazily when you open a folder and stores them in `%LocalAppData%\Microsoft\Windows\Explorer\thumbcache_*.db`. However, Windows suffers from several well-known pain points:
1. Opening large folders (or network shares) with thousands of high-resolution images, videos, PSDs, or RAW photos forces you to wait as thumbnails render one by one.
2. Windows Automatic Maintenance (`SilentCleanup`) or cache header/index desynchronization (`0x80041221` / `CACHE_E_CORRUPTED`) can unexpectedly wipe all `thumbcache_*.db` files back to empty 24-byte headers.
3. Individual `thumbcache_*.db` stream files use 32-bit offsets internally; when a single `.db` file approaches ~2 GB, Windows resets the cache.

**WinThumbsPreloader v3** solves all three problems by combining a streaming multi-threaded STA COM preloader with a binary-aware cache backup, sanitizer, and retention engine.

---

## What's New in v3

Building upon the foundation of **v1** (core preloading and Explorer context menus by **Dmitry Bruhov**) and **v2** (Cache Manager, Auto-Backup/Restore, Task Scheduler, Settings UI, and initial multi-threading by **Mutahar Farooq**), **v3** introduces major architectural upgrades:

- **Streaming Producer–Consumer Pipeline**: Replaces full-list in-memory directory materialization with a bounded `Channel<ScannedItem>` (`65,536` capacity) fed by a low-allocation `FileSystemEnumerable` scanner, starting thumbnail extraction immediately while scanning continues in the background.
- **Dedicated STA COM Worker Pool & PIDL Cache**: Reuses thread-local `IThumbnailCache` instances (`CLSID_LocalThumbnailCache`) and caches serialized parent folder `ITEMIDLIST` (PIDL) structures across threads (`SHCreateItemFromIDList` + `SHCreateItemFromRelativeName`), eliminating Shell parsing bottlenecks and third-party shell extension race conditions (`0xC0000005`).
- **Two-Lane Heavy-Media Isolation**: Automatically separates fast image formats (`.jpg`, `.png`, `.webp`, `.bmp`, etc.) from heavy/out-of-process extractors (`.mp4`, `.mkv`, `.psd`, `.pdf`, RAW camera formats, archives) so slow video/RAW codecs never block image thumbnail workers.
- **Binary Thumbnail Cache Sanitizer (`SanitizeThumbCacheDirectory`)**: Understands the internal Windows 10/11 `CMMM` (`thumbcache_*.db`) and `IMMM` (`thumbcache_idx.db`) binary structures and Windows' reflected CRC64 (`0x92C64265D32139A4`). Automatically repairs stale `FirstFreeEntryOffset` (`+0x14`) headers, reconciles torn index pointers from live backups, and updates stream sizes/entry counts so `thumbcache.dll` never triggers a `0x80041221` full-cache reset.
- **Shared-ReadWrite Live Backup & Restore**: Uses `FileShare.ReadWrite | FileShare.Delete` streams and orders `thumbcache_*.db` writes prior to `thumbcache_idx.db`, enabling reliable backups and restores even when `explorer.exe`, `dllhost.exe`, or third-party File Open/Save dialogs hold open handles to cache files.
- **Smart Bucket Spillover & `WTS_SLOWRECLAIM` Retention**: Requests `WTS_SLOWRECLAIM` to populate `thumbcache_sr.db` alongside standard streams and automatically spills over to the next unsaturated Windows cache bucket (`256 → 768 → 1280 → 1920 → 2560`) when a single `.db` file reaches the 1.6 GB soft ceiling.
- **Elevated Mapped-Drive & UNC Resolver**: Resolves mapped network drive letters (`Z:\`, etc.) across UAC elevation boundaries via `WNetGetUniversalNameW` and `HKCU\Network` so scheduled or elevated tasks work seamlessly on NAS/SMB shares.
- **Unified WebView2 UI ("Polished Obsidian / Liquid Light")**: Provides a single-window React + WebView2 desktop shell (`MainShellForm`) with Light (*Liquid Light*), Dark (*Polished Obsidian*), and System themes, accessible modal dialogs and controls, native drag-and-drop folder queueing, and automatic classic WinForms fallback if the WebView2 Runtime is unavailable.
- **Automated Test Suite**: Includes a 133-test xUnit suite (`WinThumbsPreloader.Tests`) plus 7 Rust native COM/decoder tests (`WinThumbsCodecs`) covering STA COM concurrency, binary `CMMM`/`IMMM` sanitization, cross-process backup locks, bucket spillover, all 10 built-in codec decoders, security hardening, and visual E2E GUI flows.

---

## Core Features

### 1. Streaming Producer–Consumer Preloading Engine
- **Instant Start**: Begins generating thumbnails on the first batch of discovered files without waiting for millions of nested files to finish enumerating.
- **Multi-Resolution Extraction**: Preloads thumbnails at configurable resolutions (`16`, `32`, `48`, `96`, `256`, `768`, `1280`, `1920`, `2560`), defaulting to Explorer's primary `96px` (Medium Icons) and `256px` (Large/Extra-Large Icons) streams.
- **Cache-First Fast Path**: Queries `IThumbnailCache` with `WTS_INCACHEONLY` first; already-cached thumbnails are verified in microseconds without invoking image decoders.

### 2. Thumbnail Cache Protection, Backup, Binary Sanitization & Restore
- **Automatic SilentCleanup Protection**: Sets `Autorun = 0` under `HKCU` and `HKLM` `SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache` at install time and application startup so Windows Automatic Maintenance never purges your cache.
- **Live Backup with Binary Sanitization**: Copies Explorer's `.db` files to a staging directory using shared read/write access, validates every `CMMM` block chain and `IMMM` index slot in `thumbcache_idx.db`, fixes any torn pointers or stale free-space offsets, and atomically swaps the backup into place.
- **All-or-Nothing Restore Rollback**: Prevents partial `thumbcache_*.db` restores that would desynchronize `thumbcache_idx.db` from stream files; automatically rolls back on error and terminates stale `dllhost.exe` COM surrogates holding `thumbcache.dll` during Force Restore.
- **Cross-Process Backup Lock**: Prevents concurrent manual, timer-based, or multi-instance backups from colliding on the same backup directory.

### 3. Automatic Bucket Spillover & 2 GB Offset-Limit Guard
- Monitors individual `thumbcache_*.db` file sizes in real time.
- When `thumbcache_256.db` (or any target bucket) reaches the **1.6 GB soft ceiling**, new extractions automatically spill over to the next larger unsaturated Windows bucket (`768`, `1280`, `1920`, `2560`), which Windows Explorer transparently downscales in RAM to serve `256px` and `96px` views without hitting the 2 GB file limit.

### 4. Network Drive (SMB/UNC) & Heavy-Media Isolation
- Full support for local drives, mapped network drives (`X:\`), and direct UNC paths (`\\NAS\Media`).
- Configurable **Extension Filter** (`SupportedExtensions`) with built-in defaults for common image, video, design, document, and RAW formats, plus user-customizable extension lists.

### 5. Bulk Preloader & Task Scheduler Automation
- **Bulk Preloader**: Maintain a persistent list of directories/drives (`One path per line`), drag-and-drop folders directly into the list, and run batch jobs with a configurable maximum number of parallel instances.
- **Task Scheduler**: Create background preloading tasks Directly in Windows Task Scheduler with custom intervals (Days, Hours, or Minutes) and CLI arguments.
- **System Tray & Startup**: Starts minimized to the system tray with Windows by default to monitor cache health and run auto-backup/auto-restore rules silently.

### 6. Built-In Windows Optimizer Module (`WindowsOptimizerForm`)
- **Thumbnail Cache Anti-Purge**: Disables Windows Automatic Maintenance thumbnail cache cleanup (`Autorun = 0` under `HKCU`, `HKLM`, and `WOW6432Node` `VolumeCaches\Thumbnail Cache`), zeroes out Disk Cleanup `StateFlags0000`–`StateFlags9999` values, and optionally disables the `\Microsoft\Windows\DiskCleanup\SilentCleanup` scheduled task.
- **SMB / NAS Network & Explorer Thumbnail Policies**: Disables slow `Thumbs.db` creation on network shares (`DisableThumbsDBOnNetworkFolders = 1`) so SMB/NAS folders use your fast local SSD `thumbcache_*.db`, and forces thumbnail previews (`IconsOnly = 0`, `DisableThumbnailCache = 0`).
- **Explorer ShellBag & Icon Cache Tuning**: Disables Automatic Folder Type Discovery (`FolderType = NotSpecified`) for instant opening of massive media folders, expands folder view memory to `20,000` folders (`BagMRU Size = 20000`), and expands the Windows Icon Cache (`Max Cached Icons` up to `16384 KB`).
- **Portable Context Menu & Safe Maintenance**: Register or remove the Explorer right-click cascading context menu directly from the UI (even in Portable mode without Admin via `HKCU\Software\Classes`), rebuild the Windows Icon Cache without touching `thumbcache_*.db`, and revert all tweaks to Windows defaults at any time.

### 7. Built-In Open-Source Thumbnail Codecs (`CodecsForm`)
- **Embedded Native COM `IThumbnailProvider` (`WinThumbsCodecs.dll` + `pdfium.dll`)**: Pure out-of-process Windows 10/11 x64 thumbnail providers running inside `dllhost.exe` (without `DisableProcessIsolation`), requiring no external codecs for 10 format families:
  - **Adobe Photoshop**: `.psd`, `.psb` (CMYK ICC inversion + resource `1033` BGR swap)
  - **PDF Documents**: `.pdf` (non-V8/non-XFA `pdfium.dll`)
  - **Adobe Illustrator**: `.ai` (PDF-compatible & legacy PostScript AI)
  - **Encapsulated PostScript**: `.eps` (DOS TIFF/WMF, EPSI preview, PS-to-PDF fallback)
  - **Adobe InDesign**: `.indd`, `.idml` (streaming XMP preview & ZIP package thumbnail)
  - **Adobe XD**: `.xd` (ZIP preview with 16 MiB size cap)
  - **SVG / SVGZ Vector Graphics**: `.svg`, `.svgz` (`resvg` with external URL/XXE loading blocked)
  - **CorelDRAW**: `.cdr`
  - **Affinity Suite**: `.afphoto`, `.afdesign`, `.afpub`, `.af`
  - **Camera RAW**: `.dng`, `.cr2`, `.cr3`, `.crw`, `.nef`, `.nrw`, `.arw`, `.raf`, `.orf`, `.rw2`, `.pef`, `.srw`, `.3fr`, `.fff`, `.mrw`, `.x3f`, `.erf`, `.mef`, `.rwl`, `.iiq`
- **Conflict Detection, Backup & Restore**: Open via the **Codecs** button in the main window (`AboutForm`), **Settings**, or tray menu (**Thumbnail Codecs...**). Existing handlers (such as **Ardfry PSD Codec**, **Adobe Acrobat**, or **Microsoft PowerToys**) are detected live, backed up to JSON before overriding `UserChoice\ProgId` `ShellEx` entries (never modifying `UserChoice`), and restored automatically when unchecked or uninstalled. Supports Standard User (`HKCU`), UAC-elevated system-wide registration (`HKLM` + `HKCU`), optional Preload Extensions sync, and CLI automation (`-reopencodecs`, `-apply-codecs -codecs-mask:0x...`, `-uninstall-codecs`).

---

## Installation

### Option 1: Installer (Recommended)
1. Run `WinThumbsPreloader-3.3.0-setup.exe` (located in `InnoSetup/Output/` when compiled locally, or from the [Releases page](https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases)).
2. The installer registers the Windows Explorer right-click context menus for **Folders**, **Folder Backgrounds**, and **Drives**, configures `SilentCleanup` cache protection, and adds `WinThumbsPreloader` to your system `PATH`.

### Option 2: Standalone Executable
- Run `WinThumbsPreloader.exe` directly from the build output directory. It automatically stores configuration and logs under `%LocalAppData%\WinThumbsPreloader` when installed in `Program Files`, or runs portably when placed in a user-writable folder.

---

## Usage Guide

### Windows Explorer Context Menu
Right-click any **Folder**, **Folder Background**, or **Drive** in Windows Explorer and select **WinThumbsPreloader**:
- **Preload thumbnails**: Preloads thumbnails for all files in the selected directory (`-m "%V"`).
- **Preload thumbnails recursively**: Preloads thumbnails for the selected directory and all nested subdirectories (`-m -r "%V"`).

### Graphical Interface (GUI)
Launching `WinThumbsPreloader.exe` without path arguments opens the unified WebView2 dashboard (**Polished Obsidian** / **Liquid Light**):
- **Unified Single-Window Navigation (No `Shift`/`Ctrl` Modifiers Required)**: Switch directly between **Preload**, **Cache**, **Schedule**, **Codecs**, **Optimize**, and **Settings** in the top navigation bar without hidden `Shift` or `Ctrl` modifier shortcuts.
- **Themes & Accessible Controls**: Choose **Light** (*Liquid Light*), **Dark** (*Polished Obsidian*), or **System** theme in **Settings → General**. Destructive confirmations (**Force Restore (Restart Explorer)**, **Clear Backup**, **Clear Cache**, **Reset Defaults**) and prompts use accessible modal dialogs with keyboard focus management and live-region status announcements.
- **Preload View**: Drag and drop folders or add paths directly, toggle **Recursive** and **Multithreaded** explicitly, run **Sequential** or **Simultaneous** batches, and save/load presets.
- **Cache View**: View live **Explorer Cache Size**, per-bucket sizes, and **Backup Size**; toggle **Auto Backup**, **Auto Restore**, **Cache Reset Alert**, and **Start with Windows**; trigger **Force Restore (Restart Explorer)**, **Clear Backup**, or **Clear Cache** directly from dedicated buttons; and configure backup folder location, update intervals, deletion methods, and safety modes.
- **Automatic Classic WinForms Fallback**: If Microsoft Edge WebView2 Runtime is unavailable (or `wwwroot/` assets are absent), WinThumbsPreloader automatically falls back to the classic WinForms dialogs (`AboutForm`, `CacheForm`, `BulkPreloaderForm`, `ScheduleForm`, `CodecsForm`, `WindowsOptimizerForm`, `SettingsForm`), where holding `Shift` (`Recursively` / `Force Restore` / `Clear Backup` / `Clear Cache` / `Exit`) or `Ctrl` (`Bulk Preload`) still works.

### Command-Line Interface (CLI)

```powershell
WinThumbsPreloader [options] <path1> [path2 ...]
```

| Option | Description |
| :--- | :--- |
| `-r` | **Recursive**: Include all nested subdirectories. |
| `-s` | **Silent Mode**: Run without showing the progress window. |
| `-m` | **Multi-threaded (Auto)**: Enable multi-threaded extraction using the configured/default thread count. |
| `-m:<n>` or `-m<n>` | **Multi-threaded (Custom)**: Enable multi-threaded extraction with `<n>` worker threads (`1`–`256`, e.g., `-m:16` or `-m16`). |
| `-maxinstances:<n>` or `-p:<n>` | Limit the maximum number of paths processed concurrently when multiple paths are passed (`1`–`32`). |
| `-startminimized` | Launch the Cache Manager minimized to the Windows system tray. |
| `-reopensettings` | Focus the running GUI instance on **Settings** (WebView2 shell view, or classic `SettingsForm` in fallback mode). |
| `-reopenoptimizer` | Focus the running GUI instance on **Optimize** (WebView2 shell view, or classic `WindowsOptimizerForm` in fallback mode). |
| `-reopencodecs` | Focus the running GUI instance on **Codecs** (WebView2 shell view, or classic `CodecsForm` in fallback mode). |
| `-apply-codecs -codecs-mask:0x...` | Register/update the selected built-in thumbnail codec families (`0x000`–`0x3FF`). |
| `-uninstall-codecs` | Uninstall all built-in codecs and restore backed-up external handlers. |

**Examples:**
```powershell
# Preload a picture library recursively using 16 threads
WinThumbsPreloader -r -m:16 "D:\Photos"

# Silently preload multiple folders and a NAS share with at most 2 concurrent jobs
WinThumbsPreloader -s -r -m:8 -maxinstances:2 "D:\Photos" "E:\Renders" "\\NAS\Media\Wallpapers"
```

---

## Recommended Windows Shell Extensions

WinThumbsPreloader includes built-in open-source thumbnail codecs (via the **Codecs** button) for **PSD/PSB, PDF, AI, EPS, INDD/IDML, XD, SVG/SVGZ, CDR, Affinity, and Camera RAW**, so external third-party packs are optional. For HEIF/HEIC containers (or if you prefer keeping third-party handlers):

- **Optional Third-Party Alternatives**: Built-in codecs automatically back up and restore handlers such as [Microsoft PowerToys](https://learn.microsoft.com/en-us/windows/powertoys/file-explorer), Adobe Acrobat, or Ardfry PSD Codec when toggling codecs.
- **HEIF / HEIC Images**: [HEIF Image Extensions (Microsoft Store)](https://www.microsoft.com/en-us/p/heif-image-extensions/9pmmsr1cgpwg)
- **Camera RAW & Archives**: Covered by built-in **Camera RAW** codec (or optional [Raw Image Extension (Microsoft Store)](https://apps.microsoft.com/detail/9nctdw2w1bh8) if preferred)

---

## Building from Source & Running Tests

### Prerequisites
- **.NET 10.0 SDK** (`net10.0-windows7.0`)
- **Node.js 22+ / 24+** (with `npm`, to build and test the `WebUI/` React frontend into `wwwroot/`)
- **Rust (`>= 1.95`, `x86_64-pc-windows-msvc`)** with MSVC C++ Build Tools (to compile `WinThumbsCodecs.dll`)
- **Windows 10 / 11 (x64)** (with Microsoft Edge WebView2 Runtime for the modern shell; classic WinForms fallback works if absent)
- **Inno Setup 6** (optional, required only to compile the `.exe` setup installer)

### Build & Test Commands

```powershell
# Step 1: Build embedded WebUI static assets (outputs to WinThumbsPreloader/WinThumbsPreloader/wwwroot/)
cd WebUI && npm ci && npm run build
cd ..
# Optional frontend typecheck, unit/accessibility tests, and audit (run inside WebUI/):
# npm run typecheck && npm test && npm audit --audit-level=moderate

# Step 2: Build & verify native COM thumbnail codecs (x64)
# Fetch/verify PDFium, run Rust codec tests, and build Release WinThumbsCodecs payload into bin/Release/.../Codecs/
.\scripts\build-codecs.ps1 -Configuration Release -RunTests -CopyToOutput

# Restore (locked), build, and run all automated C# unit, integration, security, and visual E2E tests
dotnet test "WinThumbsPreloader\WinThumbsPreloader.sln" --locked-mode

# Step 3: Build Release win-x64 binaries & re-stage Codecs payload
# Stage Codecs payload cleanly alongside Release binaries (requires Rust >= 1.95 on first run)
dotnet build "WinThumbsPreloader\WinThumbsPreloader.csproj" -c Release -r win-x64 --no-self-contained
.\scripts\build-codecs.ps1 -Configuration Release -SkipPdfiumDownload -CopyToOutput

# Compile the Inno Setup installer (outputs to InnoSetup\Output\WinThumbsPreloader-3.3.0-setup.exe)
ISCC.exe "InnoSetup\installer.iss"
```

---

## Credits & Acknowledgments

This project stands on the shoulders of its predecessors:

1. **[Dmitry Bruhov](https://github.com/Bruhov)** — Creator of the original **[WinThumbsPreloader (v1)](https://github.com/Bruhov/WinThumbsPreloader)** (2018–2020), who designed the original concept, Windows Shell thumbnail extraction integration, CLI interface, and Windows Explorer context menu setup.
2. **[Mutahar Farooq](https://github.com/Mfarooq360)** — Creator and maintainer of **[WinThumbsPreloader V2](https://github.com/Mfarooq360/WinThumbsPreloader-V2)** (2025–2026), who expanded the utility into a full-featured suite with the GUI dashboard, Thumbnail Cache Manager (Backup, Restore, Reset Alerts), Task Scheduler integration, Settings management, logging system, and multi-threaded scanning.
3. **[João Schlickmann](https://github.com/joaoschlickmann)** — Creator of **[WinThumbsPreloader V3](https://github.com/joaoschlickmann/WinThumbsPreloader-V3)**, adding the streaming producer–consumer pipeline, STA PIDL caching, two-lane heavy-media isolation, binary `CMMM`/`IMMM` cache sanitization and CRC64 repair, shared-handle backup/restore, bucket spillover, UNC/elevated mapped-drive resolution, automated test suite, Windows Optimizer, and built-in native thumbnail codecs.
4. **Open-Source Codec Dependencies (`WinThumbsCodecs.dll` & `Codecs/`)**:
   - **[`storytold/craft-libs`](https://github.com/storytold/craft-libs)** (`MIT OR Apache-2.0`) — memory-safe Rust decoders (`craft-psd`, `craft-cms`, `craft-eps`, `craft-ps`, `craft-cdr`, `craft-affinity`, `craft-raw`)
   - **[PDFium](https://pdfium.googlesource.com/pdfium/) / [`bblanchon/pdfium-binaries`](https://github.com/bblanchon/pdfium-binaries)** (`chromium/8165`, `BSD-3-Clause` / `Apache-2.0` / `MIT`, third-party notices bundled in `Codecs/PDFIUM-LICENSE.txt`)
   - **[`resvg`](https://github.com/linebender/resvg)** (`MPL-2.0`, statically compiled into `WinThumbsCodecs.dll` with external resource loading disabled) & **[`windows-rs`](https://github.com/microsoft/windows-rs)** (`MIT OR Apache-2.0`)

---

## License

Licensed under the **[MIT License](WinThumbsPreloader/WinThumbsPreloader/LICENSE.txt)**.

- Copyright (c) 2018 **Dmitry Bruhov**
- Copyright (c) 2026 **Mutahar Farooq**
- Copyright (c) 2026 **João Schlickmann**
