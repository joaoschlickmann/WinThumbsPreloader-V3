#nullable enable
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinThumbsPreloader.Properties;
using WinThumbsPreloader.Services;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader.Forms
{
    /// <summary>
    /// Unified single-window WebView2 shell hosting the React UI over a strict virtual-host origin
    /// with hardened CoreWebView2 settings, native drag-drop path extraction, and tray integration.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class MainShellForm : Form
    {
        public const string VirtualHostName = WebBridge.TrustedHost;
        public const string EntryUrl = "https://app.winthumbspreloader.local/index.html";

        private readonly TrayHostController _trayHost;
        private readonly WebView2 _webView;
        private readonly PreloadService _preloadService;
        private readonly ScheduleService _scheduleService;
        private readonly SettingsService _settingsService;
        private readonly OptimizerService _optimizerService;
        private readonly CodecsService _codecsService;
        private readonly WebBridge _bridge;

        private readonly TaskCompletionSource<bool> _readyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private string _initialView;
        private string _currentThemeMode;
        private bool _initializationStarted;
        private bool _coreInitialized;
        private bool _allowExplicitClose;
        private bool _fallbackTriggered;

        public event EventHandler? WebViewFallbackRequested;

        public MainShellForm(TrayHostController trayHost, string? initialView = null)
        {
            _trayHost = trayHost ?? throw new ArgumentNullException(nameof(trayHost));
            _initialView = NormalizeViewName(initialView);

            _preloadService = new PreloadService();
            _scheduleService = new ScheduleService();
            _settingsService = new SettingsService();
            _optimizerService = new OptimizerService();
            _codecsService = new CodecsService();

            _bridge = new WebBridge(
                _trayHost.CacheService,
                _preloadService,
                _scheduleService,
                _settingsService,
                _optimizerService,
                _codecsService,
                _trayHost.UpdateService,
                ownerProvider: () => this,
                uiInvokeAsync: InvokeOnUiThreadAsync,
                emitEventAsync: PostEventJsonAsync,
                onThemeChanged: OnThemeChangedFromWeb,
                onMinimizeToTray: () => BeginInvoke(new Action(HideToTray)),
                onQuit: () => BeginInvoke(new Action(RequestExit)),
                getInitialView: () => _initialView);

            SuspendLayout();
            Name = nameof(MainShellForm);
            Text = "WinThumbsPreloader";
            Size = new Size(1160, 760);
            MinimumSize = new Size(720, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Resources.MainIcon;
            KeyPreview = true;

            bool isDark = ThemeHelper.ResolveEffectiveDarkMode(ThemeHelper.GetSavedTheme());
            _currentThemeMode = ThemeHelper.GetSavedTheme();
            Color canvasColor = ThemeHelper.GetCanvasBackColor(isDark);
            BackColor = canvasColor;

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = canvasColor
            };
            Controls.Add(_webView);
            ResumeLayout(false);

            Load += MainShellForm_Load;
            FormClosing += MainShellForm_FormClosing;
            FormClosed += MainShellForm_FormClosed;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

            _trayHost.SnapshotUpdated += TrayHost_SnapshotUpdated;
            _trayHost.ProgressUpdated += TrayHost_ProgressUpdated;
            _trayHost.CacheResetDetected += TrayHost_CacheResetDetected;
            _trayHost.UpdateAvailableDetected += TrayHost_UpdateAvailableDetected;
        }

        public static bool IsWebView2RuntimeAvailable()
        {
            try
            {
                string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrWhiteSpace(version);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool CanUseWebViewShell()
        {
            return TryResolveWwwRootDir(out _) && IsWebView2RuntimeAvailable();
        }

        public static string GetWebView2UserDataFolder()
        {
            bool isAdmin = Program.IsRunningAsAdministrator();
            string? envOverride = !isAdmin ? Environment.GetEnvironmentVariable("WTP_WEBVIEW2_UDF") : null;
            string candidate;
            if (!string.IsNullOrWhiteSpace(envOverride))
            {
                candidate = Environment.ExpandEnvironmentVariables(envOverride.Trim().Trim('"'));
            }
            else
            {
                string baseAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(baseAppData))
                {
                    baseAppData = isAdmin
                        ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                        : Path.GetTempPath();
                }

                string folderName = isAdmin ? "WebView2-Admin" : "WebView2";
                candidate = Path.Combine(baseAppData, "WinThumbsPreloader", folderName);
            }

            string fullPath = Path.GetFullPath(candidate);
            if (PathSecurity.HasReparsePointOrJunctionInPath(fullPath, rejectAnyLeafOrParentReparsePoint: false) ||
                PathSecurity.IsReparsePoint(fullPath))
            {
                throw new IOException($"WebView2 UserDataFolder cannot traverse or be a reparse point: '{fullPath}'.");
            }

            DirectoryInfo dirInfo = Directory.CreateDirectory(fullPath);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"WebView2 UserDataFolder cannot be a reparse point: '{fullPath}'.");
            }

            return fullPath;
        }

        public static bool TryResolveWwwRootDir(out string wwwrootDir)
        {
            wwwrootDir = string.Empty;

            static bool IsValidWwwRoot(string candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                    return false;

                if (PathSecurity.HasReparsePointOrJunctionInPath(candidate, rejectAnyLeafOrParentReparsePoint: false) ||
                    PathSecurity.IsReparsePoint(candidate))
                {
                    return false;
                }

                string indexPath = Path.Combine(candidate, "index.html");
                return File.Exists(indexPath) && !PathSecurity.IsReparsePoint(indexPath);
            }

            string baseCandidate = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (IsValidWwwRoot(baseCandidate))
            {
                wwwrootDir = Path.GetFullPath(baseCandidate);
                return true;
            }

#if DEBUG
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int depth = 0; depth < 7 && dir != null && dir.Parent != null; depth++)
            {
                string directCandidate = Path.Combine(dir.FullName, "wwwroot");
                if (IsValidWwwRoot(directCandidate))
                {
                    wwwrootDir = Path.GetFullPath(directCandidate);
                    return true;
                }

                string nestedCandidate = Path.Combine(dir.FullName, "WinThumbsPreloader", "WinThumbsPreloader", "wwwroot");
                if (IsValidWwwRoot(nestedCandidate))
                {
                    wwwrootDir = Path.GetFullPath(nestedCandidate);
                    return true;
                }

                dir = dir.Parent;
            }
#endif

            return false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ThemeHelper.ApplyWindowTheme(this);
        }

        private async void MainShellForm_Load(object? sender, EventArgs e)
        {
            if (_initializationStarted)
                return;

            _initializationStarted = true;
            await InitializeWebViewAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                if (!TryResolveWwwRootDir(out string wwwrootDir))
                {
                    WriteLine("WebView2 shell wwwroot/index.html not found; falling back to legacy UI.", LoggingFrequency.GUILogging);
                    TriggerFallback();
                    return;
                }

                string udfPath = GetWebView2UserDataFolder();
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, udfPath);

                if (IsDisposed || Disposing)
                    return;

                await _webView.EnsureCoreWebView2Async(env);
                if (IsDisposed || Disposing || _webView.CoreWebView2 == null)
                    return;

                CoreWebView2 core = _webView.CoreWebView2;
                ConfigureCoreWebView2Security(core, wwwrootDir);

                core.NavigationStarting += Core_NavigationStarting;
                core.NewWindowRequested += Core_NewWindowRequested;
                core.PermissionRequested += Core_PermissionRequested;
                core.DownloadStarting += Core_DownloadStarting;
                core.WebMessageReceived += Core_WebMessageReceived;
                core.NavigationCompleted += Core_NavigationCompleted;

                _coreInitialized = true;
                core.Navigate(EntryUrl);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                WriteLine("WebView2 Runtime not found; falling back to legacy AboutForm: " + ex.Message, LoggingFrequency.GUILogging);
                TriggerFallback();
            }
            catch (Exception ex)
            {
                WriteLine("Failed to initialize WebView2 shell: " + ex.Message, LoggingFrequency.GUILogging);
                TriggerFallback();
            }
        }

        private static void ConfigureCoreWebView2Security(CoreWebView2 core, string wwwrootDir)
        {
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsScriptEnabled = true;
            core.Settings.IsWebMessageEnabled = true;

#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#endif

            core.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                wwwrootDir,
                CoreWebView2HostResourceAccessKind.DenyCors);
        }

        private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!WebBridge.IsTrustedWebOrigin(e.Uri))
            {
                WriteLine($"Blocked untrusted WebView2 navigation to '{e.Uri}'.", LoggingFrequency.GUILogging);
                e.Cancel = true;
            }
        }

        private void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            WriteLine($"Blocked WebView2 NewWindowRequested for '{e.Uri}'.", LoggingFrequency.GUILogging);
            e.Handled = true;
        }

        private void Core_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            WriteLine($"Denied WebView2 permission request ({e.PermissionKind}).", LoggingFrequency.GUILogging);
            e.State = CoreWebView2PermissionState.Deny;
            e.Handled = true;
        }

        private void Core_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        {
            WriteLine($"Blocked WebView2 download '{e.ResultFilePath}'.", LoggingFrequency.GUILogging);
            e.Cancel = true;
            e.Handled = true;
        }

        private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                _readyTcs.TrySetResult(true);
            }
            else
            {
                WriteLine($"WebView2 navigation completed with status: {e.WebErrorStatus}", LoggingFrequency.GUILogging);
                _readyTcs.TrySetResult(false);
            }
        }

        private async void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                List<string>? droppedPaths = null;
                try
                {
                    if (e.AdditionalObjects != null && e.AdditionalObjects.Count > 0)
                    {
                        droppedPaths = new List<string>(e.AdditionalObjects.Count);
                        foreach (object obj in e.AdditionalObjects)
                        {
                            if (obj is CoreWebView2File file && !string.IsNullOrWhiteSpace(file.Path))
                            {
                                droppedPaths.Add(file.Path);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLine("Error inspecting WebView2 AdditionalObjects: " + ex.Message, LoggingFrequency.DebugLogging);
                }

                string responseJson = await _bridge.HandleMessageJsonAsync(
                    e.WebMessageAsJson,
                    e.Source,
                    droppedPaths);

                if (IsDisposed || Disposing || _webView.CoreWebView2 == null)
                    return;

                try
                {
                    _webView.CoreWebView2.PostWebMessageAsJson(responseJson);
                }
                catch (Exception ex)
                {
                    WriteLine("Failed posting WebMessage response: " + ex.Message, LoggingFrequency.DebugLogging);
                }
            }
            catch (Exception ex)
            {
                WriteLine("Unhandled WebMessageReceived exception: " + ex.Message, LoggingFrequency.GUILogging);
            }
        }

        public void NavigateToView(string view)
        {
            string normalized = NormalizeViewName(view);
            _initialView = normalized;

            if (_coreInitialized && !IsDisposed && _webView.CoreWebView2 != null)
            {
                _ = PostEventJsonAsync(WebBridge.SerializeEvent("shell:navigate", new { view = normalized }));
            }
        }

        public void ShowFromTray(string? view = null)
        {
            if (IsDisposed)
                return;

            if (!string.IsNullOrWhiteSpace(view))
            {
                NavigateToView(view);
            }

            _trayHost.SetVisible(false);
            Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            ShowInTaskbar = true;
            Program.BringWindowToFront(this);
        }

        public void HideToTray()
        {
            if (IsDisposed)
                return;

            ShowInTaskbar = false;
            Hide();
            _trayHost.SetVisible(true);
        }

        public async Task WaitUntilReadyAsync(TimeSpan timeout)
        {
            bool ok = await _readyTcs.Task.WaitAsync(timeout).ConfigureAwait(false);
            if (!ok)
                throw new InvalidOperationException("WebView2 initial page navigation failed.");
        }

        public async Task<bool> CaptureWebViewPreviewAsync(string outputPngPath)
        {
            if (string.IsNullOrWhiteSpace(outputPngPath) || IsDisposed || Disposing)
                return false;

            try
            {
                await _readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch
            {
                return false;
            }

            if (IsDisposed || Disposing || _webView.CoreWebView2 == null)
                return false;

            string fullPath = Path.GetFullPath(outputPngPath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                if (PathSecurity.HasReparsePointOrJunctionInPath(dir, rejectAnyLeafOrParentReparsePoint: false) ||
                    PathSecurity.IsReparsePoint(dir))
                {
                    return false;
                }

                DirectoryInfo dirInfo = Directory.CreateDirectory(dir);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;
            }

            PathSecurity.EnsureSafeFileForWrite(fullPath);

            using var ms = new MemoryStream();
            await _webView.CoreWebView2.CapturePreviewAsync(
                CoreWebView2CapturePreviewImageFormat.Png,
                ms).ConfigureAwait(true);

            await File.WriteAllBytesAsync(fullPath, ms.ToArray()).ConfigureAwait(true);
            return File.Exists(fullPath);
        }

        public void RequestExit()
        {
            _allowExplicitClose = true;
            Program.ExitApplication(0);
        }

        private void MainShellForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_allowExplicitClose)
                return;

            if (e.CloseReason != CloseReason.UserClosing)
                return;

            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                WriteLine("Exiting application from MainShellForm (Shift+Close).", LoggingFrequency.GUILogging);
                RequestExit();
                return;
            }

            WriteLine("Minimizing MainShellForm to system tray.", LoggingFrequency.GUILogging);
            e.Cancel = true;
            HideToTray();
        }

        private void MainShellForm_FormClosed(object? sender, FormClosedEventArgs e)
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            _trayHost.SnapshotUpdated -= TrayHost_SnapshotUpdated;
            _trayHost.ProgressUpdated -= TrayHost_ProgressUpdated;
            _trayHost.CacheResetDetected -= TrayHost_CacheResetDetected;
            _trayHost.UpdateAvailableDetected -= TrayHost_UpdateAvailableDetected;

            _readyTcs.TrySetCanceled();
            DisposeWebViewSafe();
        }

        private void DisposeWebViewSafe()
        {
            try
            {
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.NavigationStarting -= Core_NavigationStarting;
                    _webView.CoreWebView2.NewWindowRequested -= Core_NewWindowRequested;
                    _webView.CoreWebView2.PermissionRequested -= Core_PermissionRequested;
                    _webView.CoreWebView2.DownloadStarting -= Core_DownloadStarting;
                    _webView.CoreWebView2.WebMessageReceived -= Core_WebMessageReceived;
                    _webView.CoreWebView2.NavigationCompleted -= Core_NavigationCompleted;
                }
            }
            catch { }

            try
            {
                if (!_webView.IsDisposed)
                    _webView.Dispose();
            }
            catch { }
        }

        private void TriggerFallback()
        {
            if (_fallbackTriggered)
                return;

            _fallbackTriggered = true;
            _readyTcs.TrySetResult(false);

            if (WebViewFallbackRequested != null)
            {
                WebViewFallbackRequested.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Program.FallbackToLegacyAboutForm(this);
            }
        }

        private void SystemEvents_UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (!string.Equals(_currentThemeMode, "system", StringComparison.OrdinalIgnoreCase) || IsDisposed || Disposing)
                return;

            OnThemeChangedFromWeb("system");
        }

        private void OnThemeChangedFromWeb(string theme)
        {
            _currentThemeMode = theme;

            void Apply()
            {
                if (IsDisposed)
                    return;

                bool isDark = ThemeHelper.ApplyWindowTheme(this, theme);
                if (!_webView.IsDisposed)
                {
                    _webView.DefaultBackgroundColor = ThemeHelper.GetCanvasBackColor(isDark);
                }
            }

            if (InvokeRequired)
                BeginInvoke(new Action(Apply));
            else
                Apply();
        }

        private Task<object?> InvokeOnUiThreadAsync(Func<object?> action)
        {
            if (IsDisposed || Disposing)
                return Task.FromResult<object?>(null);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Run()
            {
                try
                {
                    tcs.TrySetResult(action());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            if (InvokeRequired)
                BeginInvoke(new Action(Run));
            else
                Run();

            return tcs.Task;
        }

        private Task PostEventJsonAsync(string eventJson)
        {
            if (IsDisposed || Disposing || !_coreInitialized)
                return Task.CompletedTask;

            void Send()
            {
                if (IsDisposed || Disposing || _webView.CoreWebView2 == null)
                    return;

                try
                {
                    _webView.CoreWebView2.PostWebMessageAsJson(eventJson);
                }
                catch
                {
                    // Ignore post errors while closing
                }
            }

            if (InvokeRequired)
                BeginInvoke(new Action(Send));
            else
                Send();

            return Task.CompletedTask;
        }

        private void TrayHost_SnapshotUpdated(object? sender, CacheSnapshot snapshot)
        {
            if (Visible && _coreInitialized && !IsDisposed)
            {
                _ = PostEventJsonAsync(WebBridge.SerializeEvent("cache:telemetry", snapshot));
            }
        }

        private void TrayHost_ProgressUpdated(object? sender, TrayProgressEventArgs e)
        {
            if (Visible && _coreInitialized && !IsDisposed)
            {
                _ = PostEventJsonAsync(WebBridge.SerializeEvent("cache:progress", new { operation = e.Operation, percent = e.Percent }));
            }
        }

        private void TrayHost_CacheResetDetected(object? sender, CacheResetEventArgs e)
        {
            if (_coreInitialized && !IsDisposed)
            {
                _ = PostEventJsonAsync(WebBridge.SerializeEvent("cache:resetDetected", e));
            }
        }

        private void TrayHost_UpdateAvailableDetected(object? sender, UpdateStatusDto status)
        {
            if (_coreInitialized && !IsDisposed)
            {
                _ = PostEventJsonAsync(WebBridge.SerializeEvent("update:available", status));
            }
        }

        private static string NormalizeViewName(string? view)
        {
            if (string.IsNullOrWhiteSpace(view))
                return "overview";

            return view.Trim().ToLowerInvariant() switch
            {
                "overview" => "overview",
                "preload" => "preload",
                "cache" => "cache",
                "schedule" => "schedule",
                "codecs" => "codecs",
                "optimizer" => "optimizer",
                "settings" => "settings",
                _ => "overview"
            };
        }
    }
}
