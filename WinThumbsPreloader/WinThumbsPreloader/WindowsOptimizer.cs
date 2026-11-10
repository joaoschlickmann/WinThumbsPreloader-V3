#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;
using WinThumbsPreloader.Properties;

namespace WinThumbsPreloader
{
    /// <summary>
    /// Centralized Windows OS &amp; Explorer optimization engine for WinThumbsPreloader.
    /// Manages Thumbnail Cache protection, SMB/Network thumbnail policies, Explorer ShellBag/IconCache
    /// performance tuning, Portable Context Menu registration, and UAC-aware hybrid elevation.
    /// </summary>
    public static class WindowsOptimizer
    {
        public enum OptimizationState
        {
            Default = 0,
            PartiallyOptimized = 1,
            Optimized = 2
        }

        public sealed class OptimizationProfile
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

            public static OptimizationProfile CreateRecommended()
            {
                return new OptimizationProfile
                {
                    EnforceOnStartup = true,
                    PreventThumbCacheCleanup = true,
                    ClearDiskCleanupFlags = true,
                    DisableSilentCleanupTask = false,
                    DisableNetworkThumbsDb = true,
                    ForceThumbnailPreviews = true,
                    DisableFolderTypeDiscovery = true,
                    ExpandBagMruSize = true,
                    ExpandIconCache = true,
                    IconCacheSizeKb = 8192
                };
            }

            public static OptimizationProfile LoadFromSettings()
            {
                int sizeKb = Settings.Default.OptimizeIconCacheSizeKb;
                if (sizeKb < 1024)
                    sizeKb = 8192;

                return new OptimizationProfile
                {
                    EnforceOnStartup = Settings.Default.EnforceWindowsOptimizationsOnStartup,
                    PreventThumbCacheCleanup = Settings.Default.OptimizePreventThumbCacheCleanup,
                    ClearDiskCleanupFlags = Settings.Default.OptimizeClearDiskCleanupFlags,
                    DisableSilentCleanupTask = Settings.Default.OptimizeDisableSilentCleanupTask,
                    DisableNetworkThumbsDb = Settings.Default.OptimizeDisableNetworkThumbsDb,
                    ForceThumbnailPreviews = Settings.Default.OptimizeForceThumbnailPreviews,
                    DisableFolderTypeDiscovery = Settings.Default.OptimizeDisableFolderTypeDiscovery,
                    ExpandBagMruSize = Settings.Default.OptimizeExpandBagMruSize,
                    ExpandIconCache = Settings.Default.OptimizeExpandIconCache,
                    IconCacheSizeKb = sizeKb
                };
            }

            public void SaveToSettings()
            {
                Settings.Default.EnforceWindowsOptimizationsOnStartup = EnforceOnStartup;
                Settings.Default.OptimizePreventThumbCacheCleanup = PreventThumbCacheCleanup;
                Settings.Default.OptimizeClearDiskCleanupFlags = ClearDiskCleanupFlags;
                Settings.Default.OptimizeDisableSilentCleanupTask = DisableSilentCleanupTask;
                Settings.Default.OptimizeDisableNetworkThumbsDb = DisableNetworkThumbsDb;
                Settings.Default.OptimizeForceThumbnailPreviews = ForceThumbnailPreviews;
                Settings.Default.OptimizeDisableFolderTypeDiscovery = DisableFolderTypeDiscovery;
                Settings.Default.OptimizeExpandBagMruSize = ExpandBagMruSize;
                Settings.Default.OptimizeExpandIconCache = ExpandIconCache;
                Settings.Default.OptimizeIconCacheSizeKb = IconCacheSizeKb;
                Settings.Default.Save();
            }

            public int ToBitMask()
            {
                int mask = 0;
                if (EnforceOnStartup) mask |= 1 << 0;
                if (PreventThumbCacheCleanup) mask |= 1 << 1;
                if (ClearDiskCleanupFlags) mask |= 1 << 2;
                if (DisableSilentCleanupTask) mask |= 1 << 3;
                if (DisableNetworkThumbsDb) mask |= 1 << 4;
                if (ForceThumbnailPreviews) mask |= 1 << 5;
                if (DisableFolderTypeDiscovery) mask |= 1 << 6;
                if (ExpandBagMruSize) mask |= 1 << 7;
                if (ExpandIconCache) mask |= 1 << 8;
                return mask;
            }

            public static OptimizationProfile FromBitMask(int mask, int iconCacheSizeKb)
            {
                int clampedKb = iconCacheSizeKb <= 0
                    ? 8192
                    : Math.Clamp(iconCacheSizeKb, 1024, 65536);

                return new OptimizationProfile
                {
                    EnforceOnStartup = (mask & (1 << 0)) != 0,
                    PreventThumbCacheCleanup = (mask & (1 << 1)) != 0,
                    ClearDiskCleanupFlags = (mask & (1 << 2)) != 0,
                    DisableSilentCleanupTask = (mask & (1 << 3)) != 0,
                    DisableNetworkThumbsDb = (mask & (1 << 4)) != 0,
                    ForceThumbnailPreviews = (mask & (1 << 5)) != 0,
                    DisableFolderTypeDiscovery = (mask & (1 << 6)) != 0,
                    ExpandBagMruSize = (mask & (1 << 7)) != 0,
                    ExpandIconCache = (mask & (1 << 8)) != 0,
                    IconCacheSizeKb = clampedKb
                };
            }

            public string BuildElevatedCliArguments()
            {
                int clampedKb = Math.Clamp(IconCacheSizeKb < 1024 ? 8192 : IconCacheSizeKb, 1024, 65536);
                return $"-apply-optimizations -profile-mask:0x{ToBitMask():X4} -icon-cache-kb:{clampedKb}";
            }
        }

