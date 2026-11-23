using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinThumbsPreloader.Forms;
using WinThumbsPreloader.Properties;
using WinThumbsPreloader.Services;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    [Collection("NonParallelIntegration")]
    public sealed class WebUiHostAndBridgeTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _screenshotsDir;
        private readonly string? _originalUdfEnv;
        private readonly string _originalTheme;
        private readonly bool _originalThumbsResetAlert;

        public WebUiHostAndBridgeTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_WebUiHostTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            string repoRoot = LocateRepositoryRoot();
            _screenshotsDir = Path.Combine(repoRoot, "artifacts", "e2e-visual");
            Directory.CreateDirectory(_screenshotsDir);

            _originalUdfEnv = Environment.GetEnvironmentVariable("WTP_WEBVIEW2_UDF");
            _originalTheme = ThemeHelper.GetSavedTheme();
            _originalThumbsResetAlert = Settings.Default.ThumbsResetAlert;
            Settings.Default.ThumbsResetAlert = false;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("WTP_WEBVIEW2_UDF", _originalUdfEnv);
            ThemeHelper.SaveTheme(_originalTheme);
            Settings.Default.ThumbsResetAlert = _originalThumbsResetAlert;
            Settings.Default.Save();

            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (Directory.Exists(_tempDir))
                        Directory.Delete(_tempDir, recursive: true);
                    break;
                }
                catch when (attempt < 9)
                {
                    Thread.Sleep(150);
                }
                catch
                {
                    // Best effort cleanup for transient WebView2 file locks
                }
            }
        }

        private static string LocateRepositoryRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "README.md")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "WinThumbsPreloader")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return Path.Combine(Path.GetTempPath(), "WTP_Artifacts");
        }

        private static Task RunOnStaThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    Application.EnableVisualStyles();
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        private static void PumpEventsFor(int milliseconds)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < milliseconds)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
        }

        private static T AwaitWithMessagePump<T>(Task<T> task, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (!task.IsCompleted && sw.ElapsedMilliseconds < timeoutMs)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            if (!task.IsCompleted)
                throw new TimeoutException($"STA operation timed out after {timeoutMs} ms.");
            return task.GetAwaiter().GetResult();
        }

        private static void AwaitWithMessagePump(Task task, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (!task.IsCompleted && sw.ElapsedMilliseconds < timeoutMs)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            if (!task.IsCompleted)
                throw new TimeoutException($"STA operation timed out after {timeoutMs} ms.");
            task.GetAwaiter().GetResult();
        }

        private static WebBridge CreateBridge() => new(
            new CacheService(),
            new PreloadService(),
            new ScheduleService(),
            new SettingsService(),
            new OptimizerService(),
            new CodecsService(),
            new UpdateService());

        [Fact]
        public async Task MainShellForm_RealWebView2_LoadsAllSixViewsAndCapturesScreenshots_AndSupportsTrayToggle()
        {
            Assert.True(MainShellForm.IsWebView2RuntimeAvailable(), "WebView2 Runtime is required to verify MainShellForm.");
            Assert.True(MainShellForm.TryResolveWwwRootDir(out string wwwrootDir), "wwwroot/index.html should be built and resolvable before running WebView2 host tests.");
            Assert.True(File.Exists(Path.Combine(wwwrootDir, "index.html")));

            string isolatedUdf = Path.Combine(_tempDir, "RealWebView2Udf");
            Environment.SetEnvironmentVariable("WTP_WEBVIEW2_UDF", isolatedUdf);

            await RunOnStaThreadAsync(() =>
            {
                using var trayHost = new TrayHostController(new CacheService(), new UpdateService(), _ => { });
                using var shellForm = new MainShellForm(trayHost, "overview", isolatedUdf);
                try
                {
                    shellForm.StartPosition = FormStartPosition.Manual;
                    shellForm.Location = new Point(40, 40);
                    shellForm.Size = new Size(1240, 820);
                    shellForm.Show();
                    Application.DoEvents();

                    AwaitWithMessagePump(shellForm.WaitUntilReadyAsync(TimeSpan.FromSeconds(40)), 45000);
                    PumpEventsFor(700);

                    string[] views = ["overview", "preload", "cache", "schedule", "codecs", "optimizer", "settings"];
                    foreach (string view in views)
                    {
                        shellForm.NavigateToView(view);
                        PumpEventsFor(view == "schedule" ? 1800 : 750);

                        string pngPath = Path.Combine(_screenshotsDir, $"webui_{view}.png");
                        bool captured = AwaitWithMessagePump(shellForm.CaptureWebViewPreviewAsync(pngPath), 10000);
                        Assert.True(captured, $"Failed to capture WebView2 preview for view '{view}'.");

                        var fileInfo = new FileInfo(pngPath);
                        Assert.True(fileInfo.Exists && fileInfo.Length > 5_000,
                            $"WebView2 preview '{pngPath}' was unexpectedly empty ({fileInfo.Length} bytes).");
                    }

                    // Verify Minimize-to-Tray / Show-from-Tray lifecycle without leaving stray handles
                    shellForm.HideToTray();
                    PumpEventsFor(150);
                    Assert.False(shellForm.Visible);
                    Assert.True(trayHost.IsTrayIconVisible);

                    shellForm.ShowFromTray("cache");
                    PumpEventsFor(200);
                    Assert.True(shellForm.Visible);
                    Assert.False(trayHost.IsTrayIconVisible);
                }
                finally
                {
                    try
                    {
                        trayHost.SetVisible(false);
                        shellForm.Hide();
                        Application.DoEvents();
                    }
                    catch { }
                }
            });
        }

        [Theory]
        [InlineData("https://github.com/joaoschlickmann/WinThumbsPreloader-V3")]
        [InlineData("https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases")]
        [InlineData("https://github.com/Mfarooq360/WinThumbsPreloader-V2")]
        [InlineData("https://github.com/bruhov/WinThumbsPreloader")]
        public async Task WebBridge_OpenExternalUrl_AllowsTrustedProjectAndCreditGitHubUrls(string candidateUrl)
        {
            Assert.True(UpdateService.IsAllowedExternalUrl(candidateUrl));
            await Task.CompletedTask;
        }

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("file:///C:/Windows/win.ini")]
        [InlineData("data:text/html,<script>alert(1)</script>")]
        [InlineData("http://github.com/joaoschlickmann/WinThumbsPreloader-V3")]
        [InlineData("https://evil.github.com/joaoschlickmann/WinThumbsPreloader-V3")]
        public async Task WebBridge_OpenExternalUrl_BlocksDangerousAndNonHttpsSchemes(string candidateUrl)
        {
            WebBridge bridge = CreateBridge();
            string payloadJson = JsonSerializer.Serialize(new
            {
                id = "ext-1",
                cmd = "app.openExternalUrl",
                payload = new { url = candidateUrl }
            });

            string resp = await bridge.HandleMessageJsonAsync(payloadJson, WebBridge.TrustedOrigin + "/index.html");
            using JsonDocument doc = JsonDocument.Parse(resp);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("untrusted_url", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        [Theory]
        [InlineData("null")]
        [InlineData("https://app.winthumbspreloader.local.attacker.example/index.html")]
        [InlineData("https://app.winthumbspreloader.local:444/index.html")]
        [InlineData("http://app.winthumbspreloader.local/index.html")]
        [InlineData("ms-appx-web://app.winthumbspreloader.local/index.html")]
        public async Task WebBridge_HandleMessage_RejectsSpoofedAndNullOrigins(string origin)
        {
            WebBridge bridge = CreateBridge();
            string resp = await bridge.HandleMessageJsonAsync(
                """{"id":"o1","cmd":"app.getBootstrap","payload":{}}""",
                origin);

            using JsonDocument doc = JsonDocument.Parse(resp);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("untrusted_origin", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task WebBridge_SecurityBoundaryCases_EnforceUtf8ByteLimitSchemaAndNativeDropOnly()
        {
            WebBridge bridge = CreateBridge();
            string trustedOrigin = WebBridge.TrustedOrigin + "/index.html";

            // 1. Multi-byte UTF-8 boundary check: < 262,144 chars, > 262,144 UTF-8 bytes
            string multiBytePad = new('\u00E9', 140_000); // ~280,000 bytes in UTF-8
            Assert.True(multiBytePad.Length < WebBridge.MaxIpcMessageBytes);
            string utf8OversizedMsg = $"{{\"id\":\"mb\",\"cmd\":\"app.getBootstrap\",\"payload\":{{\"p\":\"{multiBytePad}\"}}}}";
            Assert.True(Encoding.UTF8.GetByteCount(utf8OversizedMsg) > WebBridge.MaxIpcMessageBytes);

            string utf8Resp = await bridge.HandleMessageJsonAsync(utf8OversizedMsg, trustedOrigin);
            using (JsonDocument doc = JsonDocument.Parse(utf8Resp))
            {
                Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("payload_too_large", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            // 2. Empty message, invalid syntax, array root, missing cmd, non-object payload
            foreach (string badMsg in new[] { "", "   " })
            {
                string r = await bridge.HandleMessageJsonAsync(badMsg, trustedOrigin);
                using JsonDocument doc = JsonDocument.Parse(r);
                Assert.Equal("invalid_payload", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            using (JsonDocument doc = JsonDocument.Parse(await bridge.HandleMessageJsonAsync("[]", trustedOrigin)))
            {
                Assert.Equal("invalid_payload", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            using (JsonDocument doc = JsonDocument.Parse(await bridge.HandleMessageJsonAsync("{bad", trustedOrigin)))
            {
                Assert.Equal("invalid_json", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            using (JsonDocument doc = JsonDocument.Parse(await bridge.HandleMessageJsonAsync("""{"id":"nc","payload":{}}""", trustedOrigin)))
            {
                Assert.Equal("invalid_command", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            using (JsonDocument doc = JsonDocument.Parse(await bridge.HandleMessageJsonAsync("""{"id":"np","cmd":"app.getBootstrap","payload":[1,2]}""", trustedOrigin)))
            {
                Assert.Equal("invalid_payload", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            // 3. settings.importDroppedExtensions must ignore arbitrary JSON paths and require native droppedPaths
            string secretFile = Path.Combine(_tempDir, "secret_ext.txt");
            File.WriteAllText(secretFile, "jpg,png");
            string fakeDropPayload = JsonSerializer.Serialize(new
            {
                id = "drop-js",
                cmd = "settings.importDroppedExtensions",
                payload = new { path = secretFile }
            });
            using (JsonDocument doc = JsonDocument.Parse(await bridge.HandleMessageJsonAsync(fakeDropPayload, trustedOrigin, droppedPaths: null)))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                JsonElement data = doc.RootElement.GetProperty("data");
                Assert.False(data.GetProperty("ok").GetBoolean());
            }

            // When native CoreWebView2File path IS supplied via droppedPaths, import succeeds safely
            using (JsonDocument doc = JsonDocument.Parse(await bridge.HandleMessageJsonAsync(
                """{"id":"drop-native","cmd":"settings.importDroppedExtensions","payload":{}}""",
                trustedOrigin,
                droppedPaths: [secretFile])))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                JsonElement data = doc.RootElement.GetProperty("data");
                Assert.True(data.GetProperty("ok").GetBoolean());
                Assert.Equal("jpg,png", data.GetProperty("text").GetString());
            }
        }
    }
}
