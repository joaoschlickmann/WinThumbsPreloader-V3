# Changelog

All notable changes to **WinThumbsPreloader** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.2.1] - 2026-11-10

### Security
- **Elevated Installer EoP & Reparse-Point Hardening (`installer.iss`)**: Eliminated elevated execution of `unins000.exe` from user-writable or `HKCU`-derived directories and removed recursive `DelTree` on `HKCU` registry paths, replacing legacy `%LocalAppData%\Programs\WinThumbsPreloader` cleanup with Win32 `GetFileAttributesW` `FILE_ATTRIBUTE_REPARSE_POINT` verification, strict regular-file allowlisting, and non-recursive `RemoveDir`, and aligned the `_Shutdown` kernel event identifier with `SingleInstanceCoordinator.DefaultScopeId`.
- **Single-Instance IPC & Foreground Least Privilege (`SingleInstanceCoordinator`)**: Scoped user-SID kernel `Mutex` and `EventWaitHandle` DACLs from `FullControl` down to `Synchronize | Modify`, switched external signaling to `OpenEventW(EVENT_MODIFY_STATE)`, restricted `WTP_SINGLE_INSTANCE_SCOPE` overrides to prefixed ASCII identifiers (`<= 80` chars), and scoped `AllowSetForegroundWindow` strictly to peer `WinThumbsPreloader` process IDs instead of `ASFW_ANY`.
- **Centralized Reparse-Point Guard, Cycle-Safe Scanner & Bounded Memory (`PathSecurity`, `DirectoryScanner`, `ThumbnailsPreloader`, `CacheForm`, `WindowsOptimizer`, `AutoUpdater`, `Logger`)**: Added `PathSecurity` to block reads/writes/deletions through NTFS junctions and symlinks across cache backup/restore, icon cache cleanup, post-update markers, and log rotation; added canonical `GetFinalPathNameByHandleW` cycle detection (`MaxDirectoryDepth = 128`) in sequential/parallel `DirectoryScanner` with a bounded 65,536-item `Channel<ScannedItem>` backpressure queue; enforced a 256 MB pre-allocation header/size cap in `CacheForm.SanitizeThumbCacheDirectory`; added random GUID suffixes to `_staging_*` / `_restore_*` directories; and made `WindowsOptimizer.RestartExplorerCleanly` fail closed if `%SystemRoot%\explorer.exe` is missing.
- **Safe XML Migration, Explicit UAC Profile Passing & Supply-Chain Gate (`Settings`, `WindowsOptimizer`, `Options`, `ProgressDialog`, `build-and-release.yml`)**: Hardened `Settings.ApplyUserConfigXmlFile` with `XmlReaderSettings` (`DtdProcessing.Prohibit`, `XmlResolver = null`, 1 MB limit, and reparse-point rejection), passed explicit `-profile-mask` / `-icon-cache-kb` CLI parameters across the UAC helper boundary, scoped `ProgressDialog` `FindWindowEx` lookup to the current PID with `UnmanagedType.Bool` COM marshaling, disabled `<RegisterForComInterop>`, enabled `<NuGetAudit>true</NuGetAudit>`, and enforced fail-closed `dotnet restore --locked-mode`, `DOTNET_CLI_UI_LANGUAGE=en`, and `ISCC.exe` Authenticode verification in CI.

### Fixed
- **Inno Setup 6.7+ Identifier Compatibility & Test Process Teardown (`installer.iss`, `SingleInstanceAndInstallerTests`, `run-e2e-tests.ps1`)**: Prefixed custom Pascal Script file-attribute constants (`WTP_*`) to avoid colliding with built-in `FILE_ATTRIBUTE_DIRECTORY` in Inno Setup 6.7.3+, switched GUI E2E child termination to `Kill(entireProcessTree: true)`, and added workspace-scoped test-process teardown so background test processes never hold locks (`MSB3027`) on `WinThumbsPreloader.dll`.

---

## [3.2.0] - 2026-11-05

### Added
- **Windows Optimizer Module (`WindowsOptimizer`, `WindowsOptimizerForm`)**: Added a dedicated Windows OS & Explorer optimization window accessible from `SettingsForm` (**Optimize OS**), `CacheForm` (**Optimize OS**), and the system tray context menu (**Windows Optimizer...**), featuring:
  - **Thumbnail Cache Protection**: Disables Windows Automatic Maintenance cache purge (`Autorun = 0` across `HKCU`, `HKLM`, and `WOW6432Node` under `VolumeCaches\Thumbnail Cache`), clears Disk Cleanup `StateFlagsXXXX` entries, and supports toggling the `\Microsoft\Windows\DiskCleanup\SilentCleanup` scheduled task.
  - **SMB / NAS & Thumbnail Policies**: Disables `Thumbs.db` creation on network shares (`DisableThumbsDBOnNetworkFolders = 1`) and enforces active thumbnail rendering and local caching (`IconsOnly = 0`, `DisableThumbnailCache = 0`, `NoThumbnailCache = 0`).
  - **Windows Explorer Performance**: Disables Automatic Folder Type Discovery (`FolderType = NotSpecified` under `Bags\AllFolders\Shell`) for instant loading of large media directories, expands folder view cache capacity (`BagMRU Size = 20000`), and expands the Windows Icon Cache (`Max Cached Icons` = `4096` / `8192` / `16384` KB).
  - **Shell Integration & Safe Maintenance**: Registers or removes the cascading Explorer right-click context menu in both Portable mode (`HKCU\Software\Classes`) and Administrator mode (`HKLM\Software\Classes`), rebuilds the Windows Icon Cache (`iconcache_*.db` + `ie4uinit.exe -show`) while keeping `thumbcache_*.db` untouched, restarts Windows Explorer cleanly (`TerminateProcess` exit code `1`), and restores standard Windows defaults.
  - **Smart Hybrid UAC Elevation**: Applies user-scoped (`HKCU`) tweaks immediately without requiring elevation and provides an on-demand **Elevate & Apply (UAC)** helper (`-apply-optimizations` / `-restore-defaults`) for system-wide (`HKLM` / Scheduled Task) settings.