        public sealed class WindowsOptimizationStatusReport
        {
            public bool IsProcessElevated { get; init; }
            public OptimizationState PreventThumbCacheCleanup { get; init; }
            public OptimizationState ClearDiskCleanupFlags { get; init; }
            public OptimizationState DisableSilentCleanupTask { get; init; }
            public OptimizationState DisableNetworkThumbsDb { get; init; }
            public OptimizationState ForceThumbnailPreviews { get; init; }
            public OptimizationState DisableFolderTypeDiscovery { get; init; }
            public OptimizationState ExpandBagMruSize { get; init; }
            public OptimizationState ExpandIconCache { get; init; }
            public int CurrentIconCacheSizeKb { get; init; } = 500;
            public bool ExplorerContextMenuRegistered { get; init; }
            public bool ExplorerContextMenuSystemWide { get; init; }

            public bool NeedsElevationForProfile(OptimizationProfile profile)
            {
                if (profile == null)
                    return false;

                if (profile.PreventThumbCacheCleanup && PreventThumbCacheCleanup != OptimizationState.Optimized)
                    return true;
                if (profile.DisableSilentCleanupTask && DisableSilentCleanupTask != OptimizationState.Optimized)
                    return true;
                if (profile.ExpandIconCache && ExpandIconCache != OptimizationState.Optimized)
                    return true;

                return false;
            }
        }

        public sealed class ApplyOptimizationResult
        {
            public int UserTweaksApplied { get; set; }
            public int MachineTweaksApplied { get; set; }
            public bool RequiresElevationForFullCoverage { get; set; }
        }

        /// <summary>
        /// Abstracts Registry hive access and Scheduled Task access so unit tests can run against
        /// an isolated temporary key under HKCU without needing Administrator privileges or altering
        /// real Windows settings.
        /// </summary>
        public sealed class RegistryHiveScope
        {
            private readonly RegistryKey _userBase;
            private readonly string _userPrefix;
            private readonly RegistryKey _machineBase;
            private readonly string _machinePrefix;
            private readonly RegistryKey _machineWowBase;
            private readonly string _machineWowPrefix;
            private readonly Func<bool> _querySilentCleanupDisabled;
            private readonly Func<bool, bool> _setSilentCleanupDisabled;

            public bool MachineWritable { get; }

            private RegistryHiveScope(
                RegistryKey userBase,
                string userPrefix,
                RegistryKey machineBase,
                string machinePrefix,
                RegistryKey machineWowBase,
                string machineWowPrefix,
                bool machineWritable,
                Func<bool> querySilentCleanupDisabled,
                Func<bool, bool> setSilentCleanupDisabled)
            {
                _userBase = userBase;
                _userPrefix = userPrefix;
                _machineBase = machineBase;
                _machinePrefix = machinePrefix;
                _machineWowBase = machineWowBase;
                _machineWowPrefix = machineWowPrefix;
                MachineWritable = machineWritable;
                _querySilentCleanupDisabled = querySilentCleanupDisabled;
                _setSilentCleanupDisabled = setSilentCleanupDisabled;
            }

            public static RegistryHiveScope SystemDefault => new(
                userBase: Registry.CurrentUser,
                userPrefix: string.Empty,
                machineBase: Registry.LocalMachine,
                machinePrefix: string.Empty,
                machineWowBase: Registry.LocalMachine,
                machineWowPrefix: @"SOFTWARE\WOW6432Node",
                machineWritable: IsCurrentProcessElevated(),
                querySilentCleanupDisabled: QuerySystemSilentCleanupTaskDisabled,
                setSilentCleanupDisabled: SetSystemSilentCleanupTaskDisabled);

            public static RegistryHiveScope CreateIsolatedForTesting(
                RegistryKey rootKey,
                string baseSubKeyPath,
                bool simulateMachineWritable,
                Func<bool>? querySilentCleanupDisabled = null,
                Func<bool, bool>? setSilentCleanupDisabled = null)
            {
                string trimmed = baseSubKeyPath.Trim('\\');
                return new RegistryHiveScope(
                    userBase: rootKey,
                    userPrefix: trimmed + @"\HKCU",
                    machineBase: rootKey,
                    machinePrefix: trimmed + @"\HKLM",
                    machineWowBase: rootKey,
                    machineWowPrefix: trimmed + @"\HKLM_WOW6432",
                    machineWritable: simulateMachineWritable,
                    querySilentCleanupDisabled: querySilentCleanupDisabled ?? (() => false),
                    setSilentCleanupDisabled: setSilentCleanupDisabled ?? (_ => simulateMachineWritable));
            }

            public RegistryKey? OpenUserKey(string subKeyPath, bool writable)
            {
                string fullPath = CombinePath(_userPrefix, subKeyPath);
                return writable
                    ? _userBase.CreateSubKey(fullPath, writable: true)
                    : _userBase.OpenSubKey(fullPath, writable: false);
            }

            public void DeleteUserSubKeyTree(string subKeyPath)
            {
                string fullPath = CombinePath(_userPrefix, subKeyPath);
                _userBase.DeleteSubKeyTree(fullPath, throwOnMissingSubKey: false);
            }

            public RegistryKey? OpenMachineKey(string subKeyPath, bool writable)
            {
                if (writable && !MachineWritable)
                    return null;

                string fullPath = CombinePath(_machinePrefix, subKeyPath);
                try
                {
                    return writable
                        ? _machineBase.CreateSubKey(fullPath, writable: true)
                        : _machineBase.OpenSubKey(fullPath, writable: false);
                }
                catch
                {
                    return null;
                }
            }

