using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinThumbsPreloader.Forms;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    [Collection("NonParallelIntegration")]
    public sealed class SecurityHardeningTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly List<string> _createdJunctions = new();
        private readonly string? _originalScopeEnv;

        public SecurityHardeningTests()
        {
            _originalScopeEnv = Environment.GetEnvironmentVariable("WTP_SINGLE_INSTANCE_SCOPE");
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_SecHardening_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("WTP_SINGLE_INSTANCE_SCOPE", _originalScopeEnv);

            for (int i = _createdJunctions.Count - 1; i >= 0; i--)
            {
                string junction = _createdJunctions[i];
                try
                {
                    if (Directory.Exists(junction))
                        Directory.Delete(junction, recursive: false);
                }
                catch { }
            }

            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InnoSetup", "installer.iss")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return dir!.FullName;
        }

        [Fact]
        public void InnoSetupInstaller_ShutdownEventName_MatchesSingleInstanceCoordinatorDefaultScope()
        {
            string issPath = Path.Combine(FindRepoRoot(), "InnoSetup", "installer.iss");
            string issContent = File.ReadAllText(issPath);

            Match appNameMatch = Regex.Match(issContent, @"#define\s+MyAppName\s+""([^""]+)""");
            Match appIdMatch = Regex.Match(issContent, @"#define\s+MyAppId\s+""([^""]+)""");
            Assert.True(appNameMatch.Success, "Could not parse MyAppName from installer.iss");
            Assert.True(appIdMatch.Success, "Could not parse MyAppId from installer.iss");

            string appName = appNameMatch.Groups[1].Value;
            string appId = appIdMatch.Groups[1].Value.Replace("{{", "{");

            Match openEventMatch = Regex.Match(issContent, @"OpenEventW\s*\([^,]+,\s*[^,]+,\s*'([^']+)'\s*\)");
            Assert.True(openEventMatch.Success, "Could not find OpenEventW call in installer.iss");

            string resolvedIssEventName = openEventMatch.Groups[1].Value
                .Replace("{#MyAppName}", appName)
                .Replace("{#MyAppId}", appId);

            string expectedManagedEventName = $"Local\\{SingleInstanceCoordinator.DefaultScopeId}_Shutdown";
            Assert.Equal(expectedManagedEventName, resolvedIssEventName);
        }

        [Fact]
        public void InnoSetupInstaller_PrepareToInstall_NeverExecutesUserUninsOrDelTreesHkcuPaths_AndChecksReparsePoints()
        {
            string issPath = Path.Combine(FindRepoRoot(), "InnoSetup", "installer.iss");
            string issContent = File.ReadAllText(issPath);

            // Must never execute unins000.exe from user-writable or registry-derived candidate directories while elevated
            Assert.DoesNotContain("unins000.exe", issContent, StringComparison.OrdinalIgnoreCase);

            // Must never recursively DelTree paths read from HKCU Uninstall keys
            Assert.DoesNotContain("CheckAndRemoveUninstallLocation(HKCU", issContent, StringComparison.OrdinalIgnoreCase);

            // Must inspect FILE_ATTRIBUTE_REPARSE_POINT ($0400) via GetFileAttributesW before cleaning legacy per-user directory
            Assert.Contains("GetFileAttributesW", issContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$0400", issContent, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SingleInstanceCoordinator_GetEffectiveScopeId_RejectsDotsOverlengthAndNamespaceInjection()
        {
            Environment.SetEnvironmentVariable("WTP_SINGLE_INSTANCE_SCOPE", null);

            Assert.Equal("WTP_Valid-Scope_01", SingleInstanceCoordinator.GetEffectiveScopeId("WTP_Valid-Scope_01"));

            // Reject namespace separators, path traversal, dots, spaces, and >80 char identifiers
            Assert.Equal(SingleInstanceCoordinator.DefaultScopeId, SingleInstanceCoordinator.GetEffectiveScopeId(@"Global\EvilScope"));
            Assert.Equal(SingleInstanceCoordinator.DefaultScopeId, SingleInstanceCoordinator.GetEffectiveScopeId(@"Local\Nested"));
            Assert.Equal(SingleInstanceCoordinator.DefaultScopeId, SingleInstanceCoordinator.GetEffectiveScopeId("a..b"));
            Assert.Equal(SingleInstanceCoordinator.DefaultScopeId, SingleInstanceCoordinator.GetEffectiveScopeId("scope.with.dots"));
            Assert.Equal(SingleInstanceCoordinator.DefaultScopeId, SingleInstanceCoordinator.GetEffectiveScopeId(new string('A', 81)));

            Environment.SetEnvironmentVariable("WTP_SINGLE_INSTANCE_SCOPE", @"Global\InjectedEnv");
            Assert.Equal(SingleInstanceCoordinator.DefaultScopeId, SingleInstanceCoordinator.GetEffectiveScopeId());
        }

        [Fact]
        public void SingleInstanceCoordinator_UserAccessRights_FollowLeastPrivilegeWithoutChangePermissionsOrTakeOwnership()
        {
            string testScope = "WTP_AclCheck_" + Guid.NewGuid().ToString("N");
            using var coordinator = SingleInstanceCoordinator.TryAcquire(testScope);
            Assert.NotNull(coordinator);

            using Mutex existingMutex = Mutex.OpenExisting($"Local\\{testScope}_Mutex");
            MutexSecurity security = existingMutex.GetAccessControl();

            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            bool isElevatedAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

            if (!isElevatedAdmin && identity.User != null)
            {
                AuthorizationRuleCollection rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
                bool foundUserRule = false;
                foreach (MutexAccessRule rule in rules)
                {
                    if (rule.IdentityReference.Equals(identity.User) && rule.AccessControlType == AccessControlType.Allow)
                    {
                        foundUserRule = true;
                        Assert.Equal(MutexRights.Synchronize | MutexRights.Modify, rule.MutexRights);
                        Assert.Equal((MutexRights)0, rule.MutexRights & (MutexRights.ChangePermissions | MutexRights.TakeOwnership));
                    }
                }

                Assert.True(foundUserRule, "Expected explicit least-privilege MutexAccessRule for current user SID.");
            }
        }

        private void CreateNtfsJunction(string junctionPath, string targetPath)
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            Assert.NotNull(proc);
            proc!.WaitForExit(5000);
            Assert.Equal(0, proc.ExitCode);
            _createdJunctions.Add(junctionPath);
        }

        [Fact]
        public async Task DirectoryScanner_WithCyclicNtfsJunction_TerminatesWithoutInfiniteLoopInSequentialAndParallelModes()
        {
            string scanRoot = Path.Combine(_tempDir, "ScanRoot");
            string nestedDir = Path.Combine(scanRoot, "Nested");
            string externalTarget = Path.Combine(_tempDir, "ExternalMountTarget");
            Directory.CreateDirectory(nestedDir);
            Directory.CreateDirectory(externalTarget);

            File.WriteAllBytes(Path.Combine(scanRoot, "root_image.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
            File.WriteAllBytes(Path.Combine(externalTarget, "mounted_image.png"), [0x89, 0x50, 0x4E, 0x47]);

            string cyclicJunction = Path.Combine(nestedDir, "LoopBackToRoot");
            string validMountJunction = Path.Combine(scanRoot, "MountedDriveFolder");
            CreateNtfsJunction(cyclicJunction, scanRoot);
            CreateNtfsJunction(validMountJunction, externalTarget);

            // 1. Sequential nested scan
            var seqScanner = new DirectoryScanner(scanRoot, includeNestedDirectories: true, multiThreaded: false, threadCount: 1);
            Task<ScannedItem[]> seqTask = Task.Run(() => seqScanner.GetScannedItems());
            Task completedSeq = await Task.WhenAny(seqTask, Task.Delay(TimeSpan.FromSeconds(4)));
            if (completedSeq != seqTask)
            {
                seqScanner.cancelled = true;
                await Task.WhenAny(seqTask, Task.Delay(TimeSpan.FromSeconds(2)));
                Assert.Fail("Sequential DirectoryScanner hung in an infinite loop on a cyclic NTFS junction.");
            }

            ScannedItem[] seqItems = await seqTask;
            Assert.Contains(seqItems, i => i.FullPath.EndsWith("root_image.jpg", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(seqItems, i => i.FullPath.EndsWith("mounted_image.png", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, seqItems.Count(i => i.FullPath.EndsWith("root_image.jpg", StringComparison.OrdinalIgnoreCase)));

            // 2. Parallel nested scan
            var parScanner = new DirectoryScanner(scanRoot, includeNestedDirectories: true, multiThreaded: true, threadCount: 4);
            Task<ScannedItem[]> parTask = Task.Run(() => parScanner.GetScannedItems());
            Task completedPar = await Task.WhenAny(parTask, Task.Delay(TimeSpan.FromSeconds(4)));
            if (completedPar != parTask)
            {
                parScanner.cancelled = true;
                await Task.WhenAny(parTask, Task.Delay(TimeSpan.FromSeconds(2)));
                Assert.Fail("Parallel DirectoryScanner hung in an infinite loop on a cyclic NTFS junction.");
            }

            ScannedItem[] parItems = await parTask;
            Assert.Contains(parItems, i => i.FullPath.EndsWith("root_image.jpg", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(parItems, i => i.FullPath.EndsWith("mounted_image.png", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, parItems.Count(i => i.FullPath.EndsWith("root_image.jpg", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public void SanitizeThumbCacheDirectory_WhenIdxExceeds256MbCap_FailsClosedWithoutAllocatingBuffer()
        {
            string cacheDir = Path.Combine(_tempDir, "OversizedCache");
            Directory.CreateDirectory(cacheDir);
            string idxPath = Path.Combine(cacheDir, "thumbcache_idx.db");

            using (var fs = new FileStream(idxPath, FileMode.Create, FileAccess.Write))
            {
                byte[] header = new byte[256];
                header[4] = (byte)'I';
                header[5] = (byte)'M';
                header[6] = (byte)'M';
                header[7] = (byte)'M';
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24, 4), 1u);
                fs.Write(header);
                fs.SetLength(260L * 1024 * 1024); // 260 MB > 256 MB safety cap
            }

            long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            bool modified = CacheForm.SanitizeThumbCacheDirectory(cacheDir);
            long allocatedDelta = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;

            Assert.False(modified);
            Assert.True(allocatedDelta < 16L * 1024 * 1024, $"Expected bounded memory usage (<16 MB), but allocated {allocatedDelta / (1024 * 1024)} MB.");
        }

        [Fact]
        public void AutoUpdater_PostUpdateMarker_RefusesJunctionDirectoryAndOversizedFile()
        {
            string victimDir = Path.Combine(_tempDir, "VictimMarkerDir");
            Directory.CreateDirectory(victimDir);
            string victimMarker = Path.Combine(victimDir, ".post_update_launch");
            File.WriteAllText(victimMarker, $"minimized|{DateTime.UtcNow.Ticks}", Encoding.UTF8);

            string junctionDir = Path.Combine(_tempDir, "JunctionMarkerDir");
            CreateNtfsJunction(junctionDir, victimDir);
            string markerThroughJunction = Path.Combine(junctionDir, ".post_update_launch");

            // Must refuse to read or delete through a reparse point directory
            Assert.False(AutoUpdater.ConsumePostUpdateLaunchMinimized(markerThroughJunction));
            Assert.True(File.Exists(victimMarker), "Marker inside junction target must not be deleted through reparse point.");

            AutoUpdater.DeletePostUpdateLaunchMarker(markerThroughJunction);
            Assert.True(File.Exists(victimMarker), "DeletePostUpdateLaunchMarker must not delete through reparse point.");

            // Oversized marker (> 4 KB) in a normal directory must be rejected safely
            string normalDir = Path.Combine(_tempDir, "NormalMarkerDir");
            Directory.CreateDirectory(normalDir);
            string oversizedMarker = Path.Combine(normalDir, ".post_update_launch");
            File.WriteAllText(oversizedMarker, $"minimized|{DateTime.UtcNow.Ticks}|" + new string('X', 8192), Encoding.UTF8);

            Assert.False(AutoUpdater.ConsumePostUpdateLaunchMinimized(oversizedMarker));
        }

        [Fact]
        public void CacheFormAndOptimizer_RefuseToWriteOrDeleteThroughNtfsJunction()
        {
            string victimTarget = Path.Combine(_tempDir, "VictimExplorerDir");
            Directory.CreateDirectory(victimTarget);

            string victimThumbDb = Path.Combine(victimTarget, "thumbcache_256.db");
            string victimIconDb = Path.Combine(victimTarget, "iconcache_32.db");
            File.WriteAllText(victimThumbDb, "ORIGINAL_THUMBCACHE");
            File.WriteAllText(victimIconDb, "ORIGINAL_ICONCACHE");

            string sourceDb = Path.Combine(_tempDir, "source_thumbcache_256.db");
            File.WriteAllText(sourceDb, "OVERWRITTEN_PAYLOAD");

            string junctionExplorer = Path.Combine(_tempDir, "JunctionExplorerDir");
            CreateNtfsJunction(junctionExplorer, victimTarget);

            // 1. WindowsOptimizer.ClearIconCacheFilesOnly must refuse to delete through a junction directory
            int deleted = WindowsOptimizer.ClearIconCacheFilesOnly(junctionExplorer, invokeIe4uinit: false);
            Assert.Equal(0, deleted);
            Assert.True(File.Exists(victimIconDb));

            // 2. CacheForm.CopyFileToDestinationSharedReadWrite must throw IOException rather than overwriting through a junction
            Assert.Throws<IOException>(() =>
                CacheForm.CopyFileToDestinationSharedReadWrite(sourceDb, Path.Combine(junctionExplorer, "thumbcache_256.db")));
            Assert.Equal("ORIGINAL_THUMBCACHE", File.ReadAllText(victimThumbDb));
        }

        [Fact]
        public void SettingsMigration_RejectsDtdPayloadOversizedXmlAndJunctionConfig()
        {
            int originalThreadCount = Properties.Settings.Default.ThreadCount;
            try
            {
                string settingsDir = Path.Combine(_tempDir, "SettingsMigration");
                Directory.CreateDirectory(settingsDir);

                // 0. Valid XML must still migrate normally
                string validXmlPath = Path.Combine(settingsDir, "valid_user.config");
                File.WriteAllText(validXmlPath, """
                    <?xml version="1.0" encoding="utf-8"?>
                    <configuration>
                      <userSettings>
                        <WinThumbsPreloader.Properties.Settings>
                          <setting name="ThreadCount" serializeAs="String">
                            <value>6</value>
                          </setting>
                        </WinThumbsPreloader.Properties.Settings>
                      </userSettings>
                    </configuration>
                    """, Encoding.UTF8);

                Assert.Equal(1, Properties.Settings.Default.ApplyUserConfigXmlFile(validXmlPath));
                Assert.Equal(6, Properties.Settings.Default.ThreadCount);

                // 1. DTD / XXE payload must be rejected (0 applied)
                string dtdXmlPath = Path.Combine(settingsDir, "dtd_user.config");
                File.WriteAllText(dtdXmlPath, """
                    <?xml version="1.0" encoding="utf-8"?>
                    <!DOCTYPE configuration [
                      <!ENTITY injected "999">
                    ]>
                    <configuration>
                      <userSettings>
                        <WinThumbsPreloader.Properties.Settings>
                          <setting name="ThreadCount" serializeAs="String">
                            <value>7</value>
                          </setting>
                        </WinThumbsPreloader.Properties.Settings>
                      </userSettings>
                    </configuration>
                    """, Encoding.UTF8);

                Assert.Equal(0, Properties.Settings.Default.ApplyUserConfigXmlFile(dtdXmlPath));

                // 2. Oversized (> 1 MB) XML file must be rejected before parsing
                string oversizedXmlPath = Path.Combine(settingsDir, "oversized_user.config");
                string padding = new string('A', 1_100_000);
                File.WriteAllText(oversizedXmlPath, $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <configuration>
                      <!-- {padding} -->
                      <userSettings>
                        <WinThumbsPreloader.Properties.Settings>
                          <setting name="ThreadCount" serializeAs="String">
                            <value>7</value>
                          </setting>
                        </WinThumbsPreloader.Properties.Settings>
                      </userSettings>
                    </configuration>
                    """, Encoding.UTF8);

                Assert.Equal(0, Properties.Settings.Default.ApplyUserConfigXmlFile(oversizedXmlPath));

                // 3. Config file inside an NTFS junction version directory must be skipped during migration
                string victimConfigDir = Path.Combine(_tempDir, "VictimConfigVersionDir");
                Directory.CreateDirectory(victimConfigDir);
                File.WriteAllText(Path.Combine(victimConfigDir, "user.config"), """
                    <?xml version="1.0" encoding="utf-8"?>
                    <configuration>
                      <userSettings>
                        <WinThumbsPreloader.Properties.Settings>
                          <setting name="ThreadCount" serializeAs="String">
                            <value>11</value>
                          </setting>
                        </WinThumbsPreloader.Properties.Settings>
                      </userSettings>
                    </configuration>
                    """, Encoding.UTF8);

                string appConfigRoot = Path.Combine(_tempDir, "MockLocalAppDataWTP");
                string hashDir = Path.Combine(appConfigRoot, "WinThumbsPreloader_Url_test");
                Directory.CreateDirectory(hashDir);
                string junctionVersionDir = Path.Combine(hashDir, "3.1.0.0");
                CreateNtfsJunction(junctionVersionDir, victimConfigDir);

                int migratedFromJunction = Properties.Settings.Default.MigrateFromPreviousUserConfigs(appConfigRoot, new Version(3, 2, 0, 0));
                Assert.Equal(0, migratedFromJunction);
            }
            finally
            {
                Properties.Settings.Default.ThreadCount = originalThreadCount;
            }
        }

        [Fact]
        public void OptimizationProfile_CliFlags_RoundTripAcrossElevatedHelperArguments()
        {
            var sourceProfile = new WindowsOptimizer.OptimizationProfile
            {
                EnforceOnStartup = false,
                PreventThumbCacheCleanup = true,
                ClearDiskCleanupFlags = false,
                DisableSilentCleanupTask = true,
                DisableNetworkThumbsDb = true,
                ForceThumbnailPreviews = false,
                DisableFolderTypeDiscovery = true,
                ExpandBagMruSize = false,
                ExpandIconCache = true,
                IconCacheSizeKb = 16384
            };

            string cliArgs = sourceProfile.BuildElevatedCliArguments();
            string[] tokens = cliArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var parsedOptions = new Options(tokens);
            Assert.True(parsedOptions.applyOptimizations);
            Assert.False(parsedOptions.IsGuiLaunch);
            Assert.False(parsedOptions.unknownFlagProvided);
            Assert.True(parsedOptions.hasExplicitOptimizationProfile);

            var reconstructed = WindowsOptimizer.OptimizationProfile.FromBitMask(
                parsedOptions.optimizationProfileMask,
                parsedOptions.optimizationIconCacheKb);

            Assert.Equal(sourceProfile.EnforceOnStartup, reconstructed.EnforceOnStartup);
            Assert.Equal(sourceProfile.PreventThumbCacheCleanup, reconstructed.PreventThumbCacheCleanup);
            Assert.Equal(sourceProfile.ClearDiskCleanupFlags, reconstructed.ClearDiskCleanupFlags);
            Assert.Equal(sourceProfile.DisableSilentCleanupTask, reconstructed.DisableSilentCleanupTask);
            Assert.Equal(sourceProfile.DisableNetworkThumbsDb, reconstructed.DisableNetworkThumbsDb);
            Assert.Equal(sourceProfile.ForceThumbnailPreviews, reconstructed.ForceThumbnailPreviews);
            Assert.Equal(sourceProfile.DisableFolderTypeDiscovery, reconstructed.DisableFolderTypeDiscovery);
            Assert.Equal(sourceProfile.ExpandBagMruSize, reconstructed.ExpandBagMruSize);
            Assert.Equal(sourceProfile.ExpandIconCache, reconstructed.ExpandIconCache);
            Assert.Equal(16384, reconstructed.IconCacheSizeKb);

            // Out-of-bounds icon cache size must clamp safely to [1024, 65536]
            var clamped = WindowsOptimizer.OptimizationProfile.FromBitMask(sourceProfile.ToBitMask(), 999999);
            Assert.Equal(65536, clamped.IconCacheSizeKb);

            // Bare -apply-optimizations remains backward-compatible
            var legacyOptions = new Options(["-apply-optimizations"]);
            Assert.True(legacyOptions.applyOptimizations);
            Assert.False(legacyOptions.hasExplicitOptimizationProfile);
        }

        [Fact]
        public void BuildWorkflowAndCsproj_EnforceLockedRestoreNuGetAuditAndNoComInteropRegistration()
        {
            string repoRoot = FindRepoRoot();
            string workflowContent = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "build-and-release.yml"));
            string csprojContent = File.ReadAllText(Path.Combine(repoRoot, "WinThumbsPreloader", "WinThumbsPreloader", "WinThumbsPreloader.csproj"));

            Assert.DoesNotContain("--force-evaluate", workflowContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DOTNET_CLI_UI_LANGUAGE", workflowContent, StringComparison.Ordinal);
            Assert.Contains("Get-AuthenticodeSignature", workflowContent, StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain("<RegisterForComInterop>true</RegisterForComInterop>", csprojContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<NuGetAudit>true</NuGetAudit>", csprojContent, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LockFiles_DoNotCoupleToFloatingSdkServicingPacks()
        {
            string repoRoot = FindRepoRoot();
            string csprojContent = File.ReadAllText(Path.Combine(repoRoot, "WinThumbsPreloader", "WinThumbsPreloader", "WinThumbsPreloader.csproj"));
            string appLockFile = File.ReadAllText(Path.Combine(repoRoot, "WinThumbsPreloader", "WinThumbsPreloader", "packages.lock.json"));
            string testLockFile = File.ReadAllText(Path.Combine(repoRoot, "WinThumbsPreloader", "WinThumbsPreloader.Tests", "packages.lock.json"));

            // Inno Setup packages the multi-file Release\win-x64 output directory directly.
            // Keeping PublishSingleFile enabled triggers EnableSingleFileAnalyzer -> _RequiresILLinkPack,
            // injecting an SDK-servicing-bound Microsoft.NET.ILLink.Tasks entry into packages.lock.json
            // that breaks `dotnet restore --locked-mode` (NU1004) whenever CI advances to a newer .NET 10.0.x patch SDK.
            Assert.DoesNotContain("<PublishSingleFile>true</PublishSingleFile>", csprojContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Microsoft.NET.ILLink.Tasks", appLockFile, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Microsoft.DotNet.ILCompiler", appLockFile, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Microsoft.NET.ILLink.Tasks", testLockFile, StringComparison.OrdinalIgnoreCase);
        }
    }
}
