using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    public sealed class AutoUpdaterTests : IDisposable
    {
        private readonly string _tempDir;

        public AutoUpdaterTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_AutoUpdaterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
            }
        }

        private static byte[] CreateValidMockPePayload(int size = 2048)
        {
            byte[] payload = new byte[size];
            payload[0] = (byte)'M';
            payload[1] = (byte)'Z';
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x3C, 4), 0x80);
            payload[0x80] = (byte)'P';
            payload[0x81] = (byte)'E';
            payload[0x82] = 0;
            payload[0x83] = 0;
            return payload;
        }

        [Fact]
        public void ParseReleasesJson_WhenSameVersion_ReturnsUpdated()
        {
            string json = """
            [
              {
                "tag_name": "v3.0.0",
                "draft": false,
                "prerelease": false,
                "html_url": "https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/tag/v3.0.0",
                "assets": [
                  {
                    "name": "WinThumbsPreloader-3.0.0-setup.exe",
                    "size": 4500000,
                    "browser_download_url": "https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/download/v3.0.0/WinThumbsPreloader-3.0.0-setup.exe"
                  }
                ]
              }
            ]
            """;

            var result = AutoUpdater.ParseReleasesJson(json, "3.0.0");

            Assert.Equal(UpdateState.Updated, result.State);
            Assert.False(result.IsUpdateAvailable);
            Assert.Equal("3.0.0", result.LatestVersion);
            Assert.True(result.HasInstallerAsset);
        }

        [Fact]
        public void ParseReleasesJson_WhenNewerVersionAvailable_ExtractsInstallerAssetAndDigest()
        {
            string expectedHash = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";
            string json = $$"""
            [
              {
                "tag_name": "v3.1.0",
                "draft": false,
                "prerelease": false,
                "html_url": "https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/tag/v3.1.0",
                "assets": [
                  {
                    "name": "WinThumbsPreloader-3.1.0-portable.zip",
                    "size": 1200000,
                    "browser_download_url": "https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/download/v3.1.0/WinThumbsPreloader-3.1.0-portable.zip"
                  },
                  {
                    "name": "WinThumbsPreloader-3.1.0-setup.exe",
                    "size": 4812345,
                    "digest": "sha256:{{expectedHash}}",
                    "browser_download_url": "https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/download/v3.1.0/WinThumbsPreloader-3.1.0-setup.exe"
                  }
                ]
              }
            ]
            """;

            var result = AutoUpdater.ParseReleasesJson(json, "3.0.0");

            Assert.Equal(UpdateState.UpdateAvailable, result.State);
            Assert.True(result.IsUpdateAvailable);
            Assert.Equal("3.1.0", result.LatestVersion);
            Assert.Equal("WinThumbsPreloader-3.1.0-setup.exe", result.InstallerFileName);
            Assert.Equal(4812345, result.InstallerSize);
            Assert.Equal(expectedHash, result.InstallerSha256);
            Assert.Equal(
                "https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/download/v3.1.0/WinThumbsPreloader-3.1.0-setup.exe",
                result.InstallerDownloadUrl);
        }

        [Fact]
        public void ParseReleasesJson_SkipsDraftReleases()
        {
            string json = """
            [
              {
                "tag_name": "v3.2.0",
                "draft": true,
                "prerelease": false
              },
              {
                "tag_name": "v3.0.0",
                "draft": false,
                "prerelease": false
              }
            ]
            """;

            var result = AutoUpdater.ParseReleasesJson(json, "3.0.0");

            Assert.Equal(UpdateState.Updated, result.State);
            Assert.Equal("3.0.0", result.LatestVersion);
        }

        [Fact]
        public void ParseReleasesJson_WhenCurrentIsBetaAndLatestIsSameStable_ReturnsUpdateAvailable()
        {
            string json = """
            [
              {
                "tag_name": "v3.0.0",
                "draft": false,
                "prerelease": false
              }
            ]
            """;

            var result = AutoUpdater.ParseReleasesJson(json, "3.0.0-beta2");

            Assert.Equal(UpdateState.UpdateAvailable, result.State);
            Assert.Equal("3.0.0", result.LatestVersion);
        }

        [Fact]
        public void ParseReleasesJson_WhenNewerBetaAvailable_ReturnsBetaAvailable()
        {
            string json = """
            [
              {
                "tag_name": "v3.1.0-beta2",
                "draft": false,
                "prerelease": true
              }
            ]
            """;

            var resultFromBeta1 = AutoUpdater.ParseReleasesJson(json, "3.1.0-beta1");
            Assert.Equal(UpdateState.BetaAvailable, resultFromBeta1.State);

            var resultFromStable300 = AutoUpdater.ParseReleasesJson(json, "3.0.0");
            Assert.Equal(UpdateState.BetaAvailable, resultFromStable300.State);
        }

        [Fact]
        public void ParseReleasesJson_WithInvalidJson_ReturnsError()
        {
            var result = AutoUpdater.ParseReleasesJson("{ invalid json", "3.0.0");
            Assert.Equal(UpdateState.Error, result.State);
        }

        [Fact]
        public void Security_RejectsUntrustedDownloadUrlsAndReleasePageUrls()
        {
            Assert.True(AutoUpdater.IsTrustedGitHubDownloadUrl(
                "https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/download/v3.1.0/WinThumbsPreloader-3.1.0-setup.exe",
                allowCdnRedirectHost: false));
            Assert.True(AutoUpdater.IsTrustedGitHubDownloadUrl(
                "https://objects.githubusercontent.com/github-production-release-asset-123/WinThumbsPreloader-3.1.0-setup.exe"));
            Assert.False(AutoUpdater.IsTrustedGitHubDownloadUrl(
                "https://objects.githubusercontent.com/github-production-release-asset-123/WinThumbsPreloader-3.1.0-setup.exe",
                allowCdnRedirectHost: false));

            Assert.False(AutoUpdater.IsTrustedGitHubDownloadUrl("http://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/download/v3.1.0/setup.exe"));
            Assert.False(AutoUpdater.IsTrustedGitHubDownloadUrl("https://evil.example.com/WinThumbsPreloader-3.1.0-setup.exe"));
            Assert.False(AutoUpdater.IsTrustedGitHubDownloadUrl("https://github.com/otheruser/WinThumbsPreloader-V3/releases/download/v3.1.0/setup.exe"));
            Assert.False(AutoUpdater.IsTrustedGitHubDownloadUrl("https://user:pass@github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/download/v3.1.0/setup.exe"));
            Assert.False(AutoUpdater.IsTrustedGitHubDownloadUrl("https://github.com:8443/joaoschlickmann/WinThumbsPreloader-V3/releases/download/v3.1.0/setup.exe"));

            string jsonWithUntrustedUrls = """
            [
              {
                "tag_name": "v3.1.0",
                "draft": false,
                "prerelease": false,
                "html_url": "https://phishing.example.com/fake-release",
                "assets": [
                  {
                    "name": "WinThumbsPreloader-3.1.0-setup.exe",
                    "size": 4500000,
                    "browser_download_url": "https://malware.example.com/WinThumbsPreloader-3.1.0-setup.exe"
                  }
                ]
              }
            ]
            """;

            var result = AutoUpdater.ParseReleasesJson(jsonWithUntrustedUrls, "3.0.0");
            Assert.Equal(AutoUpdater.ReleasesWebUrl, result.ReleasePageUrl);
            Assert.False(result.HasInstallerAsset);
        }

        [Fact]
        public void Security_SanitizeInstallerFileName_BlocksTraversalAndSpecialChars()
        {
            Assert.Equal("WinThumbsPreloader-3.0.0-setup.exe", AutoUpdater.SanitizeInstallerFileName("WinThumbsPreloader-3.0.0-setup.exe"));
            Assert.Empty(AutoUpdater.SanitizeInstallerFileName(@"..\..\evil-setup.exe"));
            Assert.Empty(AutoUpdater.SanitizeInstallerFileName("../evil-setup.exe"));
            Assert.Empty(AutoUpdater.SanitizeInstallerFileName("setup&calc.exe"));
            Assert.Empty(AutoUpdater.SanitizeInstallerFileName("payload.bat"));
        }

        [Fact]
        public void ValidateDownloadedExecutable_VerifiesPeHeaderSizeAndSha256()
        {
            string validExe = Path.Combine(_tempDir, "valid-setup.exe");
            byte[] payload = CreateValidMockPePayload(2048);
            File.WriteAllBytes(validExe, payload);

            string actualSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            string wrongSha256 = new string('0', 64);

            AutoUpdater.ValidateDownloadedExecutable(validExe, expectedSize: 2048, expectedSha256: actualSha256, requireSha256: true);

            Assert.Throws<InvalidDataException>(() =>
                AutoUpdater.ValidateDownloadedExecutable(validExe, expectedSize: 2048, expectedSha256: null, requireSha256: true));

            Assert.Throws<InvalidDataException>(() =>
                AutoUpdater.ValidateDownloadedExecutable(validExe, expectedSize: 2048, expectedSha256: wrongSha256));

            Assert.Throws<InvalidDataException>(() =>
                AutoUpdater.ValidateDownloadedExecutable(validExe, expectedSize: 9999));

            string htmlErrorFile = Path.Combine(_tempDir, "error-setup.exe");
            byte[] htmlBytes = new byte[2048];
            htmlBytes[0] = (byte)'<';
            htmlBytes[1] = (byte)'!';
            File.WriteAllBytes(htmlErrorFile, htmlBytes);

            Assert.Throws<InvalidDataException>(() =>
                AutoUpdater.ValidateDownloadedExecutable(htmlErrorFile));
        }

        [Fact]
        public async System.Threading.Tasks.Task DownloadInstallerAsync_AbuseCases_FailsClosedOnMissingSha256OrDirectCdnUrl()
        {
            var missingShaResult = new UpdateCheckResult
            {
                State = UpdateState.UpdateAvailable,
                LatestVersion = "3.1.0",
                InstallerFileName = "WinThumbsPreloader-3.1.0-setup.exe",
                InstallerDownloadUrl = "https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/download/v3.1.0/WinThumbsPreloader-3.1.0-setup.exe",
                InstallerSha256 = string.Empty
            };

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                AutoUpdater.DownloadInstallerAsync(missingShaResult));

            var directCdnUrlResult = new UpdateCheckResult
            {
                State = UpdateState.UpdateAvailable,
                LatestVersion = "3.1.0",
                InstallerFileName = "WinThumbsPreloader-3.1.0-setup.exe",
                InstallerDownloadUrl = "https://objects.githubusercontent.com/github-production-release-asset-123/WinThumbsPreloader-3.1.0-setup.exe",
                InstallerSha256 = new string('a', 64)
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AutoUpdater.DownloadInstallerAsync(directCdnUrlResult));
        }

        [Fact]
        public void PostUpdateLaunchMarker_PreservesMinimizedAndNormalStateAndExpires()
        {
            string markerPath = Path.Combine(_tempDir, ".post_update_launch");

            AutoUpdater.WritePostUpdateLaunchMarker(restartMinimized: true, markerFilePath: markerPath);
            Assert.True(File.Exists(markerPath));
            Assert.True(AutoUpdater.ConsumePostUpdateLaunchMinimized(markerFilePath: markerPath));
            Assert.False(File.Exists(markerPath));

            AutoUpdater.WritePostUpdateLaunchMarker(restartMinimized: false, markerFilePath: markerPath);
            Assert.True(File.Exists(markerPath));
            Assert.False(AutoUpdater.ConsumePostUpdateLaunchMinimized(markerFilePath: markerPath));
            Assert.False(File.Exists(markerPath));

            AutoUpdater.WritePostUpdateLaunchMarker(restartMinimized: true, markerFilePath: markerPath);
            DateTime expiredFuture = DateTime.UtcNow.AddMinutes(15);
            Assert.False(AutoUpdater.ConsumePostUpdateLaunchMinimized(markerFilePath: markerPath, nowUtc: expiredFuture));
            Assert.False(File.Exists(markerPath));
        }

        [Fact]
        public void SettingsMigration_RestoresSettingsFromPriorVersionsInAscendingOrder()
        {
            string appConfigRoot = Path.Combine(_tempDir, "WinThumbsPreloader");
            string hashDir = Path.Combine(appConfigRoot, "WinThumbsPreloader_Url_test123");
            string v300Dir = Path.Combine(hashDir, "3.0.0.0");
            string v310Dir = Path.Combine(hashDir, "3.1.0.0");
            string v311Dir = Path.Combine(hashDir, "3.1.1.0");
            Directory.CreateDirectory(v300Dir);
            Directory.CreateDirectory(v310Dir);
            Directory.CreateDirectory(v311Dir);

            File.WriteAllText(Path.Combine(v300Dir, "user.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
                <userSettings>
                    <WinThumbsPreloader.Properties.Settings>
                        <setting name="ThreadCount" serializeAs="String">
                            <value>4</value>
                        </setting>
                        <setting name="AutoBackupThumbs" serializeAs="String">
                            <value>False</value>
                        </setting>
                    </WinThumbsPreloader.Properties.Settings>
                </userSettings>
            </configuration>
            """);

            File.WriteAllText(Path.Combine(v310Dir, "user.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
                <userSettings>
                    <WinThumbsPreloader.Properties.Settings>
                        <setting name="ThreadCount" serializeAs="String">
                            <value>8</value>
                        </setting>
                        <setting name="AutoBackupThumbs" serializeAs="String">
                            <value>True</value>
                        </setting>
                        <setting name="AutoRestoreThumbs" serializeAs="String">
                            <value>True</value>
                        </setting>
                        <setting name="BackupCacheFolderPath" serializeAs="String">
                            <value>C:\TestBackupFolder</value>
                        </setting>
                        <setting name="LoggingFrequency" serializeAs="String">
                            <value>4</value>
                        </setting>
                    </WinThumbsPreloader.Properties.Settings>
                </userSettings>
            </configuration>
            """);

            // Version equal to currentAssemblyVersion (3.1.1.0) must not override prior version migration
            File.WriteAllText(Path.Combine(v311Dir, "user.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
                <userSettings>
                    <WinThumbsPreloader.Properties.Settings>
                        <setting name="ThreadCount" serializeAs="String">
                            <value>0</value>
                        </setting>
                        <setting name="LoggingFrequency" serializeAs="String">
                            <value>0</value>
                        </setting>
                    </WinThumbsPreloader.Properties.Settings>
                </userSettings>
            </configuration>
            """);

            var settingsInstance = new WinThumbsPreloader.Properties.Settings();
            int migratedCount = settingsInstance.MigrateFromPreviousUserConfigs(appConfigRoot, new Version(3, 1, 1, 0));

            Assert.True(migratedCount >= 5);
            Assert.Equal(8, settingsInstance.ThreadCount);
            Assert.True(settingsInstance.AutoBackupThumbs);
            Assert.True(settingsInstance.AutoRestoreThumbs);
            Assert.Equal(@"C:\TestBackupFolder", settingsInstance.BackupCacheFolderPath);
            Assert.Equal(4, settingsInstance.LoggingFrequency);
        }

        [Fact]
        public void CacheForm_WhenTopLevelClosedByUser_CancelsCloseAndMinimizesToTray()
        {
            Exception? threadException = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    using var form = new CacheForm();
                    _ = form.Handle;
                    form.Show();
                    Assert.True(form.Visible);

                    // Closing top-level CacheForm (Owner == null) without Shift should cancel FormClosing and hide to tray
                    form.Close();

                    Assert.False(form.IsDisposed);
                    Assert.Equal(System.Windows.Forms.FormWindowState.Minimized, form.WindowState);
                    Assert.False(form.ShowInTaskbar);
                    Assert.False(form.Visible);

                    // Restoring from tray brings the form back to Normal state
                    form.RestoreFromTray();
                    Assert.Equal(System.Windows.Forms.FormWindowState.Normal, form.WindowState);
                    Assert.True(form.ShowInTaskbar);
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (threadException != null)
                throw threadException;
        }
    }
}