            public void DeleteMachineSubKeyTree(string subKeyPath)
            {
                if (!MachineWritable)
                    return;

                string fullPath = CombinePath(_machinePrefix, subKeyPath);
                try
                {
                    _machineBase.DeleteSubKeyTree(fullPath, throwOnMissingSubKey: false);
                }
                catch { }
            }

            public RegistryKey? OpenMachineWow6432Key(string subKeyOfSoftware, bool writable)
            {
                if (writable && !MachineWritable)
                    return null;

                string relative = subKeyOfSoftware;
                if (relative.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase))
                    relative = relative.Substring(@"SOFTWARE\".Length);

                string basePrefix = string.IsNullOrEmpty(_machineWowPrefix)
                    ? @"SOFTWARE\WOW6432Node"
                    : _machineWowPrefix;

                string fullPath = CombinePath(basePrefix, relative);
                try
                {
                    return writable
                        ? _machineWowBase.CreateSubKey(fullPath, writable: true)
                        : _machineWowBase.OpenSubKey(fullPath, writable: false);
                }
                catch
                {
                    return null;
                }
            }

            public bool QuerySilentCleanupDisabled() => _querySilentCleanupDisabled();

            public bool SetSilentCleanupDisabled(bool disable) => _setSilentCleanupDisabled(disable);

            private static string CombinePath(string prefix, string subKey)
            {
                string cleanSub = subKey.Trim('\\');
                if (string.IsNullOrEmpty(prefix))
                    return cleanSub;
                if (string.IsNullOrEmpty(cleanSub))
                    return prefix;
                return prefix + @"\" + cleanSub;
            }
        }

        private const string ThumbnailCacheVolumeKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache";
        private const string ExplorerAdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string ExplorerCurrentVersionKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
        private const string PoliciesExplorerKey = @"Software\Policies\Microsoft\Windows\Explorer";
        private const string CurrentVersionPoliciesExplorerKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        private const string AllFoldersShellBagKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell";

        private static readonly string[] BagMruShellKeys =
        [
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell",
            @"Software\Microsoft\Windows\Shell",
            @"Software\Microsoft\Windows\ShellNoRoam"
        ];

        private static readonly string[] ContextMenuTargetClasses =
        [
            @"Software\Classes\Directory\shell\WinThumbsPreloader",
            @"Software\Classes\Directory\Background\shell\WinThumbsPreloader",
            @"Software\Classes\Drive\shell\WinThumbsPreloader"
        ];

        public static bool IsCurrentProcessElevated()
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Called at application startup to enforce user-configured Windows optimizations
        /// and legacy backup sanitization without prompting for UAC.
        /// </summary>
        public static void EnsureStartupOptimizations(RegistryHiveScope? scope = null)
        {
            RegistryHiveScope activeScope = scope ?? RegistryHiveScope.SystemDefault;
            try
            {
                OptimizationProfile profile = OptimizationProfile.LoadFromSettings();
                if (profile.EnforceOnStartup)
                {
                    ApplyOptimizations(profile, activeScope, broadcastShellChange: false);
                }
                else if (profile.PreventThumbCacheCleanup)
                {
                    ApplyPreventThumbCacheCleanup(enable: true, activeScope, out _, out _);
                }
            }
            catch
            {
                // Best-effort at startup
            }
        }

