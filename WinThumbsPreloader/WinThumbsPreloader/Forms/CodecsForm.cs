#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader
{
    public partial class CodecsForm : Form
    {
        private readonly WindowsOptimizer.RegistryHiveScope? _scopeOverride;
        private bool _suppressItemCheck;

        public bool ExtensionsSynced { get; private set; }

        public CodecsForm() : this(null)
        {
        }

        public CodecsForm(WindowsOptimizer.RegistryHiveScope? scopeOverride)
        {
            _scopeOverride = scopeOverride;
            InitializeComponent();
        }

        private void CodecsForm_Load(object? sender, EventArgs e)
        {
            WriteLine("CodecsForm loading.", LoggingFrequency.GUILogging);
            Icon = Resources.MainIcon;
            KeyPreview = true;
            KeyDown += CodecsForm_KeyDown;
            KeyUp += CodecsForm_KeyUp;
            Activated += CodecsForm_Activated;

            RefreshCodecList(preserveUserChecks: false);
        }

        private void RefreshCodecList(bool preserveUserChecks)
        {
            var previouslyChecked = new HashSet<CodecFamilyId>();
            if (preserveUserChecks)
            {
                foreach (ListViewItem item in CodecsListView.Items)
                {
                    if (item.Checked && item.Tag is CodecFamilyStatus st)
                    {
                        previouslyChecked.Add(st.Family.Id);
                    }
                }
            }

            bool isElevated = _scopeOverride?.MachineWritable ?? WindowsOptimizer.IsCurrentProcessElevated();
            bool bundleOk = CodecManager.AreBundledCodecsPresent(out string? bundleError);

            if (!bundleOk)
            {
                PrivilegeAndBundleLabel.Text = $"Codecs bundle warning (hover for details): {bundleError}";
                PrivilegeAndBundleLabel.ForeColor = Color.Firebrick;
                CodecsToolTip.SetToolTip(PrivilegeAndBundleLabel, bundleError ?? "Bundled Codecs DLL files are missing.");
            }
            else if (isElevated)
            {
                PrivilegeAndBundleLabel.Text = "Mode: Administrator (installs system-wide HKLM + HKCU for normal & Admin Preload)";
                PrivilegeAndBundleLabel.ForeColor = Color.FromArgb(20, 115, 50);
                CodecsToolTip.SetToolTip(PrivilegeAndBundleLabel, PrivilegeAndBundleLabel.Text);
            }
            else
            {
                PrivilegeAndBundleLabel.Text = "Mode: Standard User (HKCU). Use 'Elevate && Apply (UAC)' for All Users & Run As Admin.";
                PrivilegeAndBundleLabel.ForeColor = Color.FromArgb(0, 90, 158);
                CodecsToolTip.SetToolTip(PrivilegeAndBundleLabel, PrivilegeAndBundleLabel.Text);
            }

            ElevateApplyButton.Enabled = bundleOk;
            ElevateApplyButton.Text = isElevated ? "Re-apply Elevated" : "Elevate && Apply (UAC)";
            ApplyButton.Enabled = bundleOk;

            var statuses = CodecManager.DetectAllStatuses(_scopeOverride);

            _suppressItemCheck = true;
            CodecsListView.BeginUpdate();
            try
            {
                CodecsListView.Items.Clear();
                foreach (var status in statuses)
                {
                    string extText = string.Join(", ", status.Family.Extensions.Take(5));
                    if (status.Family.Extensions.Length > 5)
                        extText += $" (+{status.Family.Extensions.Length - 5})";

                    string statusText = status.IsEnabled
                        ? "Active"
                        : (status.IsPartiallyEnabled
                            ? "Partial"
                            : (status.HasExternalHandler ? "External" : "Not Installed"));

                    var item = new ListViewItem(status.Family.DisplayName)
                    {
                        Tag = status,
                        Checked = preserveUserChecks
                            ? previouslyChecked.Contains(status.Family.Id)
                            : (status.IsEnabled || status.IsPartiallyEnabled),
                        ToolTipText = $"{status.Family.Description}\nExtensions: {string.Join(", ", status.Family.Extensions)}"
                    };
                    item.SubItems.Add(extText);
                    item.SubItems.Add(status.SummaryHandlerName);
                    item.SubItems.Add(statusText);

                    if (status.IsEnabled)
                    {
                        item.BackColor = Color.FromArgb(232, 248, 236);
                        item.ForeColor = Color.FromArgb(18, 92, 38);
                    }
                    else if (status.HasExternalHandler)
                    {
                        item.BackColor = Color.FromArgb(255, 246, 224);
                        item.ForeColor = Color.FromArgb(128, 76, 0);
                    }
                    else
                    {
                        item.BackColor = SystemColors.Window;
                        item.ForeColor = SystemColors.WindowText;
                    }

                    CodecsListView.Items.Add(item);
                }
            }
            finally
            {
                CodecsListView.EndUpdate();
                _suppressItemCheck = false;
            }
        }

        private int GetSelectedCodecMask()
        {
            int mask = 0;
            foreach (ListViewItem item in CodecsListView.Items)
            {
                if (item.Checked && item.Tag is CodecFamilyStatus status)
                {
                    mask |= 1 << status.Family.BitIndex;
                }
            }
            return mask & CodecManager.AllCodecsMask;
        }

        private void SelectAllButton_Click(object? sender, EventArgs e)
        {
            _suppressItemCheck = true;
            foreach (ListViewItem item in CodecsListView.Items)
            {
                item.Checked = true;
            }
            _suppressItemCheck = false;
        }

        private void SelectMissingButton_Click(object? sender, EventArgs e)
        {
            _suppressItemCheck = true;
            foreach (ListViewItem item in CodecsListView.Items)
            {
                if (item.Tag is CodecFamilyStatus status)
                {
                    item.Checked = status.IsEnabled || !status.HasExternalHandler;
                }
            }
            _suppressItemCheck = false;
        }

        private void SelectNoneButton_Click(object? sender, EventArgs e)
        {
            _suppressItemCheck = true;
            foreach (ListViewItem item in CodecsListView.Items)
            {
                item.Checked = false;
            }
            _suppressItemCheck = false;
        }

        private void ApplyButton_Click(object? sender, EventArgs e)
        {
            ApplySelection(elevateViaUac: false);
        }

        private void ElevateApplyButton_Click(object? sender, EventArgs e)
        {
            bool isElevated = _scopeOverride?.MachineWritable ?? WindowsOptimizer.IsCurrentProcessElevated();
            ApplySelection(elevateViaUac: !isElevated);
        }

        private void ApplySelection(bool elevateViaUac)
        {
            int mask = GetSelectedCodecMask();
            if (mask != 0 && !CodecManager.AreBundledCodecsPresent(out string? err))
            {
                MessageBox.Show(this, err ?? "Bundled Codecs DLL files are missing.", "Missing Codecs", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!ConfirmExternalOverridesIfAny(mask))
                return;

            try
            {
                if (elevateViaUac && _scopeOverride == null)
                {
                    string args = CodecManager.BuildApplyCliArguments(mask);
                    if (!WindowsOptimizer.RunElevatedHelper(args))
                    {
                        MessageBox.Show(
                            this,
                            "Administrator elevation was cancelled or failed.",
                            "UAC Elevation Required",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    // Over-the-shoulder UAC applies HKLM globally but writes HKCU under the admin account;
                    // apply locally afterwards so the interactive user's HKCU UserChoice ProgId overrides sync too.
                    if (!WindowsOptimizer.IsCurrentProcessElevated())
                    {
                        CodecManager.ApplyCodecSelection(mask, broadcastShellChange: true);
                    }
                }
                else
                {
                    CodecManager.ApplyCodecSelection(mask, _scopeOverride, broadcastShellChange: _scopeOverride == null);
                }

                if (SyncExtensionsCheckBox.Checked && mask != 0 && _scopeOverride == null)
                {
                    CodecManager.MergeEnabledCodecExtensionsIntoSettings(mask);
                    ExtensionsSynced = true;
                }

                RefreshCodecList(preserveUserChecks: false);

                string scopeNotice = (!elevateViaUac && !WindowsOptimizer.IsCurrentProcessElevated() && Settings.Default.Admin)
                    ? "\n\nNote: 'Run As Admin' is enabled in Settings. Click 'Elevate & Apply (UAC)' so elevated preload processes also see these codecs."
                    : string.Empty;

                MessageBox.Show(
                    this,
                    $"Thumbnail codecs updated (mask 0x{mask:X3}).\n\nIf a folder was already opened before installing codecs, use 'Clear Cache...' so Windows Explorer regenerates empty cached icons.{scopeNotice}",
                    "Codecs Updated",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                WriteLine($"Failed applying codec selection: {ex.Message}", LoggingFrequency.AllLogging);
                MessageBox.Show(this, $"Failed to apply codec selection:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ConfirmExternalOverridesIfAny(int mask)
        {
            var overridden = new List<string>();
            foreach (ListViewItem item in CodecsListView.Items)
            {
                if (item.Tag is CodecFamilyStatus st &&
                    (mask & (1 << st.Family.BitIndex)) != 0 &&
                    st.HasExternalHandler &&
                    !st.IsEnabled)
                {
                    overridden.Add($"• {st.Family.DisplayName} (currently: {st.SummaryHandlerName})");
                }
            }

            if (overridden.Count == 0)
                return true;

            DialogResult confirm = MessageBox.Show(
                this,
                "The following formats already have an active thumbnail handler:\n\n" +
                string.Join("\n", overridden) +
                "\n\nWinThumbsPreloader will back up the current registry settings and restore them automatically if you uncheck or uninstall later.\n\nProceed?",
                "Replace Existing Thumbnail Handlers?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            return confirm == DialogResult.Yes;
        }

        private void UninstallAllButton_Click(object? sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                this,
                "Uninstall all WinThumbsPreloader built-in codecs and restore previously backed-up thumbnail handlers (e.g. Ardfry, Acrobat, PowerToys)?",
                "Uninstall Built-in Codecs",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                bool machineUninstallSkipped = false;
                if (_scopeOverride == null &&
                    !WindowsOptimizer.IsCurrentProcessElevated() &&
                    CodecManager.HasMachineLevelCodecsOrBackup())
                {
                    if (!WindowsOptimizer.RunElevatedHelper("-uninstall-codecs"))
                    {
                        machineUninstallSkipped = true;
                    }
                }

                CodecManager.UninstallAllCodecs(_scopeOverride, broadcastShellChange: _scopeOverride == null);

                RefreshCodecList(preserveUserChecks: false);
                if (machineUninstallSkipped)
                {
                    MessageBox.Show(
                        this,
                        "Current-user (HKCU) built-in codec entries were removed, while system-wide (HKLM) entries require Administrator elevation to uninstall.",
                        "Partial Uninstall (UAC Cancelled)",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(
                        this,
                        "All built-in codecs were uninstalled and backed-up handlers have been restored.",
                        "Codecs Uninstalled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                WriteLine($"Failed uninstalling codecs: {ex.Message}", LoggingFrequency.AllLogging);
                MessageBox.Show(this, $"Failed to uninstall codecs:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClearCacheButton_Click(object? sender, EventArgs e)
        {
            WriteLine("Opening CacheForm from CodecsForm.", LoggingFrequency.GUILogging);
            this.OpenFormCentered(new CacheForm());
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            if (CloseButton.Text == "Exit" || (ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                Program.ExitApplication(0);
                return;
            }
            Close();
        }

        private void CodecsForm_Activated(object? sender, EventArgs e)
        {
            UpdateShiftCloseText();
        }

        private void CodecsForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey)
                UpdateShiftCloseText();
        }

        private void CodecsForm_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey)
                UpdateShiftCloseText();
        }

        private void UpdateShiftCloseText()
        {
            CloseButton.Text = (ModifierKeys & Keys.Shift) == Keys.Shift ? "Exit" : "Close";
        }

        private void CodecsListView_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_suppressItemCheck)
                return;
        }
    }
}
