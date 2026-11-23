#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader.Services
{
    public sealed class CacheBucketInfo
    {
        public string Size { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public long SoftLimitBytes { get; init; }
        public bool Saturated { get; init; }
    }

    public sealed class BackupFolderStatusDto
    {
        public string Status { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string NormalizedPath { get; init; } = string.Empty;
        public bool IsRunningAsAdmin { get; init; }
        public bool CanUseForBackup { get; init; }
    }

    public sealed class CacheSnapshot
    {
        public long ExplorerSizeBytes { get; init; }
        public string ExplorerSizeFormatted { get; init; } = string.Empty;
        public long? BackupSizeBytesNullable { get; init; }
        public string BackupSizeFormatted { get; init; } = string.Empty;
        public string BackupFolderPath { get; init; } = string.Empty;
        public BackupFolderStatusDto BackupFolderStatus { get; init; } = new();
        public string RecommendedDefaultBackupPath { get; init; } = string.Empty;
        public string? LastBackupDateTimeIso { get; init; }
        public bool ResetRecognized { get; init; }
        public bool BackupLargerThanExplorer { get; init; }
        public bool LockHeld { get; init; }
        public bool StartWithWindows { get; init; }
        public bool AutoBackup { get; init; }
        public bool AutoRestore { get; init; }
        public bool AlertOnReset { get; init; }
        public bool AutoBackupAfterPreload { get; init; }
        public string BackupRestoreClearSafety { get; init; } = string.Empty;
        public string ExplorerCacheDeletionMethod { get; init; } = string.Empty;
        public string ExplorerCloseFrequency { get; init; } = string.Empty;
        public string CacheSizeFormat { get; init; } = string.Empty;
        public int CacheUpdateIntervalMs { get; init; }
        public int AutoBackupIntervalMs { get; init; }
        public int AutoRestoreIntervalMs { get; init; }
        public IReadOnlyList<CacheBucketInfo> Buckets { get; init; } = Array.Empty<CacheBucketInfo>();
    }

    public sealed class SetBackupFolderResult
    {
        public bool Ok { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public bool DisabledAutoToggles { get; init; }
        public bool RequiresConfirmation { get; init; }
    }

    public sealed class CacheOperationSummary
    {
        public bool Ok { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string FailureReason { get; init; } = string.Empty;
        public CacheSnapshot? Snapshot { get; init; }
    }

    public sealed class CacheFolderPickResult
    {
        public bool Cancelled { get; init; }
        public string Path { get; init; } = string.Empty;
        public BackupFolderStatusDto? Status { get; init; }
    }

    public sealed class CacheResetEventArgs : EventArgs
    {
        public long ExplorerSizeBytes { get; init; }
        public long BackupSizeBytes { get; init; }
        public bool AutoRestored { get; init; }
        public bool RestoreSucceeded { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class AdvancedCacheSettingsRequest
    {
        public bool? AutoBackupAfterPreload { get; init; }
        public string? ExplorerCacheDeletionMethod { get; init; }
        public string? ExplorerCloseFrequency { get; init; }
        public string? BackupRestoreClearSafety { get; init; }
        public string? CacheSizeFormat { get; init; }
        public int? CacheUpdateIntervalMs { get; init; }
        public int? AutoBackupIntervalMs { get; init; }
        public int? AutoRestoreIntervalMs { get; init; }
    }

    [SupportedOSPlatform("windows")]
    public sealed class CacheService
    {
        private static readonly object _settingsSync = new();

        public event EventHandler<CacheResetEventArgs>? CacheResetDetected;
        public event EventHandler? SettingsChanged;

        public void ReconcileCacheGrowthIfNeeded()
        {
            long explorerBytes = CacheForm.ExplorerCacheSize();
            long? backupBytesNullable = CacheForm.BackupCacheSizeNullable();

            if (backupBytesNullable.HasValue &&
                explorerBytes >= backupBytesNullable.Value &&
                !CacheBackupLock.IsHeldByThisProcess &&
                !AboutForm.IsCacheResetCheckInProgress)
            {
                lock (_settingsSync)
                {
                    CacheForm.AutoRestoreFailedUntilCacheGrows = false;
                    if (Settings.Default.ResetRecognized)
                    {
                        Settings.Default.ResetRecognized = false;
                        Settings.Default.Save();
                    }
                    AboutForm.ResetCacheResetFlags();
                }
            }
        }

        public CacheSnapshot GetSnapshot()
        {
            long explorerBytes = CacheForm.ExplorerCacheSize();
            long? backupBytesNullable = CacheForm.BackupCacheSizeNullable();

            string backupPath = CacheForm.TryGetBackupPath(out string configuredPath)
                ? configuredPath
                : string.Empty;

            var statusInfo = CacheForm.GetBackupFolderStatusInfo(backupPath);
            DateTimeOffset? lastBackup = string.IsNullOrWhiteSpace(backupPath)
                ? null
                : CacheForm.GetLastBackupDateTimeForFolder(backupPath);

            string sizeFormat = string.IsNullOrWhiteSpace(Settings.Default.CacheSizeFormat)
                ? "MB"
                : Settings.Default.CacheSizeFormat;

            var buckets = new List<CacheBucketInfo>(CacheForm.ThumbCacheStreamNames.Length);
            foreach (string streamName in CacheForm.ThumbCacheStreamNames)
            {
                long dbSize = CacheForm.GetExplorerDbFileSizeByName(streamName);
                buckets.Add(new CacheBucketInfo
                {
                    Size = streamName,
                    SizeBytes = dbSize,
                    SoftLimitBytes = CacheForm.SingleDbSoftLimitBytes,
                    Saturated = dbSize >= CacheForm.SingleDbSoftLimitBytes
                });
            }

            return new CacheSnapshot
            {
                ExplorerSizeBytes = explorerBytes,
                ExplorerSizeFormatted = FormatByteSizeInvariant(explorerBytes, sizeFormat),
                BackupSizeBytesNullable = backupBytesNullable,
                BackupSizeFormatted = backupBytesNullable.HasValue
                    ? FormatByteSizeInvariant(backupBytesNullable.Value, sizeFormat)
                    : "N/A",
                BackupFolderPath = backupPath,
                BackupFolderStatus = new BackupFolderStatusDto
                {
                    Status = statusInfo.Status.ToString(),
                    Message = statusInfo.Message ?? string.Empty,
                    NormalizedPath = statusInfo.NormalizedPath ?? string.Empty,
                    IsRunningAsAdmin = statusInfo.IsRunningAsAdmin,
                    CanUseForBackup = statusInfo.CanUseForBackup
                },
                RecommendedDefaultBackupPath = CacheForm.GetRecommendedDefaultBackupPath(),
                LastBackupDateTimeIso = lastBackup?.ToString("O"),
                ResetRecognized = Settings.Default.ResetRecognized,
                BackupLargerThanExplorer = backupBytesNullable.HasValue && backupBytesNullable.Value > explorerBytes,
                LockHeld = CacheBackupLock.IsHeldByThisProcess,
                StartWithWindows = Settings.Default.StartWithWindows,
                AutoBackup = Settings.Default.AutoBackupThumbs,
                AutoRestore = Settings.Default.AutoRestoreThumbs,
                AlertOnReset = Settings.Default.ThumbsResetAlert,
                AutoBackupAfterPreload = Settings.Default.AutoBackupAfterPreload,
                BackupRestoreClearSafety = Settings.Default.BackupRestoreClearSafety ?? "Allow Partial Copy",
                ExplorerCacheDeletionMethod = Settings.Default.ExplorerCacheDeletionMethod ?? "Manual Deletion",
                ExplorerCloseFrequency = Settings.Default.ExplorerCloseFrequency ?? "On Every File",
                CacheSizeFormat = sizeFormat,
                CacheUpdateIntervalMs = Settings.Default.CacheUpdateInterval > 0 ? Settings.Default.CacheUpdateInterval : 250,
                AutoBackupIntervalMs = Settings.Default.AutoBackupInterval > 0 ? Settings.Default.AutoBackupInterval : 5000,
                AutoRestoreIntervalMs = Settings.Default.AutoRestoreInterval > 0 ? Settings.Default.AutoRestoreInterval : 5000,
                Buckets = buckets
            };
        }

        public async Task<CacheOperationSummary> BackupAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CacheForm.CacheOperationResult result = await CacheForm.BackupThumbsCacheDetailedAsync(progress, cancellationToken)
                .ConfigureAwait(false);

            return new CacheOperationSummary
            {
                Ok = result.Status != CacheForm.Success.Failure,
                Status = result.Status.ToString(),
                Message = result.Message ?? "Thumbnail cache backup finished.",
                FailureReason = result.FailureReason.ToString(),
                Snapshot = GetSnapshot()
            };
        }

        public async Task<CacheOperationSummary> RestoreAsync(
            bool force,
            IProgress<int>? progress = null)
        {
            if (!CacheForm.TryGetBackupPath(out string backupPath))
            {
                return new CacheOperationSummary
                {
                    Ok = false,
                    Status = CacheForm.Success.Failure.ToString(),
                    FailureReason = CacheForm.CacheOperationFailureReason.BackupPathNotConfigured.ToString(),
                    Message = "No backup folder selected.",
                    Snapshot = GetSnapshot()
                };
            }

            if (!Directory.Exists(backupPath))
            {
                return new CacheOperationSummary
                {
                    Ok = false,
                    Status = CacheForm.Success.Failure.ToString(),
                    FailureReason = CacheForm.CacheOperationFailureReason.InvalidBackupPath.ToString(),
                    Message = "Backup folder not found.",
                    Snapshot = GetSnapshot()
                };
            }

            CacheForm.Success status = await CacheForm.RestoreThumbsCacheAsync(progress, force).ConfigureAwait(false);
            string message = status switch
            {
                CacheForm.Success.Success => force ? "Thumbnail cache force-restored." : "Thumbnail cache restored.",
                CacheForm.Success.PartialSuccess => force ? "Thumbnail cache partially force-restored." : "Thumbnail cache partially restored.",
                _ => force ? "Force restore failed. Retry as admin." : "Restore failed. Try force restore or admin."
            };

            return new CacheOperationSummary
            {
                Ok = status != CacheForm.Success.Failure,
                Status = status.ToString(),
                FailureReason = status == CacheForm.Success.Failure ? "Unknown" : "None",
                Message = message,
                Snapshot = GetSnapshot()
            };
        }

        public async Task<CacheOperationSummary> ClearBackupAsync()
        {
            CacheForm.Success status = await CacheForm.ClearBackupCacheAsync().ConfigureAwait(false);
            string message = status switch
            {
                CacheForm.Success.Success => "Backup cleared.",
                CacheForm.Success.PartialSuccess => "Backup partially cleared.",
                _ => "Failed to clear backup."
            };

            return new CacheOperationSummary
            {
                Ok = status != CacheForm.Success.Failure,
                Status = status.ToString(),
                FailureReason = status == CacheForm.Success.Failure ? "Unknown" : "None",
                Message = message,
                Snapshot = GetSnapshot()
            };
        }

        public async Task<CacheOperationSummary> ClearExplorerCacheAsync()
        {
            CacheForm.Success status = string.Equals(
                    Settings.Default.ExplorerCacheDeletionMethod,
                    "Disk Cleanup",
                    StringComparison.OrdinalIgnoreCase)
                ? await CacheForm.RunThumbnailDiskCleanupAsync().ConfigureAwait(false)
                : await CacheForm.ClearExplorerCacheAsync().ConfigureAwait(false);

            string message = status switch
            {
                CacheForm.Success.Success => "Explorer cache cleared.",
                CacheForm.Success.PartialSuccess => "Explorer cache partially cleared.",
                _ => "Failed to clear explorer cache."
            };

            return new CacheOperationSummary
            {
                Ok = status != CacheForm.Success.Failure,
                Status = status.ToString(),
                FailureReason = status == CacheForm.Success.Failure ? "Unknown" : "None",
                Message = message,
                Snapshot = GetSnapshot()
            };
        }

        public SetBackupFolderResult SetBackupFolder(string? path, bool confirmAdminWarning = false)
        {
            string oldBackupPath = Settings.Default.BackupCacheFolderPath ?? string.Empty;
            string trimmed = path?.Trim() ?? string.Empty;

            bool backupPathChanged = !string.Equals(
                NormalizeForComparison(oldBackupPath),
                NormalizeForComparison(trimmed),
                StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                Settings.Default.BackupCacheFolderPath = string.Empty;
                bool disabledAuto = false;
                if (backupPathChanged && (Settings.Default.AutoBackupThumbs || Settings.Default.AutoRestoreThumbs))
                {
                    Settings.Default.AutoBackupThumbs = false;
                    Settings.Default.AutoRestoreThumbs = false;
                    disabledAuto = true;
                }

                Settings.Default.Save();
                CacheForm.NotifyBackupPathChanged();
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                return new SetBackupFolderResult
                {
                    Ok = true,
                    Status = CacheForm.BackupFolderStatus.NotSelected.ToString(),
                    Message = disabledAuto ? "Backup path cleared. Auto backup/restore disabled." : "Backup path cleared.",
                    Path = string.Empty,
                    DisabledAutoToggles = disabledAuto,
                    RequiresConfirmation = false
                };
            }

            if (!CacheForm.TrySetBackupFolderPath(
                    trimmed,
                    confirmAdminWarning,
                    out string message,
                    out bool requiresAdminConfirmation,
                    out CacheForm.BackupFolderStatusInfo statusInfo))
            {
                return new SetBackupFolderResult
                {
                    Ok = false,
                    Status = statusInfo.Status.ToString(),
                    Message = message,
                    Path = statusInfo.NormalizedPath ?? string.Empty,
                    DisabledAutoToggles = false,
                    RequiresConfirmation = requiresAdminConfirmation
                };
            }

            bool disabledAutoToggles = false;
            if (backupPathChanged && (Settings.Default.AutoBackupThumbs || Settings.Default.AutoRestoreThumbs))
            {
                Settings.Default.AutoBackupThumbs = false;
                Settings.Default.AutoRestoreThumbs = false;
                Settings.Default.Save();
                disabledAutoToggles = true;
                message = "Saved. Auto backup/restore disabled.";
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);

            return new SetBackupFolderResult
            {
                Ok = true,
                Status = statusInfo.Status.ToString(),
                Message = message,
                Path = statusInfo.NormalizedPath ?? string.Empty,
                DisabledAutoToggles = disabledAutoToggles,
                RequiresConfirmation = false
            };
        }

        public CacheOperationSummary SaveAdvancedCacheSettings(AdvancedCacheSettingsRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.AutoBackupAfterPreload.HasValue)
                Settings.Default.AutoBackupAfterPreload = request.AutoBackupAfterPreload.Value;

            if (!string.IsNullOrWhiteSpace(request.ExplorerCacheDeletionMethod) &&
                (request.ExplorerCacheDeletionMethod == "Manual Deletion" || request.ExplorerCacheDeletionMethod == "Disk Cleanup"))
            {
                Settings.Default.ExplorerCacheDeletionMethod = request.ExplorerCacheDeletionMethod;
            }

            if (!string.IsNullOrWhiteSpace(request.ExplorerCloseFrequency) &&
                (request.ExplorerCloseFrequency == "On Every File" || request.ExplorerCloseFrequency == "On Deletion Start"))
            {
                Settings.Default.ExplorerCloseFrequency = request.ExplorerCloseFrequency;
            }

            if (!string.IsNullOrWhiteSpace(request.BackupRestoreClearSafety) &&
                (request.BackupRestoreClearSafety == "Allow Partial Copy" || request.BackupRestoreClearSafety == "Require Full Copy"))
            {
                Settings.Default.BackupRestoreClearSafety = request.BackupRestoreClearSafety;
            }

            if (!string.IsNullOrWhiteSpace(request.CacheSizeFormat) &&
                (request.CacheSizeFormat == "Auto" || request.CacheSizeFormat == "KB" ||
                 request.CacheSizeFormat == "MB" || request.CacheSizeFormat == "GB"))
            {
                Settings.Default.CacheSizeFormat = request.CacheSizeFormat;
            }

            if (request.CacheUpdateIntervalMs.HasValue)
                Settings.Default.CacheUpdateInterval = Math.Clamp(request.CacheUpdateIntervalMs.Value, 50, 60000);

            if (request.AutoBackupIntervalMs.HasValue)
                Settings.Default.AutoBackupInterval = Math.Clamp(request.AutoBackupIntervalMs.Value, 1000, 3600000);

            if (request.AutoRestoreIntervalMs.HasValue)
                Settings.Default.AutoRestoreInterval = Math.Clamp(request.AutoRestoreIntervalMs.Value, 1000, 3600000);

            Settings.Default.Save();

            string message = "Advanced cache settings saved.";
            if (string.Equals(Settings.Default.ExplorerCacheDeletionMethod, "Disk Cleanup", StringComparison.OrdinalIgnoreCase))
            {
                if (!CacheForm.TryConfigureDiskCleanupSageset(out string diskCleanupErr))
                {
                    message = "Settings saved, but Disk Cleanup Sageset failed: " + diskCleanupErr;
                }
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);

            return new CacheOperationSummary
            {
                Ok = true,
                Status = "Saved",
                Message = message,
                Snapshot = GetSnapshot()
            };
        }

        public CacheOperationSummary ResetAdvancedCacheDefaults(string scope = "all")
        {
            lock (_settingsSync)
            {
                string normalizedScope = (scope ?? "all").Trim().ToLowerInvariant();
                if (normalizedScope is "all" or "general")
                {
                    Settings.Default.CacheUpdateInterval = 250;
                    Settings.Default.AutoBackupInterval = 5000;
                    Settings.Default.AutoRestoreInterval = 5000;
                    Settings.Default.CacheSizeFormat = "MB";
                }

                if (normalizedScope is "all" or "cache")
                {
                    Settings.Default.AutoBackupAfterPreload = true;
                    Settings.Default.BackupRestoreClearSafety = "Allow Partial Copy";
                    Settings.Default.ExplorerCacheDeletionMethod = "Manual Deletion";
                    Settings.Default.ExplorerCloseFrequency = "On Every File";
                }

                Settings.Default.Save();
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                if (normalizedScope is "all" or "backup")
                {
                    string defaultPath = CacheForm.GetRecommendedDefaultBackupPath();
                    SetBackupFolderResult pathResult = SetBackupFolder(defaultPath, confirmAdminWarning: true);
                    if (!pathResult.Ok)
                    {
                        return new CacheOperationSummary
                        {
                            Ok = false,
                            Status = "PartialReset",
                            Message = $"Advanced defaults partially restored; backup folder reset failed: {pathResult.Message}",
                            Snapshot = GetSnapshot()
                        };
                    }
                }
            }

            return new CacheOperationSummary
            {
                Ok = true,
                Status = "Reset",
                Message = "Advanced cache defaults restored.",
                Snapshot = GetSnapshot()
            };
        }

        public CacheOperationSummary SetStartWithWindows(bool enabled)
        {
            bool ok = CacheForm.SetStartup(enabled);
            if (ok)
            {
                Settings.Default.StartWithWindows = enabled;
                Settings.Default.Save();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }

            return new CacheOperationSummary
            {
                Ok = ok,
                Status = ok ? "Updated" : "Failed",
                Message = ok
                    ? (enabled ? "Start with Windows enabled." : "Start with Windows disabled.")
                    : "Failed to update startup setting. Retry as admin.",
                Snapshot = GetSnapshot()
            };
        }

        public CacheOperationSummary SetAutoFlags(bool? autoBackup, bool? autoRestore, bool? alertOnReset)
        {
            bool hasBackupPath = CacheForm.TryGetBackupPath(out _);

            if ((autoBackup == true || autoRestore == true) && !hasBackupPath)
            {
                return new CacheOperationSummary
                {
                    Ok = false,
                    Status = "BackupPathRequired",
                    FailureReason = CacheForm.CacheOperationFailureReason.BackupPathNotConfigured.ToString(),
                    Message = "Select a backup folder before enabling Auto Backup or Auto Restore.",
                    Snapshot = GetSnapshot()
                };
            }

            if (autoBackup.HasValue)
                Settings.Default.AutoBackupThumbs = autoBackup.Value;

            if (autoRestore.HasValue)
                Settings.Default.AutoRestoreThumbs = autoRestore.Value;

            if (alertOnReset.HasValue)
                Settings.Default.ThumbsResetAlert = alertOnReset.Value;

            Settings.Default.Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);

            return new CacheOperationSummary
            {
                Ok = true,
                Status = "Updated",
                Message = "Cache automation flags updated.",
                Snapshot = GetSnapshot()
            };
        }

        public CacheOperationSummary OpenCacheFolder()
        {
            bool ok = CacheForm.TryOpenCacheFolder(out string message);
            return new CacheOperationSummary
            {
                Ok = ok,
                Status = ok ? "Opened" : "Failed",
                Message = message
            };
        }

        public CacheOperationSummary OpenBackupFolder()
        {
            bool ok = CacheForm.TryOpenBackupFolder(out string message);
            return new CacheOperationSummary
            {
                Ok = ok,
                Status = ok ? "Opened" : "Failed",
                Message = message
            };
        }

        public CacheFolderPickResult PickBackupFolder(IWin32Window? owner)
        {
            using var dialog = new FolderBrowserDialog
            {
                UseDescriptionForTitle = true,
                Description = "Select Backup Folder",
                ShowNewFolderButton = true
            };

            if (CacheForm.TryGetBackupPath(out string currentBackup) && Directory.Exists(currentBackup))
            {
                dialog.SelectedPath = currentBackup;
            }
            else
            {
                string defaultPath = CacheForm.GetRecommendedDefaultBackupPath();
                string? defaultParent = Path.GetDirectoryName(defaultPath);
                if (!string.IsNullOrWhiteSpace(defaultParent) && Directory.Exists(defaultParent))
                    dialog.SelectedPath = defaultParent;
            }

            DialogResult result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return new CacheFolderPickResult
                {
                    Cancelled = true,
                    Path = string.Empty
                };
            }

            var statusInfo = CacheForm.GetBackupFolderStatusInfo(dialog.SelectedPath);
            return new CacheFolderPickResult
            {
                Cancelled = false,
                Path = dialog.SelectedPath,
                Status = new BackupFolderStatusDto
                {
                    Status = statusInfo.Status.ToString(),
                    Message = statusInfo.Message ?? string.Empty,
                    NormalizedPath = statusInfo.NormalizedPath ?? string.Empty,
                    IsRunningAsAdmin = statusInfo.IsRunningAsAdmin,
                    CanUseForBackup = statusInfo.CanUseForBackup
                }
            };
        }

        public async Task<CacheResetEventArgs?> CheckForCacheResetAsync(
            bool checkResetRecognized,
            bool autoRestoreIfTriggered)
        {
            var outcome = await AboutForm.DetectCacheResetCoreAsync(
                checkResetRecognized,
                autoRestore: autoRestoreIfTriggered,
                logOnly: false).ConfigureAwait(false);

            if (!outcome.Detected || !outcome.TriggeredAlert)
                return null;

            long explorerBytes = CacheForm.ExplorerCacheSize();
            long backupBytes = CacheForm.BackupCacheSize();

            string message = outcome.Restored
                ? (outcome.RestoreSucceeded
                    ? "Explorer cache reset detected and restored automatically from backup."
                    : "Explorer cache reset detected, but automatic restore failed.")
                : "The Explorer thumbnail cache has reset. Restore from backup?";

            var args = new CacheResetEventArgs
            {
                ExplorerSizeBytes = explorerBytes,
                BackupSizeBytes = backupBytes,
                AutoRestored = outcome.Restored,
                RestoreSucceeded = outcome.RestoreSucceeded,
                Message = message
            };

            CacheResetDetected?.Invoke(this, args);
            return args;
        }

        internal static string FormatByteSizeInvariant(long bytes, string? sizeFormat = null)
        {
            string format = string.IsNullOrWhiteSpace(sizeFormat)
                ? Properties.Settings.Default.CacheSizeFormat
                : sizeFormat;

            if (string.Equals(format, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                if (bytes < 1024L * 1024L)
                    format = "KB";
                else if (bytes < 1024L * 1024L * 1024L)
                    format = "MB";
                else
                    format = "GB";
            }

            switch (format.ToUpperInvariant())
            {
                case "KB":
                {
                    double kb = bytes / 1024.0;
                    string kbFmt = kb < 100 ? "N2" : "N0";
                    return $"{kb.ToString(kbFmt, System.Globalization.CultureInfo.InvariantCulture)} KB";
                }
                case "GB":
                {
                    double gb = bytes / (1024.0 * 1024.0 * 1024.0);
                    string gbFmt = gb < 10 ? "N2" : gb < 100 ? "N1" : "N0";
                    return $"{gb.ToString(gbFmt, System.Globalization.CultureInfo.InvariantCulture)} GB";
                }
                default:
                {
                    double mb = bytes / (1024.0 * 1024.0);
                    string mbFmt = mb < 10 ? "N2" : mb < 100 ? "N1" : "N0";
                    return $"{mb.ToString(mbFmt, System.Globalization.CultureInfo.InvariantCulture)} MB";
                }
            }
        }

        private static string NormalizeForComparison(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim();
            }
        }
    }
}
