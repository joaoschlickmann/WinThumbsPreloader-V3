#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader
{
    [Flags]
    public enum CodecFamilyId
    {
        None = 0,
        PsdPsb = 1 << 0,
        Pdf = 1 << 1,
        Illustrator = 1 << 2,
        Eps = 1 << 3,
        InDesign = 1 << 4,
        AdobeXd = 1 << 5,
        Svg = 1 << 6,
        CorelDraw = 1 << 7,
        Affinity = 1 << 8,
        CameraRaw = 1 << 9,
        All = 0x3FF
    }

    public sealed class CodecFamilyInfo
    {
        public CodecFamilyId Id { get; init; }
        public int BitIndex { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Clsid { get; init; } = string.Empty;
        public string[] Extensions { get; init; } = Array.Empty<string>();
        public int Treatment { get; init; }
        public int ThumbnailCutoff { get; init; }
        public bool DefaultRecommended { get; init; } = true;
    }

    public sealed class ExtensionHandlerStatus
    {
        public string Extension { get; init; } = string.Empty;
        public string? ActiveClsid { get; init; }
        public string ActiveHandlerName { get; init; } = "None";
        public string? ActiveDllPath { get; init; }
        public string? UserChoiceProgId { get; init; }
        public bool IsWinThumbsPreloader { get; init; }
        public bool IsConflict { get; init; }
    }

    public sealed class CodecFamilyStatus
    {
        public CodecFamilyInfo Family { get; init; } = null!;
        public IReadOnlyList<ExtensionHandlerStatus> ExtensionStatuses { get; init; } = Array.Empty<ExtensionHandlerStatus>();
        public bool IsEnabled { get; init; }
        public bool IsPartiallyEnabled { get; init; }
        public bool HasExternalHandler { get; init; }
        public string SummaryHandlerName { get; init; } = "None";
    }

    public sealed class CodecApplyResult
    {
        public int AppliedMask { get; set; }
        public int EnabledFamiliesCount { get; set; }
        public int DisabledFamiliesCount { get; set; }
        public bool MachineScopeApplied { get; set; }
        public bool UserScopeApplied { get; set; }
        public bool BackupRestored { get; set; }
        public bool RequiresElevationForMachineScope { get; set; }
    }

    [SupportedOSPlatform("windows")]
    public static class CodecManager
    {
        public const int AllCodecsMask = (int)CodecFamilyId.All;
        public const string ThumbnailProviderIid = "{E357FCCD-A995-4576-B01F-234630154E96}";
        private const string SurrogateAppId = "{AB8902B4-09CA-4BB6-B78D-A8F59079A8D5}";
        private const string ShellApprovedKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved";
        private const string FileExtsBaseKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";
        private const string WindowsPhotoThumbnailClsid = "{C7657C4A-9F68-40FA-A4DF-96BC08EB3551}";
        private const int MaxBackupFileBytes = 1_048_576;

        public static readonly IReadOnlyList<CodecFamilyInfo> Families =
        [
            new CodecFamilyInfo
            {
                Id = CodecFamilyId.PsdPsb,
                BitIndex = 0,
                DisplayName = "Adobe Photoshop (PSD, PSB)",
                Description = "Embedded 1036/1033 preview + lazy PSB row-skipping + ICC CMYK/RGB/Lab",
                Clsid = "{A7F3C101-6B4E-4D28-9E15-8C4A2F01B301}",
                Extensions = [".psd", ".psb"],
                Treatment = 0,
                ThumbnailCutoff = 0
            },
            new CodecFamilyInfo
            {
                Id = CodecFamilyId.Pdf,
                BitIndex = 1,
                DisplayName = "PDF Documents (PDFium)",
                Description = "High-fidelity CMYK & transparency rendering via PDFium",
                Clsid = "{A7F3C102-6B4E-4D28-9E15-8C4A2F01B302}",
                Extensions = [".pdf"],
                Treatment = 1,
                ThumbnailCutoff = 0
            },
            new CodecFamilyInfo
            {
                Id = CodecFamilyId.Illustrator,
                BitIndex = 2,
                DisplayName = "Adobe Illustrator (AI)",
                Description = "PDF-compatible AI via PDFium + legacy PostScript AI fallback",
                Clsid = "{A7F3C103-6B4E-4D28-9E15-8C4A2F01B303}",
                Extensions = [".ai"],
                Treatment = 1,
                ThumbnailCutoff = 0
            },
            new CodecFamilyInfo
            {
                Id = CodecFamilyId.Eps,
                BitIndex = 3,
                DisplayName = "Encapsulated PostScript (EPS)",
                Description = "DOS TIFF/WMF + EPSI + clean-room PostScript Level 3 fallback",
                Clsid = "{A7F3C104-6B4E-4D28-9E15-8C4A2F01B304}",
                Extensions = [".eps"],
                Treatment = 1,
                ThumbnailCutoff = 0
            },
            new CodecFamilyInfo
            {
                Id = CodecFamilyId.InDesign,
                BitIndex = 4,
                DisplayName = "Adobe InDesign (INDD, IDML)",
                Description = "Streaming XMP page preview extraction + IDML archive preview",
                Clsid = "{A7F3C105-6B4E-4D28-9E15-8C4A2F01B305}",
                Extensions = [".indd", ".idml"],
                Treatment = 1,
                ThumbnailCutoff = 0
            },
            new CodecFamilyInfo
            {
                Id = CodecFamilyId.AdobeXd,
                BitIndex = 5,
                DisplayName = "Adobe XD (XD)",
                Description = "ZIP thumbnail.png extraction with zip-bomb cap",
                Clsid = "{A7F3C106-6B4E-4D28-9E15-8C4A2F01B306}",
                Extensions = [".xd"],
                Treatment = 0,
                ThumbnailCutoff = 0
            },
            new CodecFamilyInfo
            {
                Id = CodecFamilyId.Svg,
                BitIndex = 6,
                DisplayName = "SVG & SVGZ Vector Graphics",
                Description = "Fast resvg vector rasterizer (external URLs blocked)",
                Clsid = "{A7F3C107-6B4E-4D28-9E15-8C4A2F01B307}",
                Extensions = [".svg", ".svgz"],
                Treatment = 0,
                ThumbnailCutoff = 0
            },
            new CodecFamilyInfo
            {
                Id = CodecFamilyId.CorelDraw,
                BitIndex = 7,
                DisplayName = "CorelDRAW (CDR)",
                Description = "RIFF DISP BMP & ZIP package thumbnail extraction (v4-v27)",
                Clsid = "{A7F3C108-6B4E-4D28-9E15-8C4A2F01B308}",
                Extensions = [".cdr"],
                Treatment = 0,
                ThumbnailCutoff = 0
            },
            new CodecFamilyInfo
            {
                Id = CodecFamilyId.Affinity,
                BitIndex = 8,
                DisplayName = "Affinity Suite (AF, AFPHOTO, AFDESIGN, AFPUB)",
                Description = "Native Affinity container Thmb PNG preview extraction",
                Clsid = "{A7F3C109-6B4E-4D28-9E15-8C4A2F01B309}",
                Extensions = [".afphoto", ".afdesign", ".afpub", ".af"],
                Treatment = 0,
                ThumbnailCutoff = 0
            },
            new CodecFamilyInfo
            {
                Id = CodecFamilyId.CameraRaw,
                BitIndex = 9,
                DisplayName = "Camera RAW (DNG, CR2, CR3, NEF, ARW, RAF...)",
                Description = "Embedded JPEG/TIFF/JXL preview + EXIF orientation across 20 RAW formats",
                Clsid = "{A7F3C10A-6B4E-4D28-9E15-8C4A2F01B30A}",
                Extensions =
                [
                    ".dng", ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".raf", ".orf", ".rw2",
                    ".pef", ".srw", ".3fr", ".fff", ".mrw", ".x3f", ".erf", ".mef", ".rwl", ".iiq"
                ],
                Treatment = 0,
                ThumbnailCutoff = 0
            }
        ];

        private static readonly HashSet<string> OurClsids = new(
            Families.Select(f => f.Clsid),
            StringComparer.OrdinalIgnoreCase);

        public static bool IsOurClsid(string? clsid)
        {
            return !string.IsNullOrWhiteSpace(clsid) && OurClsids.Contains(clsid.Trim());
        }

        public static string BuildApplyCliArguments(int mask)
        {
            return $"-apply-codecs -codecs-mask:0x{mask & AllCodecsMask:X3}";
        }

        public static string GetBundledCodecsDirectory()
        {
            return Path.Combine(AppContext.BaseDirectory, "Codecs");
        }

        public static string GetBundledCodecDllPath()
        {
            return Path.Combine(GetBundledCodecsDirectory(), "WinThumbsCodecs.dll");
        }

        public static bool AreBundledCodecsPresent(out string? errorMessage)
        {
            string codecsDir = GetBundledCodecsDirectory();
            string mainDll = GetBundledCodecDllPath();
            string pdfiumDll = Path.Combine(codecsDir, "pdfium.dll");

            if (!Directory.Exists(codecsDir) || PathSecurity.HasReparsePointOrJunctionInPath(codecsDir))
            {
                errorMessage = $"Bundled Codecs folder is missing or unsafe: {codecsDir}";
                return false;
            }

            if (!File.Exists(mainDll) || PathSecurity.IsReparsePoint(mainDll))
            {
                errorMessage = $"WinThumbsCodecs.dll was not found at {mainDll}";
                return false;
            }

            if (!File.Exists(pdfiumDll) || PathSecurity.IsReparsePoint(pdfiumDll))
            {
                errorMessage = $"pdfium.dll was not found at {pdfiumDll}";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public static string GetDefaultBackupFilePath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "WinThumbsPreloader", "codecs-backup.json");
        }

        public static IReadOnlyList<CodecFamilyStatus> DetectAllStatuses(
            WindowsOptimizer.RegistryHiveScope? scope = null)
        {
            var effectiveScope = scope ?? WindowsOptimizer.RegistryHiveScope.SystemDefault;
            var result = new List<CodecFamilyStatus>(Families.Count);

            foreach (var family in Families)
            {
                var extStatuses = new List<ExtensionHandlerStatus>(family.Extensions.Length);
                foreach (string ext in family.Extensions)
                {
                    extStatuses.Add(DetectExtensionStatus(ext, effectiveScope));
                }

                bool allOurs = extStatuses.All(s => s.IsWinThumbsPreloader);
                bool anyOurs = extStatuses.Any(s => s.IsWinThumbsPreloader);
                bool anyConflict = extStatuses.Any(s => s.IsConflict);

                string summaryName;
                if (allOurs)
                {
                    summaryName = "WinThumbsPreloader Codecs";
                }
                else
                {
                    var distinctNames = extStatuses
                        .Select(s => s.ActiveHandlerName)
                        .Where(n => !string.Equals(n, "None", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (distinctNames.Count == 0)
                    {
                        summaryName = "None";
                    }
                    else if (distinctNames.Count == 1)
                    {
                        summaryName = distinctNames[0];
                    }
                    else
                    {
                        summaryName = string.Join(" / ", distinctNames.Take(2)) + (distinctNames.Count > 2 ? " ..." : string.Empty);
                    }
                }

                result.Add(new CodecFamilyStatus
                {
                    Family = family,
                    ExtensionStatuses = extStatuses,
                    IsEnabled = allOurs,
                    IsPartiallyEnabled = anyOurs && !allOurs,
                    HasExternalHandler = anyConflict,
                    SummaryHandlerName = summaryName
                });
            }

            return result;
        }

        public static int GetCurrentlyEnabledMask(WindowsOptimizer.RegistryHiveScope? scope = null)
        {
            var statuses = DetectAllStatuses(scope);
            int mask = 0;
            foreach (var status in statuses)
            {
                if (status.IsEnabled || status.IsPartiallyEnabled)
                {
                    mask |= 1 << status.Family.BitIndex;
                }
            }
            return mask;
        }

        public static bool HasMachineLevelCodecsOrBackup(
            WindowsOptimizer.RegistryHiveScope? scope = null,
            string? backupPathOverride = null)
        {
            var effectiveScope = scope ?? WindowsOptimizer.RegistryHiveScope.SystemDefault;
            foreach (var family in Families)
            {
                using var clsidKey = effectiveScope.OpenMachineKey($@"Software\Classes\CLSID\{family.Clsid}", writable: false);
                if (clsidKey != null)
                {
                    return true;
                }
            }

            string backupPath = string.IsNullOrWhiteSpace(backupPathOverride)
                ? GetDefaultBackupFilePath()
                : backupPathOverride;
            var manifest = LoadBackupManifest(backupPath);
            foreach (var entry in manifest.Entries.Values)
            {
                if (string.Equals(entry.Hive, "HKLM", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static CodecApplyResult ApplyCodecSelection(
            int mask,
            WindowsOptimizer.RegistryHiveScope? scope = null,
            string? backupPathOverride = null,
            string? dllPathOverride = null,
            bool broadcastShellChange = true)
        {
            int sanitizedMask = mask & AllCodecsMask;
            var effectiveScope = scope ?? WindowsOptimizer.RegistryHiveScope.SystemDefault;
            string backupPath = string.IsNullOrWhiteSpace(backupPathOverride)
                ? GetDefaultBackupFilePath()
                : backupPathOverride;
            string dllPath = string.IsNullOrWhiteSpace(dllPathOverride)
                ? GetBundledCodecDllPath()
                : dllPathOverride;

            if (sanitizedMask != 0)
            {
                if (!File.Exists(dllPath) || PathSecurity.HasReparsePointOrJunctionInPath(dllPath))
                {
                    throw new InvalidOperationException($"Codec DLL missing or blocked by path security: {dllPath}");
                }
            }

            var manifest = LoadBackupManifest(backupPath);
            var applyResult = new CodecApplyResult
            {
                AppliedMask = sanitizedMask,
                RequiresElevationForMachineScope = !effectiveScope.MachineWritable
            };

            foreach (var family in Families)
            {
                bool enable = (sanitizedMask & (1 << family.BitIndex)) != 0;
                if (enable)
                {
                    EnableFamily(family, dllPath, effectiveScope, manifest, applyResult);
                    applyResult.EnabledFamiliesCount++;
                }
                else
                {
                    DisableFamily(family, effectiveScope, manifest, applyResult);
                    applyResult.DisabledFamiliesCount++;
                }
            }

            SaveOrDeleteBackupManifest(backupPath, manifest);

            if (effectiveScope.IsSystemDefault)
            {
                RecycleThumbnailSurrogateHosts();
                if (broadcastShellChange)
                {
                    WindowsOptimizer.BroadcastShellSettingsChange();
                }
            }

            return applyResult;
        }

        public static CodecApplyResult UninstallAllCodecs(
            WindowsOptimizer.RegistryHiveScope? scope = null,
            string? backupPathOverride = null,
            bool broadcastShellChange = true)
        {
            return ApplyCodecSelection(
                mask: 0,
                scope: scope,
                backupPathOverride: backupPathOverride,
                dllPathOverride: null,
                broadcastShellChange: broadcastShellChange);
        }

        public static string MergeEnabledCodecExtensionsIntoSettings(int mask, string? currentExtensionsText = null)
        {
            int sanitizedMask = mask & AllCodecsMask;
            string baseText = currentExtensionsText ?? Settings.Default.ExtensionsText ?? string.Empty;

            var existing = baseText
                .Split([',', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(ext => ext.Trim().TrimStart('.').ToLowerInvariant())
                .Where(ext => ext.Length > 0)
                .ToList();

            var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            foreach (var family in Families)
            {
                if ((sanitizedMask & (1 << family.BitIndex)) == 0)
                    continue;

                foreach (string ext in family.Extensions)
                {
                    string clean = ext.TrimStart('.').ToLowerInvariant();
                    if (seen.Add(clean))
                    {
                        existing.Add(clean);
                    }
                }
            }

            existing.Sort(StringComparer.OrdinalIgnoreCase);
            string merged = string.Join(", ", existing);
            if (currentExtensionsText == null)
            {
                Settings.Default.ExtensionsText = merged;
                Settings.Default.Save();
            }
            return merged;
        }

        private static void EnableFamily(
            CodecFamilyInfo family,
            string dllPath,
            WindowsOptimizer.RegistryHiveScope scope,
            CodecBackupManifest manifest,
            CodecApplyResult result)
        {
            // 1. Register COM InprocServer32 (HKCU + HKLM when elevated). NEVER set DisableProcessIsolation.
            string inprocSubKey = $@"Software\Classes\CLSID\{family.Clsid}\InprocServer32";
            string clsidRootSubKey = $@"Software\Classes\CLSID\{family.Clsid}";

            using (var clsidKey = scope.OpenUserKey(clsidRootSubKey, writable: true))
            {
                clsidKey?.SetValue(string.Empty, $"WinThumbsPreloader - {family.DisplayName}", RegistryValueKind.String);
            }
            using (var inprocKey = scope.OpenUserKey(inprocSubKey, writable: true))
            {
                if (inprocKey != null)
                {
                    inprocKey.SetValue(string.Empty, dllPath, RegistryValueKind.String);
                    inprocKey.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
                    result.UserScopeApplied = true;
                }
            }

            if (scope.MachineWritable)
            {
                using (var clsidKey = scope.OpenMachineKey(clsidRootSubKey, writable: true))
                {
                    clsidKey?.SetValue(string.Empty, $"WinThumbsPreloader - {family.DisplayName}", RegistryValueKind.String);
                }
                using (var inprocKey = scope.OpenMachineKey(inprocSubKey, writable: true))
                {
                    if (inprocKey != null)
                    {
                        inprocKey.SetValue(string.Empty, dllPath, RegistryValueKind.String);
                        inprocKey.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
                        result.MachineScopeApplied = true;
                    }
                }
                using (var approvedKey = scope.OpenMachineKey(ShellApprovedKey, writable: true))
                {
                    approvedKey?.SetValue(family.Clsid, $"WinThumbsPreloader - {family.DisplayName}", RegistryValueKind.String);
                }
            }

            // 2. Register per-extension ShellEx + Treatment + ThumbnailCutoff and UserChoice ProgId override (without modifying UserChoice!).
            foreach (string ext in family.Extensions)
            {
                string extClassKey = $@"Software\Classes\{ext}";
                WriteExtensionMetadataWithBackup("HKCU", extClassKey, family, scope, manifest, writeMachine: false);
                WriteShellExWithBackup("HKCU", $@"{extClassKey}\ShellEx\{ThumbnailProviderIid}", family.Clsid, scope, manifest, writeMachine: false);

                if (scope.MachineWritable)
                {
                    WriteExtensionMetadataWithBackup("HKLM", extClassKey, family, scope, manifest, writeMachine: true);
                    WriteShellExWithBackup("HKLM", $@"{extClassKey}\ShellEx\{ThumbnailProviderIid}", family.Clsid, scope, manifest, writeMachine: true);
                }

                string? userChoiceProgId = ReadUserChoiceProgId(ext, scope);
                if (!string.IsNullOrWhiteSpace(userChoiceProgId))
                {
                    string progShellEx = $@"Software\Classes\{userChoiceProgId}\ShellEx\{ThumbnailProviderIid}";
                    WriteShellExWithBackup("HKCU", progShellEx, family.Clsid, scope, manifest, writeMachine: false);
                }
            }
        }

        private static void DisableFamily(
            CodecFamilyInfo family,
            WindowsOptimizer.RegistryHiveScope scope,
            CodecBackupManifest manifest,
            CodecApplyResult result)
        {
            var keysToRemove = new List<string>();

            foreach (var kvp in manifest.Entries)
            {
                var entry = kvp.Value;
                if (!string.Equals(entry.InstalledClsid, family.Clsid, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(entry.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) && !scope.MachineWritable)
                    continue;

                bool isMachine = string.Equals(entry.Hive, "HKLM", StringComparison.OrdinalIgnoreCase);
                RestoreEntry(entry, scope, isMachine);
                result.BackupRestored = true;
                keysToRemove.Add(kvp.Key);
            }

            foreach (string k in keysToRemove)
            {
                manifest.Entries.Remove(k);
            }

            // Also defensively clean any dangling our-CLSID references on family extensions / UserChoice ProgIds even if backup was missing.
            foreach (string ext in family.Extensions)
            {
                CleanIfStillOurClsid($@"Software\Classes\{ext}\ShellEx\{ThumbnailProviderIid}", family.Clsid, scope, isMachine: false);
                if (scope.MachineWritable)
                {
                    CleanIfStillOurClsid($@"Software\Classes\{ext}\ShellEx\{ThumbnailProviderIid}", family.Clsid, scope, isMachine: true);
                }

                string? userChoiceProgId = ReadUserChoiceProgId(ext, scope);
                if (!string.IsNullOrWhiteSpace(userChoiceProgId))
                {
                    CleanIfStillOurClsid($@"Software\Classes\{userChoiceProgId}\ShellEx\{ThumbnailProviderIid}", family.Clsid, scope, isMachine: false);
                }
            }

            // Remove COM registration for this disabled family CLSID.
            scope.DeleteUserSubKeyTree($@"Software\Classes\CLSID\{family.Clsid}");
            if (scope.MachineWritable)
            {
                scope.DeleteMachineSubKeyTree($@"Software\Classes\CLSID\{family.Clsid}");
                try
                {
                    using var approvedKey = scope.OpenMachineKey(ShellApprovedKey, writable: true);
                    approvedKey?.DeleteValue(family.Clsid, throwOnMissingValue: false);
                }
                catch { }
            }
        }

        private static void WriteExtensionMetadataWithBackup(
            string hiveName,
            string extClassKey,
            CodecFamilyInfo family,
            WindowsOptimizer.RegistryHiveScope scope,
            CodecBackupManifest manifest,
            bool writeMachine)
        {
            string entryKey = $"{hiveName}|META|{extClassKey}";
            if (!manifest.Entries.ContainsKey(entryKey))
            {
                using var existing = writeMachine
                    ? scope.OpenMachineKey(extClassKey, writable: false)
                    : scope.OpenUserKey(extClassKey, writable: false);

                bool existed = existing != null;
                int? prevTreatment = TryReadIntValue(existing, "Treatment", out bool hasTreatment);
                int? prevCutoff = TryReadIntValue(existing, "ThumbnailCutoff", out bool hasCutoff);

                manifest.Entries[entryKey] = new CodecBackupEntry
                {
                    Hive = hiveName,
                    SubKey = extClassKey,
                    Kind = "ExtensionMetadata",
                    KeyExistedBefore = existed,
                    PreviousTreatmentExisted = hasTreatment,
                    PreviousTreatment = prevTreatment,
                    PreviousCutoffExisted = hasCutoff,
                    PreviousCutoff = prevCutoff,
                    InstalledClsid = family.Clsid
                };
            }

            using var key = writeMachine
                ? scope.OpenMachineKey(extClassKey, writable: true)
                : scope.OpenUserKey(extClassKey, writable: true);
            if (key != null)
            {
                key.SetValue("Treatment", family.Treatment, RegistryValueKind.DWord);
                key.SetValue("ThumbnailCutoff", family.ThumbnailCutoff, RegistryValueKind.DWord);
            }
        }

        private static void WriteShellExWithBackup(
            string hiveName,
            string shellExSubKey,
            string clsid,
            WindowsOptimizer.RegistryHiveScope scope,
            CodecBackupManifest manifest,
            bool writeMachine)
        {
            string entryKey = $"{hiveName}|SHELLEX|{shellExSubKey}";

            using var existing = writeMachine
                ? scope.OpenMachineKey(shellExSubKey, writable: false)
                : scope.OpenUserKey(shellExSubKey, writable: false);
            string? currentDefault = existing?.GetValue(string.Empty)?.ToString();

            // Idempotency: preserve the original backup even across repeated Apply calls.
            if (!manifest.Entries.ContainsKey(entryKey) && !IsOurClsid(currentDefault))
            {
                manifest.Entries[entryKey] = new CodecBackupEntry
                {
                    Hive = hiveName,
                    SubKey = shellExSubKey,
                    Kind = "ShellEx",
                    KeyExistedBefore = existing != null,
                    PreviousDefaultValue = currentDefault,
                    InstalledClsid = clsid
                };
            }

            using var key = writeMachine
                ? scope.OpenMachineKey(shellExSubKey, writable: true)
                : scope.OpenUserKey(shellExSubKey, writable: true);
            key?.SetValue(string.Empty, clsid, RegistryValueKind.String);
        }

        private static void RestoreEntry(
            CodecBackupEntry entry,
            WindowsOptimizer.RegistryHiveScope scope,
            bool isMachine)
        {
            if (string.Equals(entry.Kind, "ShellEx", StringComparison.OrdinalIgnoreCase))
            {
                if (entry.KeyExistedBefore)
                {
                    using var key = isMachine
                        ? scope.OpenMachineKey(entry.SubKey, writable: true)
                        : scope.OpenUserKey(entry.SubKey, writable: true);
                    if (key != null)
                    {
                        if (entry.PreviousDefaultValue != null)
                        {
                            key.SetValue(string.Empty, entry.PreviousDefaultValue, RegistryValueKind.String);
                        }
                        else
                        {
                            key.DeleteValue(string.Empty, throwOnMissingValue: false);
                        }
                    }
                }
                else
                {
                    DeleteKeyAndEmptyParentShellEx(entry.SubKey, scope, isMachine);
                }
                return;
            }

            if (string.Equals(entry.Kind, "ExtensionMetadata", StringComparison.OrdinalIgnoreCase))
            {
                using (var key = isMachine
                    ? scope.OpenMachineKey(entry.SubKey, writable: true)
                    : scope.OpenUserKey(entry.SubKey, writable: true))
                {
                    if (key != null)
                    {
                        if (entry.PreviousTreatmentExisted && entry.PreviousTreatment.HasValue)
                            key.SetValue("Treatment", entry.PreviousTreatment.Value, RegistryValueKind.DWord);
                        else
                            key.DeleteValue("Treatment", throwOnMissingValue: false);

                        if (entry.PreviousCutoffExisted && entry.PreviousCutoff.HasValue)
                            key.SetValue("ThumbnailCutoff", entry.PreviousCutoff.Value, RegistryValueKind.DWord);
                        else
                            key.DeleteValue("ThumbnailCutoff", throwOnMissingValue: false);
                    }
                }

                if (!entry.KeyExistedBefore)
                {
                    DeleteSubKeyIfCompletelyEmpty(entry.SubKey, scope, isMachine);
                }
            }
        }

        private static void CleanIfStillOurClsid(
            string shellExSubKey,
            string clsid,
            WindowsOptimizer.RegistryHiveScope scope,
            bool isMachine)
        {
            using var existing = isMachine
                ? scope.OpenMachineKey(shellExSubKey, writable: false)
                : scope.OpenUserKey(shellExSubKey, writable: false);
            string? val = existing?.GetValue(string.Empty)?.ToString();
            if (string.Equals(val, clsid, StringComparison.OrdinalIgnoreCase))
            {
                DeleteKeyAndEmptyParentShellEx(shellExSubKey, scope, isMachine);
            }
        }

        private static void DeleteKeyAndEmptyParentShellEx(
            string shellExSubKey,
            WindowsOptimizer.RegistryHiveScope scope,
            bool isMachine)
        {
            DeleteSubKeyIfNoSubKeys(shellExSubKey, scope, isMachine);
            int lastSlash = shellExSubKey.LastIndexOf('\\');
            if (lastSlash > 0)
            {
                string parentShellEx = shellExSubKey.Substring(0, lastSlash);
                if (parentShellEx.EndsWith(@"\ShellEx", StringComparison.OrdinalIgnoreCase))
                {
                    DeleteSubKeyIfCompletelyEmpty(parentShellEx, scope, isMachine);
                }
            }
        }

        private static void DeleteSubKeyIfNoSubKeys(
            string subKey,
            WindowsOptimizer.RegistryHiveScope scope,
            bool isMachine)
        {
            if (isMachine)
                scope.DeleteMachineSubKey(subKey);
            else
                scope.DeleteUserSubKey(subKey);
        }

        private static void DeleteSubKeyIfCompletelyEmpty(
            string subKey,
            WindowsOptimizer.RegistryHiveScope scope,
            bool isMachine)
        {
            bool empty = false;
            using (var key = isMachine
                ? scope.OpenMachineKey(subKey, writable: false)
                : scope.OpenUserKey(subKey, writable: false))
            {
                if (key != null && key.SubKeyCount == 0 && key.ValueCount == 0)
                {
                    empty = true;
                }
            }

            if (empty)
            {
                DeleteSubKeyIfNoSubKeys(subKey, scope, isMachine);
            }
        }

        private static ExtensionHandlerStatus DetectExtensionStatus(
            string ext,
            WindowsOptimizer.RegistryHiveScope scope)
        {
            string? userChoiceProgId = ReadUserChoiceProgId(ext, scope);
            string? activeClsid = null;

            if (scope.IsSystemDefault)
            {
                activeClsid = QuerySystemAssociationThumbnailClsid(ext);
            }

            if (string.IsNullOrWhiteSpace(activeClsid))
            {
                activeClsid = ResolveThumbnailClsidFromScope(ext, userChoiceProgId, scope);
            }

            if (string.IsNullOrWhiteSpace(activeClsid))
            {
                return new ExtensionHandlerStatus
                {
                    Extension = ext,
                    ActiveClsid = null,
                    ActiveHandlerName = "None",
                    ActiveDllPath = null,
                    UserChoiceProgId = userChoiceProgId,
                    IsWinThumbsPreloader = false,
                    IsConflict = false
                };
            }

            bool isOurs = IsOurClsid(activeClsid);
            string? dllPath = ResolveInprocServerPath(activeClsid, scope);
            string handlerName = ResolveFriendlyHandlerName(activeClsid, dllPath, scope, isOurs);

            return new ExtensionHandlerStatus
            {
                Extension = ext,
                ActiveClsid = activeClsid,
                ActiveHandlerName = handlerName,
                ActiveDllPath = dllPath,
                UserChoiceProgId = userChoiceProgId,
                IsWinThumbsPreloader = isOurs,
                IsConflict = !isOurs
            };
        }

        private static string? ReadUserChoiceProgId(string ext, WindowsOptimizer.RegistryHiveScope scope)
        {
            using var key = scope.OpenUserKey($@"{FileExtsBaseKey}\{ext}\UserChoice", writable: false);
            string? progId = key?.GetValue("ProgId")?.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(progId) ? null : progId;
        }

        private static string? ResolveThumbnailClsidFromScope(
            string ext,
            string? userChoiceProgId,
            WindowsOptimizer.RegistryHiveScope scope)
        {
            // Precedence mirrors Windows Shell Explorer resolution:
            // 1) UserChoice ProgId\ShellEx
            if (!string.IsNullOrWhiteSpace(userChoiceProgId))
            {
                string progShellEx = $@"Software\Classes\{userChoiceProgId}\ShellEx\{ThumbnailProviderIid}";
                string? val = ReadMergedClassDefault(progShellEx, scope);
                if (!string.IsNullOrWhiteSpace(val))
                    return val;
            }

            // 2) Direct .ext\ShellEx
            string? direct = ReadMergedClassDefault($@"Software\Classes\{ext}\ShellEx\{ThumbnailProviderIid}", scope);
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            // 3) Default ProgId of .ext -> <ProgId>\ShellEx
            string? defaultProgId = ReadMergedClassDefault($@"Software\Classes\{ext}", scope);
            if (!string.IsNullOrWhiteSpace(defaultProgId))
            {
                string? progVal = ReadMergedClassDefault($@"Software\Classes\{defaultProgId}\ShellEx\{ThumbnailProviderIid}", scope);
                if (!string.IsNullOrWhiteSpace(progVal))
                    return progVal;
            }

            // 4) SystemFileAssociations\.ext\ShellEx
            string? sfa = ReadMergedClassDefault($@"Software\Classes\SystemFileAssociations\{ext}\ShellEx\{ThumbnailProviderIid}", scope);
            if (!string.IsNullOrWhiteSpace(sfa))
                return sfa;

            return null;
        }

        private static string? ReadMergedClassDefault(string subKey, WindowsOptimizer.RegistryHiveScope scope)
        {
            using (var userKey = scope.OpenUserKey(subKey, writable: false))
            {
                string? val = userKey?.GetValue(string.Empty)?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(val))
                    return val;
            }

            using (var machineKey = scope.OpenMachineKey(subKey, writable: false))
            {
                string? val = machineKey?.GetValue(string.Empty)?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(val))
                    return val;
            }

            return null;
        }

        private static string? ResolveInprocServerPath(string clsid, WindowsOptimizer.RegistryHiveScope scope)
        {
            string? raw = ReadMergedClassDefault($@"Software\Classes\CLSID\{clsid}\InprocServer32", scope);
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            return Environment.ExpandEnvironmentVariables(raw).Trim().Trim('"');
        }

        private static string ResolveFriendlyHandlerName(
            string clsid,
            string? dllPath,
            WindowsOptimizer.RegistryHiveScope scope,
            bool isOurs)
        {
            if (isOurs)
                return "WinThumbsPreloader Codecs";

            if (string.Equals(clsid, WindowsPhotoThumbnailClsid, StringComparison.OrdinalIgnoreCase))
                return "Windows Photo Thumbnail Provider";

            string? clsidLabel = ReadMergedClassDefault($@"Software\Classes\CLSID\{clsid}", scope);
            string combinedHint = $"{clsidLabel} {dllPath}";

            if (!string.IsNullOrWhiteSpace(dllPath) && File.Exists(dllPath) && !PathSecurity.IsReparsePoint(dllPath))
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(dllPath);
                    combinedHint = $"{combinedHint} {vi.CompanyName} {vi.ProductName} {vi.FileDescription}";

                    if (ContainsIgnoreCase(combinedHint, "Ardfry"))
                        return "Ardfry PSD Codec";
                    if (ContainsIgnoreCase(combinedHint, "PowerToys"))
                        return "Microsoft PowerToys";
                    if (ContainsIgnoreCase(combinedHint, "Adobe") || ContainsIgnoreCase(combinedHint, "Acrobat") || ContainsIgnoreCase(combinedHint, "PDFPrevHndlr"))
                        return "Adobe Acrobat";
                    if (ContainsIgnoreCase(combinedHint, "SageThumbs"))
                        return "SageThumbs";
                    if (ContainsIgnoreCase(combinedHint, "MysticThumbs"))
                        return "MysticThumbs";
                    if (ContainsIgnoreCase(combinedHint, "SumatraPDF"))
                        return "SumatraPDF";

                    string? best = !string.IsNullOrWhiteSpace(vi.ProductName)
                        ? vi.ProductName.Trim()
                        : (!string.IsNullOrWhiteSpace(vi.FileDescription) ? vi.FileDescription.Trim() : null);
                    if (!string.IsNullOrWhiteSpace(best))
                        return best;
                }
                catch { }
            }

            if (ContainsIgnoreCase(combinedHint, "Ardfry") || ContainsIgnoreCase(combinedHint, "psdcodec"))
                return "Ardfry PSD Codec";
            if (ContainsIgnoreCase(combinedHint, "PowerToys"))
                return "Microsoft PowerToys";
            if (ContainsIgnoreCase(combinedHint, "Adobe") || ContainsIgnoreCase(combinedHint, "Acrobat"))
                return "Adobe Acrobat";
            if (ContainsIgnoreCase(combinedHint, "SageThumbs"))
                return "SageThumbs";
            if (ContainsIgnoreCase(combinedHint, "MysticThumbs"))
                return "MysticThumbs";

            if (!string.IsNullOrWhiteSpace(clsidLabel))
                return clsidLabel.Trim();
            if (!string.IsNullOrWhiteSpace(dllPath))
                return Path.GetFileName(dllPath);

            return clsid;
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        private static int? TryReadIntValue(RegistryKey? key, string name, out bool exists)
        {
            exists = false;
            if (key == null)
                return null;

            object? raw = key.GetValue(name);
            if (raw == null)
                return null;

            exists = true;
            if (raw is int i)
                return i;
            return int.TryParse(raw.ToString(), out int parsed) ? parsed : null;
        }

        private static CodecBackupManifest LoadBackupManifest(string backupPath)
        {
            try
            {
                if (!File.Exists(backupPath))
                    return new CodecBackupManifest();

                if (PathSecurity.HasReparsePointOrJunctionInPath(backupPath, rejectAnyLeafOrParentReparsePoint: true))
                {
                    WriteLine($"Refusing to load codec backup from unsafe path: {backupPath}", LoggingFrequency.AllLogging);
                    return new CodecBackupManifest();
                }

                var fi = new FileInfo(backupPath);
                if (fi.Length == 0 || fi.Length > MaxBackupFileBytes)
                {
                    WriteLine($"Codec backup file size out of safe bounds ({fi.Length} bytes); ignoring.", LoggingFrequency.AllLogging);
                    return new CodecBackupManifest();
                }

                string json = File.ReadAllText(backupPath, Encoding.UTF8);
                var parsed = JsonSerializer.Deserialize<CodecBackupManifest>(json);
                if (parsed?.Entries != null)
                {
                    return new CodecBackupManifest
                    {
                        Version = parsed.Version,
                        Entries = new Dictionary<string, CodecBackupEntry>(parsed.Entries, StringComparer.OrdinalIgnoreCase)
                    };
                }
            }
            catch (Exception ex)
            {
                WriteLine($"Failed reading codec backup manifest: {ex.Message}", LoggingFrequency.AllLogging);
            }

            return new CodecBackupManifest();
        }

        private static void SaveOrDeleteBackupManifest(string backupPath, CodecBackupManifest manifest)
        {
            if (manifest.Entries.Count == 0)
            {
                try
                {
                    if (File.Exists(backupPath) && !PathSecurity.HasReparsePointOrJunctionInPath(backupPath, rejectAnyLeafOrParentReparsePoint: true))
                    {
                        File.Delete(backupPath);
                    }
                }
                catch { }
                return;
            }

            string? dir = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (PathSecurity.HasReparsePointOrJunctionInPath(backupPath, rejectAnyLeafOrParentReparsePoint: true))
            {
                throw new InvalidOperationException($"Refusing to write codec backup to unsafe path: {backupPath}");
            }

            string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            string tempPath = backupPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                File.Move(tempPath, backupPath, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }
            }
        }

        public static void RecycleThumbnailSurrogateHosts()
        {
            try
            {
                foreach (Process proc in Process.GetProcessesByName("dllhost"))
                {
                    try
                    {
                        string? cmdLine = TryGetProcessCommandLine(proc);
                        if (!string.IsNullOrWhiteSpace(cmdLine) &&
                            cmdLine.Contains(SurrogateAppId, StringComparison.OrdinalIgnoreCase))
                        {
                            proc.Kill();
                            proc.WaitForExit(2000);
                        }
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch { }
        }

        private static string? TryGetProcessCommandLine(Process process)
        {
            const int ProcessCommandLineInformation = 60;
            int bufferSize = 4096;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int status = NtQueryInformationProcess(
                    process.Handle,
                    ProcessCommandLineInformation,
                    buffer,
                    bufferSize,
                    out int needed);

                if (status != 0 && needed > bufferSize && needed <= 65536)
                {
                    Marshal.FreeHGlobal(buffer);
                    bufferSize = needed;
                    buffer = Marshal.AllocHGlobal(bufferSize);
                    status = NtQueryInformationProcess(
                        process.Handle,
                        ProcessCommandLineInformation,
                        buffer,
                        bufferSize,
                        out _);
                }

                if (status != 0)
                    return null;

                ushort lengthBytes = (ushort)Marshal.ReadInt16(buffer, 0);
                IntPtr strPtr = Marshal.ReadIntPtr(buffer, IntPtr.Size == 8 ? 8 : 4);
                if (strPtr == IntPtr.Zero || lengthBytes == 0)
                    return null;

                return Marshal.PtrToStringUni(strPtr, lengthBytes / 2);
            }
            catch
            {
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string? QuerySystemAssociationThumbnailClsid(string ext)
        {
            try
            {
                uint cch = 128;
                var sb = new StringBuilder((int)cch);
                int hr = AssocQueryStringW(
                    0, // ASSOCF_NONE
                    16, // ASSOCSTR_SHELLEXTENSION
                    ext,
                    ThumbnailProviderIid,
                    sb,
                    ref cch);

                if (hr == 0 && sb.Length > 0)
                {
                    return sb.ToString().Trim();
                }

                if (cch > 128 && cch <= 1024)
                {
                    sb = new StringBuilder((int)cch);
                    hr = AssocQueryStringW(0, 16, ext, ThumbnailProviderIid, sb, ref cch);
                    if (hr == 0 && sb.Length > 0)
                        return sb.ToString().Trim();
                }
            }
            catch { }
            return null;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int AssocQueryStringW(
            uint flags,
            uint str,
            string pszAssoc,
            string pszExtra,
            StringBuilder pszOut,
            ref uint pcchOut);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            IntPtr processInformation,
            int processInformationLength,
            out int returnLength);

        private sealed class CodecBackupManifest
        {
            public int Version { get; set; } = 1;
            public Dictionary<string, CodecBackupEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class CodecBackupEntry
        {
            public string Hive { get; set; } = "HKCU";
            public string SubKey { get; set; } = string.Empty;
            public string Kind { get; set; } = "ShellEx";
            public bool KeyExistedBefore { get; set; }
            public string? PreviousDefaultValue { get; set; }
            public bool PreviousTreatmentExisted { get; set; }
            public int? PreviousTreatment { get; set; }
            public bool PreviousCutoffExisted { get; set; }
            public int? PreviousCutoff { get; set; }
            public string? InstalledClsid { get; set; }
        }
    }
}
