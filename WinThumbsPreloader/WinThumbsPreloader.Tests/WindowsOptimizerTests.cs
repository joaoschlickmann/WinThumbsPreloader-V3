using System;
using System.IO;
using Microsoft.Win32;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    public sealed class WindowsOptimizerTests : IDisposable
    {
        private readonly string _testRootKeyPath;
        private readonly WindowsOptimizer.RegistryHiveScope _scope;
        private readonly string _tempDir;
        private bool _simulatedSilentCleanupDisabled;
        private bool _simulatedCanModifyTask;

        public WindowsOptimizerTests()
        {
            _testRootKeyPath = @"Software\WinThumbsPreloader_UnitTests\" + Guid.NewGuid().ToString("N");
            _simulatedSilentCleanupDisabled = false;
            _simulatedCanModifyTask = true;

            _scope = WindowsOptimizer.RegistryHiveScope.CreateIsolatedForTesting(
                Registry.CurrentUser,
                _testRootKeyPath,
                simulateMachineWritable: true,
                querySilentCleanupDisabled: () => _simulatedSilentCleanupDisabled,
                setSilentCleanupDisabled: disable =>
                {
                    if (!_simulatedCanModifyTask)
                        return false;
                    _simulatedSilentCleanupDisabled = disable;
                    return true;
                });

            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_OptimizerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(_testRootKeyPath, throwOnMissingSubKey: false);
            }
            catch { }

            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        [Fact]
        public void InspectSystemState_OnFreshRegistry_ReportsAllDefault()
        {
            // Act
            var report = WindowsOptimizer.InspectSystemState(_scope);

            // Assert
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.PreventThumbCacheCleanup);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.ClearDiskCleanupFlags); // No non-zero StateFlags present
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.DisableSilentCleanupTask);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.DisableNetworkThumbsDb);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.ForceThumbnailPreviews);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.DisableFolderTypeDiscovery);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.ExpandBagMruSize);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.ExpandIconCache);
            Assert.False(report.ExplorerContextMenuRegistered);
        }

        [Fact]
        public void ApplyOptimizations_WhenMachineWritable_ReachesFullOptimizedStateForAllTweaks()
        {
            // Arrange
            var profile = new WindowsOptimizer.OptimizationProfile
            {
                PreventThumbCacheCleanup = true,
                ClearDiskCleanupFlags = true,
                DisableSilentCleanupTask = true,
                DisableNetworkThumbsDb = true,
                ForceThumbnailPreviews = true,
                DisableFolderTypeDiscovery = true,
                ExpandBagMruSize = true,
                ExpandIconCache = true,
                IconCacheSizeKb = 8192
            };

            // Seed a non-zero StateFlags0001 value to verify ClearDiskCleanupFlags zeroes it out
            using (var key = _scope.OpenMachineKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache", writable: true))
            {
                key?.SetValue("StateFlags0001", 2, RegistryValueKind.DWord);
            }

            // Act
            var result = WindowsOptimizer.ApplyOptimizations(profile, _scope, broadcastShellChange: false);
            var report = WindowsOptimizer.InspectSystemState(_scope);

            // Assert
            Assert.False(result.RequiresElevationForFullCoverage);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.PreventThumbCacheCleanup);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.ClearDiskCleanupFlags);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.DisableSilentCleanupTask);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.DisableNetworkThumbsDb);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.ForceThumbnailPreviews);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.DisableFolderTypeDiscovery);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.ExpandBagMruSize);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.ExpandIconCache);
            Assert.Equal(8192, report.CurrentIconCacheSizeKb);

            using (var key = _scope.OpenMachineKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache", writable: false))
            {
                Assert.Equal(0, key?.GetValue("StateFlags0001"));
                Assert.Equal(0, key?.GetValue("Autorun"));
            }
        }

        [Fact]
        public void ApplyOptimizations_WhenMachineNotWritable_SetsPartiallyOptimizedForMachineTweaksAndOptimizedForUserTweaks()
        {
            // Arrange
            _simulatedCanModifyTask = false;
            var userOnlyScope = WindowsOptimizer.RegistryHiveScope.CreateIsolatedForTesting(
                Registry.CurrentUser,
                _testRootKeyPath,
                simulateMachineWritable: false,
                querySilentCleanupDisabled: () => _simulatedSilentCleanupDisabled,
                setSilentCleanupDisabled: _ => false);

            var profile = new WindowsOptimizer.OptimizationProfile
            {
                PreventThumbCacheCleanup = true,
                ClearDiskCleanupFlags = true,
                DisableSilentCleanupTask = true,
                DisableNetworkThumbsDb = true,
                ForceThumbnailPreviews = true,
                DisableFolderTypeDiscovery = true,
                ExpandBagMruSize = true,
                ExpandIconCache = true,
                IconCacheSizeKb = 16384
            };

            // Act
            var result = WindowsOptimizer.ApplyOptimizations(profile, userOnlyScope, broadcastShellChange: false);
            var report = WindowsOptimizer.InspectSystemState(userOnlyScope);

            // Assert
            Assert.True(result.RequiresElevationForFullCoverage);
            Assert.Equal(WindowsOptimizer.OptimizationState.PartiallyOptimized, report.PreventThumbCacheCleanup);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.DisableSilentCleanupTask);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.DisableNetworkThumbsDb);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.ForceThumbnailPreviews);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.DisableFolderTypeDiscovery);
            Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, report.ExpandBagMruSize);
            Assert.Equal(WindowsOptimizer.OptimizationState.PartiallyOptimized, report.ExpandIconCache);
            Assert.Equal(16384, report.CurrentIconCacheSizeKb);
        }

        [Fact]
        public void RestoreWindowsDefaults_AfterApplyingAllTweaks_RestoresDefaultState()
        {
            // Arrange
            var profile = WindowsOptimizer.OptimizationProfile.CreateRecommended();
            profile.DisableSilentCleanupTask = true;
            WindowsOptimizer.ApplyOptimizations(profile, _scope, broadcastShellChange: false);

            // Act
            WindowsOptimizer.RestoreWindowsDefaults(_scope, broadcastShellChange: false);
            var report = WindowsOptimizer.InspectSystemState(_scope);

            // Assert
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.PreventThumbCacheCleanup);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.DisableSilentCleanupTask);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.DisableNetworkThumbsDb);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.ForceThumbnailPreviews);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.DisableFolderTypeDiscovery);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.ExpandBagMruSize);
            Assert.Equal(WindowsOptimizer.OptimizationState.Default, report.ExpandIconCache);
        }

        [Fact]
        public void RegisterAndUnregisterExplorerContextMenu_RoundTripsCorrectly()
        {
            // Arrange
            string fakeExePath = @"C:\Tools\WinThumbsPreloader\WinThumbsPreloader.exe";

            // Act 1: Register
            bool registered = WindowsOptimizer.RegisterExplorerContextMenu(fakeExePath, _scope);
            bool isRegisteredAfterAdd = WindowsOptimizer.IsExplorerContextMenuRegistered(_scope);

            // Assert 1
            Assert.True(registered);
            Assert.True(isRegisteredAfterAdd);

            using (var cmdKey = _scope.OpenUserKey(@"Software\Classes\Directory\shell\WinThumbsPreloader\shell\command1\command", writable: false))
            {
                Assert.NotNull(cmdKey);
                string val = cmdKey.GetValue("") as string ?? string.Empty;
                Assert.Contains(fakeExePath, val, StringComparison.OrdinalIgnoreCase);
            }

            // Act 2: Unregister
            bool unregistered = WindowsOptimizer.UnregisterExplorerContextMenu(_scope);
            bool isRegisteredAfterRemove = WindowsOptimizer.IsExplorerContextMenuRegistered(_scope);

            // Assert 2
            Assert.True(unregistered);
            Assert.False(isRegisteredAfterRemove);
        }

        [Fact]
        public void ClearIconCacheFilesOnly_DeletesIconCacheDbFiles_AndLeavesThumbCacheUntouched()
        {
            // Arrange
            string explorerDir = Path.Combine(_tempDir, "Microsoft", "Windows", "Explorer");
            Directory.CreateDirectory(explorerDir);

            string iconCache1 = Path.Combine(explorerDir, "iconcache_32.db");
            string iconCache2 = Path.Combine(explorerDir, "iconcache_256.db");
            string legacyIconCache = Path.Combine(_tempDir, "IconCache.db");
            string thumbCache1 = Path.Combine(explorerDir, "thumbcache_256.db");
            string thumbCache2 = Path.Combine(explorerDir, "thumbcache_1024.db");

            File.WriteAllBytes(iconCache1, [1, 2, 3]);
            File.WriteAllBytes(iconCache2, [4, 5, 6]);
            File.WriteAllBytes(legacyIconCache, [7, 8, 9]);
            File.WriteAllBytes(thumbCache1, [10, 11, 12]);
            File.WriteAllBytes(thumbCache2, [13, 14, 15]);

            // Act
            int deletedCount = WindowsOptimizer.ClearIconCacheFilesOnly(explorerDir, invokeIe4uinit: false);

            // Assert
            Assert.Equal(3, deletedCount);
            Assert.False(File.Exists(iconCache1));
            Assert.False(File.Exists(iconCache2));
            Assert.False(File.Exists(legacyIconCache));
            Assert.True(File.Exists(thumbCache1));
            Assert.True(File.Exists(thumbCache2));
        }
    }
}
