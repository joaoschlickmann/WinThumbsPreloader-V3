#nullable enable
using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader.Services
{
    public sealed class TrayProgressEventArgs : EventArgs
    {
        public string Operation { get; init; } = string.Empty;
        public int Percent { get; init; }
    }

    /// <summary>
    /// Owns the single background NotifyIcon, ContextMenuStrip, and cache telemetry / auto-backup /
    /// auto-restore / update-check timers across both minimized tray sessions and active WebView2 shell sessions.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class TrayHostController : IDisposable
    {
        private readonly CacheService _cacheService;
        private readonly UpdateService _updateService;
        private readonly Action<string?> _onNavigateRequest;

        private readonly Control _marshalControl;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;

        private readonly ToolStripMenuItem _openItem;
        private readonly ToolStripMenuItem _preloadItem;
        private readonly ToolStripMenuItem _cacheItem;
        private readonly ToolStripMenuItem _scheduleItem;
        private readonly ToolStripMenuItem _codecsItem;
        private readonly ToolStripMenuItem _optimizerItem;
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _startWithWindowsItem;
        private readonly ToolStripMenuItem _autoBackupItem;
        private readonly ToolStripMenuItem _autoRestoreItem;
        private readonly ToolStripMenuItem _alertOnResetItem;
        private readonly ToolStripMenuItem _checkUpdatesItem;
        private readonly ToolStripMenuItem _exitItem;

        private readonly System.Windows.Forms.Timer _updateTimer;
        private readonly System.Windows.Forms.Timer _autoBackupTimer;
        private readonly System.Windows.Forms.Timer _autoRestoreTimer;

        private UpdateStatusDto? _latestUpdate;
        private Font? _ownedOpenFont;
        private Font? _ownedUpdateBoldFont;
        private string? _balloonAction;
        private bool _disposed;
        private bool _updateTickBusy;
        private bool _autoBackupBusy;
        private bool _autoRestoreBusy;

        public event EventHandler<CacheSnapshot>? SnapshotUpdated;
        public event EventHandler<TrayProgressEventArgs>? ProgressUpdated;
        public event EventHandler<CacheResetEventArgs>? CacheResetDetected;
        public event EventHandler<UpdateStatusDto>? UpdateAvailableDetected;

        public CacheService CacheService => _cacheService;
        public UpdateService UpdateService => _updateService;

        public bool IsTrayIconVisible => !_disposed && _notifyIcon.Visible;

        public TrayHostController(
            CacheService cacheService,
            UpdateService updateService,
            Action<string?> onNavigateRequest)
        {
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _onNavigateRequest = onNavigateRequest ?? throw new ArgumentNullException(nameof(onNavigateRequest));

            _marshalControl = new Control();
            _ = _marshalControl.Handle;

            _contextMenu = new ContextMenuStrip();
            _openItem = new ToolStripMenuItem("Open");
            _ownedOpenFont = new Font(_openItem.Font, FontStyle.Bold);
            _openItem.Font = _ownedOpenFont;
            _openItem.Click += (_, _) => _onNavigateRequest(null);

            _preloadItem = new ToolStripMenuItem("Preload & Batch...", null, (_, _) => _onNavigateRequest("preload"));
            _cacheItem = new ToolStripMenuItem("Cache Manager...", null, (_, _) => _onNavigateRequest("cache"));
            _scheduleItem = new ToolStripMenuItem("Schedule...", null, (_, _) => _onNavigateRequest("schedule"));
            _codecsItem = new ToolStripMenuItem("Thumbnail Codecs...", null, (_, _) => _onNavigateRequest("codecs"));
            _optimizerItem = new ToolStripMenuItem("Windows Optimizer...", null, (_, _) => _onNavigateRequest("optimizer"));
            _settingsItem = new ToolStripMenuItem("Settings...", null, (_, _) => _onNavigateRequest("settings"));

            _startWithWindowsItem = new ToolStripMenuItem("Start with Windows", null, OnStartWithWindowsClick);
            _autoBackupItem = new ToolStripMenuItem("Auto Backup", null, OnAutoBackupClick);
            _autoRestoreItem = new ToolStripMenuItem("Auto Restore", null, OnAutoRestoreClick);
            _alertOnResetItem = new ToolStripMenuItem("Cache Reset Alert", null, OnAlertOnResetClick);

            _checkUpdatesItem = new ToolStripMenuItem("Check for Updates...", null, OnCheckForUpdatesClick);
            _exitItem = new ToolStripMenuItem("Exit", null, (_, _) =>
            {
                WriteLine("Exiting application from tray icon.", LoggingFrequency.GUILogging);
                Program.ExitApplication(0);
            });

            _contextMenu.Items.AddRange(new ToolStripItem[]
            {
                _openItem,
                _preloadItem,
                _cacheItem,
                _scheduleItem,
                _codecsItem,
                _optimizerItem,
                _settingsItem,
                new ToolStripSeparator(),
                _startWithWindowsItem,
                _autoBackupItem,
                _autoRestoreItem,
                _alertOnResetItem,
                new ToolStripSeparator(),
                _checkUpdatesItem,
                new ToolStripSeparator(),
                _exitItem
            });

            _notifyIcon = new NotifyIcon
            {
                Icon = Resources.MainIcon,
                Text = "WinThumbsPreloader",
                ContextMenuStrip = _contextMenu,
                Visible = false
            };
            _notifyIcon.DoubleClick += (_, _) => _onNavigateRequest(null);
            _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;

            _updateTimer = new System.Windows.Forms.Timer();
            _autoBackupTimer = new System.Windows.Forms.Timer();
            _autoRestoreTimer = new System.Windows.Forms.Timer();

            _updateTimer.Tick += UpdateTimer_Tick;
            _autoBackupTimer.Tick += AutoBackupTimer_Tick;
            _autoRestoreTimer.Tick += AutoRestoreTimer_Tick;

            _cacheService.SettingsChanged += OnCacheSettingsChanged;
            _cacheService.CacheResetDetected += OnServiceCacheResetDetected;
            CacheForm.BackupInfoChanged += OnBackupInfoChanged;

            RefreshMenuState();
            RefreshTimerIntervals();

            _updateTimer.Start();
            _autoBackupTimer.Start();
            _autoRestoreTimer.Start();
        }

        public void InvokeOnUiThread(Action action)
        {
            if (_disposed || action == null)
                return;

            if (_marshalControl.IsHandleCreated)
            {
                if (_marshalControl.InvokeRequired)
                {
                    _marshalControl.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            else
            {
                action();
            }
        }

        public void SetVisible(bool visible)
        {
            if (_disposed)
                return;

            if (visible)
            {
                if (Program.TrayCacheForm != null && !Program.TrayCacheForm.IsDisposed)
                {
                    Program.TrayCacheForm.SetTrayIconVisible(false);
                }

                RefreshMenuState();
                if (_notifyIcon.Icon == null)
                    _notifyIcon.Icon = Resources.MainIcon;
            }

            _notifyIcon.Visible = visible;
        }

        public void RefreshMenuState()
        {
            if (_disposed)
                return;

            bool hasBackupPath = CacheForm.TryGetBackupPath(out _);
            if (!hasBackupPath && (Settings.Default.AutoBackupThumbs || Settings.Default.AutoRestoreThumbs))
            {
                Settings.Default.AutoBackupThumbs = false;
                Settings.Default.AutoRestoreThumbs = false;
                Settings.Default.Save();
            }

            _startWithWindowsItem.Checked = Settings.Default.StartWithWindows;
            _autoBackupItem.Enabled = hasBackupPath;
            _autoRestoreItem.Enabled = hasBackupPath;
            _autoBackupItem.Checked = Settings.Default.AutoBackupThumbs;
            _autoRestoreItem.Checked = Settings.Default.AutoRestoreThumbs;
            _alertOnResetItem.Checked = Settings.Default.ThumbsResetAlert;
        }

        public void RefreshTimerIntervals()
        {
            if (_disposed)
                return;

            int updateMs = Settings.Default.CacheUpdateInterval > 0 ? Settings.Default.CacheUpdateInterval : 250;
            int backupMs = Settings.Default.AutoBackupInterval > 0 ? Settings.Default.AutoBackupInterval : 5000;
            int restoreMs = Settings.Default.AutoRestoreInterval > 0 ? Settings.Default.AutoRestoreInterval : 5000;

            _updateTimer.Interval = Math.Clamp(updateMs, 50, 60000);
            _autoBackupTimer.Interval = Math.Clamp(backupMs, 500, 3600000);
            _autoRestoreTimer.Interval = Math.Clamp(restoreMs, 500, 3600000);
        }

        public async Task InitializeMinimizedSessionAsync()
        {
            if (_disposed)
                return;

            try
            {
                await _cacheService.CheckForCacheResetAsync(
                    checkResetRecognized: true,
                    autoRestoreIfTriggered: Settings.Default.AutoRestoreThumbs);
            }
            catch (Exception ex)
            {
                WriteLine("Startup cache reset check error: " + ex.Message, LoggingFrequency.DebugLogging);
            }

            await CheckForTrayUpdatesAsync(silentIfUpToDate: true);
        }

        private async void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_disposed || _updateTickBusy)
                return;

            _updateTickBusy = true;
            _updateTimer.Stop();
            try
            {
                CacheSnapshot snapshot = await Task.Run(() =>
                {
                    _cacheService.ReconcileCacheGrowthIfNeeded();
                    return _cacheService.GetSnapshot();
                });
                if (_disposed)
                    return;

                SnapshotUpdated?.Invoke(this, snapshot);

                if (Settings.Default.ThumbsResetAlert)
                {
                    await _cacheService.CheckForCacheResetAsync(
                        checkResetRecognized: true,
                        autoRestoreIfTriggered: Settings.Default.AutoRestoreThumbs);
                }
            }
            catch (Exception ex)
            {
                WriteLine("TrayHost telemetry tick error: " + ex.Message, LoggingFrequency.DebugLogging);
            }
            finally
            {
                _updateTickBusy = false;
                if (!_disposed)
                    _updateTimer.Start();
            }
        }

        private async void AutoBackupTimer_Tick(object? sender, EventArgs e)
        {
            if (_disposed || _autoBackupBusy)
                return;

            _autoBackupBusy = true;
            _autoBackupTimer.Stop();
            try
            {
                if (!AboutForm.IsCacheResetCheckInProgress &&
                    !CacheBackupLock.IsHeldByThisProcess &&
                    Settings.Default.AutoBackupThumbs &&
                    CacheForm.TryGetBackupPath(out _))
                {
                    bool shouldBackup = await Task.Run(() => CacheForm.ExplorerCacheSize() > CacheForm.BackupCacheSize());
                    if (shouldBackup && !_disposed)
                    {
                        WriteLine("Auto-backup triggered from TrayHostController", LoggingFrequency.DebugLogging);
                        var progress = new Progress<int>(percent =>
                            ProgressUpdated?.Invoke(this, new TrayProgressEventArgs { Operation = "backup", Percent = percent }));

                        var summary = await _cacheService.BackupAsync(progress);
                        if (summary.Snapshot != null && !_disposed)
                            SnapshotUpdated?.Invoke(this, summary.Snapshot);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine("Auto-backup tick error: " + ex.Message, LoggingFrequency.DebugLogging);
            }
            finally
            {
                _autoBackupBusy = false;
                if (!_disposed)
                    _autoBackupTimer.Start();
            }
        }

        private async void AutoRestoreTimer_Tick(object? sender, EventArgs e)
        {
            if (_disposed || _autoRestoreBusy)
                return;

            _autoRestoreBusy = true;
            _autoRestoreTimer.Stop();
            try
            {
                (long backupSize, long explorerSize) = await Task.Run(() =>
                    (CacheForm.BackupCacheSize(), CacheForm.ExplorerCacheSize()));

                if (explorerSize >= backupSize)
                {
                    CacheForm.AutoRestoreFailedUntilCacheGrows = false;
                }
                else if (!AboutForm.IsCacheResetCheckInProgress &&
                         !CacheBackupLock.IsHeldByThisProcess &&
                         !Settings.Default.ResetRecognized &&
                         !CacheForm.AutoRestoreFailedUntilCacheGrows &&
                         Settings.Default.AutoRestoreThumbs &&
                         backupSize > explorerSize)
                {
                    WriteLine("Auto-restore triggered from TrayHostController", LoggingFrequency.DebugLogging);
                    var progress = new Progress<int>(percent =>
                        ProgressUpdated?.Invoke(this, new TrayProgressEventArgs { Operation = "restore", Percent = percent }));

                    var summary = await _cacheService.RestoreAsync(force: false, progress);
                    bool stillSmaller = await Task.Run(() => CacheForm.ExplorerCacheSize() < CacheForm.BackupCacheSize());
                    if (!summary.Ok || stillSmaller)
                    {
                        CacheForm.AutoRestoreFailedUntilCacheGrows = true;
                    }
                    if (summary.Snapshot != null && !_disposed)
                        SnapshotUpdated?.Invoke(this, summary.Snapshot);
                }
            }
            catch (Exception ex)
            {
                WriteLine("Auto-restore tick error: " + ex.Message, LoggingFrequency.DebugLogging);
            }
            finally
            {
                _autoRestoreBusy = false;
                if (!_disposed)
                    _autoRestoreTimer.Start();
            }
        }

        private void OnStartWithWindowsClick(object? sender, EventArgs e)
        {
            _cacheService.SetStartWithWindows(!Settings.Default.StartWithWindows);
            RefreshMenuState();
        }

        private void OnAutoBackupClick(object? sender, EventArgs e)
        {
            _cacheService.SetAutoFlags(!Settings.Default.AutoBackupThumbs, null, null);
            RefreshMenuState();
        }

        private void OnAutoRestoreClick(object? sender, EventArgs e)
        {
            _cacheService.SetAutoFlags(null, !Settings.Default.AutoRestoreThumbs, null);
            RefreshMenuState();
        }

        private void OnAlertOnResetClick(object? sender, EventArgs e)
        {
            _cacheService.SetAutoFlags(null, null, !Settings.Default.ThumbsResetAlert);
            RefreshMenuState();
        }

        private async void OnCheckForUpdatesClick(object? sender, EventArgs e)
        {
            if (_latestUpdate != null && _latestUpdate.IsUpdateAvailable)
            {
                await InstallTrayUpdateAsync();
            }
            else
            {
                await CheckForTrayUpdatesAsync(silentIfUpToDate: false);
            }
        }

        private async void OnBalloonTipClicked(object? sender, EventArgs e)
        {
            string? action = _balloonAction;
            _balloonAction = null;

            if (string.Equals(action, "update", StringComparison.OrdinalIgnoreCase) &&
                _latestUpdate != null &&
                _latestUpdate.IsUpdateAvailable)
            {
                await InstallTrayUpdateAsync();
                return;
            }

            if (string.Equals(action, "cache", StringComparison.OrdinalIgnoreCase))
            {
                _onNavigateRequest("cache");
                return;
            }

            _onNavigateRequest(null);
        }

        private async Task CheckForTrayUpdatesAsync(bool silentIfUpToDate)
        {
            try
            {
                UpdateStatusDto status = await _updateService.CheckForUpdatesAsync();
                if (_disposed)
                    return;

                _latestUpdate = status;
                if (status.IsUpdateAvailable)
                {
                    _checkUpdatesItem.Text = $"Install Update (v{status.LatestVersion})...";
                    if (_ownedUpdateBoldFont == null)
                        _ownedUpdateBoldFont = new Font(_checkUpdatesItem.Font, FontStyle.Bold);
                    _checkUpdatesItem.Font = _ownedUpdateBoldFont;
                    UpdateAvailableDetected?.Invoke(this, status);

                    if (silentIfUpToDate && _notifyIcon.Visible)
                    {
                        _balloonAction = "update";
                        _notifyIcon.ShowBalloonTip(
                            8000,
                            "WinThumbsPreloader Update Available",
                            $"Version {status.LatestVersion} is available. Click to download and install.",
                            ToolTipIcon.Info);
                    }
                    else if (!silentIfUpToDate)
                    {
                        _onNavigateRequest("settings");
                    }
                }
                else if (!silentIfUpToDate && _notifyIcon.Visible)
                {
                    _balloonAction = null;
                    string msg = string.Equals(status.State, nameof(UpdateState.Error), StringComparison.OrdinalIgnoreCase)
                        ? Resources.AboutForm_WinThumbsPreloader_UpdateCheckFailed
                        : $"{Resources.AboutForm_WinThumbsPreloader_IsUpToDate} (v{WebBridge.GetAppVersion()}).";
                    _notifyIcon.ShowBalloonTip(4000, "WinThumbsPreloader", msg, ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                WriteLine("Tray update check error: " + ex.Message, LoggingFrequency.DebugLogging);
            }
        }

        private async Task InstallTrayUpdateAsync()
        {
            if (_latestUpdate == null || !_latestUpdate.IsUpdateAvailable)
                return;

            var res = await _updateService.DownloadAndInstallAsync(
                restartMinimized: _notifyIcon.Visible,
                onProgress: null,
                onWillExit: () => Program.ExitApplication(0));

            if (!res.Ok && !res.Launched && _notifyIcon.Visible)
            {
                _notifyIcon.ShowBalloonTip(5000, "WinThumbsPreloader Update", res.Message, ToolTipIcon.Warning);
            }
        }

        private void OnCacheSettingsChanged(object? sender, EventArgs e)
        {
            InvokeOnUiThread(() =>
            {
                RefreshMenuState();
                RefreshTimerIntervals();
            });
        }

        private void OnBackupInfoChanged(object? sender, EventArgs e)
        {
            InvokeOnUiThread(RefreshMenuState);
        }

        private void OnServiceCacheResetDetected(object? sender, CacheResetEventArgs e)
        {
            InvokeOnUiThread(() =>
            {
                CacheResetDetected?.Invoke(this, e);
                if (!e.AutoRestored && _notifyIcon.Visible)
                {
                    _balloonAction = "cache";
                    _notifyIcon.ShowBalloonTip(
                        8000,
                        "Thumbnail Cache Reset Detected",
                        "Explorer thumbnail cache was reset. Click to restore from backup.",
                        ToolTipIcon.Warning);
                }
            });
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _cacheService.SettingsChanged -= OnCacheSettingsChanged;
            _cacheService.CacheResetDetected -= OnServiceCacheResetDetected;
            CacheForm.BackupInfoChanged -= OnBackupInfoChanged;

            try
            {
                _updateTimer.Stop();
                _updateTimer.Tick -= UpdateTimer_Tick;
                _updateTimer.Dispose();
            }
            catch { }

            try
            {
                _autoBackupTimer.Stop();
                _autoBackupTimer.Tick -= AutoBackupTimer_Tick;
                _autoBackupTimer.Dispose();
            }
            catch { }

            try
            {
                _autoRestoreTimer.Stop();
                _autoRestoreTimer.Tick -= AutoRestoreTimer_Tick;
                _autoRestoreTimer.Dispose();
            }
            catch { }

            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch { }

            try { _contextMenu.Dispose(); } catch { }
            try { _ownedOpenFont?.Dispose(); } catch { }
            try { _ownedUpdateBoldFont?.Dispose(); } catch { }
            try { _marshalControl.Dispose(); } catch { }
        }
    }
}
