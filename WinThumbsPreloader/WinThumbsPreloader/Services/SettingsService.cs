#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader.Services
{
    public sealed class GeneralSettingsDto
    {
        public bool Admin { get; init; }
        public bool IsRunningAsAdmin { get; init; }
        public bool Multithreaded { get; init; }
        public int ThreadCount { get; init; }
        public string PreloaderThumbnailSizes { get; init; } = "96,256";
        public string PreloaderProcessPriority { get; init; } = "Below Normal";
        public int ProgressDialogUpdateSpeed { get; init; } = 250;
        public bool WaitAfterPreloading { get; init; } = true;
        public int WaitTimeAfterPreloading { get; init; } = 10;
        public string WaitAfterPreloadingUnit { get; init; } = "Seconds";
        public bool WaitAfterCacheBackup { get; init; } = true;
        public int WaitTimeAfterCacheBackup { get; init; } = 10;
        public string WaitAfterCacheUnit { get; init; } = "Seconds";
    }

    public sealed class ExtensionsSettingsDto
    {
        public bool PreloadFolderIcons { get; init; }
        public bool PreloadAllFolders { get; init; }
        public string ExtensionsAutoFormatting { get; init; } = "Disabled";
        public string ExtensionsText { get; init; } = string.Empty;
        public string DefaultExtensionsText { get; init; } = SettingsService.DefaultExtensions;
    }

    public sealed class LogFolderStatusDto
    {
        public string Status { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string NormalizedPath { get; init; } = string.Empty;
        public bool IsRunningAsAdmin { get; init; }
        public bool UsesDefaultPath { get; init; }
        public bool CanUseForLogging { get; init; }
    }

    public sealed class LoggingSettingsDto
    {
        public int LoggingFrequency { get; init; }
        public string LoggerFolderPath { get; init; } = string.Empty;
        public string RecommendedDefaultLogPath { get; init; } = string.Empty;
        public LogFolderStatusDto LogFolderStatus { get; init; } = new();
        public long LogsSizeBytes { get; init; }
        public string LogsSizeFormatted { get; init; } = "0.00 KB";
        public bool AutoDeleteLogsByAge { get; init; }
        public int LogRetentionDays { get; init; } = 30;
        public int MaxLogFileSizeMB { get; init; } = 1024;
    }

    public sealed class AboutInfoDto
    {
        public string AppName { get; init; } = "WinThumbsPreloader";
        public string Version { get; init; } = string.Empty;
        public bool IsRunningAsAdmin { get; init; }
        public string ProjectUrl { get; init; } = AutoUpdater.ProjectWebUrl;
        public string LicenseText { get; init; } = string.Empty;
    }

    public sealed class SettingsStateDto
    {
        public GeneralSettingsDto General { get; init; } = new();
        public ExtensionsSettingsDto Extensions { get; init; } = new();
        public LoggingSettingsDto Logging { get; init; } = new();
        public AboutInfoDto About { get; init; } = new();
    }

    public sealed class GeneralSettingsSaveRequest
    {
        public bool? Admin { get; init; }
        public bool Multithreaded { get; init; } = true;
        public int ThreadCount { get; init; }
        public string PreloaderThumbnailSizes { get; init; } = "96,256";
        public string PreloaderProcessPriority { get; init; } = "Below Normal";
        public int ProgressDialogUpdateSpeed { get; init; } = 250;
        public bool WaitAfterPreloading { get; init; } = true;
        public int WaitTimeAfterPreloading { get; init; } = 10;
        public string WaitAfterPreloadingUnit { get; init; } = "Seconds";
        public bool WaitAfterCacheBackup { get; init; } = true;
        public int WaitTimeAfterCacheBackup { get; init; } = 10;
        public string WaitAfterCacheUnit { get; init; } = "Seconds";
    }

    public sealed class ExtensionsSaveRequest
    {
        public bool PreloadFolderIcons { get; init; }
        public bool PreloadAllFolders { get; init; }
        public string ExtensionsAutoFormatting { get; init; } = "Disabled";
        public string ExtensionsText { get; init; } = string.Empty;
    }

    public sealed class LoggingSaveRequest
    {
        public int LoggingFrequency { get; init; }
        public string? LoggerFolderPath { get; init; }
        public bool AutoDeleteLogsByAge { get; init; }
        public int LogRetentionDays { get; init; } = 30;
        public int? MaxLogFileSizeMB { get; init; }
        public bool ConfirmAdminWarning { get; init; }
    }

    public sealed class SettingsActionResult
    {
        public bool Ok { get; init; }
        public bool Cancelled { get; init; }
        public bool RequiresConfirmation { get; init; }
        public bool RelaunchRequired { get; init; }
        public string Message { get; init; } = string.Empty;
        public SettingsStateDto? State { get; init; }
    }

    public sealed class ExtensionsImportResult
    {
        public bool Ok { get; init; }
        public bool Cancelled { get; init; }
        public string Text { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public sealed class LoggerFolderPickResult
    {
        public bool Cancelled { get; init; }
        public string Path { get; init; } = string.Empty;
        public LogFolderStatusDto? LogFolderStatus { get; init; }
    }

    public sealed class ClearLogsResult
    {
        public bool Ok { get; init; }
        public int DeletedCount { get; init; }
        public int FailedCount { get; init; }
        public string Message { get; init; } = string.Empty;
        public long LogsSizeBytes { get; init; }
        public string LogsSizeFormatted { get; init; } = "0.00 KB";
    }

    [SupportedOSPlatform("windows")]
    public sealed class SettingsService
    {
        public const string DefaultExtensions = "ai, avi, avif, bmp, gif, heic, heif, jpg, jpeg, mkv, mov, mp4, png, psb, psd, svg, tif, tiff, webp";
        public const long MaxImportFileBytes = 1024L * 1024L;
        public const int MaxExtensionsTextChars = 10_000;

        private static readonly HashSet<string> ValidThumbnailSizes = new(StringComparer.Ordinal)
        {
            "16", "32", "48", "96", "256", "768", "1280", "1920", "2560"
        };

        private static readonly HashSet<string> ValidPriorities = new(StringComparer.Ordinal)
        {
            "Realtime", "High", "Above Normal", "Normal", "Below Normal", "Idle"
        };

        private static readonly HashSet<string> ValidTimeUnits = new(StringComparer.Ordinal)
        {
            "Seconds", "Minutes", "Hours"
        };

        public SettingsStateDto GetState()
        {
            Logger.EnsureNoLoggingWithoutConfiguredPath();

            bool isRunningAsAdmin = Program.IsRunningAsAdministrator();
            string logDisplayPath = Logger.GetLogFolderPathForDisplay();
            string normalizedLogFolder = string.Equals(logDisplayPath, "N/A", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : logDisplayPath;

            long logsSize = Logger.GetTotalLogsSize();
            string logsSizeFormatted = Logger.TryGetLogDirectory(out _)
                ? (logsSize > 0 ? CacheService.FormatByteSizeInvariant(logsSize, "Auto") : "0.00 KB")
                : "N/A";

            int savedFreq = Settings.Default.LoggingFrequency;
            if (!Enum.IsDefined(typeof(LoggingFrequency), savedFreq))
                savedFreq = (int)LoggingFrequency.NoLogging;

            return new SettingsStateDto
            {
                General = new GeneralSettingsDto
                {
                    Admin = Settings.Default.Admin,
                    IsRunningAsAdmin = isRunningAsAdmin,
                    Multithreaded = Settings.Default.Multithreaded,
                    ThreadCount = Math.Max(0, Settings.Default.ThreadCount),
                    PreloaderThumbnailSizes = string.IsNullOrWhiteSpace(Settings.Default.PreloaderThumbnailSizes)
                        ? "96,256"
                        : Settings.Default.PreloaderThumbnailSizes,
                    PreloaderProcessPriority = ValidPriorities.Contains(Settings.Default.PreloaderProcessPriority ?? string.Empty)
                        ? Settings.Default.PreloaderProcessPriority!
                        : "Below Normal",
                    ProgressDialogUpdateSpeed = Math.Clamp(Settings.Default.ProgressDialogUpdateSpeed, 100, 1000),
                    WaitAfterPreloading = Settings.Default.WaitAfterPreloading,
                    WaitAfterPreloadingUnit = ValidTimeUnits.Contains(Settings.Default.WaitAfterPreloadingUnit ?? string.Empty)
                        ? Settings.Default.WaitAfterPreloadingUnit!
                        : "Seconds",
                    WaitTimeAfterPreloading = Math.Clamp(
                        Settings.Default.WaitTimeAfterPreloading,
                        0,
                        string.Equals(Settings.Default.WaitAfterPreloadingUnit, "Hours", StringComparison.OrdinalIgnoreCase) ? 24 : 60),
                    WaitAfterCacheBackup = Settings.Default.WaitAfterCacheBackup,
                    WaitAfterCacheUnit = ValidTimeUnits.Contains(Settings.Default.WaitAfterCacheUnit ?? string.Empty)
                        ? Settings.Default.WaitAfterCacheUnit!
                        : "Seconds",
                    WaitTimeAfterCacheBackup = Math.Clamp(
                        Settings.Default.WaitTimeAfterCacheBackup,
                        0,
                        string.Equals(Settings.Default.WaitAfterCacheUnit, "Hours", StringComparison.OrdinalIgnoreCase) ? 24 : 60)
                },
                Extensions = new ExtensionsSettingsDto
                {
                    PreloadFolderIcons = Settings.Default.PreloadFolderIcons,
                    PreloadAllFolders = Settings.Default.PreloadAllFolders,
                    ExtensionsAutoFormatting = NormalizeFormattingName(Settings.Default.ExtensionsAutoFormatting),
                    ExtensionsText = Settings.Default.ExtensionsText ?? string.Empty,
                    DefaultExtensionsText = DefaultExtensions
                },
                Logging = new LoggingSettingsDto
                {
                    LoggingFrequency = savedFreq,
                    LoggerFolderPath = normalizedLogFolder,
                    RecommendedDefaultLogPath = Logger.GetRecommendedDefaultLogPath(),
                    LogFolderStatus = ToLogFolderStatusDto(Logger.GetLogFolderStatusInfo(normalizedLogFolder)),
                    LogsSizeBytes = logsSize,
                    LogsSizeFormatted = logsSizeFormatted,
                    AutoDeleteLogsByAge = Settings.Default.AutoDeleteLogsByAge,
                    LogRetentionDays = Math.Clamp(Settings.Default.LogRetentionDays > 0 ? Settings.Default.LogRetentionDays : 30, 1, 3650),
                    MaxLogFileSizeMB = Math.Clamp(Settings.Default.MaxLogFileSizeMB > 0 ? Settings.Default.MaxLogFileSizeMB : 1024, 1, 10240)
                },
                About = new AboutInfoDto
                {
                    AppName = "WinThumbsPreloader",
                    Version = WebBridge.GetAppVersion(),
                    IsRunningAsAdmin = isRunningAsAdmin,
                    ProjectUrl = AutoUpdater.ProjectWebUrl,
                    LicenseText = LoadLicenseText()
                }
            };
        }

        public SettingsActionResult SaveGeneral(GeneralSettingsSaveRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            string[] parsedSizes = (request.PreloaderThumbnailSizes ?? string.Empty)
                .Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => ValidThumbnailSizes.Contains(s))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (parsedSizes.Length == 0)
            {
                return new SettingsActionResult
                {
                    Ok = false,
                    Message = "Select at least one requested thumbnail size.",
                    State = GetState()
                };
            }

            string priority = ValidPriorities.Contains(request.PreloaderProcessPriority ?? string.Empty)
                ? request.PreloaderProcessPriority!
                : "Below Normal";

            string preloadUnit = ValidTimeUnits.Contains(request.WaitAfterPreloadingUnit ?? string.Empty)
                ? request.WaitAfterPreloadingUnit!
                : "Seconds";

            string cacheUnit = ValidTimeUnits.Contains(request.WaitAfterCacheUnit ?? string.Empty)
                ? request.WaitAfterCacheUnit!
                : "Seconds";

            int preloadMax = preloadUnit == "Hours" ? 24 : 60;
            int cacheMax = cacheUnit == "Hours" ? 24 : 60;

            bool isAdmin = Program.IsRunningAsAdministrator();
            bool wantAdmin = request.Admin ?? Settings.Default.Admin;

            Settings.Default.Admin = wantAdmin;
            Settings.Default.Multithreaded = request.Multithreaded;
            Settings.Default.ThreadCount = Math.Clamp(request.ThreadCount, 0, 512);
            Settings.Default.PreloaderThumbnailSizes = string.Join(",", parsedSizes);
            Settings.Default.PreloaderProcessPriority = priority;
            Settings.Default.ProgressDialogUpdateSpeed = Math.Clamp(request.ProgressDialogUpdateSpeed, 100, 1000);
            Settings.Default.WaitAfterPreloading = request.WaitAfterPreloading;
            Settings.Default.WaitTimeAfterPreloading = Math.Clamp(request.WaitTimeAfterPreloading, 0, preloadMax);
            Settings.Default.WaitAfterPreloadingUnit = preloadUnit;
            Settings.Default.WaitAfterCacheBackup = request.WaitAfterCacheBackup;
            Settings.Default.WaitTimeAfterCacheBackup = Math.Clamp(request.WaitTimeAfterCacheBackup, 0, cacheMax);
            Settings.Default.WaitAfterCacheUnit = cacheUnit;
            Settings.Default.Save();

            bool relaunchRequired = (wantAdmin && !isAdmin) || (!wantAdmin && isAdmin);
            string msg = relaunchRequired
                ? "General settings saved. Relaunch required for privilege mode change."
                : "General settings saved.";

            WriteLine("General settings saved via SettingsService.", LoggingFrequency.GUILogging);
            return new SettingsActionResult
            {
                Ok = true,
                RelaunchRequired = relaunchRequired,
                Message = msg,
                State = GetState()
            };
        }

        public SettingsActionResult ResetGeneralDefaults()
        {
            Settings.Default.Multithreaded = true;
            Settings.Default.ThreadCount = 0;
            Settings.Default.PreloaderThumbnailSizes = "96,256";
            Settings.Default.PreloaderProcessPriority = "Below Normal";
            Settings.Default.ProgressDialogUpdateSpeed = 250;
            Settings.Default.WaitAfterPreloading = true;
            Settings.Default.WaitTimeAfterPreloading = 10;
            Settings.Default.WaitAfterPreloadingUnit = "Seconds";
            Settings.Default.WaitAfterCacheBackup = true;
            Settings.Default.WaitTimeAfterCacheBackup = 10;
            Settings.Default.WaitAfterCacheUnit = "Seconds";
            Settings.Default.Save();

            WriteLine("General settings reset to defaults via SettingsService.", LoggingFrequency.GUILogging);
            return new SettingsActionResult
            {
                Ok = true,
                Message = "General settings reset to defaults.",
                State = GetState()
            };
        }

        public SettingsActionResult RequestAdminElevation()
        {
            if (Program.IsRunningAsAdministrator())
            {
                return new SettingsActionResult
                {
                    Ok = true,
                    Message = "Already running as Administrator.",
                    State = GetState()
                };
            }

            Settings.Default.Admin = true;
            Settings.Default.Save();

            bool started = Program.RestartAsAdmin(["-reopensettings"], showDialogs: false);
            return new SettingsActionResult
            {
                Ok = started,
                Cancelled = !started,
                Message = started
                    ? "Restarting with Administrator privileges..."
                    : "Administrator elevation was cancelled or failed.",
                State = GetState()
            };
        }

        public string OrganizeExtensions(string? text, string? method)
        {
            string normalized = NormalizeFormattingName(method);
            if (!Enum.TryParse(normalized, ignoreCase: true, out SettingsForm.SortingMethod parsed))
            {
                parsed = SettingsForm.SortingMethod.Disabled;
            }

            string raw = text ?? string.Empty;
            if (raw.Length > MaxExtensionsTextChars)
                raw = raw.Substring(0, MaxExtensionsTextChars);

            return SettingsForm.OrganizeExtensions(raw, parsed);
        }

        public SettingsActionResult SaveExtensions(ExtensionsSaveRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            string formatting = NormalizeFormattingName(request.ExtensionsAutoFormatting);
            string formattedText = OrganizeExtensions(request.ExtensionsText, formatting);
            if (formattedText.Length > MaxExtensionsTextChars)
                formattedText = formattedText.Substring(0, MaxExtensionsTextChars);

            Settings.Default.PreloadFolderIcons = request.PreloadFolderIcons;
            Settings.Default.PreloadAllFolders = request.PreloadAllFolders;
            Settings.Default.ExtensionsAutoFormatting = formatting;
            Settings.Default.ExtensionsText = formattedText;
            Settings.Default.Save();

            WriteLine("Extensions settings saved via SettingsService.", LoggingFrequency.GUILogging);
            return new SettingsActionResult
            {
                Ok = true,
                Message = "Extension settings saved.",
                State = GetState()
            };
        }

        public SettingsActionResult RestoreDefaultExtensions()
        {
            string formatting = NormalizeFormattingName(Settings.Default.ExtensionsAutoFormatting);
            string formatted = OrganizeExtensions(DefaultExtensions, formatting);

            Settings.Default.ExtensionsText = formatted;
            Settings.Default.Save();

            return new SettingsActionResult
            {
                Ok = true,
                Message = "Default extensions restored.",
                State = GetState()
            };
        }

        public ExtensionsImportResult ReadExtensionsFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ExtensionsImportResult { Ok = false, Message = "No file path provided." };
            }

            if (!PathSecurity.IsSafeRegularFileWithinLimit(path, MaxImportFileBytes, out _))
            {
                return new ExtensionsImportResult
                {
                    Ok = false,
                    Message = "File is missing, exceeds 1 MB limit, or traverses a reparse point."
                };
            }

            try
            {
                string content = File.ReadAllText(path);
                if (content.Length > MaxExtensionsTextChars)
                    content = content.Substring(0, MaxExtensionsTextChars);

                return new ExtensionsImportResult
                {
                    Ok = true,
                    Text = content,
                    Message = "Extensions imported."
                };
            }
            catch (Exception ex)
            {
                return new ExtensionsImportResult
                {
                    Ok = false,
                    Message = $"Failed to read file: {ex.Message}"
                };
            }
        }

        public ExtensionsImportResult ImportExtensionsFromDialog(IWin32Window? owner)
        {
            using var openDialog = new OpenFileDialog
            {
                Filter = "Text/CSV Files (*.txt;*.csv)|*.txt;*.csv|All Files (*.*)|*.*",
                Title = "Import Extensions"
            };

            DialogResult result = owner != null ? openDialog.ShowDialog(owner) : openDialog.ShowDialog();
            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(openDialog.FileName))
            {
                return new ExtensionsImportResult { Ok = false, Cancelled = true, Message = "Import cancelled." };
            }

            return ReadExtensionsFile(openDialog.FileName);
        }

        public PreloadExportResult ExportExtensionsWithDialog(IWin32Window? owner, string? text)
        {
            using var saveDialog = new SaveFileDialog
            {
                FileName = "ThumbnailExtensions.txt",
                Filter = "Text Files (*.txt)|*.txt|CSV Files (*.csv)|*.csv",
                DefaultExt = "txt",
                AddExtension = true,
                Title = "Save Extensions"
            };

            DialogResult result = owner != null ? saveDialog.ShowDialog(owner) : saveDialog.ShowDialog();
            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(saveDialog.FileName))
            {
                return new PreloadExportResult { Ok = false, Cancelled = true, Message = "Export cancelled." };
            }

            try
            {
                PathSecurity.EnsureSafeFileForWrite(saveDialog.FileName);
                File.WriteAllText(saveDialog.FileName, text ?? string.Empty);
                return new PreloadExportResult
                {
                    Ok = true,
                    Path = saveDialog.FileName,
                    Message = "Extensions exported."
                };
            }
            catch (Exception ex)
            {
                return new PreloadExportResult
                {
                    Ok = false,
                    Message = $"Failed to export extensions: {ex.Message}"
                };
            }
        }

        public SettingsActionResult SaveLogging(LoggingSaveRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            int freqInt = Math.Clamp(request.LoggingFrequency, 0, (int)LoggingFrequency.DebugLogging);
            LoggingFrequency selectedFrequency = (LoggingFrequency)freqInt;
            string selectedPath = (request.LoggerFolderPath ?? string.Empty).Trim();

            if (selectedFrequency != LoggingFrequency.NoLogging && string.IsNullOrWhiteSpace(selectedPath))
            {
                return new SettingsActionResult
                {
                    Ok = false,
                    Message = "Choose a log folder or disable logging before saving.",
                    State = GetState()
                };
            }

            string normalizedPath = string.Empty;
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                if (!Logger.TryValidateLogFolderPath(
                        selectedPath,
                        request.ConfirmAdminWarning,
                        out normalizedPath,
                        out string validationMsg,
                        out bool requiresAdminConfirmation))
                {
                    return new SettingsActionResult
                    {
                        Ok = false,
                        RequiresConfirmation = requiresAdminConfirmation,
                        Message = requiresAdminConfirmation
                            ? validationMsg + " Logging may fail when WinThumbsPreloader is launched normally without administrator privileges. Use this folder anyway?"
                            : validationMsg,
                        State = GetState()
                    };
                }
            }

            Settings.Default.LoggerFolderPath = normalizedPath;
            Settings.Default.LoggingFrequency = (int)selectedFrequency;
            Settings.Default.AutoDeleteLogsByAge = request.AutoDeleteLogsByAge;
            Settings.Default.LogRetentionDays = Math.Clamp(request.LogRetentionDays > 0 ? request.LogRetentionDays : 30, 1, 3650);
            if (request.MaxLogFileSizeMB.HasValue)
            {
                Settings.Default.MaxLogFileSizeMB = Math.Clamp(request.MaxLogFileSizeMB.Value, 1, 10240);
            }
            Settings.Default.Save();

            currentLoggingFrequency = selectedFrequency;
            LogFrequency = selectedFrequency;

            Logger.InitializeLogger();
            Logger.LogCleanupResult cleanupResult = Logger.RunMaintenanceNow();

            string msg = !string.IsNullOrWhiteSpace(Logger.LastError)
                ? "Saved with logger warning: " + Logger.LastError
                : cleanupResult.TotalDeleted > 0
                    ? $"Log settings saved. Deleted {cleanupResult.TotalDeleted} old log(s)."
                    : "Log settings saved.";

            return new SettingsActionResult
            {
                Ok = true,
                Message = msg,
                State = GetState()
            };
        }

        public SettingsActionResult ResetLoggingDefaults()
        {
            currentLoggingFrequency = LoggingFrequency.NoLogging;
            LogFrequency = LoggingFrequency.NoLogging;

            Settings.Default.LoggingFrequency = (int)LoggingFrequency.NoLogging;
            Settings.Default.AutoDeleteLogsByAge = false;
            Settings.Default.LogRetentionDays = 30;
            Settings.Default.MaxLogFileSizeMB = 1024;
            Settings.Default.Save();

            Logger.InitializeLogger();

            return new SettingsActionResult
            {
                Ok = true,
                Message = "Log defaults saved.",
                State = GetState()
            };
        }

        public LoggerFolderPickResult PickLoggerFolder(IWin32Window? owner)
        {
            using var dialog = new FolderBrowserDialog
            {
                UseDescriptionForTitle = true,
                Description = "Select Logger Folder",
                ShowNewFolderButton = true
            };

            string current = Logger.GetLogFolderPathForDisplay();
            if (!string.Equals(current, "N/A", StringComparison.OrdinalIgnoreCase) && Directory.Exists(current))
            {
                dialog.SelectedPath = current;
            }
            else
            {
                string defaultPath = Logger.GetRecommendedDefaultLogPath();
                string? defaultParent = Path.GetDirectoryName(defaultPath);
                if (!string.IsNullOrWhiteSpace(defaultParent) && Directory.Exists(defaultParent))
                    dialog.SelectedPath = defaultParent;
            }

            DialogResult result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return new LoggerFolderPickResult { Cancelled = true };
            }

            return new LoggerFolderPickResult
            {
                Cancelled = false,
                Path = dialog.SelectedPath,
                LogFolderStatus = GetLogFolderStatus(dialog.SelectedPath)
            };
        }

        public LogFolderStatusDto GetLogFolderStatus(string? path)
        {
            return ToLogFolderStatusDto(Logger.GetLogFolderStatusInfo((path ?? string.Empty).Trim()));
        }

        public SettingsActionResult OpenLogsFolder()
        {
            if (!Logger.TryGetLogDirectory(out string logFolder))
            {
                return new SettingsActionResult
                {
                    Ok = false,
                    Message = "Select and save a log folder before opening logs."
                };
            }

            try
            {
                if (PathSecurity.HasReparsePointOrJunctionInPath(logFolder, rejectAnyLeafOrParentReparsePoint: false) ||
                    PathSecurity.IsReparsePoint(logFolder))
                {
                    return new SettingsActionResult
                    {
                        Ok = false,
                        Message = "Refusing to open a reparse-point or junction log folder."
                    };
                }

                Directory.CreateDirectory(logFolder);
                Process.Start(new ProcessStartInfo { FileName = logFolder, UseShellExecute = true });
                return new SettingsActionResult { Ok = true, Message = "Logs folder opened." };
            }
            catch (Exception ex)
            {
                WriteLine("Failed to open logs folder: " + ex.Message, LoggingFrequency.GUILogging);
                return new SettingsActionResult { Ok = false, Message = "Failed to open logs folder." };
            }
        }

        public ClearLogsResult ClearLogs()
        {
            bool ok = Logger.TryClearAllLogs(out int deletedCount, out int failedCount, out string message);
            long totalSize = Logger.GetTotalLogsSize();
            string formatted = Logger.TryGetLogDirectory(out _)
                ? (totalSize > 0 ? CacheService.FormatByteSizeInvariant(totalSize, "Auto") : "0.00 KB")
                : "N/A";

            return new ClearLogsResult
            {
                Ok = ok,
                DeletedCount = deletedCount,
                FailedCount = failedCount,
                Message = message,
                LogsSizeBytes = totalSize,
                LogsSizeFormatted = formatted
            };
        }

        private static string NormalizeFormattingName(string? value)
        {
            string trimmed = (value ?? "Disabled").Trim();
            if (trimmed.Equals("Commas and Spaces", StringComparison.OrdinalIgnoreCase))
                return "CommasAndSpaces";

            return trimmed switch
            {
                "Vertically" => "Vertically",
                "Commas" => "Commas",
                "Spaces" => "Spaces",
                "CommasAndSpaces" => "CommasAndSpaces",
                _ => "Disabled"
            };
        }

        private static LogFolderStatusDto ToLogFolderStatusDto(Logger.LogFolderStatusInfo info)
        {
            return new LogFolderStatusDto
            {
                Status = info.Status.ToString(),
                Message = info.Message ?? string.Empty,
                NormalizedPath = info.NormalizedPath ?? string.Empty,
                IsRunningAsAdmin = info.IsRunningAsAdmin,
                UsesDefaultPath = info.UsesDefaultPath,
                CanUseForLogging = info.CanUseForLogging
            };
        }

        private static string LoadLicenseText()
        {
            try
            {
                Assembly asm = typeof(SettingsService).Assembly;
                string? resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("LICENSE.txt", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(resourceName))
                {
                    using Stream? stream = asm.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        return reader.ReadToEnd();
                    }
                }

                string diskLicense = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
                if (PathSecurity.IsSafeRegularFileWithinLimit(diskLicense, 256 * 1024, out _))
                {
                    return File.ReadAllText(diskLicense);
                }
            }
            catch
            {
                // Fallback below
            }

            return "MIT License - Copyright (c) 2018 Dmitry Bruhov, 2026 Mutahar Farooq, 2026 João Schlickmann";
        }
    }
}