        /// <summary>
        /// Inspects the live Windows Registry and Scheduled Task state to report which optimizations
        /// are active, partially active (HKCU only), or at Windows defaults.
        /// </summary>
        public static WindowsOptimizationStatusReport InspectSystemState(RegistryHiveScope? scope = null)
        {
            RegistryHiveScope activeScope = scope ?? RegistryHiveScope.SystemDefault;

            // 1. PreventThumbCacheCleanup (Autorun = 0 in HKCU & HKLM)
            bool userAutorunDisabled = TryGetDword(activeScope.OpenUserKey(ThumbnailCacheVolumeKey, writable: false), "Autorun") == 0;
            bool machineAutorunDisabled = TryGetDword(activeScope.OpenMachineKey(ThumbnailCacheVolumeKey, writable: false), "Autorun") == 0;
            OptimizationState thumbCacheState = (userAutorunDisabled && machineAutorunDisabled)
                ? OptimizationState.Optimized
                : (userAutorunDisabled || machineAutorunDisabled)
                    ? OptimizationState.PartiallyOptimized
                    : OptimizationState.Default;

            // 2. ClearDiskCleanupFlags (No non-zero StateFlagsXXXX values)
            bool hasUserFlags = HasNonZeroStateFlags(activeScope.OpenUserKey(ThumbnailCacheVolumeKey, writable: false));
            bool hasMachineFlags = HasNonZeroStateFlags(activeScope.OpenMachineKey(ThumbnailCacheVolumeKey, writable: false))
                                || HasNonZeroStateFlags(activeScope.OpenMachineWow6432Key(ThumbnailCacheVolumeKey, writable: false));
            OptimizationState diskCleanupFlagsState = (!hasUserFlags && !hasMachineFlags)
                ? OptimizationState.Optimized
                : (!hasUserFlags && hasMachineFlags)
                    ? OptimizationState.PartiallyOptimized
                    : OptimizationState.Default;

            // 3. DisableSilentCleanupTask
            OptimizationState silentCleanupState = activeScope.QuerySilentCleanupDisabled()
                ? OptimizationState.Optimized
                : OptimizationState.Default;

            // 4. DisableNetworkThumbsDb
            bool netThumbsPolicyUser = TryGetDword(activeScope.OpenUserKey(PoliciesExplorerKey, writable: false), "DisableThumbsDBOnNetworkFolders") == 1;
            bool netThumbsAdvUser = TryGetDword(activeScope.OpenUserKey(ExplorerAdvancedKey, writable: false), "DisableThumbsDBOnNetworkFolders") == 1;
            OptimizationState netThumbsState = (netThumbsPolicyUser || netThumbsAdvUser)
                ? OptimizationState.Optimized
                : OptimizationState.Default;

            // 5. ForceThumbnailPreviews
            bool iconsOnlyZero = TryGetDword(activeScope.OpenUserKey(ExplorerAdvancedKey, writable: false), "IconsOnly") == 0;
            bool cacheEnabledPolicy = TryGetDword(activeScope.OpenUserKey(CurrentVersionPoliciesExplorerKey, writable: false), "DisableThumbnailCache") == 0;
            OptimizationState forcePreviewsState = (iconsOnlyZero && cacheEnabledPolicy)
                ? OptimizationState.Optimized
                : OptimizationState.Default;

            // 6. DisableFolderTypeDiscovery
            string? folderType = TryGetString(activeScope.OpenUserKey(AllFoldersShellBagKey, writable: false), "FolderType");
            OptimizationState folderDiscoveryState = string.Equals(folderType, "NotSpecified", StringComparison.OrdinalIgnoreCase)
                ? OptimizationState.Optimized
                : OptimizationState.Default;

            // 7. ExpandBagMruSize
            int? bagMruSize = TryGetDword(activeScope.OpenUserKey(BagMruShellKeys[0], writable: false), "BagMRU Size");
            OptimizationState bagMruState = (bagMruSize.HasValue && bagMruSize.Value >= 10000)
                ? OptimizationState.Optimized
                : OptimizationState.Default;

            // 8. ExpandIconCache
            int? machineIconCache = TryParseIntString(TryGetString(activeScope.OpenMachineKey(ExplorerCurrentVersionKey, writable: false), "Max Cached Icons"));
            int? userIconCache = TryParseIntString(TryGetString(activeScope.OpenUserKey(ExplorerCurrentVersionKey, writable: false), "Max Cached Icons"));
            int effectiveIconCacheKb = machineIconCache ?? userIconCache ?? 500;
            OptimizationState iconCacheState = (machineIconCache.HasValue && machineIconCache.Value >= 4096)
                ? OptimizationState.Optimized
                : (userIconCache.HasValue && userIconCache.Value >= 4096)
                    ? OptimizationState.PartiallyOptimized
                    : OptimizationState.Default;

            // 9. Context Menu
            bool userMenu = HasKey(activeScope.OpenUserKey(ContextMenuTargetClasses[0], writable: false));
            bool machineMenu = HasKey(activeScope.OpenMachineKey(ContextMenuTargetClasses[0], writable: false));

            return new WindowsOptimizationStatusReport
            {
                IsProcessElevated = activeScope.MachineWritable,
                PreventThumbCacheCleanup = thumbCacheState,
                ClearDiskCleanupFlags = diskCleanupFlagsState,
                DisableSilentCleanupTask = silentCleanupState,
                DisableNetworkThumbsDb = netThumbsState,
                ForceThumbnailPreviews = forcePreviewsState,
                DisableFolderTypeDiscovery = folderDiscoveryState,
                ExpandBagMruSize = bagMruState,
                ExpandIconCache = iconCacheState,
                CurrentIconCacheSizeKb = effectiveIconCacheKb,
                ExplorerContextMenuRegistered = userMenu || machineMenu,
                ExplorerContextMenuSystemWide = machineMenu
            };
        }

        /// <summary>
        /// Applies the desired optimization profile to HKCU immediately and to HKLM/TaskScheduler
        /// when permissions allow. Returns whether elevation is needed for full coverage.
        /// </summary>
        public static ApplyOptimizationResult ApplyOptimizations(
            OptimizationProfile profile,
            RegistryHiveScope? scope = null,
            bool broadcastShellChange = true)
        {
            ArgumentNullException.ThrowIfNull(profile);
            RegistryHiveScope activeScope = scope ?? RegistryHiveScope.SystemDefault;

            var result = new ApplyOptimizationResult();

            // 1. Prevent Automatic Thumbnail Cache Cleanup (Autorun = 0)
            ApplyPreventThumbCacheCleanup(profile.PreventThumbCacheCleanup, activeScope, out bool userApplied, out bool machineApplied);
            if (userApplied) result.UserTweaksApplied++;
            if (machineApplied) result.MachineTweaksApplied++;
            if (profile.PreventThumbCacheCleanup && !machineApplied)
                result.RequiresElevationForFullCoverage = true;

            // 2. Clear Disk Cleanup StateFlagsXXXX
            if (profile.ClearDiskCleanupFlags)
            {
                ClearDiskCleanupStateFlags(activeScope, out bool flagsUser, out bool flagsMachine);
                if (flagsUser) result.UserTweaksApplied++;
                if (flagsMachine) result.MachineTweaksApplied++;
            }

            // 3. SilentCleanup Scheduled Task
            if (profile.DisableSilentCleanupTask)
            {
                if (activeScope.SetSilentCleanupDisabled(true))
                    result.MachineTweaksApplied++;
                else
                    result.RequiresElevationForFullCoverage = true;
            }
            else if (activeScope.MachineWritable && activeScope.QuerySilentCleanupDisabled())
            {
                activeScope.SetSilentCleanupDisabled(false);
            }

            // 4. Disable Network Thumbs.db
            ApplyDisableNetworkThumbsDb(profile.DisableNetworkThumbsDb, activeScope);
            result.UserTweaksApplied++;

            // 5. Force Thumbnail Previews & Local Cache Enabled
            ApplyForceThumbnailPreviews(profile.ForceThumbnailPreviews, activeScope);
            result.UserTweaksApplied++;

            // 6. Disable Automatic Folder Type Discovery
            ApplyDisableFolderTypeDiscovery(profile.DisableFolderTypeDiscovery, activeScope);
            result.UserTweaksApplied++;

            // 7. Expand BagMRU Size (20,000 folders)
            ApplyExpandBagMruSize(profile.ExpandBagMruSize, activeScope);
            result.UserTweaksApplied++;

            // 8. Expand Icon Cache Size (Max Cached Icons)
            ApplyExpandIconCache(profile.ExpandIconCache, profile.IconCacheSizeKb, activeScope, out bool iconUser, out bool iconMachine);
            if (iconUser) result.UserTweaksApplied++;
            if (iconMachine) result.MachineTweaksApplied++;
            if (profile.ExpandIconCache && !iconMachine)
                result.RequiresElevationForFullCoverage = true;

            if (broadcastShellChange)
                BroadcastShellSettingsChange();

            return result;
        }