### Fixed
- **Single-Instance GUI Enforcement & System Tray Deduplication (`SingleInstanceCoordinator`, `Program`, `CacheForm`, `Options`)**: Added session-local single-instance coordination (`Mutex` + `EventWaitHandle` IPC) so launching `WinThumbsPreloader.exe` from the Start Menu or Explorer while already running (including when minimized to the system tray) restores and focuses the existing window instead of spawning duplicate processes, ensured only one `NotifyIcon` is ever active per process, and routed all exits through `Program.ExitApplication` / `-shutdown` so tray icons are cleanly removed without leaving ghost icons.
- **Single Canonical Installation Replacement (`installer.iss`)**: Enforced a single canonical installation path (`{commonpf}\WinThumbsPreloader`) with `DisableDirPage=yes` and `UsePreviousAppDir=no`, and added `PrepareToInstall` cleanup that signals `-shutdown`, terminates lingering instances, uninstalls/removes duplicate installations from alternate directories (`%LocalAppData%\Programs\WinThumbsPreloader`, 32-bit Program Files, or prior `Uninstall` locations), removes duplicate per-user Start Menu/Desktop shortcuts, and cleans stale `HKCU\Software\Classes` context menu overrides.

---

## [3.1.3] - 2026-11-03

### Fixed
- **False Cache Reset Alerts & Icon Cache Exclusion (`CacheForm`, `AboutForm`)**: Scoped cache size calculation, backup, restore, and cleanup strictly to `thumbcache_*.db` files and automatically purged legacy `iconcache_*.db` files from backup folders so routine Windows Icon Cache resets (`IconCacheToDelete`) no longer trigger false thumbnail cache reset alerts or restore unsanitized icon cache files.
- **Repeated Reset Alert & Auto-Restore Loop (`AboutForm`, `CacheForm`)**: Fixed the inverted `!recognizedLogged` early-return condition in `AboutForm.CheckForCacheReset` that caused `UpdateTimer_Tick` to re-evaluate cache resets every 250 ms after the first check, synchronized `RestoreThumbsCache` with `CacheBackupLock`, and guarded `ResetRecognized` state transitions while a restore or alert dialog is active.
- **Clean Explorer Termination During Force Restore (`CacheForm`)**: Switched `CloseExplorerAsync` from `Process.Kill()` (exit code `-1`) to Win32 `TerminateProcess` with exit code `1` so `winlogon.exe` does not treat intentional Explorer closure as an unexpected shell crash (Event ID 1002) and prematurely respawn `explorer.exe` mid-restore.

---

## [3.1.2] - 2026-11-03

### Fixed
- **Automatic Settings Migration Across Updates (`Settings`)**: Added `LastUpgradedVersion` tracking and automatic `EnsureSettingsUpgraded()` migration inside the `Settings` constructor (before static `Logger` initialization), invoking `Upgrade()` and overlaying prior `<Version>\user.config` settings in ascending version order so custom backup paths, intervals, and preferences are never reset when installing a new release.
- **Silent Update Auto-Relaunch & Windows Startup Sync (`AutoUpdater`, `installer.iss`, `CacheForm`)**: Removed `skipifsilent` from the Inno Setup `postinstall` `[Run]` entry so silent in-app updates reliably relaunch `WinThumbsPreloader.exe` as the interactive user even when UAC elevation strips custom command-line switches, persisted a short-lived `.post_update_launch` marker to preserve minimized tray state across UAC restarts, wired the system tray **Start with Windows** menu item, and ensured Windows `StartupApproved\Run` state is re-enabled when startup is active.
- **Minimize to System Tray on Window Close (`AboutForm`, `CacheForm`, `Program`)**: Closing the main window (`AboutForm` or top-level `CacheForm`) via the **Close** button or title-bar **X** now keeps the application running in the system tray with background thumbnail cache auto-backup/auto-restore timers active instead of terminating the process (holding `Shift` switches **Close** to **Exit** for immediate termination), and added a **Main Window...** entry to the tray context menu.

---

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
