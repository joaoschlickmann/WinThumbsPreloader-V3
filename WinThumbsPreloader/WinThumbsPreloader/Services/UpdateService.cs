#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader.Services
{
    public sealed class UpdateStatusDto
    {
        public string State { get; init; } = nameof(UpdateState.Updated);
        public string CurrentVersion { get; init; } = string.Empty;
        public string LatestVersion { get; init; } = string.Empty;
        public string ReleaseTag { get; init; } = string.Empty;
        public string ReleasePageUrl { get; init; } = AutoUpdater.ReleasesWebUrl;
        public bool HasInstallerAsset { get; init; }
        public string InstallerFileName { get; init; } = string.Empty;
        public long InstallerSize { get; init; }
        public bool IsUpdateAvailable { get; init; }
    }

    public sealed class UpdateInstallResult
    {
        public bool Ok { get; init; }
        public bool Launched { get; init; }
        public bool CancelledByUser { get; init; }
        public bool OpenedReleasePageFallback { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? ReleasePageUrl { get; init; }
    }

    public sealed class UpdateService
    {
        private UpdateCheckResult? _latestVerifiedResult;

        public async Task<UpdateStatusDto> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            UpdateCheckResult result = await AutoUpdater.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);
            _latestVerifiedResult = result;
            return ToDto(result);
        }

        public UpdateStatusDto? GetLastCachedStatus() =>
            _latestVerifiedResult != null ? ToDto(_latestVerifiedResult) : null;

        public async Task<UpdateInstallResult> DownloadAndInstallAsync(
            bool restartMinimized,
            Action<int, string>? onProgress = null,
            Action? onWillExit = null,
            CancellationToken cancellationToken = default)
        {
            UpdateCheckResult? result = _latestVerifiedResult;
            if (result == null || !result.IsUpdateAvailable)
            {
                result = await AutoUpdater.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);
                _latestVerifiedResult = result;
            }

            if (!result.IsUpdateAvailable)
            {
                return new UpdateInstallResult
                {
                    Ok = false,
                    Message = "WinThumbsPreloader is already up to date."
                };
            }

            if (!result.HasInstallerAsset)
            {
                OpenReleasePage(result.ReleasePageUrl);
                return new UpdateInstallResult
                {
                    Ok = false,
                    OpenedReleasePageFallback = true,
                    ReleasePageUrl = result.ReleasePageUrl,
                    Message = "No installer asset found; opened GitHub release page."
                };
            }

            try
            {
                onProgress?.Invoke(0, $"Downloading v{result.LatestVersion}... 0%");
                var progress = new Progress<int>(percent =>
                {
                    onProgress?.Invoke(percent, $"Downloading v{result.LatestVersion}... {percent}%");
                });

                string installerPath = await AutoUpdater.DownloadInstallerAsync(result, progress, cancellationToken)
                    .ConfigureAwait(false);

                onProgress?.Invoke(100, "Launching update installer...");
                bool launched = AutoUpdater.LaunchInstaller(
                    installerPath,
                    restartMinimized,
                    out bool cancelledByUser,
                    result.InstallerSize,
                    result.InstallerSha256,
                    requireSha256: true);

                if (launched)
                {
                    onWillExit?.Invoke();
                    return new UpdateInstallResult
                    {
                        Ok = true,
                        Launched = true,
                        Message = "Update installer launched."
                    };
                }

                if (cancelledByUser)
                {
                    return new UpdateInstallResult
                    {
                        Ok = false,
                        CancelledByUser = true,
                        Message = "Administrator permission is required to install the update."
                    };
                }

                return new UpdateInstallResult
                {
                    Ok = false,
                    Message = "Failed to launch the update installer."
                };
            }
            catch (Exception ex)
            {
                WriteLine("Auto-update failed: " + ex.Message, LoggingFrequency.GUILogging);
                return new UpdateInstallResult
                {
                    Ok = false,
                    ReleasePageUrl = result.ReleasePageUrl,
                    Message = $"Failed to download or verify the automatic update: {ex.Message}"
                };
            }
        }

        public bool OpenReleasePage(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return OpenTrustedUrl(AutoUpdater.ReleasesWebUrl);
            }

            string trimmed = url.Trim();
            if (!IsAllowedExternalUrl(trimmed) ||
                !Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsedUri))
            {
                WriteLine("Blocked untrusted external URL open request: " + trimmed, LoggingFrequency.GUILogging);
                return false;
            }

            return OpenTrustedUrl(parsedUri.AbsoluteUri);
        }

        public static bool IsAllowedExternalUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            string trimmed = url.Trim();
            return trimmed.Equals(AutoUpdater.ProjectWebUrl, StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals(AutoUpdater.ReleasesWebUrl, StringComparison.OrdinalIgnoreCase) ||
                   AutoUpdater.IsTrustedGitHubReleasePageUrl(trimmed);
        }

        private static bool OpenTrustedUrl(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                WriteLine("Opened browser URL: " + target, LoggingFrequency.GUILogging);
                return true;
            }
            catch (Exception ex)
            {
                WriteLine("Unable to open URL: " + ex.Message, LoggingFrequency.GUILogging);
                return false;
            }
        }

        private static UpdateStatusDto ToDto(UpdateCheckResult result) => new()
        {
            State = result.State.ToString(),
            CurrentVersion = result.CurrentVersion,
            LatestVersion = result.LatestVersion,
            ReleaseTag = result.ReleaseTag,
            ReleasePageUrl = result.ReleasePageUrl,
            HasInstallerAsset = result.HasInstallerAsset,
            InstallerFileName = result.InstallerFileName,
            InstallerSize = result.InstallerSize,
            IsUpdateAvailable = result.IsUpdateAvailable
        };
    }
}
