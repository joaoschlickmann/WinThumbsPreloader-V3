#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader
{
    public partial class WindowsOptimizerForm : Form
    {
        private bool _suppressEvents;
        private readonly HashSet<Control> _hoveredHelpBadges = new();
        private readonly Dictionary<Control, string> _helpTexts = new();

        public IReadOnlyDictionary<Control, string> HelpTexts => _helpTexts;

        public WindowsOptimizerForm()
        {
            InitializeComponent();
        }

        private void WindowsOptimizerForm_Load(object sender, EventArgs e)
        {
            WriteLine("WindowsOptimizerForm loading.", LoggingFrequency.GUILogging);
            Icon = Resources.MainIcon;

            ConfigureToolTipsAndHelpBadges();
            LoadProfileIntoControls(WindowsOptimizer.OptimizationProfile.LoadFromSettings());
            RefreshLiveSystemStatus();
        }

        private void ConfigureToolTipsAndHelpBadges()
        {
            BindHelpBadge(
                EnforceStartupHelpLabel,
                EnforceStartupCheckBox,
                "WHAT IT DOES:\n" +
                "Automatically re-applies your saved Windows optimizations every time WinThumbsPreloader starts.\n\n" +
                "[+] PROS:\n" +
                "• Prevents major Windows Feature Updates from silently resetting thumbnail cache protection back to defaults (Autorun = 3).\n" +
                "• Keeps your user-level optimizations active with zero manual effort.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• If the application starts without Administrator privileges, only user-scoped (HKCU) settings are re-applied at startup.");

            BindHelpBadge(
                PreventCacheCleanupHelpLabel,
                PreventCacheCleanupCheckBox,
                "WHAT IT DOES:\n" +
                "Sets 'Autorun = 0' (DWORD) across HKCU, HKLM, and WOW6432Node under:\n" +
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VolumeCaches\\Thumbnail Cache.\n\n" +
                "[+] PROS:\n" +
                "• The most important safeguard against unexpected thumbnail cache resets.\n" +
                "• Stops Windows 10/11 Automatic Maintenance from silently wiping your thumbcache_*.db files when the PC is idle.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• Your thumbcache_*.db files will continue to grow on disk as more folders are preloaded (managed automatically by the app's 1.6 GB bucket spillover and backup engine).");

            BindHelpBadge(
                ClearDiskCleanupFlagsHelpLabel,
                ClearDiskCleanupFlagsCheckBox,
                "WHAT IT DOES:\n" +
                "Zeroes out all 'StateFlags0000' through 'StateFlags9999' values under the VolumeCaches\\Thumbnail Cache registry key.\n\n" +
                "[+] PROS:\n" +
                "• Ensures saved or automated Disk Cleanup profiles (cleanmgr.exe /sagerun) never include the thumbnail cache for deletion.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• If you manually open Windows Disk Cleanup in the future and intentionally want to delete thumbnails there, you will need to re-check the 'Thumbnails' box.");

            BindHelpBadge(
                DisableSilentCleanupHelpLabel,
                DisableSilentCleanupTaskCheckBox,
                "WHAT IT DOES:\n" +
                "Disables the '\\Microsoft\\Windows\\DiskCleanup\\SilentCleanup' scheduled task in Windows Task Scheduler (requires Administrator).\n\n" +
                "[+] PROS:\n" +
                "• Completely blocks Windows from running background Disk Cleanup (cleanmgr.exe) during idle maintenance windows.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• Because SilentCleanup also purges general temporary files (%TEMP% and old Windows Update logs), disabling it stops automatic temp-file cleanup as well.\n" +
                "• For most users, keeping 'Autorun = 0' enabled (the first option) already protects thumbnails without needing to disable this scheduled task.");

            BindHelpBadge(
                DisableNetworkThumbsDbHelpLabel,
                DisableNetworkThumbsDbCheckBox,
                "WHAT IT DOES:\n" +
                "Sets 'DisableThumbsDBOnNetworkFolders = 1' (DWORD) in:\n" +
                "• HKCU\\Software\\Policies\\Microsoft\\Windows\\Explorer\n" +
                "• HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\n\n" +
                "[+] PROS:\n" +
                "• Eliminates the classic 'The action can't be completed because the file is open in Windows Explorer' error caused by locked Thumbs.db files when renaming or deleting SMB/NAS folders.\n" +
                "• Avoids slow network writes and forces Windows Explorer to store all network thumbnails in your fast local SSD cache (thumbcache_*.db).\n\n" +
                "[-] CONS / NOTES:\n" +
                "• Other computers on the network will not share a pre-generated Thumbs.db file inside the remote folder (each PC will maintain its own local cache).");

            BindHelpBadge(
                ForceThumbnailPreviewsHelpLabel,
                ForceThumbnailPreviewsCheckBox,
                "WHAT IT DOES:\n" +
                "Sets 'IconsOnly = 0' in Explorer\\Advanced and 'DisableThumbnailCache = 0' / 'NoThumbnailCache = 0' in Explorer Policies.\n\n" +
                "[+] PROS:\n" +
                "• Ensures Windows Explorer always renders real image and video thumbnails (instead of static file icons) and keeps local thumbnail caching enabled.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• On very old machines with slow mechanical hard drives, rendering thumbnails uses slightly more CPU and disk I/O than displaying static icons.");

            BindHelpBadge(
                DisableFolderDiscoveryHelpLabel,
                DisableFolderDiscoveryCheckBox,
                "WHAT IT DOES:\n" +
                "Sets 'FolderType = NotSpecified' (REG_SZ) under:\n" +
                "HKCU\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\Bags\\AllFolders\\Shell.\n\n" +
                "[+] PROS:\n" +
                "• Stops Windows Explorer from inspecting every file inside a folder to guess whether it contains 'Pictures', 'Videos', 'Music', or 'General Items'.\n" +
                "• Makes folders with thousands of media files open instantaneously and eliminates the slow green loading bar in Explorer.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• Newly opened folders will default to the 'General Items' column template (though you can still freely change icon sizes and view modes in any folder).");

            BindHelpBadge(
                ExpandBagMruHelpLabel,
                ExpandBagMruCheckBox,
                "WHAT IT DOES:\n" +
                "Increases 'BagMRU Size' from 5,000 (the Windows default) to 20,000 folders under the Shell and ShellNoRoam registry keys.\n\n" +
                "[+] PROS:\n" +
                "• Prevents Windows Explorer from 'forgetting' your custom thumbnail sizes (e.g., Large / Extra Large Icons) and sort order after browsing more than 5,000 folders.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• Uses a few extra megabytes in your user registry hive (UsrClass.dat) as thousands of folder view preferences are stored.");

            BindHelpBadge(
                ExpandIconCacheHelpLabel,
                ExpandIconCacheCheckBox,
                "WHAT IT DOES:\n" +
                "Sets 'Max Cached Icons' under HKLM and HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer, expanding the Windows icon cache from its legacy ~500 KB default.\n\n" +
                "[+] PROS:\n" +
                "• Prevents desktop, taskbar, and Explorer application/folder icons from turning blank white or flickering and redrawing slowly.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• Uses 4 MB to 16 MB of disk/RAM for the icon cache and requires restarting Windows Explorer (or signing out and back in) to take full effect.");

            BindHelpBadge(
                IconCacheSizeHelpLabel,
                IconCacheSizeComboBox,
                "WHAT IT DOES:\n" +
                "Selects the maximum capacity for the Windows system icon cache (4 MB, 8 MB, or 16 MB).\n\n" +
                "[+] PROS:\n" +
                "• 8192 KB (8 MB) is the recommended sweet spot for most modern PCs.\n" +
                "• 16384 KB (16 MB) is ideal for 4K / High-DPI displays with hundreds of installed applications.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• Increasing beyond 8–16 MB rarely provides any noticeable additional benefit.");

            BindHelpBadge(
                ContextMenuHelpLabel,
                RegisterContextMenuButton,
                "WHAT IT DOES:\n" +
                "Registers or removes the cascading 'WinThumbsPreloader' right-click context menu for Folders, Folder Backgrounds, and Drives.\n\n" +
                "[+] PROS:\n" +
                "• Works even in Portable mode without Administrator privileges (by registering under HKCU\\Software\\Classes).\n" +
                "• Lets you preload any folder (standard, bulk, silent, or recursive) with a single right-click in Windows Explorer.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• If you use the Portable version and move WinThumbsPreloader.exe to a different folder later, click 'Register Context Menu' again to update the executable path.");
            MainToolTip.SetToolTip(UnregisterContextMenuButton, _helpTexts[ContextMenuHelpLabel]);

            BindHelpBadge(
                RebuildIconCacheHelpLabel,
                RebuildIconCacheButton,
                "WHAT IT DOES:\n" +
                "Deletes only 'iconcache_*.db' and 'IconCache.db' files and runs 'ie4uinit.exe -show' to refresh Windows system icons.\n\n" +
                "[+] PROS:\n" +
                "• Fixes corrupted or blank application/shortcut icons WITHOUT touching any 'thumbcache_*.db' files (your preloaded thumbnails remain 100% intact).\n\n" +
                "[-] CONS / NOTES:\n" +
                "• Any iconcache_*.db files currently locked by explorer.exe will only be fully recreated after restarting Windows Explorer.");

            BindHelpBadge(
                RestartExplorerHelpLabel,
                RestartExplorerButton,
                "WHAT IT DOES:\n" +
                "Cleanly terminates 'explorer.exe' (using TerminateProcess exit code 1) and immediately relaunches it.\n\n" +
                "[+] PROS:\n" +
                "• Applies ShellBag, Folder Type Discovery, and Icon Cache changes immediately without requiring a system reboot or sign-out.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• Closes currently open File Explorer windows and interrupts any active file copy/move operations being performed by Explorer.");

            BindHelpBadge(
                RestoreDefaultsHelpLabel,
                RestoreDefaultsButton,
                "WHAT IT DOES:\n" +
                "Removes all optimization keys and policies managed by this module and restores standard Windows 10/11 defaults (Autorun = 3, default BagMRU, SilentCleanup enabled, etc.).\n\n" +
                "[+] PROS:\n" +
                "• Guarantees 100% reversibility if you ever want to return Windows to its stock out-of-the-box behavior.\n\n" +
                "[-] CONS / NOTES:\n" +
                "• Once Windows defaults are restored, Windows Automatic Maintenance will resume periodically purging the thumbnail cache during idle periods.");

            MainToolTip.SetToolTip(
                RecommendedButton,
                "Automatically selects all safe, recommended optimizations for maximum Windows Explorer speed and full thumbnail cache protection.");

            MainToolTip.SetToolTip(
                ElevateApplyButton,
                "Saves your profile and launches a quick Administrator helper (UAC) to apply system-wide (HKLM and Scheduled Task) settings.");

            MainToolTip.SetToolTip(
                ApplyButton,
                "Saves and immediately applies all selected optimizations.");
        }

        private void BindHelpBadge(Label badge, Control associatedControl, string helpText)
        {
            _helpTexts[badge] = helpText;
            badge.Text = string.Empty;
            badge.Cursor = Cursors.Help;

            MainToolTip.SetToolTip(badge, helpText);
            if (associatedControl != null)
            {
                MainToolTip.SetToolTip(associatedControl, helpText);
            }

            badge.Paint -= HelpBadge_Paint;
            badge.Paint += HelpBadge_Paint;

            badge.MouseEnter -= HelpBadge_MouseEnter;
            badge.MouseEnter += HelpBadge_MouseEnter;

            badge.MouseLeave -= HelpBadge_MouseLeave;
            badge.MouseLeave += HelpBadge_MouseLeave;

            badge.Click -= HelpBadge_Click;
            badge.Click += HelpBadge_Click;
        }

        private void HelpBadge_Paint(object sender, PaintEventArgs e)
        {
            if (sender is not Label badge)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool isHovered = _hoveredHelpBadges.Contains(badge);

            Rectangle circleRect = new Rectangle(1, 1, badge.Width - 3, badge.Height - 3);
            Color fillColor = isHovered ? Color.FromArgb(0, 120, 215) : Color.FromArgb(232, 242, 252);
            Color borderColor = Color.FromArgb(0, 120, 215);
            Color textColor = isHovered ? Color.White : Color.FromArgb(0, 102, 184);

            using (SolidBrush fillBrush = new SolidBrush(fillColor))
            {
                e.Graphics.FillEllipse(fillBrush, circleRect);
            }

            using (Pen borderPen = new Pen(borderColor, 1.2f))
            {
                e.Graphics.DrawEllipse(borderPen, circleRect);
            }

            using Font questionFont = new Font("Segoe UI", 8.25F, FontStyle.Bold, GraphicsUnit.Point);
            TextRenderer.DrawText(
                e.Graphics,
                "?",
                questionFont,
                new Rectangle(0, 0, badge.Width, badge.Height),
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void HelpBadge_MouseEnter(object sender, EventArgs e)
        {
            if (sender is Label badge)
            {
                _hoveredHelpBadges.Add(badge);
                badge.Invalidate();
            }
        }

        private void HelpBadge_MouseLeave(object sender, EventArgs e)
        {
            if (sender is Label badge)
            {
                _hoveredHelpBadges.Remove(badge);
                badge.Invalidate();
            }
        }

        private void HelpBadge_Click(object sender, EventArgs e)
        {
            if (sender is Label badge && _helpTexts.TryGetValue(badge, out string text))
            {
                MainToolTip.Show(text, badge, badge.Width + 4, 0, 15000);
            }
        }

        private void LoadProfileIntoControls(WindowsOptimizer.OptimizationProfile profile)
        {
            _suppressEvents = true;
            try
            {
                EnforceStartupCheckBox.Checked = profile.EnforceOnStartup;
                PreventCacheCleanupCheckBox.Checked = profile.PreventThumbCacheCleanup;
                ClearDiskCleanupFlagsCheckBox.Checked = profile.ClearDiskCleanupFlags;
                DisableSilentCleanupTaskCheckBox.Checked = profile.DisableSilentCleanupTask;
                DisableNetworkThumbsDbCheckBox.Checked = profile.DisableNetworkThumbsDb;
                ForceThumbnailPreviewsCheckBox.Checked = profile.ForceThumbnailPreviews;
                DisableFolderDiscoveryCheckBox.Checked = profile.DisableFolderTypeDiscovery;
                ExpandBagMruCheckBox.Checked = profile.ExpandBagMruSize;
                ExpandIconCacheCheckBox.Checked = profile.ExpandIconCache;

                IconCacheSizeComboBox.SelectedIndex = profile.IconCacheSizeKb switch
                {
                    <= 4096 => 0,
                    >= 16384 => 2,
                    _ => 1
                };
                IconCacheSizeComboBox.Enabled = ExpandIconCacheCheckBox.Checked;
                IconCacheSizeComboBox.Refresh();
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private WindowsOptimizer.OptimizationProfile BuildProfileFromControls()
        {
            int sizeKb = IconCacheSizeComboBox.SelectedIndex switch
            {
                0 => 4096,
                2 => 16384,
                _ => 8192
            };

            return new WindowsOptimizer.OptimizationProfile
            {
                EnforceOnStartup = EnforceStartupCheckBox.Checked,
                PreventThumbCacheCleanup = PreventCacheCleanupCheckBox.Checked,
                ClearDiskCleanupFlags = ClearDiskCleanupFlagsCheckBox.Checked,
                DisableSilentCleanupTask = DisableSilentCleanupTaskCheckBox.Checked,
                DisableNetworkThumbsDb = DisableNetworkThumbsDbCheckBox.Checked,
                ForceThumbnailPreviews = ForceThumbnailPreviewsCheckBox.Checked,
                DisableFolderTypeDiscovery = DisableFolderDiscoveryCheckBox.Checked,
                ExpandBagMruSize = ExpandBagMruCheckBox.Checked,
                ExpandIconCache = ExpandIconCacheCheckBox.Checked,
                IconCacheSizeKb = sizeKb
            };
        }

        public void RefreshLiveSystemStatus()
        {
            var report = WindowsOptimizer.InspectSystemState();

            if (report.IsProcessElevated)
            {
                PrivilegeStatusLabel.Text = "Privilege Mode: Administrator (Full System Access)";
                PrivilegeStatusLabel.ForeColor = Color.LimeGreen;
            }
            else
            {
                PrivilegeStatusLabel.Text = "Privilege Mode: Standard User (HKCU Active)";
                PrivilegeStatusLabel.ForeColor = Color.DodgerBlue;
            }

            SetStatusBadge(PreventCacheCleanupStatusLabel, report.PreventThumbCacheCleanup);
            SetStatusBadge(ClearDiskCleanupFlagsStatusLabel, report.ClearDiskCleanupFlags);
            SetStatusBadge(DisableSilentCleanupStatusLabel, report.DisableSilentCleanupTask);
            SetStatusBadge(DisableNetworkThumbsDbStatusLabel, report.DisableNetworkThumbsDb);
            SetStatusBadge(ForceThumbnailPreviewsStatusLabel, report.ForceThumbnailPreviews);
            SetStatusBadge(DisableFolderDiscoveryStatusLabel, report.DisableFolderTypeDiscovery);
            SetStatusBadge(ExpandBagMruStatusLabel, report.ExpandBagMruSize);
            SetStatusBadge(ExpandIconCacheStatusLabel, report.ExpandIconCache);

            if (report.ExplorerContextMenuRegistered)
            {
                string scopeLabel = report.ExplorerContextMenuSystemWide ? "All Users (HKLM)" : "Current User (HKCU)";
                ContextMenuStatusLabel.Text = $"Status: Registered — {scopeLabel}";
                ContextMenuStatusLabel.ForeColor = Color.LimeGreen;
            }
            else
            {
                ContextMenuStatusLabel.Text = "Status: Not Registered";
                ContextMenuStatusLabel.ForeColor = SystemColors.GrayText;
            }

            var currentProfile = BuildProfileFromControls();
            ElevateApplyButton.Enabled = !report.IsProcessElevated && report.NeedsElevationForProfile(currentProfile);
        }

        private static void SetStatusBadge(Label label, WindowsOptimizer.OptimizationState state)
        {
            switch (state)
            {
                case WindowsOptimizer.OptimizationState.Optimized:
                    label.Text = "Optimized";
                    label.ForeColor = Color.LimeGreen;
                    break;
                case WindowsOptimizer.OptimizationState.PartiallyOptimized:
                    label.Text = "User Only (HKCU)";
                    label.ForeColor = Color.DarkOrange;
                    break;
                default:
                    label.Text = "Default";
                    label.ForeColor = SystemColors.GrayText;
                    break;
            }
        }

        private void AnyOptimizationCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (_suppressEvents)
                return;

            IconCacheSizeComboBox.Refresh();
            var report = WindowsOptimizer.InspectSystemState();
            ElevateApplyButton.Enabled = !report.IsProcessElevated && report.NeedsElevationForProfile(BuildProfileFromControls());
        }

        private void ExpandIconCacheCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            IconCacheSizeComboBox.Enabled = ExpandIconCacheCheckBox.Checked;
            IconCacheSizeComboBox.Refresh();
            AnyOptimizationCheckBox_CheckedChanged(sender, e);
        }

        private void RecommendedButton_Click(object sender, EventArgs e)
        {
            WriteLine("Applying Recommended preset in WindowsOptimizerForm.", LoggingFrequency.GUILogging);
            var recommended = WindowsOptimizer.OptimizationProfile.CreateRecommended();
            LoadProfileIntoControls(recommended);
            AnyOptimizationCheckBox_CheckedChanged(sender, e);
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            WriteLine("Applying Windows optimizations from WindowsOptimizerForm.", LoggingFrequency.GUILogging);
            var profile = BuildProfileFromControls();
            profile.SaveToSettings();

            var result = WindowsOptimizer.ApplyOptimizations(profile, broadcastShellChange: true);
            RefreshLiveSystemStatus();

            if (result.RequiresElevationForFullCoverage && !WindowsOptimizer.IsCurrentProcessElevated())
            {
                MessageBox.Show(
                    this,
                    "User-level (HKCU) optimizations have been applied immediately.\n\n" +
                    "Some selected system-wide settings (HKLM Thumbnail Cache Autorun, Icon Cache, or SilentCleanup Task) " +
                    "require Administrator privileges for full protection.\n\n" +
                    "Click 'Elevate & Apply (UAC)' if you wish to apply them system-wide.",
                    "Optimizations Applied (User Scope)",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    this,
                    "All selected Windows optimizations have been applied successfully.",
                    "Optimizations Applied",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void ElevateApplyButton_Click(object sender, EventArgs e)
        {
            WriteLine("Elevating via UAC helper to apply system-wide Windows optimizations.", LoggingFrequency.GUILogging);
            var profile = BuildProfileFromControls();
            profile.SaveToSettings();

            WindowsOptimizer.ApplyOptimizations(profile, broadcastShellChange: false);

            bool elevatedSuccess = WindowsOptimizer.RunElevatedHelper(profile.BuildElevatedCliArguments());
            RefreshLiveSystemStatus();

            if (elevatedSuccess)
            {
                MessageBox.Show(
                    this,
                    "System-wide (HKLM & Scheduled Task) optimizations were applied with Administrator privileges.",
                    "Elevated Optimizations Applied",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void RegisterContextMenuButton_Click(object sender, EventArgs e)
        {
            WriteLine("Registering Explorer Context Menu from WindowsOptimizerForm.", LoggingFrequency.GUILogging);
            bool ok = WindowsOptimizer.RegisterExplorerContextMenu();
            RefreshLiveSystemStatus();

            if (ok)
            {
                MessageBox.Show(
                    this,
                    "WinThumbsPreloader right-click context menu has been registered for Folders, Folder Backgrounds, and Drives.",
                    "Context Menu Registered",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void UnregisterContextMenuButton_Click(object sender, EventArgs e)
        {
            WriteLine("Unregistering Explorer Context Menu from WindowsOptimizerForm.", LoggingFrequency.GUILogging);
            WindowsOptimizer.UnregisterExplorerContextMenu();
            RefreshLiveSystemStatus();

            MessageBox.Show(
                this,
                "WinThumbsPreloader right-click context menu has been removed.",
                "Context Menu Removed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void RebuildIconCacheButton_Click(object sender, EventArgs e)
        {
            WriteLine("Rebuilding Icon Cache Only from WindowsOptimizerForm.", LoggingFrequency.GUILogging);
            int deleted = WindowsOptimizer.ClearIconCacheFilesOnly(invokeIe4uinit: true);

            MessageBox.Show(
                this,
                $"Windows Icon Cache rebuild triggered ({deleted} unlocked iconcache_*.db file(s) removed).\n\n" +
                "All of your preloaded 'thumbcache_*.db' thumbnail files were kept 100% intact.",
                "Icon Cache Rebuilt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void RestartExplorerButton_Click(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                this,
                "Restart Windows Explorer (explorer.exe) now so all ShellBag, Icon Cache, and Policy changes take immediate effect?",
                "Restart Windows Explorer",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return;

            WriteLine("Restarting Windows Explorer from WindowsOptimizerForm.", LoggingFrequency.GUILogging);
            WindowsOptimizer.RestartExplorerCleanly();
        }

        private void RestoreDefaultsButton_Click(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                this,
                "Restore all Windows Registry and Scheduled Task optimizations managed by this module back to standard Windows defaults?",
                "Restore Windows Defaults",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            WriteLine("Restoring Windows defaults from WindowsOptimizerForm.", LoggingFrequency.GUILogging);

            var defaultProfile = new WindowsOptimizer.OptimizationProfile
            {
                EnforceOnStartup = false,
                PreventThumbCacheCleanup = false,
                ClearDiskCleanupFlags = false,
                DisableSilentCleanupTask = false,
                DisableNetworkThumbsDb = false,
                ForceThumbnailPreviews = false,
                DisableFolderTypeDiscovery = false,
                ExpandBagMruSize = false,
                ExpandIconCache = false,
                IconCacheSizeKb = 8192
            };
            defaultProfile.SaveToSettings();
            LoadProfileIntoControls(defaultProfile);

            WindowsOptimizer.RestoreWindowsDefaults(broadcastShellChange: true);
            if (!WindowsOptimizer.IsCurrentProcessElevated())
            {
                WindowsOptimizer.RunElevatedHelper("-restore-defaults");
            }

            RefreshLiveSystemStatus();
            MessageBox.Show(
                this,
                "Windows default settings have been restored.",
                "Defaults Restored",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void WindowsOptimizerForm_Activated(object sender, EventArgs e)
        {
            CloseButton.Text = ModifierKeys == Keys.Shift ? "Exit" : "Close";
            RefreshLiveSystemStatus();
        }

        private void WindowsOptimizerForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey)
                CloseButton.Text = "Exit";
        }

        private void WindowsOptimizerForm_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey)
                CloseButton.Text = "Close";
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            if (CloseButton.Text == "Exit")
                Program.ExitApplication(0);
            else
                Close();
        }
    }
}
