using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinThumbsPreloader.Forms;
using WinThumbsPreloader.Properties;
using WinThumbsPreloader.Services;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    [Collection("NonParallelIntegration")]
    public sealed class WebBridgeAndServicesTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly List<string> _createdJunctions = new();
        private readonly string? _originalUdfEnv;
        private readonly string _originalTheme;
        private readonly string _originalBackupFolder;
        private readonly bool _originalAutoBackup;
        private readonly bool _originalAutoRestore;

        public WebBridgeAndServicesTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_WebBridgeTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _originalUdfEnv = Environment.GetEnvironmentVariable("WTP_WEBVIEW2_UDF");
            _originalTheme = ThemeHelper.GetSavedTheme();
            _originalBackupFolder = Settings.Default.BackupCacheFolderPath;
            _originalAutoBackup = Settings.Default.AutoBackupThumbs;
            _originalAutoRestore = Settings.Default.AutoRestoreThumbs;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("WTP_WEBVIEW2_UDF", _originalUdfEnv);
            ThemeHelper.SaveTheme(_originalTheme);

            Settings.Default.BackupCacheFolderPath = _originalBackupFolder;
            Settings.Default.AutoBackupThumbs = _originalAutoBackup;
            Settings.Default.AutoRestoreThumbs = _originalAutoRestore;
            Settings.Default.Save();

            for (int i = _createdJunctions.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (Directory.Exists(_createdJunctions[i]))
                        Directory.Delete(_createdJunctions[i], recursive: false);
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

        private static WebBridge CreateBridge() => new(
            new CacheService(),
            new PreloadService(),
            new ScheduleService(),
            new SettingsService(),
            new OptimizerService(),
            new CodecsService(),
            new UpdateService());

        [Theory]
        [InlineData("https://app.winthumbspreloader.local/index.html", true)]
        [InlineData("https://app.winthumbspreloader.local/", true)]
        [InlineData("https://APP.WINTHUMBSPRELOADER.LOCAL/cache", true)]
        [InlineData("http://app.winthumbspreloader.local/index.html", false)]
        [InlineData("https://app.winthumbspreloader.local:8443/index.html", false)]
        [InlineData("https://user@app.winthumbspreloader.local/index.html", false)]
        [InlineData("https://evil.example.com/index.html", false)]
        [InlineData("file:///C:/index.html", false)]
        [InlineData("about:blank", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void WebBridge_IsTrustedWebOrigin_EnforcesHttpsAndExactVirtualHost(string? origin, bool expected)
        {
            Assert.Equal(expected, WebBridge.IsTrustedWebOrigin(origin));
        }

        [Fact]
        public async Task WebBridge_RejectsUntrustedOriginOversizedPayloadDeepJsonUnknownCommandAndUntrustedUrl()
        {
            WebBridge bridge = CreateBridge();

            // 1. Untrusted origin
            string untrustedResp = await bridge.HandleMessageJsonAsync(
                """{"id":"1","cmd":"app.getBootstrap","payload":{}}""",
                "https://evil.com/index.html");
            using (JsonDocument doc = JsonDocument.Parse(untrustedResp))
            {
                Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("untrusted_origin", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            // 2. Oversized (> 256 KB) payload
            string bigString = new string('x', 270_000);
            string oversizedResp = await bridge.HandleMessageJsonAsync(
                $"{{\"id\":\"2\",\"cmd\":\"app.getBootstrap\",\"payload\":{{\"pad\":\"{bigString}\"}}}}",
                WebBridge.TrustedOrigin + "/index.html");
            using (JsonDocument doc = JsonDocument.Parse(oversizedResp))
            {
                Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("payload_too_large", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            // 3. JSON depth > 16
            var sb = new StringBuilder("{\"id\":\"3\",\"cmd\":\"app.getBootstrap\",\"payload\":");
            for (int i = 0; i < 20; i++) sb.Append("{\"n\":");
            sb.Append("1");
            for (int i = 0; i < 20; i++) sb.Append('}');
            sb.Append('}');
            string deepResp = await bridge.HandleMessageJsonAsync(sb.ToString(), WebBridge.TrustedOrigin + "/index.html");
            using (JsonDocument doc = JsonDocument.Parse(deepResp))
            {
                Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("invalid_json", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            // 4. Unknown command
            string unknownCmdResp = await bridge.HandleMessageJsonAsync(
                """{"id":"4","cmd":"shell.execArbitrary","payload":{}}""",
                WebBridge.TrustedOrigin + "/index.html");
            using (JsonDocument doc = JsonDocument.Parse(unknownCmdResp))
            {
                Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("unknown_command", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            // 5. Untrusted external URL
            string badUrlResp = await bridge.HandleMessageJsonAsync(
                """{"id":"5","cmd":"app.openExternalUrl","payload":{"url":"https://evil.example.com/payload.exe"}}""",
                WebBridge.TrustedOrigin + "/index.html");
            using (JsonDocument doc = JsonDocument.Parse(badUrlResp))
            {
                Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("untrusted_url", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }
        }

        [Fact]
        public async Task WebBridge_BootstrapThemeAndDragDropResolve_WorkEndToEnd()
        {
            WebBridge bridge = CreateBridge();
            string origin = WebBridge.TrustedOrigin + "/index.html";

            string bootResp = await bridge.HandleMessageJsonAsync(
                """{"id":"b1","cmd":"app.getBootstrap","payload":{}}""",
                origin);
            using (JsonDocument doc = JsonDocument.Parse(bootResp))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                JsonElement data = doc.RootElement.GetProperty("data");
                Assert.Equal("WinThumbsPreloader", data.GetProperty("appName").GetString());
                Assert.True(data.GetProperty("isWebView2").GetBoolean());
            }

            string themeResp = await bridge.HandleMessageJsonAsync(
                """{"id":"t1","cmd":"app.setTheme","payload":{"theme":"light"}}""",
                origin);
            using (JsonDocument doc = JsonDocument.Parse(themeResp))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("light", doc.RootElement.GetProperty("data").GetProperty("theme").GetString());
                Assert.False(doc.RootElement.GetProperty("data").GetProperty("effectiveDarkMode").GetBoolean());
            }

            string missingDir = Path.Combine(_tempDir, "DoesNotExist");
            string dropResp = await bridge.HandleMessageJsonAsync(
                """{"id":"d1","cmd":"dragDrop.resolve","payload":{}}""",
                origin,
                droppedPaths: [_tempDir, missingDir]);
            using (JsonDocument doc = JsonDocument.Parse(dropResp))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                JsonElement data = doc.RootElement.GetProperty("data");
                JsonElement items = data.GetProperty("items");
                Assert.Equal(2, items.GetArrayLength());
                Assert.True(items[0].GetProperty("valid").GetBoolean());
                Assert.False(items[1].GetProperty("valid").GetBoolean());

                JsonElement validPaths = data.GetProperty("paths");
                Assert.Equal(1, validPaths.GetArrayLength());
                Assert.Equal(Path.GetFullPath(_tempDir), validPaths[0].GetString());
            }
        }

        [Fact]
        public void SingleInstanceCoordinator_ReopenCodecsSignal_IsDeliveredToPrimary()
        {
            string scopeId = "WTP_ReopenCodecsTest_" + Guid.NewGuid().ToString("N");
            using var signal = new ManualResetEventSlim(false);
            SingleInstanceActivationKind received = SingleInstanceActivationKind.None;

            using var primary = SingleInstanceCoordinator.TryAcquire(scopeId, kind =>
            {
                received = kind;
                signal.Set();
            });
            Assert.NotNull(primary);

            bool acquiredSecondary = SingleInstanceCoordinator.TryAcquireOrNotifyExisting(
                new Options(["-reopencodecs"]),
                out var secondary,
                onActivate: null,
                scopeId: scopeId);

            Assert.False(acquiredSecondary);
            Assert.Null(secondary);
            Assert.True(signal.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(SingleInstanceActivationKind.ReopenCodecs, received);
        }

        [Fact]
        public void Logger_NonMessageBoxValidation_AcceptsWritableAndRejectsJunction()
        {
            string writableSub = Path.Combine(_tempDir, "LogsValid");
            bool valid = Logger.TryValidateLogFolderPath(
                writableSub,
                confirmAdminWarning: false,
                out string normalized,
                out string message,
                out bool requiresConfirm);

            Assert.True(valid, message);
            Assert.False(requiresConfirm);
            Assert.Equal(Path.GetFullPath(writableSub), normalized);
            Assert.True(Directory.Exists(normalized));

            string victimTarget = Path.Combine(_tempDir, "LogsTarget");
            Directory.CreateDirectory(victimTarget);
            string junctionLogs = Path.Combine(_tempDir, "LogsJunction");
            CreateNtfsJunction(junctionLogs, victimTarget);

            bool junctionAllowed = Logger.TryValidateLogFolderPath(
                junctionLogs,
                confirmAdminWarning: true,
                out _,
                out _,
                out _);
            Assert.False(junctionAllowed);
        }

        [Fact]
        public void CacheService_SetBackupFolderAndGetSnapshot_WorkWithoutDialogsAndTrackBuckets()
        {
            var svc = new CacheService();

            string backupDir = Path.Combine(_tempDir, "BackupA");
            var setResult = svc.SetBackupFolder(backupDir, confirmAdminWarning: true);
            Assert.True(setResult.Ok);
            Assert.Equal(Path.GetFullPath(backupDir), setResult.Path);

            Settings.Default.AutoBackupThumbs = true;
            Settings.Default.AutoRestoreThumbs = true;
            Settings.Default.Save();

            // Changing backup path disables auto backup/restore toggles cleanly
            string backupDirB = Path.Combine(_tempDir, "BackupB");
            var changeResult = svc.SetBackupFolder(backupDirB, confirmAdminWarning: true);
            Assert.True(changeResult.Ok);
            Assert.True(changeResult.DisabledAutoToggles);
            Assert.False(Settings.Default.AutoBackupThumbs);
            Assert.False(Settings.Default.AutoRestoreThumbs);

            // Clearing backup path clears setting
            var clearResult = svc.SetBackupFolder(string.Empty);
            Assert.True(clearResult.Ok);
            Assert.Equal(string.Empty, clearResult.Path);

            CacheSnapshot snap = svc.GetSnapshot();
            Assert.Equal(14, snap.Buckets.Count);
            Assert.Contains(snap.Buckets, b => b.Size == "256" && b.SoftLimitBytes == CacheForm.SingleDbSoftLimitBytes);
            Assert.Contains(snap.Buckets, b => b.Size == "sr");
        }

        [Fact]
        public void PreloadService_PresetsAndSecurityGuards_SaveLoadDeleteAndRejectOversizedOrJunction()
        {
            var svc = new PreloadService();
            string presetsPath = Path.Combine(_tempDir, "presets.json");

            var saveRes = svc.SavePreset(new BulkPreloaderForm.BulkPreset
            {
                Name = "Studio RAWs",
                Paths = [_tempDir],
                Recursive = true,
                MultiThreaded = true,
                Threads = 4,
                Method = "Simultaneously",
                MaximumInstances = 2
            }, presetsPath);

            Assert.True(saveRes.Ok);
            Assert.Single(saveRes.SavedPresets);
            Assert.Equal("Studio RAWs", saveRes.SavedPresets[0].Name);

            var loaded = PreloadService.LoadPresetsFromDisk(presetsPath);
            Assert.Single(loaded);
            Assert.Equal(2, loaded[0].MaximumInstances);

            var delRes = svc.DeletePreset("studio raws", presetsPath);
            Assert.True(delRes.Ok);
            Assert.Empty(delRes.SavedPresets);

            // Junction guard
            string realDir = Path.Combine(_tempDir, "RealPresetDir");
            Directory.CreateDirectory(realDir);
            string realPreset = Path.Combine(realDir, "p.json");
            File.WriteAllText(realPreset, """[{"Name":"ShouldNotLoad"}]""");
            string junctionDir = Path.Combine(_tempDir, "JunctionPresetDir");
            CreateNtfsJunction(junctionDir, realDir);

            Assert.Empty(PreloadService.LoadPresetsFromDisk(Path.Combine(junctionDir, "p.json")));
        }

        [Fact]
        public void SettingsService_OrganizeReadExtensionsAndLicense_EnforceLimitsAndFormats()
        {
            var svc = new SettingsService();

            Assert.Equal("jpg, png, psd", svc.OrganizeExtensions("jpg  png\r\npsd", "CommasAndSpaces"));
            Assert.Equal("jpg,png,psd", svc.OrganizeExtensions("jpg png psd", "Commas"));
            Assert.Equal("jpg png psd", svc.OrganizeExtensions("jpg,png,psd", "Spaces"));

            string longExtFile = Path.Combine(_tempDir, "ext.txt");
            File.WriteAllText(longExtFile, new string('a', 12000));
            var importRes = svc.ReadExtensionsFile(longExtFile);
            Assert.True(importRes.Ok);
            Assert.Equal(SettingsService.MaxExtensionsTextChars, importRes.Text.Length);

            var state = svc.GetState();
            Assert.Contains("MIT License", state.About.LicenseText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(SettingsService.DefaultExtensions, state.Extensions.DefaultExtensionsText);
        }

        [Fact]
        public void OptimizerAndCodecsServices_ReturnCompleteStructuredCatalogs()
        {
            var optSvc = new OptimizerService();
            OptimizerStateDto optState = optSvc.GetState();
            Assert.Equal(9, optState.Tweaks.Count);
            Assert.Equal(4, optState.ShellTools.Count);
            Assert.All(optState.Tweaks, t =>
            {
                Assert.False(string.IsNullOrWhiteSpace(t.Id));
                Assert.False(string.IsNullOrWhiteSpace(t.WhatItDoes));
                Assert.False(string.IsNullOrWhiteSpace(t.Pros));
                Assert.False(string.IsNullOrWhiteSpace(t.Cons));
                Assert.Contains(t.Tab, new[] { "cacheNetwork", "explorer", "shell" });
            });

            var codecsSvc = new CodecsService();
            CodecsStateDto codecsState = codecsSvc.GetState();
            Assert.Equal(10, codecsState.Families.Count);
            Assert.Equal(Enumerable.Range(0, 10), codecsState.Families.Select(f => f.BitIndex));
        }

        [Fact]
        public void MainShellForm_GetWebView2UserDataFolder_RespectsEnvAndRejectsJunction()
        {
            string validUdf = Path.Combine(_tempDir, "WebView2Udf");
            Environment.SetEnvironmentVariable("WTP_WEBVIEW2_UDF", validUdf);
            string resolved = MainShellForm.GetWebView2UserDataFolder();
            Assert.Equal(Path.GetFullPath(validUdf), resolved);
            Assert.True(Directory.Exists(resolved));

            string realUdfTarget = Path.Combine(_tempDir, "UdfTarget");
            Directory.CreateDirectory(realUdfTarget);
            string junctionUdf = Path.Combine(_tempDir, "UdfJunction");
            CreateNtfsJunction(junctionUdf, realUdfTarget);

            Environment.SetEnvironmentVariable("WTP_WEBVIEW2_UDF", junctionUdf);
            Assert.Throws<IOException>(() => MainShellForm.GetWebView2UserDataFolder());
        }

        [Fact]
        public void ScheduleService_ParseAndConstructArguments_HandlesCapturingAndWhitespaceAndControlChars()
        {
            var drives = new List<string>();
            var customPaths = new List<string>();
            ScheduleService.ParseArguments(
                @"-r -s ""D:\Photos"" ""D:\my-media\clip"" ""\\NAS\Media""",
                out bool multi1,
                out int threads1,
                drives,
                customPaths,
                driveSelectionEnabled: true);

            Assert.False(multi1);
            Assert.Equal(0, threads1);
            Assert.Empty(drives);
            Assert.Equal(2, customPaths.Count(p => p.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(@"\\NAS\", customPaths);

            string? built = ScheduleService.ConstructArguments(
                multithreaded: true,
                threads: 4,
                drives: ["C"],
                customPaths: ["C:\\Space Folder", "C:\\Bad\t-flag", "-evilFlag"]);

            Assert.NotNull(built);
            Assert.Contains("-m:4", built, StringComparison.Ordinal);
            Assert.Contains("\"C:\\Space Folder\"", built, StringComparison.Ordinal);
            Assert.DoesNotContain("-flag", built, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("-evilFlag", built, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SettingsService_SaveGeneral_PreservesZeroWaitDelays()
        {
            bool prevWaitPreload = Settings.Default.WaitAfterPreloading;
            int prevWaitPreloadTime = Settings.Default.WaitTimeAfterPreloading;
            string prevWaitPreloadUnit = Settings.Default.WaitAfterPreloadingUnit;
            bool prevWaitCache = Settings.Default.WaitAfterCacheBackup;
            int prevWaitCacheTime = Settings.Default.WaitTimeAfterCacheBackup;
            string prevWaitCacheUnit = Settings.Default.WaitAfterCacheUnit;

            try
            {
                var svc = new SettingsService();
                var res = svc.SaveGeneral(new GeneralSettingsSaveRequest
                {
                    Multithreaded = true,
                    ThreadCount = 0,
                    PreloaderThumbnailSizes = "96,256",
                    PreloaderProcessPriority = "Below Normal",
                    ProgressDialogUpdateSpeed = 250,
                    WaitAfterPreloading = true,
                    WaitTimeAfterPreloading = 0,
                    WaitAfterPreloadingUnit = "Seconds",
                    WaitAfterCacheBackup = true,
                    WaitTimeAfterCacheBackup = 0,
                    WaitAfterCacheUnit = "Seconds"
                });

                Assert.True(res.Ok);
                Assert.NotNull(res.State);
                Assert.Equal(0, res.State!.General.WaitTimeAfterPreloading);
                Assert.Equal(0, res.State.General.WaitTimeAfterCacheBackup);
            }
            finally
            {
                Settings.Default.WaitAfterPreloading = prevWaitPreload;
                Settings.Default.WaitTimeAfterPreloading = prevWaitPreloadTime;
                Settings.Default.WaitAfterPreloadingUnit = prevWaitPreloadUnit;
                Settings.Default.WaitAfterCacheBackup = prevWaitCache;
                Settings.Default.WaitTimeAfterCacheBackup = prevWaitCacheTime;
                Settings.Default.WaitAfterCacheUnit = prevWaitCacheUnit;
                Settings.Default.Save();
            }
        }

        [Fact]
        public void CacheFormAndLogger_StatusProbes_RejectJunctionWithoutProbeWrite()
        {
            string probeTarget = Path.Combine(_tempDir, "EmptyTarget");
            Directory.CreateDirectory(probeTarget);
            string junctionPath = Path.Combine(_tempDir, "JunctionProbe");
            CreateNtfsJunction(junctionPath, probeTarget);

            var cacheStatus = CacheForm.GetBackupFolderStatusInfo(junctionPath);
            Assert.False(cacheStatus.CanUseForBackup);
            Assert.Equal(CacheForm.BackupFolderStatus.InvalidPath, cacheStatus.Status);

            var logStatus = Logger.GetLogFolderStatusInfo(junctionPath);
            Assert.False(logStatus.CanUseForLogging);
            Assert.Equal(Logger.LogFolderStatus.InvalidPath, logStatus.Status);

            Assert.Empty(Directory.GetFileSystemEntries(probeTarget));
        }
    }
}
