#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader.Services
{
    public sealed class OptimizerTweakDto
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Tab { get; init; } = "cacheNetwork";
        public bool RequiresAdmin { get; init; }
        public bool Enabled { get; init; }
        public string State { get; init; } = "Default";
        public string WhatItDoes { get; init; } = string.Empty;
        public string Pros { get; init; } = string.Empty;
        public string Cons { get; init; } = string.Empty;
    }

    public sealed class OptimizerProfileDto
    {
        public bool EnforceOnStartup { get; set; } = true;
        public bool PreventThumbCacheCleanup { get; set; } = true;
        public bool ClearDiskCleanupFlags { get; set; }
        public bool DisableSilentCleanupTask { get; set; }
        public bool DisableNetworkThumbsDb { get; set; }
        public bool ForceThumbnailPreviews { get; set; }
        public bool DisableFolderTypeDiscovery { get; set; }
        public bool ExpandBagMruSize { get; set; }
        public bool ExpandIconCache { get; set; }
        public int IconCacheSizeKb { get; set; } = 8192;
    }

    public sealed class OptimizerShellToolHelpDto
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string WhatItDoes { get; init; } = string.Empty;
        public string Pros { get; init; } = string.Empty;
        public string Cons { get; init; } = string.Empty;
    }

    public sealed class OptimizerStateDto
    {
        public bool IsElevated { get; init; }
        public bool NeedsElevationForProfile { get; init; }
        public bool ContextMenuRegistered { get; init; }
        public bool ContextMenuSystemWide { get; init; }
        public int CurrentIconCacheSizeKb { get; init; }
        public int ConfiguredIconCacheSizeKb { get; init; }
        public bool EnforceOnStartup { get; init; }
        public OptimizerProfileDto Profile { get; init; } = new();
        public IReadOnlyList<OptimizerTweakDto> Tweaks { get; init; } = Array.Empty<OptimizerTweakDto>();
        public IReadOnlyList<OptimizerShellToolHelpDto> ShellTools { get; init; } = Array.Empty<OptimizerShellToolHelpDto>();
    }

    public sealed class OptimizerActionResult
    {
        public bool Ok { get; init; }
        public bool RequiresElevationForFullCoverage { get; init; }
        public bool UacCancelled { get; init; }
        public string Message { get; init; } = string.Empty;
        public OptimizerStateDto? State { get; init; }
    }

    [SupportedOSPlatform("windows")]
    public sealed class OptimizerService
    {
        public OptimizerStateDto GetState(WindowsOptimizer.OptimizationProfile? profileOverride = null)
        {
            WindowsOptimizer.OptimizationProfile profile = profileOverride ?? WindowsOptimizer.OptimizationProfile.LoadFromSettings();
            WindowsOptimizer.WindowsOptimizationStatusReport report = WindowsOptimizer.InspectSystemState();

            var tweaks = new List<OptimizerTweakDto>
            {
                new()
                {
                    Id = "preventThumbCacheCleanup",
                    Title = "Disable Automatic Cache Cleanup (Autorun = 0)",
                    Tab = "cacheNetwork",
                    RequiresAdmin = true,
                    Enabled = profile.PreventThumbCacheCleanup,
                    State = report.PreventThumbCacheCleanup.ToString(),
                    WhatItDoes = "Sets 'Autorun = 0' (DWORD) across HKCU, HKLM, and WOW6432Node under SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VolumeCaches\\Thumbnail Cache.",
                    Pros = "Most important safeguard against unexpected thumbnail cache resets. Stops Windows 10/11 Automatic Maintenance from silently wiping thumbcache_*.db files when idle.",
                    Cons = "Your thumbcache_*.db files will continue to grow on disk as more folders are preloaded (managed automatically by the 1.6 GB bucket spillover and backup engine)."
                },
                new()
                {
                    Id = "clearDiskCleanupFlags",
                    Title = "Clear Disk Cleanup Thumbnail Flags (StateFlags)",
                    Tab = "cacheNetwork",
                    RequiresAdmin = true,
                    Enabled = profile.ClearDiskCleanupFlags,
                    State = report.ClearDiskCleanupFlags.ToString(),
                    WhatItDoes = "Zeroes out all 'StateFlags0000' through 'StateFlags9999' values under the VolumeCaches\\Thumbnail Cache registry key.",
                    Pros = "Ensures saved or automated Disk Cleanup profiles (cleanmgr.exe /sagerun) never include the thumbnail cache for deletion.",
                    Cons = "If you manually open Windows Disk Cleanup later and intentionally want to delete thumbnails there, you will need to re-check 'Thumbnails'."
                },
                new()
                {
                    Id = "disableSilentCleanupTask",
                    Title = "Disable 'SilentCleanup' Scheduled Task (Admin)",
                    Tab = "cacheNetwork",
                    RequiresAdmin = true,
                    Enabled = profile.DisableSilentCleanupTask,
                    State = report.DisableSilentCleanupTask.ToString(),
                    WhatItDoes = "Disables the '\\Microsoft\\Windows\\DiskCleanup\\SilentCleanup' scheduled task in Windows Task Scheduler (requires Administrator).",
                    Pros = "Completely blocks Windows from running background Disk Cleanup (cleanmgr.exe) during idle maintenance windows.",
                    Cons = "Also stops automatic cleanup of %TEMP% and old Windows Update logs. For most users, keeping 'Autorun = 0' enabled already protects thumbnails without disabling this task."
                },
                new()
                {
                    Id = "disableNetworkThumbsDb",
                    Title = "Disable Thumbs.db on Network Folders (SMB/NAS)",
                    Tab = "cacheNetwork",
                    RequiresAdmin = false,
                    Enabled = profile.DisableNetworkThumbsDb,
                    State = report.DisableNetworkThumbsDb.ToString(),
                    WhatItDoes = "Sets 'DisableThumbsDBOnNetworkFolders = 1' (DWORD) in HKCU Explorer Policies and Explorer\\Advanced.",
                    Pros = "Eliminates 'file is open in Windows Explorer' lock errors when renaming or deleting SMB/NAS folders, and stores network thumbnails in your fast local SSD cache.",
                    Cons = "Other computers on the network will not share a pre-generated Thumbs.db inside the remote folder (each PC maintains its own local cache)."
                },
                new()
                {
                    Id = "forceThumbnailPreviews",
                    Title = "Force Thumbnail Previews & Local Cache Active",
                    Tab = "cacheNetwork",
                    RequiresAdmin = false,
                    Enabled = profile.ForceThumbnailPreviews,
                    State = report.ForceThumbnailPreviews.ToString(),
                    WhatItDoes = "Sets 'IconsOnly = 0' in Explorer\\Advanced and 'DisableThumbnailCache = 0' / 'NoThumbnailCache = 0' in Explorer Policies.",
                    Pros = "Ensures Windows Explorer always renders real image and video thumbnails (instead of static file icons) and keeps local thumbnail caching enabled.",
                    Cons = "On very old machines with slow mechanical hard drives, rendering thumbnails uses slightly more CPU and disk I/O than static icons."
                },
                new()
                {
                    Id = "disableFolderTypeDiscovery",
                    Title = "Disable Automatic Folder Type Discovery",
                    Tab = "explorer",
                    RequiresAdmin = false,
                    Enabled = profile.DisableFolderTypeDiscovery,
                    State = report.DisableFolderTypeDiscovery.ToString(),
                    WhatItDoes = "Sets 'FolderType = NotSpecified' under HKCU\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\Bags\\AllFolders\\Shell.",
                    Pros = "Stops Explorer from sniffing every file to guess Pictures/Videos/Music/General Items. Makes folders with thousands of media files open instantaneously.",
                    Cons = "Newly opened folders default to the 'General Items' column template (you can still freely change icon sizes and view modes in any folder)."
                },
                new()
                {
                    Id = "expandBagMruSize",
                    Title = "Expand Explorer Folder View Memory (BagMRU Size)",
                    Tab = "explorer",
                    RequiresAdmin = false,
                    Enabled = profile.ExpandBagMruSize,
                    State = report.ExpandBagMruSize.ToString(),
                    WhatItDoes = "Increases 'BagMRU Size' from 5,000 (Windows default) to 20,000 folders under the Shell and ShellNoRoam registry keys.",
                    Pros = "Prevents Explorer from forgetting your custom thumbnail sizes (Large / Extra Large Icons) and sort order after browsing more than 5,000 folders.",
                    Cons = "Uses a few extra megabytes in your user registry hive (UsrClass.dat) as thousands of folder view preferences are stored."
                },
                new()
                {
                    Id = "expandIconCache",
                    Title = "Expand Windows System Icon Cache",
                    Tab = "explorer",
                    RequiresAdmin = true,
                    Enabled = profile.ExpandIconCache,
                    State = report.ExpandIconCache.ToString(),
                    WhatItDoes = "Sets 'Max Cached Icons' under HKLM and HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer, expanding the icon cache from ~500 KB (4 MB, 8 MB, or 16 MB).",
                    Pros = "Prevents desktop, taskbar, and Explorer icons from turning blank white or flickering and redrawing slowly.",
                    Cons = "Uses 4 MB to 16 MB of disk/RAM for the icon cache and requires restarting Windows Explorer (or signing out) to take full effect."
                },
                new()
                {
                    Id = "enforceOnStartup",
                    Title = "Re-apply on App Startup",
                    Tab = "shell",
                    RequiresAdmin = false,
                    Enabled = profile.EnforceOnStartup,
                    State = profile.EnforceOnStartup ? "Optimized" : "Default",
                    WhatItDoes = "Automatically re-applies your saved Windows optimizations every time WinThumbsPreloader starts.",
                    Pros = "Prevents major Windows Feature Updates from silently resetting thumbnail cache protection back to defaults (Autorun = 3) with zero manual effort.",
                    Cons = "If the application starts without Administrator privileges, only user-scoped (HKCU) settings are re-applied at startup."
                }
            };

            var shellTools = new List<OptimizerShellToolHelpDto>
            {
                new()
                {
                    Id = "contextMenu",
                    Title = "Explorer Context Menu",
                    WhatItDoes = "Registers or removes the cascading 'WinThumbsPreloader' right-click context menu for Folders, Folder Backgrounds, and Drives.",
                    Pros = "Works even in Portable mode without Administrator privileges (via HKCU\\Software\\Classes). Lets you preload any folder with one right-click.",
                    Cons = "If you move the portable executable to a different folder later, click 'Register Context Menu' again to update the path."
                },
                new()
                {
                    Id = "rebuildIconCache",
                    Title = "Rebuild Icon Cache Only",
                    WhatItDoes = "Deletes only 'iconcache_*.db' and 'IconCache.db' files and runs 'ie4uinit.exe -show' to refresh Windows system icons.",
                    Pros = "Fixes corrupted or blank application/shortcut icons WITHOUT touching any 'thumbcache_*.db' files (your preloaded thumbnails remain 100% intact).",
                    Cons = "Any iconcache_*.db files currently locked by explorer.exe will only be fully recreated after restarting Windows Explorer."
                },
                new()
                {
                    Id = "restartExplorer",
                    Title = "Restart Windows Explorer",
                    WhatItDoes = "Cleanly terminates 'explorer.exe' (exit code 1) and immediately relaunches it.",
                    Pros = "Applies ShellBag, Folder Type Discovery, and Icon Cache changes immediately without requiring a system reboot or sign-out.",
                    Cons = "Closes currently open File Explorer windows and interrupts any active file copy/move operations being performed by Explorer."
                },
                new()
                {
                    Id = "restoreDefaults",
                    Title = "Restore Windows Defaults",
                    WhatItDoes = "Removes all optimization keys and policies managed by this module and restores standard Windows 10/11 defaults (Autorun = 3, default BagMRU, SilentCleanup enabled, etc.).",
                    Pros = "Guarantees 100% reversibility if you ever want to return Windows to its stock out-of-the-box behavior.",
                    Cons = "Once Windows defaults are restored, Windows Automatic Maintenance will resume periodically purging the thumbnail cache during idle periods."
                }
            };

            return new OptimizerStateDto
            {
                IsElevated = report.IsProcessElevated,
                NeedsElevationForProfile = !report.IsProcessElevated && report.NeedsElevationForProfile(profile),
                ContextMenuRegistered = report.ExplorerContextMenuRegistered,
                ContextMenuSystemWide = report.ExplorerContextMenuSystemWide,
                CurrentIconCacheSizeKb = report.CurrentIconCacheSizeKb,
                ConfiguredIconCacheSizeKb = profile.IconCacheSizeKb,
                EnforceOnStartup = profile.EnforceOnStartup,
                Profile = ToDto(profile),
                Tweaks = tweaks,
                ShellTools = shellTools
            };
        }

        public OptimizerProfileDto GetRecommendedProfile()
        {
            return ToDto(WindowsOptimizer.OptimizationProfile.CreateRecommended());
        }

        public OptimizerActionResult Apply(OptimizerProfileDto profileDto, bool elevateViaUac)
        {
            ArgumentNullException.ThrowIfNull(profileDto);
            var profile = FromDto(profileDto);
            profile.SaveToSettings();

            if (elevateViaUac && !WindowsOptimizer.IsCurrentProcessElevated())
            {
                WriteLine("Elevating via UAC helper to apply system-wide Windows optimizations.", LoggingFrequency.GUILogging);
                WindowsOptimizer.ApplyOptimizations(profile, broadcastShellChange: false);
                bool elevatedSuccess = WindowsOptimizer.RunElevatedHelper(profile.BuildElevatedCliArguments());

                return new OptimizerActionResult
                {
                    Ok = elevatedSuccess,
                    UacCancelled = !elevatedSuccess,
                    RequiresElevationForFullCoverage = !elevatedSuccess,
                    Message = elevatedSuccess
                        ? "System-wide (HKLM & Scheduled Task) optimizations were applied with Administrator privileges."
                        : "Administrator elevation was cancelled or failed. User-scope (HKCU) optimizations were still applied.",
                    State = GetState(profile)
                };
            }

            var result = WindowsOptimizer.ApplyOptimizations(profile, broadcastShellChange: true);
            return new OptimizerActionResult
            {
                Ok = true,
                RequiresElevationForFullCoverage = result.RequiresElevationForFullCoverage,
                Message = result.RequiresElevationForFullCoverage && !WindowsOptimizer.IsCurrentProcessElevated()
                    ? "User-level (HKCU) optimizations applied. Some system-wide settings require Administrator elevation."
                    : "All selected Windows optimizations have been applied successfully.",
                State = GetState(profile)
            };
        }

        public OptimizerActionResult RestoreDefaults()
        {
            WriteLine("Restoring Windows defaults via OptimizerService.", LoggingFrequency.GUILogging);
            var defaultProfile = new WindowsOptimizer.OptimizationProfile
            {
                EnforceOnStartup = false,
                PreventThumbCacheCleanup = false,
                ClearDiskCleanupFlags = false,
                DisableSilentCleanupTask = false,
                DisableNetworkThumbsDb = false,
                ForceThumbnailPreviews = false,
                DisableFolderTypeDiscovery = false,
                ExpandBagMruSize = false,
                ExpandIconCache = false,
                IconCacheSizeKb = 8192
            };
            defaultProfile.SaveToSettings();

            WindowsOptimizer.RestoreWindowsDefaults(broadcastShellChange: true);
            bool uacCancelled = false;
            if (!WindowsOptimizer.IsCurrentProcessElevated())
            {
                if (!WindowsOptimizer.RunElevatedHelper("-restore-defaults"))
                    uacCancelled = true;
            }

            return new OptimizerActionResult
            {
                Ok = !uacCancelled,
                UacCancelled = uacCancelled,
                Message = uacCancelled
                    ? "User-scope defaults restored, but Administrator elevation was cancelled for system-wide defaults."
                    : "Windows default settings have been restored.",
                State = GetState(defaultProfile)
            };
        }

        public OptimizerActionResult RegisterContextMenu()
        {
            bool ok = WindowsOptimizer.RegisterExplorerContextMenu();
            return new OptimizerActionResult
            {
                Ok = ok,
                Message = ok
                    ? "WinThumbsPreloader right-click context menu has been registered."
                    : "Failed to register Explorer context menu.",
                State = GetState()
            };
        }

        public OptimizerActionResult UnregisterContextMenu()
        {
            bool ok = WindowsOptimizer.UnregisterExplorerContextMenu();
            return new OptimizerActionResult
            {
                Ok = ok,
                Message = "WinThumbsPreloader right-click context menu has been removed.",
                State = GetState()
            };
        }

        public OptimizerActionResult RebuildIconCacheOnly()
        {
            int deleted = WindowsOptimizer.ClearIconCacheFilesOnly(invokeIe4uinit: true);
            return new OptimizerActionResult
            {
                Ok = true,
                Message = $"Windows Icon Cache rebuild triggered ({deleted} unlocked iconcache_*.db file(s) removed). Preloaded thumbcache_*.db files kept intact.",
                State = GetState()
            };
        }

        public OptimizerActionResult RestartExplorer()
        {
            bool ok = WindowsOptimizer.RestartExplorerCleanly();
            return new OptimizerActionResult
            {
                Ok = ok,
                Message = ok ? "Windows Explorer restarted." : "Failed to restart Windows Explorer.",
                State = GetState()
            };
        }

        private static OptimizerProfileDto ToDto(WindowsOptimizer.OptimizationProfile profile) => new()
        {
            EnforceOnStartup = profile.EnforceOnStartup,
            PreventThumbCacheCleanup = profile.PreventThumbCacheCleanup,
            ClearDiskCleanupFlags = profile.ClearDiskCleanupFlags,
            DisableSilentCleanupTask = profile.DisableSilentCleanupTask,
            DisableNetworkThumbsDb = profile.DisableNetworkThumbsDb,
            ForceThumbnailPreviews = profile.ForceThumbnailPreviews,
            DisableFolderTypeDiscovery = profile.DisableFolderTypeDiscovery,
            ExpandBagMruSize = profile.ExpandBagMruSize,
            ExpandIconCache = profile.ExpandIconCache,
            IconCacheSizeKb = profile.IconCacheSizeKb
        };

        private static WindowsOptimizer.OptimizationProfile FromDto(OptimizerProfileDto dto) => new()
        {
            EnforceOnStartup = dto.EnforceOnStartup,
            PreventThumbCacheCleanup = dto.PreventThumbCacheCleanup,
            ClearDiskCleanupFlags = dto.ClearDiskCleanupFlags,
            DisableSilentCleanupTask = dto.DisableSilentCleanupTask,
            DisableNetworkThumbsDb = dto.DisableNetworkThumbsDb,
            ForceThumbnailPreviews = dto.ForceThumbnailPreviews,
            DisableFolderTypeDiscovery = dto.DisableFolderTypeDiscovery,
            ExpandBagMruSize = dto.ExpandBagMruSize,
            ExpandIconCache = dto.ExpandIconCache,
            IconCacheSizeKb = Math.Clamp(dto.IconCacheSizeKb <= 0 ? 8192 : dto.IconCacheSizeKb, 1024, 65536)
        };
    }
}
