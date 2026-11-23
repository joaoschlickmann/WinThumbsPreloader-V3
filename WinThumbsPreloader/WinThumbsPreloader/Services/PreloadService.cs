#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using WinThumbsPreloader.Forms;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader.Services
{
    public sealed class PreloadInitialState
    {
        public bool DefaultMultithreaded { get; init; }
        public int DefaultThreadCount { get; init; }
        public IReadOnlyList<BulkPreloaderForm.BulkPreset> SavedPresets { get; init; } = Array.Empty<BulkPreloaderForm.BulkPreset>();
        public string? InvalidLaunchPathWarning { get; init; }
    }

    public sealed class PreloadPathValidationItem
    {
        public string Input { get; init; } = string.Empty;
        public string? NormalizedPath { get; init; }
        public bool Valid { get; init; }
        public bool IsNetwork { get; init; }
    }

    public sealed class PreloadRunRequest
    {
        public string[] Paths { get; init; } = Array.Empty<string>();
        public bool Recursive { get; init; } = true;
        public bool Multithreaded { get; init; } = true;
        public int Threads { get; init; }
        public string Method { get; init; } = "Sequentially";
        public int MaximumInstances { get; init; }
    }

    public sealed class PreloadStartResult
    {
        public bool Ok { get; init; }
        public bool RequiresConfirmation { get; init; }
        public string Message { get; init; } = string.Empty;
        public IReadOnlyList<string> LaunchedPaths { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> InvalidPaths { get; init; } = Array.Empty<string>();
    }

    public sealed class PreloadPresetsResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
        public IReadOnlyList<BulkPreloaderForm.BulkPreset> SavedPresets { get; init; } = Array.Empty<BulkPreloaderForm.BulkPreset>();
    }

    public sealed class PreloadExportResult
    {
        public bool Ok { get; init; }
        public bool Cancelled { get; init; }
        public string Path { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public sealed class PreloadFolderPickResult
    {
        public bool Cancelled { get; init; }
        public string? Path { get; init; }
        public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();
    }

    [SupportedOSPlatform("windows")]
    public sealed class PreloadService
    {
        public const long MaxPresetsFileBytes = 1024L * 1024L;

        public static string DefaultPresetsFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinThumbsPreloader",
                "BulkPreloaderPresets.json");

        public PreloadInitialState GetInitialState()
        {
            string? warning = null;
            Options? options = Program.AppOptions;
            if (options != null && options.invalidPathProvided && !string.IsNullOrWhiteSpace(options.invalidPath))
            {
                warning = options.invalidPath
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();
                options.invalidPathProvided = false;
            }

            return new PreloadInitialState
            {
                DefaultMultithreaded = Settings.Default.Multithreaded,
                DefaultThreadCount = Math.Max(0, Settings.Default.ThreadCount),
                SavedPresets = LoadPresetsFromDisk(),
                InvalidLaunchPathWarning = warning
            };
        }

        public IReadOnlyList<PreloadPathValidationItem> ValidatePaths(string[]? rawPaths)
        {
            if (rawPaths == null || rawPaths.Length == 0)
                return Array.Empty<PreloadPathValidationItem>();

            var items = new List<PreloadPathValidationItem>(rawPaths.Length);
            foreach (string raw in rawPaths)
            {
                string cleaned = (raw ?? string.Empty).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(cleaned))
                    continue;

                string? resolved = NetworkPathResolver.NormalizeAndResolvePath(cleaned);
                bool valid = !string.IsNullOrWhiteSpace(resolved);
                bool isNetwork = valid
                    ? NetworkPathResolver.IsNetworkOrMappedPath(resolved)
                    : NetworkPathResolver.IsNetworkOrMappedPath(cleaned);

                items.Add(new PreloadPathValidationItem
                {
                    Input = cleaned,
                    NormalizedPath = resolved,
                    Valid = valid,
                    IsNetwork = isNetwork
                });
            }

            return items;
        }

        public PreloadStartResult StartPreload(PreloadRunRequest request, bool skipInvalid = false)
        {
            ArgumentNullException.ThrowIfNull(request);

            List<string> rawPaths = (request.Paths ?? Array.Empty<string>())
                .Select(p => (p ?? string.Empty).Trim().Trim('"'))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rawPaths.Count == 0)
            {
                return new PreloadStartResult
                {
                    Ok = false,
                    Message = "Please add at least one folder or file path to preload."
                };
            }

            var validPaths = new List<string>();
            var invalidPaths = new List<string>();

            foreach (string raw in rawPaths)
            {
                string? resolved = NetworkPathResolver.NormalizeAndResolvePath(raw);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    if (!validPaths.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                        validPaths.Add(resolved);
                }
                else
                {
                    invalidPaths.Add(raw);
                }
            }

            if (invalidPaths.Count > 0)
            {
                if (validPaths.Count == 0)
                {
                    return new PreloadStartResult
                    {
                        Ok = false,
                        Message = "None of the provided paths exist or could be accessed.",
                        InvalidPaths = invalidPaths
                    };
                }

                if (!skipInvalid)
                {
                    return new PreloadStartResult
                    {
                        Ok = false,
                        RequiresConfirmation = true,
                        Message = $"Some paths could not be found or accessed. Continue with the remaining {validPaths.Count} valid path(s)?",
                        LaunchedPaths = validPaths,
                        InvalidPaths = invalidPaths
                    };
                }
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                if (request.Recursive)
                    psi.ArgumentList.Add("-r");

                if (request.Multithreaded)
                {
                    int threads = Math.Clamp(request.Threads, 0, 256);
                    psi.ArgumentList.Add(threads > 0 ? $"-m:{threads}" : "-m");
                }

                bool isSequential = string.Equals(request.Method, "Sequentially", StringComparison.OrdinalIgnoreCase);
                if (isSequential)
                {
                    psi.ArgumentList.Add("-maxinstances:1");
                }
                else if (request.MaximumInstances > 0)
                {
                    psi.ArgumentList.Add($"-maxinstances:{Math.Clamp(request.MaximumInstances, 1, 128)}");
                }

                foreach (string validPath in validPaths)
                {
                    psi.ArgumentList.Add(validPath);
                }

                WriteLine($"Launching Preloader for {validPaths.Count} path(s)", LoggingFrequency.GUILogging);
                Process.Start(psi);

                return new PreloadStartResult
                {
                    Ok = true,
                    Message = $"Preloader started for {validPaths.Count} path(s).",
                    LaunchedPaths = validPaths,
                    InvalidPaths = invalidPaths
                };
            }
            catch (Exception ex)
            {
                WriteLine("Failed to start Preloader: " + ex.Message, LoggingFrequency.GUILogging);
                return new PreloadStartResult
                {
                    Ok = false,
                    Message = $"Failed to start WinThumbsPreloader: {ex.Message}",
                    InvalidPaths = invalidPaths
                };
            }
        }

        public PreloadPresetsResult SavePreset(BulkPreloaderForm.BulkPreset preset, string? presetsFilePathOverride = null)
        {
            ArgumentNullException.ThrowIfNull(preset);

            string presetName = (preset.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(presetName))
            {
                return new PreloadPresetsResult
                {
                    Ok = false,
                    Message = "Please enter a name for the preset.",
                    SavedPresets = LoadPresetsFromDisk(presetsFilePathOverride)
                };
            }

            List<string> paths = (preset.Paths ?? new List<string>())
                .Select(p => (p ?? string.Empty).Trim().Trim('"'))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count == 0)
            {
                return new PreloadPresetsResult
                {
                    Ok = false,
                    Message = "Please add at least one path before saving a preset.",
                    SavedPresets = LoadPresetsFromDisk(presetsFilePathOverride)
                };
            }

            List<BulkPreloaderForm.BulkPreset> presets = LoadPresetsFromDisk(presetsFilePathOverride);
            string normalizedMethod = string.Equals(preset.Method, "Simultaneously", StringComparison.OrdinalIgnoreCase)
                ? "Simultaneously"
                : "Sequentially";

            var existing = presets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Name = presetName;
                existing.Paths = paths;
                existing.Recursive = preset.Recursive;
                existing.MultiThreaded = preset.MultiThreaded;
                existing.Threads = Math.Clamp(preset.Threads, 0, 256);
                existing.MaximumInstances = Math.Clamp(preset.MaximumInstances, 0, 128);
                existing.Method = normalizedMethod;
            }
            else
            {
                presets.Add(new BulkPreloaderForm.BulkPreset
                {
                    Name = presetName,
                    Paths = paths,
                    Recursive = preset.Recursive,
                    MultiThreaded = preset.MultiThreaded,
                    Threads = Math.Clamp(preset.Threads, 0, 256),
                    MaximumInstances = Math.Clamp(preset.MaximumInstances, 0, 128),
                    Method = normalizedMethod
                });
            }

            SavePresetsToDisk(presets, presetsFilePathOverride);
            return new PreloadPresetsResult
            {
                Ok = true,
                Message = $"Preset '{presetName}' saved.",
                SavedPresets = presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        public PreloadPresetsResult DeletePreset(string name, string? presetsFilePathOverride = null)
        {
            string targetName = (name ?? string.Empty).Trim();
            List<BulkPreloaderForm.BulkPreset> presets = LoadPresetsFromDisk(presetsFilePathOverride);

            if (string.IsNullOrWhiteSpace(targetName))
            {
                return new PreloadPresetsResult
                {
                    Ok = false,
                    Message = "Preset name is required.",
                    SavedPresets = presets
                };
            }

            int removed = presets.RemoveAll(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                SavePresetsToDisk(presets, presetsFilePathOverride);
            }

            return new PreloadPresetsResult
            {
                Ok = removed > 0,
                Message = removed > 0 ? $"Preset '{targetName}' deleted." : $"Preset '{targetName}' not found.",
                SavedPresets = presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        public PreloadExportResult ExportPaths(IWin32Window? owner, string[]? paths)
        {
            using var saveDialog = new SaveFileDialog
            {
                FileName = "BulkPreloaderPaths.txt",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = "txt",
                Title = "Export Paths"
            };

            DialogResult result = owner != null ? saveDialog.ShowDialog(owner) : saveDialog.ShowDialog();
            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(saveDialog.FileName))
            {
                return new PreloadExportResult { Ok = false, Cancelled = true, Message = "Export cancelled." };
            }

            try
            {
                PathSecurity.EnsureSafeFileForWrite(saveDialog.FileName);
                string content = string.Join(Environment.NewLine, paths ?? Array.Empty<string>());
                File.WriteAllText(saveDialog.FileName, content);
                return new PreloadExportResult
                {
                    Ok = true,
                    Path = saveDialog.FileName,
                    Message = "Paths exported."
                };
            }
            catch (Exception ex)
            {
                WriteLine("Failed to export paths: " + ex.Message, LoggingFrequency.GUILogging);
                return new PreloadExportResult
                {
                    Ok = false,
                    Message = $"Failed to export paths: {ex.Message}"
                };
            }
        }

        public PreloadExportResult ExportPresets(IWin32Window? owner)
        {
            using var saveDialog = new SaveFileDialog
            {
                FileName = "BulkPreloaderPresets.json",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json",
                Title = "Export Presets"
            };

            DialogResult result = owner != null ? saveDialog.ShowDialog(owner) : saveDialog.ShowDialog();
            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(saveDialog.FileName))
            {
                return new PreloadExportResult { Ok = false, Cancelled = true, Message = "Export cancelled." };
            }

            try
            {
                PathSecurity.EnsureSafeFileForWrite(saveDialog.FileName);
                List<BulkPreloaderForm.BulkPreset> presets = LoadPresetsFromDisk();
                string json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(saveDialog.FileName, json);
                return new PreloadExportResult
                {
                    Ok = true,
                    Path = saveDialog.FileName,
                    Message = "Presets exported."
                };
            }
            catch (Exception ex)
            {
                WriteLine("Failed to export presets: " + ex.Message, LoggingFrequency.GUILogging);
                return new PreloadExportResult
                {
                    Ok = false,
                    Message = $"Failed to export presets: {ex.Message}"
                };
            }
        }

        public PreloadFolderPickResult PickFolder(IWin32Window? owner, bool multiSelectFallback)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = multiSelectFallback
                    ? "Select Folder(s) to Add (Local, Mapped Drive, or Network Share)"
                    : "Select Folder to Preload",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                Multiselect = multiSelectFallback
            };

            DialogResult result = owner != null ? folderDialog.ShowDialog(owner) : folderDialog.ShowDialog();
            if (result != DialogResult.OK)
            {
                return new PreloadFolderPickResult { Cancelled = true };
            }

            List<string> picked = new List<string>();
            if (multiSelectFallback && folderDialog.SelectedPaths is { Length: > 0 })
            {
                foreach (string p in folderDialog.SelectedPaths)
                {
                    if (!string.IsNullOrWhiteSpace(p) && !picked.Contains(p, StringComparer.OrdinalIgnoreCase))
                        picked.Add(p);
                }
            }
            else if (!string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
            {
                picked.Add(folderDialog.SelectedPath);
            }

            if (picked.Count == 0)
            {
                return new PreloadFolderPickResult { Cancelled = true };
            }

            return new PreloadFolderPickResult
            {
                Cancelled = false,
                Path = picked[0],
                Paths = picked
            };
        }

        public static List<BulkPreloaderForm.BulkPreset> LoadPresetsFromDisk(string? filePathOverride = null)
        {
            try
            {
                string filePath = string.IsNullOrWhiteSpace(filePathOverride) ? DefaultPresetsFilePath : filePathOverride;
                if (!PathSecurity.IsSafeRegularFileWithinLimit(filePath, MaxPresetsFileBytes, out _))
                    return new List<BulkPreloaderForm.BulkPreset>();

                string json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<List<BulkPreloaderForm.BulkPreset>>(
                    json,
                    new JsonSerializerOptions { MaxDepth = 16 });

                return loaded ?? new List<BulkPreloaderForm.BulkPreset>();
            }
            catch (Exception ex)
            {
                WriteLine("Failed to load Bulk Preloader presets: " + ex.Message, LoggingFrequency.GUILogging);
                return new List<BulkPreloaderForm.BulkPreset>();
            }
        }

        public static void SavePresetsToDisk(List<BulkPreloaderForm.BulkPreset> presets, string? filePathOverride = null)
        {
            try
            {
                string filePath = string.IsNullOrWhiteSpace(filePathOverride) ? DefaultPresetsFilePath : filePathOverride;
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    if (PathSecurity.HasReparsePointOrJunctionInPath(dir, rejectAnyLeafOrParentReparsePoint: false) ||
                        PathSecurity.IsReparsePoint(dir))
                    {
                        throw new IOException("Presets directory cannot be or traverse a reparse point.");
                    }

                    DirectoryInfo dirInfo = Directory.CreateDirectory(dir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Presets directory cannot be a reparse point.");
                }

                PathSecurity.EnsureSafeFileForWrite(filePath);
                string json = JsonSerializer.Serialize(presets ?? new List<BulkPreloaderForm.BulkPreset>(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                WriteLine("Failed to save Bulk Preloader presets: " + ex.Message, LoggingFrequency.GUILogging);
            }
        }
    }
}