        /// <summary>
        /// Reverts all optimization tweaks managed by WinThumbsPreloader back to standard Windows 10/11 defaults.
        /// </summary>
        public static void RestoreWindowsDefaults(RegistryHiveScope? scope = null, bool broadcastShellChange = true)
        {
            RegistryHiveScope activeScope = scope ?? RegistryHiveScope.SystemDefault;

            // 1. Restore Autorun = 3 on VolumeCaches\Thumbnail Cache
            try
            {
                using var userKey = activeScope.OpenUserKey(ThumbnailCacheVolumeKey, writable: true);
                userKey?.DeleteValue("Autorun", throwOnMissingValue: false);
            }
            catch { }

            try
            {
                using var machineKey = activeScope.OpenMachineKey(ThumbnailCacheVolumeKey, writable: true);
                machineKey?.SetValue("Autorun", 3, RegistryValueKind.DWord);
            }
            catch { }

            try
            {
                using var wowKey = activeScope.OpenMachineWow6432Key(ThumbnailCacheVolumeKey, writable: true);
                wowKey?.SetValue("Autorun", 3, RegistryValueKind.DWord);
            }
            catch { }

            // 2. Re-enable SilentCleanup task
            try
            {
                activeScope.SetSilentCleanupDisabled(false);
            }
            catch { }

            // 3. Revert Network Thumbs.db policy
            ApplyDisableNetworkThumbsDb(enable: false, activeScope);

            // 4. Revert Force Thumbnail Previews policy overrides
            ApplyForceThumbnailPreviews(enable: false, activeScope);

            // 5. Revert FolderTypeDiscovery
            ApplyDisableFolderTypeDiscovery(enable: false, activeScope);

            // 6. Revert BagMRU Size
            ApplyExpandBagMruSize(enable: false, activeScope);

            // 7. Revert Max Cached Icons
            ApplyExpandIconCache(enable: false, sizeKb: 500, activeScope, out _, out _);

            if (broadcastShellChange)
                BroadcastShellSettingsChange();
        }

        /// <summary>
        /// Registers the WinThumbsPreloader cascading context menu for Directories, Directory Backgrounds, and Drives.
        /// Works in both Portable mode (HKCU\Software\Classes) and Administrator mode (HKLM\Software\Classes).
        /// </summary>
        public static bool RegisterExplorerContextMenu(string? executablePath = null, RegistryHiveScope? scope = null)
        {
            RegistryHiveScope activeScope = scope ?? RegistryHiveScope.SystemDefault;
            string exePath = string.IsNullOrWhiteSpace(executablePath)
                ? Application.ExecutablePath
                : executablePath;

            bool anyRegistered = false;

            foreach (string classPath in ContextMenuTargetClasses)
            {
                if (WriteContextMenuTree( updateKey: sub => activeScope.OpenUserKey(sub, writable: true), classPath, exePath))
                    anyRegistered = true;

                if (activeScope.MachineWritable)
                {
                    if (WriteContextMenuTree(sub => activeScope.OpenMachineKey(sub, writable: true), classPath, exePath))
                        anyRegistered = true;
                }
            }

            if (anyRegistered && scope == null)
                BroadcastShellSettingsChange();

            return anyRegistered;
        }

        /// <summary>
        /// Removes the WinThumbsPreloader cascading context menu from both HKCU and HKLM Classes.
        /// </summary>
        public static bool UnregisterExplorerContextMenu(RegistryHiveScope? scope = null)
        {
            RegistryHiveScope activeScope = scope ?? RegistryHiveScope.SystemDefault;
            bool anyRemoved = false;

            foreach (string classPath in ContextMenuTargetClasses)
            {
                try
                {
                    activeScope.DeleteUserSubKeyTree(classPath);
                    anyRemoved = true;
                }
                catch { }

                if (activeScope.MachineWritable)
                {
                    try
                    {
                        activeScope.DeleteMachineSubKeyTree(classPath);
                        anyRemoved = true;
                    }
                    catch { }
                }
            }

            if (anyRemoved && scope == null)
                BroadcastShellSettingsChange();

            return anyRemoved;
        }

        public static bool IsExplorerContextMenuRegistered(RegistryHiveScope? scope = null)
        {
            RegistryHiveScope activeScope = scope ?? RegistryHiveScope.SystemDefault;
            using var userKey = activeScope.OpenUserKey(ContextMenuTargetClasses[0], writable: false);
            if (userKey != null)
                return true;

            using var machineKey = activeScope.OpenMachineKey(ContextMenuTargetClasses[0], writable: false);
            return machineKey != null;
        }

