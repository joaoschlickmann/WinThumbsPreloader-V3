# Changelog

All notable changes to **WinThumbsPreloader** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.1.1] - 2026-11-03

### Security
- **Fail-Closed Updater Verification (`AutoUpdater`)**: Enforced mandatory SHA-256 checksum verification (`requireSha256: true` with `CryptographicOperations.FixedTimeEquals`) before downloading and before launching the installer, restricted initial asset URLs strictly to `github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/download/` while allowing GitHub CDN hosts only after HTTP redirect, and enforced TLS 1.2/1.3 with post-redirect API URI validation.
- **System Binary Path Qualification & Argument Hardening**: Qualified Windows system executables (`cleanmgr.exe`, `explorer.exe`, `taskschd.msc`) with absolute `System32` / `Windows` paths to prevent binary planting, stripped embedded quotes and blocked leading `-` flags in scheduled task path arguments, and quoted `%V` in `Drive\shell` context menu registry entries.
- **Defensive I/O Bounds, Log Sanitization & Rollback Isolation**: Added 1 MB file size limits, `FileAttributes.ReparsePoint` checks, and `JsonSerializerOptions { MaxDepth = 16 }` to preset loading and drag-and-drop file reads, sanitized CR/LF characters in `Logger.WriteLine` against log injection, and moved temporary cache restore rollback folders from shared `%TEMP%` to user-scoped `%LocalAppData%\WinThumbsPreloader` with random GUID suffixes.
- **CI/CD & Supply-Chain Lockfiles**: Pinned all GitHub Actions in `.github/workflows/build-and-release.yml` to full 40-character commit SHAs with `persist-credentials: false`, committed authoritative NuGet `packages.lock.json` files with `dotnet restore --locked-mode`, and expanded `.gitignore` secret exclusions.

### Fixed
- **Task Scheduler Thread Count Parsing (`ScheduleForm`)**: Replaced raw `int.Parse` with `int.TryParse` and `Math.Clamp` when reading `-m:<threads>` from existing scheduled tasks to prevent `ArgumentOutOfRangeException` on out-of-bounds values.

---

## [3.1.0] - 2026-11-03

### Added
- **Automatic In-App & System Tray Updater (`AutoUpdater`)**: Checks GitHub Releases via `System.Text.Json`, resolves the `-setup.exe` installer asset, downloads atomically to a `.part` file with live progress and MZ/size verification, and launches the Inno Setup installer with `/SILENT /CLOSEAPPLICATIONS` and automatic relaunch (`/AUTOLAUNCH` or `/AUTOLAUNCHMINIMIZED`).
- **System Tray Update Menu & Notifications**: Added a **Check for Updates...** item to the Cache Manager tray context menu and silent startup update checks with balloon tip notifications when launched with `-startminimized`.
- **Automatic Thumbcache Bucket Spillover**: When a primary Windows `thumbcache_*.db` bucket reaches the 1.6 GB soft ceiling guard (`IsBucketSaturated`), thumbnail extraction automatically spills over into the next larger cache bucket (`256` -> `768` -> `1280` -> `1920` -> `2560`) instead of dropping thumbnails or risking Explorer cache resets.
- **Thumbcache Retention & Ceiling Guards**: Added `WTS_SLOWRECLAIM` retention flag during extraction, a per-DB 1.6 GB soft ceiling guard (`MaxSafeThumbcacheDbFileBytes`), and startup `SilentCleanup` scheduled-task protection to prevent Windows Disk Cleanup from wiping the thumbnail cache.
- **CLI Parallelism Shorthand (`-p:<n>`)**: Added `-p:<n>` as a concise CLI alias for `-maxinstances:<n>` to cap concurrent folder preload instances.
- **Automated CI/CD Build & Release Pipeline**: Added GitHub Actions workflow (`.github/workflows/build-and-release.yml`) to run the xUnit test suite, build the `win-x64` Release binary on .NET 10, compile the Inno Setup installer with SHA-256 checksums, and publish GitHub Releases on `v*` tags.

### Changed
- **Start with Windows Enabled by Default**: Enabled `StartWithWindows` by default across application settings, startup registry synchronization, and the Inno Setup installer so thumbnail cache protection is active out of the box.
- **Expanded Documentation & Attribution**: Expanded `README.md` with full v3 architecture diagrams, CLI reference, and v1/v2/v3 lineage credits; synchronized copyright headers across `LICENSE`, `AssemblyInfo.cs`, `LicenseForm`, and localized resources (`EN`/`RU`).

### Fixed
- **Thumbcache Backup & Restore Sanitization**: Sanitized `thumbcache_*.db` IMMM headers and `thumbcache_idx.db` index headers during backup and restore, and switched to `FileShare.ReadWrite | FileShare.Delete` streaming to prevent corruption or lock failures when Windows Explorer holds open handles.
- **Network Drive & UI Stability**: Fixed STA thread crashes on unavailable network drives, throttled high-frequency progress bar UI updates, resolved timer `NullReferenceException` edge cases on form disposal, and excluded heavy raw/video formats from default preload extensions unless opted in.

### Performance
- **Directory Scanner & STA Shell Item Caching**: Optimized recursive directory enumeration, cached parent `IShellItem` COM folders per STA worker thread, and streamlined cache reset checks.

---

## [3.0.0] - 2026-10-29

### Added
- **.NET 10 Windows Desktop Migration**: Upgraded the target framework and build pipeline to `.NET 10.0` (`net10.0-windows7.0`, `win-x64`).
- **Streaming Producer-Consumer Preloader Pipeline**: Replaced upfront full-directory memory buffering with an asynchronous `Channel<string>` streaming pipeline feeding a dedicated pool of STA COM worker threads (`IShellItemImageFactory`).
- **UNC & Mapped Network Drive Resolver (`PathResolver`)**: Added resilient path normalization and mapped-drive-to-UNC fallback (`WNetGetUniversalName`) for NAS and SMB shares.
- **Redesigned Bulk Preloader UI**: Replaced the legacy bulk preloader dialog with a responsive multi-path editor supporting comments (`#`), environment variable expansion, live path validation, and bounded concurrency (`-maxinstances:<n>`).
- **Automated xUnit Test Suite**: Added unit, concurrency regression, and real STA COM integration tests (`WinThumbsPreloader.Tests`).

### Fixed
- **24-Hour Scheduler & Resource Leaks**: Fixed 24-hour interval truncation in scheduled tasks and eliminated GDI `HBITMAP`, COM `IShellItem`, timer, and process handle leaks during long preloading runs.
- **Cleanup of Tracked Build Artifacts**: Removed tracked `.vs/` and build output artifacts from version control and hardened `.gitignore`.
