#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader.Services
{
    public sealed class CodecFamilyDto
    {
        public int BitIndex { get; init; }
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string[] Extensions { get; init; } = Array.Empty<string>();
        public string CurrentHandlerName { get; init; } = "None";
        public string StatusText { get; init; } = "Not Installed";
        public bool IsEnabled { get; init; }
        public bool IsPartiallyEnabled { get; init; }
        public bool HasExternalHandler { get; init; }
        public bool Recommended { get; init; } = true;
    }

    public sealed class CodecsStateDto
    {
        public bool BundlePresent { get; init; }
        public string? BundleError { get; init; }
        public bool IsElevated { get; init; }
        public bool AdminSettingEnabled { get; init; }
        public int EnabledMask { get; init; }
        public bool HasMachineLevelCodecsOrBackup { get; init; }
        public IReadOnlyList<CodecFamilyDto> Families { get; init; } = Array.Empty<CodecFamilyDto>();
    }

    public sealed class CodecsActionResult
    {
        public bool Ok { get; init; }
        public bool UacCancelled { get; init; }
        public bool MachineUninstallSkipped { get; init; }
        public bool ExtensionsSynced { get; init; }
        public int AppliedMask { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? ScopeNotice { get; init; }
        public CodecsStateDto? State { get; init; }
    }

    [SupportedOSPlatform("windows")]
    public sealed class CodecsService
    {
        public CodecsStateDto GetState()
        {
            bool bundlePresent = CodecManager.AreBundledCodecsPresent(out string? bundleError);
            bool isElevated = WindowsOptimizer.IsCurrentProcessElevated();
            var statuses = CodecManager.DetectAllStatuses();

            int enabledMask = 0;
            var families = new List<CodecFamilyDto>(statuses.Count);
            foreach (var status in statuses)
            {
                if (status.IsEnabled || status.IsPartiallyEnabled)
                    enabledMask |= 1 << status.Family.BitIndex;

                string statusText = status.IsEnabled
                    ? "Active"
                    : (status.IsPartiallyEnabled
                        ? "Partial"
                        : (status.HasExternalHandler ? "External" : "Not Installed"));

                families.Add(new CodecFamilyDto
                {
                    BitIndex = status.Family.BitIndex,
                    Id = status.Family.Id.ToString(),
                    DisplayName = status.Family.DisplayName,
                    Description = status.Family.Description,
                    Extensions = status.Family.Extensions,
                    CurrentHandlerName = status.SummaryHandlerName,
                    StatusText = statusText,
                    IsEnabled = status.IsEnabled,
                    IsPartiallyEnabled = status.IsPartiallyEnabled,
                    HasExternalHandler = status.HasExternalHandler,
                    Recommended = status.Family.DefaultRecommended
                });
            }

            return new CodecsStateDto
            {
                BundlePresent = bundlePresent,
                BundleError = bundleError,
                IsElevated = isElevated,
                AdminSettingEnabled = Settings.Default.Admin,
                EnabledMask = enabledMask,
                HasMachineLevelCodecsOrBackup = CodecManager.HasMachineLevelCodecsOrBackup(),
                Families = families
            };
        }

        public CodecsActionResult Apply(int mask, bool elevateViaUac, bool syncExtensions)
        {
            int sanitizedMask = mask & CodecManager.AllCodecsMask;

            if (sanitizedMask != 0 && !CodecManager.AreBundledCodecsPresent(out string? bundleError))
            {
                return new CodecsActionResult
                {
                    Ok = false,
                    Message = bundleError ?? "Bundled Codecs DLL files are missing.",
                    State = GetState()
                };
            }

            try
            {
                if (elevateViaUac)
                {
                    string args = CodecManager.BuildApplyCliArguments(sanitizedMask);
                    if (!WindowsOptimizer.RunElevatedHelper(args))
                    {
                        return new CodecsActionResult
                        {
                            Ok = false,
                            UacCancelled = true,
                            Message = "Administrator elevation was cancelled or failed.",
                            State = GetState()
                        };
                    }

                    // Over-the-shoulder UAC applies HKLM globally but writes HKCU under the admin account;
                    // apply locally afterwards so the interactive user's HKCU UserChoice ProgId overrides sync too.
                    if (!WindowsOptimizer.IsCurrentProcessElevated())
                    {
                        CodecManager.ApplyCodecSelection(sanitizedMask, broadcastShellChange: true);
                    }
                }
                else
                {
                    CodecManager.ApplyCodecSelection(sanitizedMask, broadcastShellChange: true);
                }

                bool synced = false;
                if (syncExtensions && sanitizedMask != 0)
                {
                    CodecManager.MergeEnabledCodecExtensionsIntoSettings(sanitizedMask);
                    synced = true;
                }

                string? scopeNotice = (!elevateViaUac && !WindowsOptimizer.IsCurrentProcessElevated() && Settings.Default.Admin)
                    ? "'Run As Admin' is enabled in Settings. Use 'Elevate & Apply (UAC)' so elevated preload processes also see these codecs."
                    : null;

                return new CodecsActionResult
                {
                    Ok = true,
                    ExtensionsSynced = synced,
                    AppliedMask = sanitizedMask,
                    ScopeNotice = scopeNotice,
                    Message = $"Thumbnail codecs updated (mask 0x{sanitizedMask:X3}).",
                    State = GetState()
                };
            }
            catch (Exception ex)
            {
                WriteLine($"Failed applying codec selection: {ex.Message}", LoggingFrequency.AllLogging);
                return new CodecsActionResult
                {
                    Ok = false,
                    Message = $"Failed to apply codec selection: {ex.Message}",
                    State = GetState()
                };
            }
        }

        public CodecsActionResult UninstallAll()
        {
            try
            {
                bool machineUninstallSkipped = false;
                if (!WindowsOptimizer.IsCurrentProcessElevated() && CodecManager.HasMachineLevelCodecsOrBackup())
                {
                    if (!WindowsOptimizer.RunElevatedHelper("-uninstall-codecs"))
                    {
                        machineUninstallSkipped = true;
                    }
                }

                CodecManager.UninstallAllCodecs(broadcastShellChange: true);

                string message = machineUninstallSkipped
                    ? "Current-user (HKCU) built-in codec entries were removed, while system-wide (HKLM) entries require Administrator elevation to uninstall."
                    : "All built-in codecs were uninstalled and backed-up handlers have been restored.";

                return new CodecsActionResult
                {
                    Ok = true,
                    MachineUninstallSkipped = machineUninstallSkipped,
                    Message = message,
                    State = GetState()
                };
            }
            catch (Exception ex)
            {
                WriteLine($"Failed uninstalling codecs: {ex.Message}", LoggingFrequency.AllLogging);
                return new CodecsActionResult
                {
                    Ok = false,
                    Message = $"Failed to uninstall codecs: {ex.Message}",
                    State = GetState()
                };
            }
        }
    }
}