        /// <summary>
        /// Deletes only icon cache database files (iconcache_*.db and legacy IconCache.db) without touching
        /// any thumbcache_*.db files, then optionally invokes ie4uinit.exe to refresh Windows icons live.
        /// </summary>
        public static int ClearIconCacheFilesOnly(string? explorerCacheDirectory = null, bool invokeIe4uinit = true)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string explorerDir = string.IsNullOrWhiteSpace(explorerCacheDirectory)
                ? Path.Combine(localAppData, "Microsoft", "Windows", "Explorer")
                : explorerCacheDirectory;

            int deletedCount = 0;

            if (PathSecurity.HasReparsePointOrJunctionInPath(explorerDir))
                return 0;

            if (Directory.Exists(explorerDir))
            {
                try
                {
                    foreach (string file in Directory.GetFiles(explorerDir, "iconcache_*.db", SearchOption.TopDirectoryOnly))
                    {
                        if (TryDeleteFileForce(file))
                            deletedCount++;
                    }
                }
                catch { }
            }

            // Also check parent LocalAppData directory for legacy IconCache.db
            try
            {
                DirectoryInfo? parent = Directory.GetParent(explorerDir)?.Parent?.Parent;
                string candidateRoot = (parent != null && Directory.Exists(parent.FullName))
                    ? parent.FullName
                    : localAppData;

                if (!PathSecurity.HasReparsePointOrJunctionInPath(candidateRoot))
                {
                    string legacyIconCache = Path.Combine(candidateRoot, "IconCache.db");
                    if (File.Exists(legacyIconCache) && TryDeleteFileForce(legacyIconCache))
                        deletedCount++;
                }
            }
            catch { }

            if (invokeIe4uinit)
            {
                TryRunIe4uinit();
            }

