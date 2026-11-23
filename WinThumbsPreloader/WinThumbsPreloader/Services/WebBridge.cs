#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader.Services
{
    public sealed class WebBridgeResponse
    {
        public string? Id { get; init; }
        public bool Ok { get; init; }
        public object? Data { get; init; }
        public WebBridgeError? Error { get; init; }
    }

    public sealed class WebBridgeError
    {
        public string Code { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// Pure, testable JSON IPC router for WebView2 messages. Enforces origin allowlisting,
    /// payload size bounds (256 KB), JSON depth bounds (MaxDepth = 16), and command allowlisting.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WebBridge
    {
        public const string TrustedHost = "app.winthumbspreloader.local";
        public const string TrustedOrigin = "https://app.winthumbspreloader.local";
        public const int MaxIpcMessageBytes = 262_144; // 256 KB
        public const int MaxJsonDepth = 16;

        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            MaxDepth = MaxJsonDepth,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        private static readonly JsonDocumentOptions ParseOptions = new()
        {
            MaxDepth = MaxJsonDepth,
            AllowTrailingCommas = false
        };

        private readonly CacheService _cacheService;
        private readonly PreloadService _preloadService;
        private readonly ScheduleService _scheduleService;
        private readonly SettingsService _settingsService;
        private readonly OptimizerService _optimizerService;
        private readonly CodecsService _codecsService;
        private readonly UpdateService _updateService;

        private readonly Func<IWin32Window?>? _ownerProvider;
        private readonly Func<Func<object?>, Task<object?>>? _uiInvokeAsync;
        private readonly Func<string, Task>? _emitEventAsync;
        private readonly Action<string>? _onThemeChanged;
        private readonly Action? _onMinimizeToTray;
        private readonly Action? _onQuit;
        private readonly Func<string>? _getInitialView;
        private bool _launchWarningConsumed;

        public WebBridge(
            CacheService cacheService,
            PreloadService preloadService,
            ScheduleService scheduleService,
            SettingsService settingsService,
            OptimizerService optimizerService,
            CodecsService codecsService,
            UpdateService updateService,
            Func<IWin32Window?>? ownerProvider = null,
            Func<Func<object?>, Task<object?>>? uiInvokeAsync = null,
            Func<string, Task>? emitEventAsync = null,
            Action<string>? onThemeChanged = null,
            Action? onMinimizeToTray = null,
            Action? onQuit = null,
            Func<string>? getInitialView = null)
        {
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _preloadService = preloadService ?? throw new ArgumentNullException(nameof(preloadService));
            _scheduleService = scheduleService ?? throw new ArgumentNullException(nameof(scheduleService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _optimizerService = optimizerService ?? throw new ArgumentNullException(nameof(optimizerService));
            _codecsService = codecsService ?? throw new ArgumentNullException(nameof(codecsService));
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

            _ownerProvider = ownerProvider;
            _uiInvokeAsync = uiInvokeAsync;
            _emitEventAsync = emitEventAsync;
            _onThemeChanged = onThemeChanged;
            _onMinimizeToTray = onMinimizeToTray;
            _onQuit = onQuit;
            _getInitialView = getInitialView;
        }

        public static bool IsTrustedWebOrigin(string? sourceUri)
        {
            if (string.IsNullOrWhiteSpace(sourceUri))
                return false;

            if (!Uri.TryCreate(sourceUri.Trim(), UriKind.Absolute, out Uri? uri))
                return false;

            return uri.Scheme == Uri.UriSchemeHttps &&
                   uri.Host.Equals(TrustedHost, StringComparison.OrdinalIgnoreCase) &&
                   uri.IsDefaultPort &&
                   string.IsNullOrEmpty(uri.UserInfo);
        }

        internal static string GetAppVersion()
        {
            Assembly asm = typeof(WebBridge).Assembly;
            string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                int plusIndex = info.IndexOf('+');
                string clean = plusIndex > 0 ? info.Substring(0, plusIndex) : info.Trim();
                if (!string.IsNullOrWhiteSpace(clean))
                    return clean;
            }

            return asm.GetName().Version?.ToString(3) ?? "3.3.0";
        }

        public static string SerializeEvent(string eventName, object? payload)
        {
            var envelope = new
            {
                @event = eventName,
                payload
            };
            return JsonSerializer.Serialize(envelope, JsonOptions);
        }

        public async Task<string> HandleMessageJsonAsync(
            string rawJson,
            string? sourceUri,
            IReadOnlyList<string>? droppedPaths = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsTrustedWebOrigin(sourceUri))
            {
                WriteLine($"Blocked WebView2 IPC from untrusted origin: '{sourceUri}'", LoggingFrequency.GUILogging);
                return SerializeError(null, "untrusted_origin", "Untrusted WebView2 message origin.");
            }

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return SerializeError(null, "invalid_payload", "Empty IPC payload.");
            }

            if (Encoding.UTF8.GetByteCount(rawJson) > MaxIpcMessageBytes)
            {
                return SerializeError(null, "payload_too_large", "IPC payload exceeds 256 KB limit.");
            }

            string? requestId = null;
            string command;
            JsonElement payloadElement = default;
            bool hasPayload = false;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(rawJson, ParseOptions);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return SerializeError(null, "invalid_payload", "IPC message root must be a JSON object.");
                }

                if (root.TryGetProperty("id", out JsonElement idEl))
                {
                    requestId = idEl.ValueKind switch
                    {
                        JsonValueKind.String => idEl.GetString(),
                        JsonValueKind.Number => idEl.GetRawText(),
                        _ => null
                    };
                    if (requestId != null && requestId.Length > 128)
                        requestId = requestId.Substring(0, 128);
                }

                if (!root.TryGetProperty("cmd", out JsonElement cmdEl) || cmdEl.ValueKind != JsonValueKind.String)
                {
                    return SerializeError(requestId, "invalid_command", "Missing required string 'cmd' field.");
                }

                command = cmdEl.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(command) || command.Length > 128)
                {
                    return SerializeError(requestId, "invalid_command", "Command name is invalid.");
                }

                if (root.TryGetProperty("payload", out JsonElement payloadProp))
                {
                    if (payloadProp.ValueKind != JsonValueKind.Object && payloadProp.ValueKind != JsonValueKind.Null && payloadProp.ValueKind != JsonValueKind.Undefined)
                    {
                        return SerializeError(requestId, "invalid_payload", "'payload' must be a JSON object or null.");
                    }

                    if (payloadProp.ValueKind == JsonValueKind.Object)
                    {
                        payloadElement = payloadProp.Clone();
                        hasPayload = true;
                    }
                }
            }
            catch (JsonException ex)
            {
                return SerializeError(requestId, "invalid_json", "Invalid JSON IPC message: " + ex.Message);
            }

            try
            {
                object? result = await DispatchCommandAsync(command, payloadElement, hasPayload, droppedPaths, cancellationToken)
                    .ConfigureAwait(false);
                return SerializeOk(requestId, result);
            }
            catch (IpcCommandException ex)
            {
                return SerializeError(requestId, ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                WriteLine($"IPC command '{command}' failed: {ex.Message}", LoggingFrequency.GUILogging);
                return SerializeError(requestId, "command_failed", ex.Message);
            }
        }

        private async Task<object?> DispatchCommandAsync(
            string command,
            JsonElement payload,
            bool hasPayload,
            IReadOnlyList<string>? droppedPaths,
            CancellationToken cancellationToken)
        {
            switch (command)
            {
                // ---- App & Shell ----
                case "app.getBootstrap":
                {
                    string theme = ThemeHelper.GetSavedTheme();
                    string? invalidWarning = !_launchWarningConsumed &&
                                              Program.AppOptions?.invalidPathProvided == true &&
                                              !string.IsNullOrWhiteSpace(Program.AppOptions.invalidPath)
                        ? Program.AppOptions.invalidPath.Replace("\r", " ").Replace("\n", " ").Trim()
                        : null;
                    if (!string.IsNullOrWhiteSpace(invalidWarning))
                    {
                        _launchWarningConsumed = true;
                    }

                    return new
                    {
                        appName = "WinThumbsPreloader",
                        version = GetAppVersion(),
                        isRunningAsAdmin = Program.IsRunningAsAdministrator(),
                        theme,
                        effectiveDarkMode = ThemeHelper.ResolveEffectiveDarkMode(theme),
                        initialView = _getInitialView?.Invoke() ?? "preload",
                        isWebView2 = true,
                        invalidLaunchPathWarning = invalidWarning
                    };
                }

                case "app.setTheme":
                {
                    string rawTheme = GetString(payload, hasPayload, "theme", "dark");
                    string saved = ThemeHelper.SaveTheme(rawTheme);
                    bool dark = ThemeHelper.ResolveEffectiveDarkMode(saved);
                    _onThemeChanged?.Invoke(saved);
                    return new { ok = true, theme = saved, effectiveDarkMode = dark };
                }

                case "app.minimizeToTray":
                    _onMinimizeToTray?.Invoke();
                    return new { ok = true };

                case "app.quit":
                    _onQuit?.Invoke();
                    return new { ok = true };

                case "app.openExternalUrl":
                {
                    string url = GetString(payload, hasPayload, "url", string.Empty);
                    if (!UpdateService.IsAllowedExternalUrl(url))
                    {
                        throw new IpcCommandException("untrusted_url", "URL is not an allowed GitHub repository or release URL.");
                    }
                    bool opened = _updateService.OpenReleasePage(url);
                    return new { ok = opened, url };
                }

                case "app.checkForUpdates":
                    return await _updateService.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);

                case "app.downloadAndInstallUpdate":
                {
                    bool restartMinimized = GetBool(payload, hasPayload, "restartMinimized", false);
                    return await _updateService.DownloadAndInstallAsync(
                        restartMinimized,
                        onProgress: (percent, status) => _ = EmitEventSafeAsync("update:progress", new { percent, status }),
                        onWillExit: () => _onQuit?.Invoke(),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                // ---- Drag & Drop ----
                case "dragDrop.resolve":
                {
                    var combined = new List<string>();
                    if (droppedPaths != null && droppedPaths.Count > 0)
                    {
                        foreach (string p in droppedPaths)
                        {
                            if (!string.IsNullOrWhiteSpace(p) && !combined.Contains(p, StringComparer.OrdinalIgnoreCase))
                                combined.Add(p);
                        }
                    }
                    else
                    {
                        string[] payloadPaths = GetStringArray(payload, hasPayload, "paths");
                        foreach (string p in payloadPaths)
                        {
                            string trimmed = p.Trim();
                            if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
                                continue;
                            if (!string.IsNullOrWhiteSpace(trimmed) && !combined.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                                combined.Add(trimmed);
                        }
                    }

                    var items = await Task.Run(() => _preloadService.ValidatePaths(combined.ToArray()), cancellationToken)
                        .ConfigureAwait(false);

                    return new
                    {
                        items,
                        paths = items.Where(i => i.Valid && !string.IsNullOrWhiteSpace(i.NormalizedPath))
                            .Select(i => i.NormalizedPath!)
                            .ToArray()
                    };
                }

                // ---- Preload ----
                case "preload.getState":
                    return await Task.Run(() => _preloadService.GetInitialState(), cancellationToken).ConfigureAwait(false);

                case "preload.pickFolder":
                {
                    bool multiSelect = GetBool(payload, hasPayload, "multiSelect", false);
                    return await RunOnUiAsync(owner => _preloadService.PickFolder(owner, multiSelect))
                        .ConfigureAwait(false);
                }

                case "preload.validatePaths":
                {
                    string[] rawPaths = GetStringArray(payload, hasPayload, "paths");
                    var items = await Task.Run(() => _preloadService.ValidatePaths(rawPaths), cancellationToken)
                        .ConfigureAwait(false);
                    return new { items };
                }

                case "preload.start":
                {
                    var req = DeserializePayload<PreloadRunRequest>(payload, hasPayload) ?? new PreloadRunRequest();
                    bool skipInvalid = GetBool(payload, hasPayload, "skipInvalid", false);
                    return await Task.Run(() => _preloadService.StartPreload(req, skipInvalid), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "preload.savePreset":
                {
                    var preset = DeserializePayload<WinThumbsPreloader.Forms.BulkPreloaderForm.BulkPreset>(payload, hasPayload)
                        ?? new WinThumbsPreloader.Forms.BulkPreloaderForm.BulkPreset();
                    return await Task.Run(() => _preloadService.SavePreset(preset), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "preload.deletePreset":
                {
                    string name = GetString(payload, hasPayload, "name", string.Empty);
                    return await Task.Run(() => _preloadService.DeletePreset(name), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "preload.exportPaths":
                {
                    string[] paths = GetStringArray(payload, hasPayload, "paths");
                    return await RunOnUiAsync(owner => _preloadService.ExportPaths(owner, paths))
                        .ConfigureAwait(false);
                }

                case "preload.exportPresets":
                    return await RunOnUiAsync(owner => _preloadService.ExportPresets(owner))
                        .ConfigureAwait(false);

                // ---- Cache ----
                case "cache.getState":
                    return await Task.Run(() => _cacheService.GetSnapshot(), cancellationToken).ConfigureAwait(false);

                case "cache.backup":
                {
                    try
                    {
                        var progress = new Progress<int>(p => _ = EmitEventSafeAsync("cache:progress", new { operation = "backup", percent = p, done = false }));
                        var res = await _cacheService.BackupAsync(progress, cancellationToken).ConfigureAwait(false);
                        if (res.Snapshot != null)
                            await EmitEventSafeAsync("cache:telemetry", res.Snapshot).ConfigureAwait(false);
                        return res;
                    }
                    finally
                    {
                        await EmitEventSafeAsync("cache:progress", new { operation = "backup", percent = 100, done = true }).ConfigureAwait(false);
                    }
                }

                case "cache.restore":
                {
                    bool force = GetBool(payload, hasPayload, "force", false);
                    try
                    {
                        var progress = new Progress<int>(p => _ = EmitEventSafeAsync("cache:progress", new { operation = "restore", percent = p, done = false }));
                        var res = await _cacheService.RestoreAsync(force, progress).ConfigureAwait(false);
                        if (res.Snapshot != null)
                            await EmitEventSafeAsync("cache:telemetry", res.Snapshot).ConfigureAwait(false);
                        return res;
                    }
                    finally
                    {
                        await EmitEventSafeAsync("cache:progress", new { operation = "restore", percent = 100, done = true }).ConfigureAwait(false);
                    }
                }

                case "cache.clearBackup":
                {
                    var res = await _cacheService.ClearBackupAsync().ConfigureAwait(false);
                    if (res.Snapshot != null)
                        await EmitEventSafeAsync("cache:telemetry", res.Snapshot).ConfigureAwait(false);
                    return res;
                }

                case "cache.clearCache":
                {
                    var res = await _cacheService.ClearExplorerCacheAsync().ConfigureAwait(false);
                    if (res.Snapshot != null)
                        await EmitEventSafeAsync("cache:telemetry", res.Snapshot).ConfigureAwait(false);
                    return res;
                }

                case "cache.pickBackupFolder":
                    return await RunOnUiAsync(owner => _cacheService.PickBackupFolder(owner))
                        .ConfigureAwait(false);

                case "cache.setBackupFolder":
                {
                    string? path = GetNullableString(payload, hasPayload, "path");
                    bool confirmAdmin = GetBool(payload, hasPayload, "confirmAdminWarning", false);
                    var res = await Task.Run(() => _cacheService.SetBackupFolder(path, confirmAdmin), cancellationToken)
                        .ConfigureAwait(false);
                    await EmitEventSafeAsync("cache:telemetry", _cacheService.GetSnapshot()).ConfigureAwait(false);
                    return res;
                }

                case "cache.openCacheFolder":
                    return await Task.Run(() => _cacheService.OpenCacheFolder(), cancellationToken).ConfigureAwait(false);

                case "cache.openBackupFolder":
                    return await Task.Run(() => _cacheService.OpenBackupFolder(), cancellationToken).ConfigureAwait(false);

                case "cache.setStartWithWindows":
                {
                    bool enabled = GetBool(payload, hasPayload, "enabled", false);
                    return await Task.Run(() => _cacheService.SetStartWithWindows(enabled), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "cache.setAutoFlags":
                {
                    bool? autoBackup = GetNullableBool(payload, hasPayload, "autoBackup");
                    bool? autoRestore = GetNullableBool(payload, hasPayload, "autoRestore");
                    bool? alertOnReset = GetNullableBool(payload, hasPayload, "alertOnReset");
                    return await Task.Run(() => _cacheService.SetAutoFlags(autoBackup, autoRestore, alertOnReset), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "cache.saveAdvanced":
                {
                    var req = DeserializePayload<AdvancedCacheSettingsRequest>(payload, hasPayload)
                        ?? new AdvancedCacheSettingsRequest();
                    return await Task.Run(() => _cacheService.SaveAdvancedCacheSettings(req), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "cache.resetAdvanced":
                {
                    string scope = GetString(payload, hasPayload, "scope", "all");
                    return await Task.Run(() => _cacheService.ResetAdvancedCacheDefaults(scope), cancellationToken)
                        .ConfigureAwait(false);
                }

                // ---- Schedule ----
                case "schedule.getState":
                    return await Task.Run(() => _scheduleService.GetState(), cancellationToken).ConfigureAwait(false);

                case "schedule.setEnabled":
                {
                    bool enabled = GetBool(payload, hasPayload, "enabled", false);
                    return await Task.Run(() => _scheduleService.SetTaskEnabled(enabled), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "schedule.saveTask":
                {
                    var req = DeserializePayload<ScheduleSaveRequest>(payload, hasPayload) ?? new ScheduleSaveRequest();
                    return await Task.Run(() => _scheduleService.SaveTask(req), cancellationToken).ConfigureAwait(false);
                }

                case "schedule.deleteTask":
                    return await Task.Run(() => _scheduleService.DeleteTask(), cancellationToken).ConfigureAwait(false);

                case "schedule.savePreferences":
                {
                    bool driveSelection = GetBool(payload, hasPayload, "driveSelectionEnabled", true);
                    string driveSort = GetString(payload, hasPayload, "driveSortChoice", "Vertical");
                    bool twentyFourHour = GetBool(payload, hasPayload, "twentyFourHourMode", false);
                    bool detailedTime = GetBool(payload, hasPayload, "detailedTimeMode", false);
                    return await Task.Run(
                        () => _scheduleService.SaveDisplayPreferences(driveSelection, driveSort, twentyFourHour, detailedTime),
                        cancellationToken).ConfigureAwait(false);
                }

                case "schedule.resetPreferences":
                    return await Task.Run(() => _scheduleService.ResetDisplayPreferences(), cancellationToken)
                        .ConfigureAwait(false);

                case "schedule.openTaskScheduler":
                    return await Task.Run(() => _scheduleService.OpenWindowsTaskScheduler(), cancellationToken)
                        .ConfigureAwait(false);

                // ---- Codecs ----
                case "codecs.getState":
                    return await Task.Run(() => _codecsService.GetState(), cancellationToken).ConfigureAwait(false);

                case "codecs.apply":
                {
                    int mask = GetInt(payload, hasPayload, "mask", 0);
                    bool elevateViaUac = GetBool(payload, hasPayload, "elevateViaUac", false);
                    bool syncExtensions = GetBool(payload, hasPayload, "syncExtensions", true);
                    return await Task.Run(() => _codecsService.Apply(mask, elevateViaUac, syncExtensions), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "codecs.uninstallAll":
                    return await Task.Run(() => _codecsService.UninstallAll(), cancellationToken).ConfigureAwait(false);

                // ---- Windows Optimizer ----
                case "optimizer.getState":
                    return await Task.Run(() => _optimizerService.GetState(), cancellationToken).ConfigureAwait(false);

                case "optimizer.getRecommended":
                    return _optimizerService.GetRecommendedProfile();

                case "optimizer.apply":
                {
                    OptimizerProfileDto profile = hasPayload && payload.TryGetProperty("profile", out JsonElement profileEl) && profileEl.ValueKind == JsonValueKind.Object
                        ? (JsonSerializer.Deserialize<OptimizerProfileDto>(profileEl.GetRawText(), JsonOptions) ?? new OptimizerProfileDto())
                        : (DeserializePayload<OptimizerProfileDto>(payload, hasPayload) ?? new OptimizerProfileDto());
                    bool elevateViaUac = GetBool(payload, hasPayload, "elevateViaUac", false);
                    return await Task.Run(() => _optimizerService.Apply(profile, elevateViaUac), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "optimizer.restoreDefaults":
                    return await Task.Run(() => _optimizerService.RestoreDefaults(), cancellationToken).ConfigureAwait(false);

                case "optimizer.registerContextMenu":
                    return await Task.Run(() => _optimizerService.RegisterContextMenu(), cancellationToken).ConfigureAwait(false);

                case "optimizer.unregisterContextMenu":
                    return await Task.Run(() => _optimizerService.UnregisterContextMenu(), cancellationToken).ConfigureAwait(false);

                case "optimizer.rebuildIconCache":
                    return await Task.Run(() => _optimizerService.RebuildIconCacheOnly(), cancellationToken).ConfigureAwait(false);

                case "optimizer.restartExplorer":
                    return await Task.Run(() => _optimizerService.RestartExplorer(), cancellationToken).ConfigureAwait(false);

                // ---- Settings ----
                case "settings.getState":
                    return await Task.Run(() => _settingsService.GetState(), cancellationToken).ConfigureAwait(false);

                case "settings.saveGeneral":
                {
                    var req = DeserializePayload<GeneralSettingsSaveRequest>(payload, hasPayload)
                        ?? new GeneralSettingsSaveRequest();
                    return await Task.Run(() => _settingsService.SaveGeneral(req), cancellationToken).ConfigureAwait(false);
                }

                case "settings.resetGeneral":
                    return await Task.Run(() => _settingsService.ResetGeneralDefaults(), cancellationToken).ConfigureAwait(false);

                case "settings.requestAdminElevation":
                    return await RunOnUiAsync(_ => _settingsService.RequestAdminElevation())
                        .ConfigureAwait(false);

                case "settings.organizeExtensions":
                {
                    string text = GetString(payload, hasPayload, "text", string.Empty);
                    string method = GetString(payload, hasPayload, "method", "Disabled");
                    return new { text = _settingsService.OrganizeExtensions(text, method) };
                }

                case "settings.saveExtensions":
                {
                    var req = DeserializePayload<ExtensionsSaveRequest>(payload, hasPayload) ?? new ExtensionsSaveRequest();
                    return await Task.Run(() => _settingsService.SaveExtensions(req), cancellationToken).ConfigureAwait(false);
                }

                case "settings.restoreDefaultExtensions":
                    return await Task.Run(() => _settingsService.RestoreDefaultExtensions(), cancellationToken).ConfigureAwait(false);

                case "settings.importExtensions":
                    return await RunOnUiAsync(owner => _settingsService.ImportExtensionsFromDialog(owner))
                        .ConfigureAwait(false);

                case "settings.importDroppedExtensions":
                {
                    // Strictly consumes native CoreWebView2File paths extracted from e.AdditionalObjects (never arbitrary JS paths).
                    string? droppedFile = droppedPaths?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                    if (string.IsNullOrWhiteSpace(droppedFile))
                    {
                        return new ExtensionsImportResult { Ok = false, Message = "No dropped file was received." };
                    }
                    return await Task.Run(() => _settingsService.ReadExtensionsFile(droppedFile), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "settings.exportExtensions":
                {
                    string text = GetString(payload, hasPayload, "text", string.Empty);
                    return await RunOnUiAsync(owner => _settingsService.ExportExtensionsWithDialog(owner, text))
                        .ConfigureAwait(false);
                }

                case "settings.saveLogging":
                {
                    var req = DeserializePayload<LoggingSaveRequest>(payload, hasPayload) ?? new LoggingSaveRequest();
                    return await Task.Run(() => _settingsService.SaveLogging(req), cancellationToken).ConfigureAwait(false);
                }

                case "settings.resetLogging":
                    return await Task.Run(() => _settingsService.ResetLoggingDefaults(), cancellationToken).ConfigureAwait(false);

                case "settings.pickLoggerFolder":
                    return await RunOnUiAsync(owner => _settingsService.PickLoggerFolder(owner))
                        .ConfigureAwait(false);

                case "settings.getLogFolderStatus":
                {
                    string path = GetString(payload, hasPayload, "path", string.Empty);
                    return await Task.Run(() => _settingsService.GetLogFolderStatus(path), cancellationToken)
                        .ConfigureAwait(false);
                }

                case "settings.openLogsFolder":
                    return await Task.Run(() => _settingsService.OpenLogsFolder(), cancellationToken).ConfigureAwait(false);

                case "settings.clearLogs":
                    return await Task.Run(() => _settingsService.ClearLogs(), cancellationToken).ConfigureAwait(false);

                default:
                    throw new IpcCommandException("unknown_command", $"Unsupported IPC command: '{command}'.");
            }
        }

        private async Task<T?> RunOnUiAsync<T>(Func<IWin32Window?, T> action)
        {
            if (_uiInvokeAsync != null)
            {
                object? res = await _uiInvokeAsync(() => action(_ownerProvider?.Invoke())).ConfigureAwait(false);
                return res is T typed ? typed : default;
            }

            return action(_ownerProvider?.Invoke());
        }

        private async Task EmitEventSafeAsync(string eventName, object? payload)
        {
            if (_emitEventAsync == null)
                return;

            try
            {
                string json = SerializeEvent(eventName, payload);
                await _emitEventAsync(json).ConfigureAwait(false);
            }
            catch
            {
                // Ignore WebView2 closed/disposed event push failures
            }
        }

        private static T? DeserializePayload<T>(JsonElement payload, bool hasPayload)
        {
            if (!hasPayload)
                return default;

            return JsonSerializer.Deserialize<T>(payload.GetRawText(), JsonOptions);
        }

        private static string GetString(JsonElement payload, bool hasPayload, string propertyName, string fallback)
        {
            if (hasPayload &&
                payload.TryGetProperty(propertyName, out JsonElement prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? fallback;
            }
            return fallback;
        }

        private static string? GetNullableString(JsonElement payload, bool hasPayload, string propertyName)
        {
            if (!hasPayload || !payload.TryGetProperty(propertyName, out JsonElement prop))
                return null;

            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
        }

        private static bool GetBool(JsonElement payload, bool hasPayload, string propertyName, bool fallback)
        {
            if (hasPayload && payload.TryGetProperty(propertyName, out JsonElement prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
            return fallback;
        }

        private static bool? GetNullableBool(JsonElement payload, bool hasPayload, string propertyName)
        {
            if (hasPayload && payload.TryGetProperty(propertyName, out JsonElement prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
            return null;
        }

        private static int GetInt(JsonElement payload, bool hasPayload, string propertyName, int fallback)
        {
            if (hasPayload &&
                payload.TryGetProperty(propertyName, out JsonElement prop) &&
                prop.ValueKind == JsonValueKind.Number &&
                prop.TryGetInt32(out int val))
            {
                return val;
            }
            return fallback;
        }

        private static string[] GetStringArray(JsonElement payload, bool hasPayload, string propertyName)
        {
            if (!hasPayload ||
                !payload.TryGetProperty(propertyName, out JsonElement prop) ||
                prop.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (JsonElement item in prop.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(s);
                }
            }
            return list.ToArray();
        }

        private static string SerializeOk(string? id, object? data)
        {
            var response = new WebBridgeResponse
            {
                Id = id,
                Ok = true,
                Data = data,
                Error = null
            };
            return JsonSerializer.Serialize(response, JsonOptions);
        }

        private static string SerializeError(string? id, string code, string message)
        {
            var response = new WebBridgeResponse
            {
                Id = id,
                Ok = false,
                Data = null,
                Error = new WebBridgeError
                {
                    Code = code,
                    Message = message
                }
            };
            return JsonSerializer.Serialize(response, JsonOptions);
        }

        private sealed class IpcCommandException : Exception
        {
            public string Code { get; }

            public IpcCommandException(string code, string message) : base(message)
            {
                Code = code;
            }
        }
    }
}
