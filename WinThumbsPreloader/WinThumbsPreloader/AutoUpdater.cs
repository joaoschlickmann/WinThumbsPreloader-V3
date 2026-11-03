using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader
{
    public enum UpdateState
    {
        Updated,
        UpdateAvailable,
        BetaAvailable,
        Error
    }

    public sealed class UpdateCheckResult
    {
        public UpdateState State { get; init; } = UpdateState.Updated;
        public string CurrentVersion { get; init; } = string.Empty;
        public string LatestVersion { get; init; } = string.Empty;
        public string ReleaseTag { get; init; } = string.Empty;
        public string ReleasePageUrl { get; init; } = AutoUpdater.ReleasesWebUrl;
        public string InstallerDownloadUrl { get; init; } = string.Empty;
        public string InstallerFileName { get; init; } = string.Empty;
        public long InstallerSize { get; init; }
        public string InstallerSha256 { get; init; } = string.Empty;

        public bool HasInstallerAsset => !string.IsNullOrWhiteSpace(InstallerDownloadUrl);
        public bool IsUpdateAvailable => State == UpdateState.UpdateAvailable || State == UpdateState.BetaAvailable;
    }

    public static class AutoUpdater
    {
        public const string RepositoryOwner = "joaoschlickmann";
        public const string RepositoryName = "WinThumbsPreloader-V3";
        public const string ProjectWebUrl = "https://github.com/joaoschlickmann/WinThumbsPreloader-V3";
        public const string ReleasesWebUrl = "https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases";
        public const string ReleasesApiUrl = "https://api.github.com/repos/joaoschlickmann/WinThumbsPreloader-V3/releases";

        public const int MaxApiResponseBytes = 2 * 1024 * 1024; // 2 MB
        public const long MaxInstallerSizeBytes = 200L * 1024 * 1024; // 200 MB

        private static readonly Regex SafeFileNameRegex = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);
        private static readonly Regex Sha256HexRegex = new(@"^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                }
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
                MaxResponseContentBufferSize = MaxApiResponseBytes
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinThumbsPreloader");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public static async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            WriteLine("Checking for updates via GitHub Releases API", LoggingFrequency.DebugLogging);
            try
            {
                using var response = await SharedHttpClient.GetAsync(
                    ReleasesApiUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                Uri finalApiUri = response.RequestMessage?.RequestUri;
                if (finalApiUri != null && (!TryParseSecureHttpsUri(finalApiUri.AbsoluteUri, out Uri verifiedApiUri) ||
                    !verifiedApiUri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
                    !verifiedApiUri.AbsolutePath.StartsWith($"/repos/{RepositoryOwner}/{RepositoryName}/", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("Redirected API URL is not a trusted GitHub API endpoint.");
                }

                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > MaxApiResponseBytes)
                {
                    throw new InvalidDataException("GitHub API response exceeded maximum allowed size.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var ms = new MemoryStream();
                byte[] buffer = new byte[16384];
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (ms.Length + read > MaxApiResponseBytes)
                        throw new InvalidDataException("GitHub API response exceeded maximum allowed size.");
                    ms.Write(buffer, 0, read);
                }

                string json = Encoding.UTF8.GetString(ms.ToArray());
                return ParseReleasesJson(json, Application.ProductVersion);
            }
            catch (Exception ex)
            {
                WriteLine("Failed to check for updates: " + ex.Message, LoggingFrequency.GUILogging);
                return new UpdateCheckResult
                {
                    State = UpdateState.Error,
                    CurrentVersion = NormalizeVersionDisplay(Application.ProductVersion)
                };
            }
        }

        public static UpdateCheckResult ParseReleasesJson(string json, string currentProductVersion)
        {
            string normalizedCurrent = NormalizeVersionDisplay(currentProductVersion);

            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxApiResponseBytes)
            {
                return new UpdateCheckResult
                {
                    State = string.IsNullOrWhiteSpace(json) ? UpdateState.Updated : UpdateState.Error,
                    CurrentVersion = normalizedCurrent
                };
            }

            try
            {
                var options = new JsonDocumentOptions
                {
                    MaxDepth = 16,
                    AllowTrailingCommas = false
                };

                using JsonDocument doc = JsonDocument.Parse(json, options);
                JsonElement root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement release in root.EnumerateArray())
                    {
                        if (TryGetBoolean(release, "draft"))
                            continue;

                        var parsed = TryParseSingleRelease(release, currentProductVersion);
                        if (parsed != null)
                            return parsed;
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (!TryGetBoolean(root, "draft"))
                    {
                        var parsed = TryParseSingleRelease(root, currentProductVersion);
                        if (parsed != null)
                            return parsed;
                    }
                }

                return new UpdateCheckResult
                {
                    State = UpdateState.Updated,
                    CurrentVersion = normalizedCurrent
                };
            }
            catch (JsonException ex)
            {
                WriteLine("Failed to parse GitHub releases JSON: " + ex.Message, LoggingFrequency.GUILogging);
                return new UpdateCheckResult
                {
                    State = UpdateState.Error,
                    CurrentVersion = normalizedCurrent
                };
            }
        }

        private static UpdateCheckResult TryParseSingleRelease(JsonElement release, string currentProductVersion)
        {
            string tagName = TryGetString(release, "tag_name");
            if (string.IsNullOrWhiteSpace(tagName))
                return null;

            var tagMatch = Regex.Match(tagName.Trim(), @"^v?(\d+\.\d+\.\d+(-beta\d+)?)$", RegexOptions.IgnoreCase);
            if (!tagMatch.Success)
                return null;

            string latestVersionString = tagMatch.Groups[1].Value;
            string currentVersionString = NormalizeVersionDisplay(currentProductVersion);

            var currentNumMatch = Regex.Match(currentVersionString, @"(\d+\.\d+\.\d+)");
            var latestNumMatch = Regex.Match(latestVersionString, @"(\d+\.\d+\.\d+)");
            if (!currentNumMatch.Success || !latestNumMatch.Success)
                return null;

            Version currentNumericVersion = new Version(currentNumMatch.Value);
            Version latestNumericVersion = new Version(latestNumMatch.Value);

            var currentBetaMatch = Regex.Match(currentVersionString, @"-beta(\d+)", RegexOptions.IgnoreCase);
            var latestBetaMatch = Regex.Match(latestVersionString, @"-beta(\d+)", RegexOptions.IgnoreCase);

            bool isCurrentBeta = currentBetaMatch.Success;
            bool isLatestBeta = latestBetaMatch.Success || TryGetBoolean(release, "prerelease");

            int currentBetaNumber = currentBetaMatch.Success ? int.Parse(currentBetaMatch.Groups[1].Value) : 0;
            int latestBetaNumber = latestBetaMatch.Success ? int.Parse(latestBetaMatch.Groups[1].Value) : 0;

            UpdateState state;
            if (!isLatestBeta)
            {
                if (currentNumericVersion < latestNumericVersion ||
                    (isCurrentBeta && currentNumericVersion == latestNumericVersion))
                {
                    state = UpdateState.UpdateAvailable;
                }
                else
                {
                    state = UpdateState.Updated;
                }
            }
            else
            {
                if (currentNumericVersion < latestNumericVersion)
                {
                    state = UpdateState.BetaAvailable;
                }
                else if (isCurrentBeta && currentNumericVersion == latestNumericVersion && currentBetaNumber < latestBetaNumber)
                {
                    state = UpdateState.BetaAvailable;
                }
                else
                {
                    state = UpdateState.Updated;
                }
            }

            string releaseHtmlUrl = TryGetString(release, "html_url");
            if (!IsTrustedGitHubReleasePageUrl(releaseHtmlUrl))
                releaseHtmlUrl = ReleasesWebUrl;

            ExtractInstallerAsset(
                release,
                out string downloadUrl,
                out string fileName,
                out long fileSize,
                out string sha256);

            return new UpdateCheckResult
            {
                State = state,
                CurrentVersion = currentVersionString,
                LatestVersion = latestVersionString,
                ReleaseTag = tagName,
                ReleasePageUrl = releaseHtmlUrl,
                InstallerDownloadUrl = downloadUrl,
                InstallerFileName = fileName,
                InstallerSize = fileSize,
                InstallerSha256 = sha256
            };
        }

        private static void ExtractInstallerAsset(
            JsonElement release,
            out string downloadUrl,
            out string fileName,
            out long fileSize,
            out string sha256)
        {
            downloadUrl = string.Empty;
            fileName = string.Empty;
            fileSize = 0;
            sha256 = string.Empty;

            if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
                return;

            string releaseBody = TryGetString(release, "body");

            string fallbackUrl = string.Empty;
            string fallbackName = string.Empty;
            long fallbackSize = 0;
            string fallbackSha256 = string.Empty;

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string rawName = TryGetString(asset, "name");
                string url = TryGetString(asset, "browser_download_url");
                long size = TryGetInt64(asset, "size");
                string digest = TryGetString(asset, "digest");

                string safeName = SanitizeInstallerFileName(rawName);
                if (string.IsNullOrEmpty(safeName) || !IsTrustedGitHubDownloadUrl(url, allowCdnRedirectHost: false))
                    continue;

                if (size < 0 || size > MaxInstallerSizeBytes)
                    continue;

                string parsedSha256 = ExtractSha256FromDigestOrBody(digest, releaseBody, safeName);

                if (safeName.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase) ||
                    safeName.Contains("setup", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = url;
                    fileName = safeName;
                    fileSize = size;
                    sha256 = parsedSha256;
                    return;
                }

                if (string.IsNullOrEmpty(fallbackUrl))
                {
                    fallbackUrl = url;
                    fallbackName = safeName;
                    fallbackSize = size;
                    fallbackSha256 = parsedSha256;
                }
            }

            downloadUrl = fallbackUrl;
            fileName = fallbackName;
            fileSize = fallbackSize;
            sha256 = fallbackSha256;
        }

        private static string ExtractSha256FromDigestOrBody(string assetDigest, string releaseBody, string assetFileName)
        {
            if (!string.IsNullOrWhiteSpace(assetDigest))
            {
                string trimmed = assetDigest.Trim();
                if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = trimmed.Substring("sha256:".Length).Trim();
                    if (Sha256HexRegex.IsMatch(candidate))
                        return candidate.ToLowerInvariant();
                }
            }

            if (!string.IsNullOrWhiteSpace(releaseBody))
            {
                if (!string.IsNullOrWhiteSpace(assetFileName))
                {
                    string escapedFile = Regex.Escape(assetFileName);
                    var fileLineMatch = Regex.Match(releaseBody, $@"\b([a-fA-F0-9]{{64}})\s+\*?{escapedFile}\b");
                    if (fileLineMatch.Success)
                        return fileLineMatch.Groups[1].Value.ToLowerInvariant();
                }

                var labeledMatch = Regex.Match(releaseBody, @"SHA-?256:\s*`?([a-fA-F0-9]{64})`?", RegexOptions.IgnoreCase);
                if (labeledMatch.Success)
                    return labeledMatch.Groups[1].Value.ToLowerInvariant();
            }

            return string.Empty;
        }

        public static string SanitizeInstallerFileName(string rawFileName)
        {
            if (string.IsNullOrWhiteSpace(rawFileName))
                return string.Empty;

            if (rawFileName.Contains('/') || rawFileName.Contains('\\') || rawFileName.Contains("..", StringComparison.Ordinal))
                return string.Empty;

            string name = Path.GetFileName(rawFileName.Trim());
            if (string.IsNullOrWhiteSpace(name) ||
                !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !SafeFileNameRegex.IsMatch(name))
            {
                return string.Empty;
            }

            return name;
        }

        public static bool IsTrustedGitHubReleasePageUrl(string url)
        {
            if (!TryParseSecureHttpsUri(url, out Uri uri))
                return false;

            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return false;

            string expectedPrefix = $"/{RepositoryOwner}/{RepositoryName}/";
            string exactRepoPath = $"/{RepositoryOwner}/{RepositoryName}";
            return uri.AbsolutePath.Equals(exactRepoPath, StringComparison.OrdinalIgnoreCase) ||
                   uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsTrustedGitHubDownloadUrl(string url, bool allowCdnRedirectHost = true)
        {
            if (!TryParseSecureHttpsUri(url, out Uri uri))
                return false;

            if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                string expectedPrefix = $"/{RepositoryOwner}/{RepositoryName}/releases/download/";
                return uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
            }

            if (allowCdnRedirectHost && (
                uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private static bool TryParseSecureHttpsUri(string url, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri parsed))
                return false;

            if (parsed.Scheme != Uri.UriSchemeHttps ||
                !parsed.IsDefaultPort ||
                !string.IsNullOrEmpty(parsed.UserInfo))
            {
                return false;
            }

            uri = parsed;
            return true;
        }

        public static async Task<string> DownloadInstallerAsync(
            UpdateCheckResult updateResult,
            IProgress<int> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (updateResult == null || !updateResult.HasInstallerAsset)
                throw new InvalidOperationException("No installer download URL is available for this release.");

            if (!IsTrustedGitHubDownloadUrl(updateResult.InstallerDownloadUrl, allowCdnRedirectHost: false))
                throw new InvalidOperationException("Untrusted installer download URL blocked by security policy.");

            if (string.IsNullOrWhiteSpace(updateResult.InstallerSha256))
                throw new InvalidDataException("Release asset is missing a required SHA-256 checksum.");

            string safeFileName = SanitizeInstallerFileName(updateResult.InstallerFileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                string safeVersion = Regex.Replace(updateResult.LatestVersion ?? "3.0.0", @"[^0-9A-Za-z._-]", "");
                safeFileName = $"WinThumbsPreloader-{safeVersion}-setup.exe";
            }

            string baseLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseLocalAppData))
                baseLocalAppData = Path.GetTempPath();

            string updatesDir = Path.Combine(baseLocalAppData, "WinThumbsPreloader", "Updates");
            DirectoryInfo dirInfo = Directory.CreateDirectory(updatesDir);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Update directory cannot be a symbolic link or junction.");

            string destinationPath = Path.GetFullPath(Path.Combine(updatesDir, safeFileName));
            if (!destinationPath.StartsWith(Path.GetFullPath(updatesDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid update destination path.");

            string tempPartPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".part";

            WriteLine($"Downloading update installer from {updateResult.InstallerDownloadUrl} to {destinationPath}", LoggingFrequency.GUILogging);

            try
            {
                using var response = await SharedHttpClient.GetAsync(
                    updateResult.InstallerDownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                Uri finalRequestUri = response.RequestMessage?.RequestUri;
                if (finalRequestUri != null && !IsTrustedGitHubDownloadUrl(finalRequestUri.AbsoluteUri, allowCdnRedirectHost: true))
                {
                    throw new InvalidOperationException("Redirected download URL is not a trusted GitHub host.");
                }

                long? totalBytes = response.Content.Headers.ContentLength;
                if ((!totalBytes.HasValue || totalBytes.Value <= 0) && updateResult.InstallerSize > 0)
                {
                    totalBytes = updateResult.InstallerSize;
                }

                if (totalBytes.HasValue && totalBytes.Value > MaxInstallerSizeBytes)
                {
                    throw new InvalidDataException($"Installer exceeds maximum allowed size ({MaxInstallerSizeBytes} bytes).");
                }

                await using (Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (FileStream fileStream = new FileStream(tempPartPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int lastReportedPercent = -1;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        totalRead += bytesRead;
                        if (totalRead > MaxInstallerSizeBytes)
                        {
                            throw new InvalidDataException("Download aborted: file size exceeded maximum allowed limit.");
                        }

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);

                        if (progress != null && totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            int percent = (int)Math.Clamp((totalRead * 100L) / totalBytes.Value, 0L, 100L);
                            if (percent != lastReportedPercent)
                            {
                                lastReportedPercent = percent;
                                progress.Report(percent);
                            }
                        }
                    }
                }

                ValidateDownloadedExecutable(tempPartPath, updateResult.InstallerSize, updateResult.InstallerSha256, requireSha256: true);

                File.Move(tempPartPath, destinationPath, overwrite: true);
                progress?.Report(100);

                WriteLine("Update installer downloaded and verified: " + destinationPath, LoggingFrequency.GUILogging);
                return destinationPath;
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPartPath))
                        File.Delete(tempPartPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
                throw;
            }
        }

        public static void ValidateDownloadedExecutable(string filePath, long expectedSize = 0, string expectedSha256 = null, bool requireSha256 = false)
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < 1024)
                throw new InvalidDataException("Downloaded update file is incomplete or empty.");

            if (fileInfo.Length > MaxInstallerSizeBytes)
                throw new InvalidDataException("Downloaded update file exceeds maximum allowed size.");

            if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Downloaded update file cannot be a reparse point.");

            if (expectedSize > 0 && fileInfo.Length != expectedSize)
                throw new InvalidDataException($"Downloaded file size ({fileInfo.Length} bytes) does not match expected size ({expectedSize} bytes).");

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            Span<byte> dosHeader = stackalloc byte[64];
            if (fs.Read(dosHeader) < 64 || dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
                throw new InvalidDataException("Downloaded file is not a valid Windows executable (missing MZ header).");

            int peOffset = BinaryPrimitives.ReadInt32LittleEndian(dosHeader.Slice(0x3C, 4));
            if (peOffset > 0 && peOffset <= fileInfo.Length - 4)
            {
                fs.Position = peOffset;
                Span<byte> peSig = stackalloc byte[4];
                if (fs.Read(peSig) < 4 || peSig[0] != (byte)'P' || peSig[1] != (byte)'E' || peSig[2] != 0 || peSig[3] != 0)
                {
                    throw new InvalidDataException("Downloaded file does not have a valid PE signature.");
                }
            }
            else if (peOffset != 0)
            {
                throw new InvalidDataException("Downloaded file has an invalid PE header offset.");
            }

            if (requireSha256 && string.IsNullOrWhiteSpace(expectedSha256))
                throw new InvalidDataException("Missing required SHA-256 checksum for downloaded update installer.");

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string normalizedExpected = expectedSha256.Trim();
                if (normalizedExpected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    normalizedExpected = normalizedExpected.Substring("sha256:".Length).Trim();

                if (!Sha256HexRegex.IsMatch(normalizedExpected))
                    throw new InvalidDataException("Invalid expected SHA-256 format.");

                fs.Position = 0;
                byte[] actualHashBytes = SHA256.HashData(fs);
                byte[] expectedHashBytes = Convert.FromHexString(normalizedExpected);

                if (!CryptographicOperations.FixedTimeEquals(actualHashBytes, expectedHashBytes))
                {
                    throw new InvalidDataException("Downloaded installer SHA-256 checksum verification failed.");
                }
            }
        }

        public static bool LaunchInstaller(
            string installerPath,
            bool restartMinimized,
            out bool cancelledByUser,
            long expectedSize = 0,
            string expectedSha256 = null,
            bool requireSha256 = false)
        {
            cancelledByUser = false;

            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
                throw new FileNotFoundException("Update installer file was not found.", installerPath);

            string fullPath = Path.GetFullPath(installerPath);
            ValidateDownloadedExecutable(fullPath, expectedSize, expectedSha256, requireSha256);

            var psi = new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(fullPath) ?? Path.GetTempPath()
            };
            psi.ArgumentList.Add("/SILENT");
            psi.ArgumentList.Add("/CLOSEAPPLICATIONS");
            psi.ArgumentList.Add(restartMinimized ? "/AUTOLAUNCHMINIMIZED" : "/AUTOLAUNCH");

            try
            {
                WriteLine($"Launching update installer: {psi.FileName}", LoggingFrequency.GUILogging);
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                cancelledByUser = true;
                WriteLine("User cancelled UAC prompt for update installer.", LoggingFrequency.GUILogging);
                return false;
            }
        }

        public static async Task<bool> PromptAndInstallUpdateAsync(
            Form owner,
            UpdateCheckResult updateResult,
            bool restartMinimized = false,
            Action<string> statusCallback = null)
        {
            if (updateResult == null || !updateResult.IsUpdateAvailable)
                return false;

            if (!updateResult.HasInstallerAsset)
            {
                OpenUrlInBrowser(updateResult.ReleasePageUrl);
                return false;
            }

            string releaseType = updateResult.State == UpdateState.BetaAvailable ? "beta version" : "new version";
            string promptMessage =
                $"A {releaseType} of WinThumbsPreloader (v{updateResult.LatestVersion}) is available!\n" +
                $"Current version: v{updateResult.CurrentVersion}\n\n" +
                "Would you like to download and install the update automatically now?\n\n" +
                "• Click 'Yes' to download and install automatically\n" +
                "• Click 'No' to open the GitHub Release page in your browser\n" +
                "• Click 'Cancel' to dismiss";

            DialogResult choice = MessageBox.Show(
                owner,
                promptMessage,
                $"Update Available — WinThumbsPreloader v{updateResult.LatestVersion}",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Information);

            if (choice == DialogResult.No)
            {
                OpenUrlInBrowser(updateResult.ReleasePageUrl);
                return false;
            }

            if (choice != DialogResult.Yes)
                return false;

            try
            {
                statusCallback?.Invoke($"Downloading v{updateResult.LatestVersion}... 0%");
                var progress = new Progress<int>(percent =>
                {
                    statusCallback?.Invoke($"Downloading v{updateResult.LatestVersion}... {percent}%");
                });

                string installerPath = await DownloadInstallerAsync(updateResult, progress);

                statusCallback?.Invoke("Launching update installer...");
                if (LaunchInstaller(
                    installerPath,
                    restartMinimized,
                    out bool cancelledByUser,
                    updateResult.InstallerSize,
                    updateResult.InstallerSha256,
                    requireSha256: true))
                {
                    Environment.Exit(0);
                    return true;
                }

                if (cancelledByUser)
                {
                    MessageBox.Show(
                        owner,
                        "Administrator permission is required to install the update.",
                        "Update Cancelled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return false;
            }
            catch (Exception ex)
            {
                WriteLine("Auto-update failed: " + ex.Message, LoggingFrequency.GUILogging);
                DialogResult fallback = MessageBox.Show(
                    owner,
                    "Failed to download or verify the automatic update. Would you like to open the official GitHub Release page in your browser instead?",
                    "Update Error",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Error);

                if (fallback == DialogResult.Yes)
                {
                    OpenUrlInBrowser(updateResult.ReleasePageUrl);
                }

                return false;
            }
        }

        public static void OpenUrlInBrowser(string url)
        {
            string target = IsTrustedGitHubReleasePageUrl(url) ? url : ReleasesWebUrl;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                WriteLine("Opened browser URL: " + target, LoggingFrequency.GUILogging);
            }
            catch (Exception ex)
            {
                WriteLine("Unable to open URL: " + ex.Message, LoggingFrequency.GUILogging);
                MessageBox.Show("Unable to open the link.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string NormalizeVersionDisplay(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return "0.0.0";

            var match = Regex.Match(version.Trim(), @"(\d+\.\d+\.\d+(-beta\d+)?)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : version.Trim();
        }

        private static string TryGetString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? string.Empty;
            return string.Empty;
        }

        private static bool TryGetBoolean(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
            return false;
        }

        private static long TryGetInt64(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out long val))
                return val;
            return 0;
        }
    }
}