            return deletedCount;
        }

        /// <summary>
        /// Cleanly restarts Windows Explorer so registry tweaks (ShellBags, IconCache, Policies) take immediate effect.
        /// </summary>
        public static bool RestartExplorerCleanly()
        {
            try
            {
                foreach (Process proc in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        using (proc)
                        {
                            if (!TerminateProcess(proc.Handle, 1))
                                proc.Kill();
                            proc.WaitForExit(3000);
                        }
                    }
                    catch { }
                }

                string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (string.IsNullOrWhiteSpace(windowsDir))
                    return false;

                string explorerExe = Path.Combine(windowsDir, "explorer.exe");
                if (!File.Exists(explorerExe))
                    return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = explorerExe,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Spawns an elevated helper instance of WinThumbsPreloader with the specified CLI flag
        /// (e.g., "-apply-optimizations" or "-restore-defaults") to write HKLM and Scheduled Task settings.
        /// </summary>
        public static bool RunElevatedHelper(string cliFlag)
        {
            if (string.IsNullOrWhiteSpace(cliFlag))
                return false;

            string trimmedArgs = cliFlag.Trim();
            bool allowed = trimmedArgs.Equals("-restore-defaults", StringComparison.OrdinalIgnoreCase) ||
                           trimmedArgs.Equals("-apply-optimizations", StringComparison.OrdinalIgnoreCase) ||
                           trimmedArgs.StartsWith("-apply-optimizations ", StringComparison.OrdinalIgnoreCase);

            if (!allowed || trimmedArgs.IndexOfAny(['"', '\'', '&', '|', ';', '\r', '\n']) >= 0)
                return false;

            try
            {
                using Process? proc = Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = trimmedArgs,
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                if (proc == null)
                    return false;

                proc.WaitForExit(15000);
                return proc.ExitCode == 0;
            }
            catch
            {
                // User cancelled UAC prompt or elevation failed
                return false;
            }
        }

        private static void ApplyPreventThumbCacheCleanup(
            bool enable,
            RegistryHiveScope scope,
            out bool userApplied,
            out bool machineApplied)
        {
            userApplied = false;
            machineApplied = false;

            try
            {
                using var userKey = scope.OpenUserKey(ThumbnailCacheVolumeKey, writable: true);
                if (userKey != null)
                {
                    if (enable)
                        userKey.SetValue("Autorun", 0, RegistryValueKind.DWord);
                    else
                        userKey.DeleteValue("Autorun", throwOnMissingValue: false);
                    userApplied = true;
                }
            }
            catch { }

            try
            {
                using var machineKey = scope.OpenMachineKey(ThumbnailCacheVolumeKey, writable: true);
                if (machineKey != null)
                {
                    machineKey.SetValue("Autorun", enable ? 0 : 3, RegistryValueKind.DWord);
                    machineApplied = true;
                }
            }
            catch { }

            try
            {
                using var wowKey = scope.OpenMachineWow6432Key(ThumbnailCacheVolumeKey, writable: true);
                wowKey?.SetValue("Autorun", enable ? 0 : 3, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static void ClearDiskCleanupStateFlags(
            RegistryHiveScope scope,
            out bool userCleared,
            out bool machineCleared)
        {
            userCleared = ClearStateFlagsOnKey(scope.OpenUserKey(ThumbnailCacheVolumeKey, writable: true));
            bool m64 = ClearStateFlagsOnKey(scope.OpenMachineKey(ThumbnailCacheVolumeKey, writable: true));
            bool m32 = ClearStateFlagsOnKey(scope.OpenMachineWow6432Key(ThumbnailCacheVolumeKey, writable: true));
            machineCleared = m64 || m32;
        }

        private static bool ClearStateFlagsOnKey(RegistryKey? key)
        {
            if (key == null)
                return false;

            using (key)
            {
                try
                {
                    foreach (string valueName in key.GetValueNames())
                    {
                        if (valueName.StartsWith("StateFlags", StringComparison.OrdinalIgnoreCase))
                        {
                            key.SetValue(valueName, 0, RegistryValueKind.DWord);
                        }
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static void ApplyDisableNetworkThumbsDb(bool enable, RegistryHiveScope scope)
        {
            try
            {
                using var userPolicyKey = scope.OpenUserKey(PoliciesExplorerKey, writable: true);
                if (userPolicyKey != null)
                {
                    if (enable)
                        userPolicyKey.SetValue("DisableThumbsDBOnNetworkFolders", 1, RegistryValueKind.DWord);
                    else
                        userPolicyKey.DeleteValue("DisableThumbsDBOnNetworkFolders", throwOnMissingValue: false);
                }
            }
            catch { }

            try
            {
                using var userAdvKey = scope.OpenUserKey(ExplorerAdvancedKey, writable: true);
                if (userAdvKey != null)
                {
                    if (enable)
                        userAdvKey.SetValue("DisableThumbsDBOnNetworkFolders", 1, RegistryValueKind.DWord);
                    else
                        userAdvKey.DeleteValue("DisableThumbsDBOnNetworkFolders", throwOnMissingValue: false);
                }
            }
            catch { }

            try
            {
                using var machinePolicyKey = scope.OpenMachineKey(PoliciesExplorerKey, writable: true);
                if (machinePolicyKey != null)
                {
                    if (enable)
                        machinePolicyKey.SetValue("DisableThumbsDBOnNetworkFolders", 1, RegistryValueKind.DWord);
                    else
                        machinePolicyKey.DeleteValue("DisableThumbsDBOnNetworkFolders", throwOnMissingValue: false);
                }
            }
            catch { }
        }

        private static void ApplyForceThumbnailPreviews(bool enable, RegistryHiveScope scope)
        {
            try
            {
                using var advKey = scope.OpenUserKey(ExplorerAdvancedKey, writable: true);
                if (advKey != null)
                {
                    if (enable)
                        advKey.SetValue("IconsOnly", 0, RegistryValueKind.DWord);
                    else
                        advKey.DeleteValue("IconsOnly", throwOnMissingValue: false);
                }
            }
            catch { }

            try
            {
                using var cvPolicyKey = scope.OpenUserKey(CurrentVersionPoliciesExplorerKey, writable: true);
                if (cvPolicyKey != null)
                {
                    if (enable)
                    {
                        cvPolicyKey.SetValue("DisableThumbnailCache", 0, RegistryValueKind.DWord);
                        cvPolicyKey.SetValue("NoThumbnailCache", 0, RegistryValueKind.DWord);
                        cvPolicyKey.SetValue("DisableThumbnails", 0, RegistryValueKind.DWord);
                    }
                    else
                    {
                        cvPolicyKey.DeleteValue("DisableThumbnailCache", throwOnMissingValue: false);
                        cvPolicyKey.DeleteValue("NoThumbnailCache", throwOnMissingValue: false);
                        cvPolicyKey.DeleteValue("DisableThumbnails", throwOnMissingValue: false);
                    }
                }
            }
            catch { }

            try
            {
                using var policyKey = scope.OpenUserKey(PoliciesExplorerKey, writable: true);
                if (policyKey != null)
                {
                    if (enable)
                    {
                        policyKey.SetValue("DisableThumbnailCache", 0, RegistryValueKind.DWord);
                        policyKey.SetValue("NoThumbnailCache", 0, RegistryValueKind.DWord);
                    }
                    else
                    {
                        policyKey.DeleteValue("DisableThumbnailCache", throwOnMissingValue: false);
                        policyKey.DeleteValue("NoThumbnailCache", throwOnMissingValue: false);
                    }
                }
            }
            catch { }
        }

        private static void ApplyDisableFolderTypeDiscovery(bool enable, RegistryHiveScope scope)
        {
            try
            {
                using var bagKey = scope.OpenUserKey(AllFoldersShellBagKey, writable: true);
                if (bagKey != null)
                {
                    if (enable)
                        bagKey.SetValue("FolderType", "NotSpecified", RegistryValueKind.String);
                    else
                        bagKey.DeleteValue("FolderType", throwOnMissingValue: false);
                }
            }
            catch { }
        }

        private static void ApplyExpandBagMruSize(bool enable, RegistryHiveScope scope)
        {
            foreach (string keyPath in BagMruShellKeys)
            {
                try
                {
                    using var key = scope.OpenUserKey(keyPath, writable: true);
                    if (key != null)
                    {
                        if (enable)
                            key.SetValue("BagMRU Size", 20000, RegistryValueKind.DWord);
                        else
                            key.DeleteValue("BagMRU Size", throwOnMissingValue: false);
                    }
                }
                catch { }
            }
        }

        private static void ApplyExpandIconCache(
            bool enable,
            int sizeKb,
            RegistryHiveScope scope,
            out bool userApplied,
            out bool machineApplied)
        {
            userApplied = false;
            machineApplied = false;
            string sizeString = Math.Clamp(sizeKb, 1024, 65536).ToString();

            try
            {
                using var userKey = scope.OpenUserKey(ExplorerCurrentVersionKey, writable: true);
                if (userKey != null)
                {
                    if (enable)
                        userKey.SetValue("Max Cached Icons", sizeString, RegistryValueKind.String);
                    else
                        userKey.DeleteValue("Max Cached Icons", throwOnMissingValue: false);
                    userApplied = true;
                }
            }
            catch { }

            try
            {
                using var machineKey = scope.OpenMachineKey(ExplorerCurrentVersionKey, writable: true);
                if (machineKey != null)
                {
                    if (enable)
                        machineKey.SetValue("Max Cached Icons", sizeString, RegistryValueKind.String);
                    else
                        machineKey.DeleteValue("Max Cached Icons", throwOnMissingValue: false);
                    machineApplied = true;
                }
            }
            catch { }

            try
            {
                using var wowKey = scope.OpenMachineWow6432Key(ExplorerCurrentVersionKey, writable: true);
                if (wowKey != null)
                {
                    if (enable)
                        wowKey.SetValue("Max Cached Icons", sizeString, RegistryValueKind.String);
                    else
                        wowKey.DeleteValue("Max Cached Icons", throwOnMissingValue: false);
                }
            }
            catch { }
        }

        private static bool WriteContextMenuTree(Func<string, RegistryKey?> updateKey, string baseMenuPath, string exePath)
        {
            try
            {
                using (var root = updateKey(baseMenuPath))
                {
                    if (root == null)
                        return false;

                    root.SetValue("MUIVerb", "WinThumbsPreloader", RegistryValueKind.String);
                    root.SetValue("Icon", $"\"{exePath}\"", RegistryValueKind.String);
                    root.SetValue("SubCommands", "", RegistryValueKind.String);
                }

                (string subKey, string title, string args)[] commands =
                [
                    ("command1", "Preload Thumbnails", $"\"{exePath}\" \"%V\""),
                    ("command2", "Preload Thumbnails (Bulk)", $"\"{exePath}\" -b"),
                    ("command3", "Preload Thumbnails Silently", $"\"{exePath}\" -s \"%V\""),
                    ("command4", "Preload Thumbnails Recursively", $"\"{exePath}\" -r \"%V\""),
                    ("command5", "Preload Thumbnails Recursively And Silently", $"\"{exePath}\" -r -s \"%V\"")
                ];

                foreach (var (subKey, title, args) in commands)
                {
                    string cmdRootPath = $@"{baseMenuPath}\shell\{subKey}";
                    using (var cmdKey = updateKey(cmdRootPath))
                    {
                        cmdKey?.SetValue("", title, RegistryValueKind.String);
                    }

                    using (var execKey = updateKey($@"{cmdRootPath}\command"))
                    {
                        execKey?.SetValue("", args, RegistryValueKind.String);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool QuerySystemSilentCleanupTaskDisabled()
        {
            try
            {
                string schtasksExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
                if (!File.Exists(schtasksExe))
                    return false;

                using Process? proc = Process.Start(new ProcessStartInfo
                {
                    FileName = schtasksExe,
                    Arguments = @"/Query /TN ""\Microsoft\Windows\DiskCleanup\SilentCleanup"" /FO CSV /NH",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (proc == null)
                    return false;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                return output.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("Desativado", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("Desabilitado", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool SetSystemSilentCleanupTaskDisabled(bool disable)
        {
            if (!IsCurrentProcessElevated())
                return false;

            try
            {
                string schtasksExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
                if (!File.Exists(schtasksExe))
                    return false;

                string flag = disable ? "/Disable" : "/Enable";
                using Process? proc = Process.Start(new ProcessStartInfo
                {
                    FileName = schtasksExe,
                    Arguments = $@"/Change /TN ""\Microsoft\Windows\DiskCleanup\SilentCleanup"" {flag}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (proc == null)
                    return false;

                proc.WaitForExit(4000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void TryRunIe4uinit()
        {
            try
            {
                string ie4uinitPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ie4uinit.exe");
                if (!File.Exists(ie4uinitPath))
                    return;

                using Process? p = Process.Start(new ProcessStartInfo
                {
                    FileName = ie4uinitPath,
                    Arguments = "-show",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                p?.WaitForExit(3000);
            }
            catch { }
        }

        private static bool TryDeleteFileForce(string filePath)
        {
            try
            {
                if (PathSecurity.HasReparsePointOrJunctionInPath(filePath))
                    return false;

                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);
                return !File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasNonZeroStateFlags(RegistryKey? key)
        {
            if (key == null)
                return false;

            using (key)
            {
                try
                {
                    foreach (string name in key.GetValueNames())
                    {
                        if (name.StartsWith("StateFlags", StringComparison.OrdinalIgnoreCase))
                        {
                            object? val = key.GetValue(name);
                            if (val is int intVal && intVal != 0)
                                return true;
                        }
                    }
                }
                catch { }
                return false;
            }
        }

        private static int? TryGetDword(RegistryKey? key, string valueName)
        {
            if (key == null)
                return null;

            using (key)
            {
                try
                {
                    object? val = key.GetValue(valueName);
                    if (val is int i)
                        return i;
                    if (val != null && int.TryParse(val.ToString(), out int parsed))
                        return parsed;
                }
                catch { }
                return null;
            }
        }

        private static string? TryGetString(RegistryKey? key, string valueName)
        {
            if (key == null)
                return null;

            using (key)
            {
                try
                {
                    return key.GetValue(valueName)?.ToString();
                }
                catch
                {
                    return null;
                }
            }
        }

        private static int? TryParseIntString(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return int.TryParse(value.Trim(), out int parsed) ? parsed : null;
        }

        private static bool HasKey(RegistryKey? key)
        {
            if (key == null)
                return false;
            key.Dispose();
            return true;
        }

        public static void BroadcastShellSettingsChange()
        {
            try
            {
                SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x0000 /* SHCNF_IDLIST */, IntPtr.Zero, IntPtr.Zero);
                SendMessageTimeout(
                    new IntPtr(0xFFFF) /* HWND_BROADCAST */,
                    0x001A /* WM_SETTINGCHANGE */,
                    IntPtr.Zero,
                    "ShellState",
                    0x0002 /* SMTO_ABORTIFHUNG */,
                    1000,
                    out _);
            }
            catch { }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);
    }
}
