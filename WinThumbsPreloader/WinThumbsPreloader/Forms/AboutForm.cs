using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinThumbsPreloader.Forms;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
            this.KeyDown += AboutForm_KeyDown;
            this.KeyUp += AboutForm_KeyUp;
            this.Activated += AboutForm_Activated;
            this.KeyPreview = true;
            this.Shown += AboutForm_Shown;
            InitializePreloadContextMenu();
        }

        private void InitializePreloadContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Preload Folder", null, (_, __) => StartFolderSelectionPreload(recursive: false));
            menu.Items.Add("Preload Recursively (Shift+Click)", null, (_, __) => StartFolderSelectionPreload(recursive: true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Bulk Preloader... (Ctrl+Click)", null, (_, __) => OpenBulkPreloader());
            PreloadButton.ContextMenuStrip = menu;

            PreloadTooltip.SetToolTip(
                PreloadButton,
                "Click: Preload Folder\r\nShift+Click: Preload Recursively\r\nCtrl+Click (or Right-Click): Open Bulk Preloader");
        }

        private void AboutForm_Load(object sender, EventArgs e)
        {
            AppNameLabel.Text += " " + Application.ProductVersion;
            this.Icon = Resources.MainIcon;
            using (var icon = new Icon(Resources.MainIcon, 48, 48))
            {
                AppIconPictureBox.Image = icon.ToBitmap();
            }
            CheckForUpdates();

            WriteLine($"AboutForm loaded with product version: {Application.ProductVersion}", LoggingFrequency.GUILogging);
            if (Program.AppOptions.reopenSettings)
            {
                Program.AppOptions.reopenSettings = false;
                SettingsButton_Click(sender, e);
            }
        }

        private bool invalidLaunchPathMessageShown;

        private async void AboutForm_Shown(object sender, EventArgs e)
        {
            await Task.Yield();
            if (IsDisposed || Disposing)
                return;

            ShowInvalidLaunchPathMessage();

            await CheckForCacheReset(true, false);
        }

        private void ShowInvalidLaunchPathMessage()
        {
            if (invalidLaunchPathMessageShown)
                return;

            Options options = Program.AppOptions;

            if (options == null || !options.invalidPathProvided || string.IsNullOrWhiteSpace(options.invalidPath))
            {
                return;
            }

            invalidLaunchPathMessageShown = true;

            string displayPath = options.invalidPath
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            WriteLine("Displaying invalid launch path warning for path: " + displayPath, LoggingFrequency.GUILogging);

            MessageBox.Show(this, "The provided path is invalid or does not exist." + Environment.NewLine + Environment.NewLine + "Path: " + displayPath, "Invalid Path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private enum UpdateState
        {
            Updated,
            UpdateAvailable,
            BetaAvailable,
            Error
        }

        private async void CheckForUpdates()
        {
            WriteLine("Checking for updates", LoggingFrequency.DebugLogging);
            UpdateState updateState = await Task.Run(async () =>
            {
                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinThumbPreloader");
                        string GitHubApiResponse = await client.GetStringAsync("https://api.github.com/repos/joaoschlickmann/WinThumbsPreloader-V3/releases");
                        var match = Regex.Match(GitHubApiResponse, @"""tag_name"":\s*""v([\d\.]+(-beta\d+)?)""");
                        if (!match.Success)
                            return UpdateState.Updated;

                        string latestVersionString = match.Groups[1].Captures[0].ToString();
                        WriteLine("LatestVersionString: " + latestVersionString, LoggingFrequency.DebugLogging);

                        Version currentNumericVersion = new Version(Regex.Match(Application.ProductVersion, @"(\d+\.\d+\.\d+)").Value);
                        WriteLine("Current version: " + currentNumericVersion, LoggingFrequency.DebugLogging);
                        Version latestNumericVersion = new Version(Regex.Match(latestVersionString, @"(\d+\.\d+\.\d+)").Value);
                        WriteLine("Latest version: " + latestNumericVersion, LoggingFrequency.DebugLogging);

                        var currentBetaMatch = Regex.Match(Application.ProductVersion, @"-beta(\d+)");
                        WriteLine("CurrentBetaMatch: " + currentBetaMatch, LoggingFrequency.DebugLogging);
                        var latestBetaMatch = Regex.Match(latestVersionString, @"-beta(\d+)");
                        WriteLine("LatestBetaMatch: " + latestBetaMatch, LoggingFrequency.DebugLogging);
                        bool isCurrentBeta = currentBetaMatch.Success;
                        WriteLine("IsCurrentBeta: " + isCurrentBeta, LoggingFrequency.DebugLogging);
                        bool isLatestBeta = latestBetaMatch.Success;
                        WriteLine("IsLatestBeta: " + isLatestBeta, LoggingFrequency.DebugLogging);
                        int currentBetaNumber = isCurrentBeta ? int.Parse(currentBetaMatch.Groups[1].Value) : 0;
                        WriteLine("CurrentBetaNumber: " + currentBetaNumber, LoggingFrequency.DebugLogging);
                        int latestBetaNumber = isLatestBeta ? int.Parse(latestBetaMatch.Groups[1].Value) : 0;
                        WriteLine("LatestBetaNumber: " + latestBetaNumber, LoggingFrequency.DebugLogging);

                        // If latest version is NOT a beta and current version IS a beta or is older, then update is available
                        if (!isLatestBeta && (isCurrentBeta && currentNumericVersion == latestNumericVersion))
                        {
                            WriteLine("Update available", LoggingFrequency.GUILogging);
                            return UpdateState.UpdateAvailable;
                        }
                        // If both current and latest versions are betas, check their beta numbers
                        else if (isCurrentBeta && isLatestBeta)
                        {
                            if (currentBetaNumber < latestBetaNumber)
                            {
                                WriteLine("Beta available", LoggingFrequency.GUILogging);
                                return UpdateState.BetaAvailable;
                            }
                            else
                            {
                                WriteLine("Up to date", LoggingFrequency.GUILogging);
                                return UpdateState.Updated;
                            }
                        }
                        else if (currentNumericVersion < latestNumericVersion)
                        {
                            WriteLine("Update available", LoggingFrequency.GUILogging);
                            return UpdateState.UpdateAvailable;
                        }
                        else
                        {
                            WriteLine("Up to date", LoggingFrequency.GUILogging);
                            return UpdateState.Updated;
                        }
                    }
                }
                catch (Exception e)
                {
                    WriteLine("Failed to check for updates: " + e.Message, LoggingFrequency.GUILogging);
                    return UpdateState.Error;
                }
            });

            if (IsDisposed || Disposing)
                return;

            switch (updateState)
            {
                case UpdateState.Updated:
                    UpdateLabel.Text = Resources.AboutForm_WinThumbsPreloader_IsUpToDate;
                    break;
                case UpdateState.Error:
                    UpdateLabel.Text = Resources.AboutForm_WinThumbsPreloader_UpdateCheckFailed;
                    break;
                case UpdateState.UpdateAvailable:
                    UpdateLabel.Text = Resources.AboutForm_WinThumbsPreloader_NewVersionAvailable;
                    UpdateLabel.ForeColor = Color.FromArgb(0, 102, 204);
                    UpdateLabel.Font = new Font(UpdateLabel.Font.Name, UpdateLabel.Font.SizeInPoints, FontStyle.Underline);
                    UpdateLabel.Cursor = Cursors.Hand;
                    break;
                case UpdateState.BetaAvailable:
                    UpdateLabel.Text = Resources.AboutForm_WinThumbsPreloader_BetaVersionAvailable;
                    UpdateLabel.ForeColor = Color.FromArgb(0, 102, 204);
                    UpdateLabel.Font = new Font(UpdateLabel.Font.Name, UpdateLabel.Font.SizeInPoints, FontStyle.Underline);
                    UpdateLabel.Cursor = Cursors.Hand;
                    break;
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            WriteLine("Exiting application from AboutForm", LoggingFrequency.GUILogging);
            if (Program.activeInstances > 0)
            {
                this.Hide();
                Program.formOpen = false;
            }
            else
            {
                Environment.Exit(0);
            }
        }

        private void LicenceButton_Click(object sender, EventArgs e)
        {
            WriteLine("Opening LicenseForm", LoggingFrequency.GUILogging);
            LicenseForm licenseForm = new LicenseForm();
            this.OpenSecondaryFormCentered(licenseForm);
        }

        private void RichTextBox_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            WriteLine("Link clicked: " + e.LinkText, LoggingFrequency.GUILogging);
            try
            {
                if (!string.IsNullOrWhiteSpace(e.LinkText) &&
                    Uri.TryCreate(e.LinkText, UriKind.Absolute, out Uri uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = uri.AbsoluteUri,
                        UseShellExecute = true
                    });
                    WriteLine("Opened link: " + uri.AbsoluteUri, LoggingFrequency.GUILogging);
                }
            }
            catch (Exception ex)
            {
                WriteLine("Failed to open link: " + e.LinkText + " with error: " + ex.Message, LoggingFrequency.GUILogging);
                MessageBox.Show($"Failed to open link: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateLabel_Click(object sender, EventArgs e)
        {
            WriteLine("Opening GitHub link from UpdateLabel", LoggingFrequency.GUILogging);
            string url = "";

            if (UpdateLabel.Text == Resources.AboutForm_WinThumbsPreloader_NewVersionAvailable ||
                UpdateLabel.Text == Resources.AboutForm_WinThumbsPreloader_BetaVersionAvailable)
            {
                url = "https://github.com/joaoschlickmann/WinThumbsPreloader-V3";
                WriteLine("Opening GitHub link: " + url, LoggingFrequency.GUILogging);
            }

            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    WriteLine("Unable to open the link: " + ex.Message, LoggingFrequency.GUILogging);
                    MessageBox.Show("Unable to open the link: " + ex.Message);
                }
            }
        }

        private void SettingsButton_Click(object sender, EventArgs e)
        {
            WriteLine("Opening SettingsForm from AboutForm", LoggingFrequency.GUILogging);
            SettingsForm settingsForm = new SettingsForm();
            this.OpenFormCentered(settingsForm);
        }

        private void PreloadButton_Click(object sender, EventArgs e)
        {
            WriteLine("PreloadButton clicked", LoggingFrequency.GUILogging);

            if (PreloadButton.Text == "Bulk Preload" || (Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                OpenBulkPreloader();
                return;
            }

            bool recursive = PreloadButton.Text == "Recursively" || (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            StartFolderSelectionPreload(recursive);
        }

        private void OpenBulkPreloader()
        {
            WriteLine("Opening BulkPreloaderForm from AboutForm", LoggingFrequency.GUILogging);
            BulkPreloaderForm bulkForm = new BulkPreloaderForm();
            this.OpenFormCentered(bulkForm);
        }

        private void StartFolderSelectionPreload(bool recursive)
        {
            string preloadMode = recursive ? "Recursively" : "Preload";
            WriteLine("Preload mode: " + preloadMode, LoggingFrequency.GUILogging);

            string folderName = PathFromFolderBrowser();

            if (string.IsNullOrWhiteSpace(folderName))
                return;

            string resolvedFolder = NetworkPathResolver.NormalizeAndResolvePath(folderName) ?? folderName;
            WriteLine("Selected folder: " + resolvedFolder, LoggingFrequency.GUILogging);

            try
            {
                LaunchPreloaderInstance(resolvedFolder, recursive);
            }
            catch (Exception ex)
            {
                WriteLine("Failed to start WinThumbsPreloader: " + ex.Message, LoggingFrequency.GUILogging);
                MessageBox.Show($"Failed to start WinThumbsPreloader: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LaunchPreloaderInstance(string folderPath, bool recursive)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            if (recursive)
                psi.ArgumentList.Add("-r");

            if (Settings.Default.Multithreaded)
            {
                psi.ArgumentList.Add(Settings.Default.ThreadCount > 0
                    ? $"-m:{Settings.Default.ThreadCount}"
                    : "-m");
            }

            psi.ArgumentList.Add(folderPath);

            WriteLine("Launching new preloader process for: " + folderPath, LoggingFrequency.GUILogging);
            Process.Start(psi);
        }

        private string PathFromFolderBrowser()
        {
            using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();

            if (folderBrowserDialog.ShowDialog(this) != DialogResult.OK)
                return null;

            return folderBrowserDialog.SelectedPath;
        }

        private void AboutForm_Activated(object sender, EventArgs e)
        {
            if (Control.ModifierKeys == Keys.Shift)
            {
                PreloadButton.Text = "Recursively";
            }
            else if (Control.ModifierKeys == Keys.Control)
            {
                PreloadButton.Text = "Bulk Preload";
            }
            else
            {
                PreloadButton.Text = "Preload";
            }
        }

        private void AboutForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey)
            {
                PreloadButton.Text = "Recursively";
            }
            else if (e.KeyCode == Keys.ControlKey)
            {
                PreloadButton.Text = "Bulk Preload";
            }
        }

        private void AboutForm_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.ControlKey)
            {
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
                    PreloadButton.Text = "Recursively";
                else if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
                    PreloadButton.Text = "Bulk Preload";
                else
                    PreloadButton.Text = "Preload";
            }
        }

        private static bool recognizedLogged = false;
        private static bool cacheResetLogged = false;
        private static int _cacheResetCheckInProgress = 0;

        public static async Task CheckForCacheReset(bool checkResetRecognized, bool logOnly)
        {
            if (checkResetRecognized && !recognizedLogged && Settings.Default.ResetRecognized)
            {
                WriteLine("Cache reset already recognized, skipping check", LoggingFrequency.DebugLogging);
                recognizedLogged = true;
                return;
            }

            if (System.Threading.Interlocked.CompareExchange(ref _cacheResetCheckInProgress, 1, 0) != 0)
                return;

            try
            {
                bool isReset = await Task.Run(() =>
                    Settings.Default.ThumbsResetAlert && !CacheForm.CompareThumbsCacheSize());

                if (!isReset)
                    return;

                if (!cacheResetLogged)
                {
                    WriteLine("WARNING: Cache Reset Detected", LoggingFrequency.DebugLogging);
                    cacheResetLogged = true;
                }

                if (!logOnly && !Settings.Default.ResetRecognized)
                {
                    if (checkResetRecognized)
                    {
                        Settings.Default.ResetRecognized = true;
                        Settings.Default.Save();
                        WriteLine("cacheResetRecognized set to true", LoggingFrequency.DebugLogging);
                    }

                    DialogResult dialogResult = MessageBox.Show(
                        "The Explorer cache has reset. Would you like to restore the backup?",
                        "Restore Backup",
                        MessageBoxButtons.YesNo);

                    if (dialogResult == DialogResult.Yes)
                    {
                        await CacheForm.RestoreThumbsCache(null, true);
                        if (CacheForm.CompareThumbsCacheSize())
                        {
                            Settings.Default.ResetRecognized = false;
                            Settings.Default.Save();
                            recognizedLogged = false;
                            cacheResetLogged = false;
                            WriteLine("Explorer cache successfully restored", LoggingFrequency.DebugLogging);
                            MessageBox.Show("Explorer cache successfully restored.", "Restore Successful", MessageBoxButtons.OK);
                        }
                        else
                        {
                            WriteLine("Failed to restore Explorer cache", LoggingFrequency.DebugLogging);
                            MessageBox.Show("Failed to restore Explorer cache.", "Restore Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _cacheResetCheckInProgress, 0);
            }
        }
    }
}
