using System.Drawing;
using System.Windows.Forms;

namespace WinThumbsPreloader
{
    partial class WindowsOptimizerForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            MainToolTip = new ToolTip(components);
            OptimizerGroupBox = new GroupBox();
            PrivilegeStatusLabel = new Label();
            EnforceStartupCheckBox = new CheckBox();
            EnforceStartupHelpLabel = new Label();
            OptimizerTabControl = new TabControl();
            CacheNetworkTabPage = new TabPage();
            CacheProtectionGroupBox = new GroupBox();
            PreventCacheCleanupCheckBox = new CheckBox();
            PreventCacheCleanupHelpLabel = new Label();
            PreventCacheCleanupStatusLabel = new Label();
            ClearDiskCleanupFlagsCheckBox = new CheckBox();
            ClearDiskCleanupFlagsHelpLabel = new Label();
            ClearDiskCleanupFlagsStatusLabel = new Label();
            DisableSilentCleanupTaskCheckBox = new CheckBox();
            DisableSilentCleanupHelpLabel = new Label();
            DisableSilentCleanupStatusLabel = new Label();
            NetworkPolicyGroupBox = new GroupBox();
            DisableNetworkThumbsDbCheckBox = new CheckBox();
            DisableNetworkThumbsDbHelpLabel = new Label();
            DisableNetworkThumbsDbStatusLabel = new Label();
            ForceThumbnailPreviewsCheckBox = new CheckBox();
            ForceThumbnailPreviewsHelpLabel = new Label();
            ForceThumbnailPreviewsStatusLabel = new Label();
            ExplorerPerfTabPage = new TabPage();
            FolderPerfGroupBox = new GroupBox();
            DisableFolderDiscoveryCheckBox = new CheckBox();
            DisableFolderDiscoveryHelpLabel = new Label();
            DisableFolderDiscoveryStatusLabel = new Label();
            ExpandBagMruCheckBox = new CheckBox();
            ExpandBagMruHelpLabel = new Label();
            ExpandBagMruStatusLabel = new Label();
            IconCacheGroupBox = new GroupBox();
            ExpandIconCacheCheckBox = new CheckBox();
            ExpandIconCacheHelpLabel = new Label();
            ExpandIconCacheStatusLabel = new Label();
            IconCacheSizeLabel = new Label();
            IconCacheSizeComboBox = new ComboBox();
            IconCacheSizeHelpLabel = new Label();
            ShellMaintenanceTabPage = new TabPage();
            ContextMenuGroupBox = new GroupBox();
            ContextMenuStatusLabel = new Label();
            RegisterContextMenuButton = new Button();
            UnregisterContextMenuButton = new Button();
            ContextMenuHelpLabel = new Label();
            QuickActionsGroupBox = new GroupBox();
            RebuildIconCacheButton = new Button();
            RebuildIconCacheInfoLabel = new Label();
            RebuildIconCacheHelpLabel = new Label();
            RestartExplorerButton = new Button();
            RestartExplorerInfoLabel = new Label();
            RestartExplorerHelpLabel = new Label();
            RestoreDefaultsButton = new Button();
            RestoreDefaultsInfoLabel = new Label();
            RestoreDefaultsHelpLabel = new Label();
            RecommendedButton = new Button();
            ElevateApplyButton = new Button();
            ApplyButton = new Button();
            CloseButton = new Button();

            OptimizerGroupBox.SuspendLayout();
            OptimizerTabControl.SuspendLayout();
            CacheNetworkTabPage.SuspendLayout();
            CacheProtectionGroupBox.SuspendLayout();
            NetworkPolicyGroupBox.SuspendLayout();
            ExplorerPerfTabPage.SuspendLayout();
            FolderPerfGroupBox.SuspendLayout();
            IconCacheGroupBox.SuspendLayout();
            ShellMaintenanceTabPage.SuspendLayout();
            ContextMenuGroupBox.SuspendLayout();
            QuickActionsGroupBox.SuspendLayout();
            SuspendLayout();

            // 
            // MainToolTip
            // 
            MainToolTip.AutoPopDelay = 32000;
            MainToolTip.InitialDelay = 150;
            MainToolTip.ReshowDelay = 50;
            MainToolTip.ShowAlways = true;
            // 
            // OptimizerGroupBox
            // 
            OptimizerGroupBox.BackColor = SystemColors.ControlLightLight;
            OptimizerGroupBox.Controls.Add(PrivilegeStatusLabel);
            OptimizerGroupBox.Controls.Add(EnforceStartupCheckBox);
            OptimizerGroupBox.Controls.Add(EnforceStartupHelpLabel);
            OptimizerGroupBox.Controls.Add(OptimizerTabControl);
            OptimizerGroupBox.Location = new Point(12, 12);
            OptimizerGroupBox.Name = "OptimizerGroupBox";
            OptimizerGroupBox.Size = new Size(516, 352);
            OptimizerGroupBox.TabIndex = 0;
            OptimizerGroupBox.TabStop = false;
            OptimizerGroupBox.Text = "Windows Optimization && Thumbnail Cache Protection";
            // 
            // PrivilegeStatusLabel
            // 
            PrivilegeStatusLabel.AutoSize = true;
            PrivilegeStatusLabel.Location = new Point(10, 22);
            PrivilegeStatusLabel.Name = "PrivilegeStatusLabel";
            PrivilegeStatusLabel.Size = new Size(180, 15);
            PrivilegeStatusLabel.TabIndex = 0;
            PrivilegeStatusLabel.Text = "Privilege Mode: Checking...";
            // 
            // EnforceStartupCheckBox
            // 
            EnforceStartupCheckBox.AutoSize = true;
            EnforceStartupCheckBox.Location = new Point(322, 20);
            EnforceStartupCheckBox.Name = "EnforceStartupCheckBox";
            EnforceStartupCheckBox.Size = new Size(158, 19);
            EnforceStartupCheckBox.TabIndex = 1;
            EnforceStartupCheckBox.Text = "Re-apply on App Startup";
            EnforceStartupCheckBox.UseVisualStyleBackColor = true;
            // 
            // EnforceStartupHelpLabel
            // 
            EnforceStartupHelpLabel.Location = new Point(486, 20);
            EnforceStartupHelpLabel.Name = "EnforceStartupHelpLabel";
            EnforceStartupHelpLabel.Size = new Size(18, 18);
            EnforceStartupHelpLabel.TabIndex = 2;
            EnforceStartupHelpLabel.Text = "?";
            // 
            // OptimizerTabControl
            // 
            OptimizerTabControl.Controls.Add(CacheNetworkTabPage);
            OptimizerTabControl.Controls.Add(ExplorerPerfTabPage);
            OptimizerTabControl.Controls.Add(ShellMaintenanceTabPage);
            OptimizerTabControl.Location = new Point(10, 44);
            OptimizerTabControl.Name = "OptimizerTabControl";
            OptimizerTabControl.SelectedIndex = 0;
            OptimizerTabControl.Size = new Size(496, 298);
            OptimizerTabControl.TabIndex = 3;
            // 
            // CacheNetworkTabPage
            // 
            CacheNetworkTabPage.BackColor = SystemColors.ControlLightLight;
            CacheNetworkTabPage.Controls.Add(CacheProtectionGroupBox);
            CacheNetworkTabPage.Controls.Add(NetworkPolicyGroupBox);
            CacheNetworkTabPage.Location = new Point(4, 24);
            CacheNetworkTabPage.Name = "CacheNetworkTabPage";
            CacheNetworkTabPage.Padding = new Padding(3);
            CacheNetworkTabPage.Size = new Size(488, 270);
            CacheNetworkTabPage.TabIndex = 0;
            CacheNetworkTabPage.Text = "Cache && Network";
            // 
            // CacheProtectionGroupBox
            // 
            CacheProtectionGroupBox.Controls.Add(PreventCacheCleanupCheckBox);
            CacheProtectionGroupBox.Controls.Add(PreventCacheCleanupHelpLabel);
            CacheProtectionGroupBox.Controls.Add(PreventCacheCleanupStatusLabel);
            CacheProtectionGroupBox.Controls.Add(ClearDiskCleanupFlagsCheckBox);
            CacheProtectionGroupBox.Controls.Add(ClearDiskCleanupFlagsHelpLabel);
            CacheProtectionGroupBox.Controls.Add(ClearDiskCleanupFlagsStatusLabel);
            CacheProtectionGroupBox.Controls.Add(DisableSilentCleanupTaskCheckBox);
            CacheProtectionGroupBox.Controls.Add(DisableSilentCleanupHelpLabel);
            CacheProtectionGroupBox.Controls.Add(DisableSilentCleanupStatusLabel);
            CacheProtectionGroupBox.Location = new Point(8, 6);
            CacheProtectionGroupBox.Name = "CacheProtectionGroupBox";
            CacheProtectionGroupBox.Size = new Size(472, 134);
            CacheProtectionGroupBox.TabIndex = 0;
            CacheProtectionGroupBox.TabStop = false;
            CacheProtectionGroupBox.Text = "Thumbnail Cache Protection (Anti-Purge)";
            // 
            // PreventCacheCleanupCheckBox
            // 
            PreventCacheCleanupCheckBox.AutoSize = true;
            PreventCacheCleanupCheckBox.Location = new Point(12, 26);
            PreventCacheCleanupCheckBox.Name = "PreventCacheCleanupCheckBox";
            PreventCacheCleanupCheckBox.Size = new Size(315, 19);
            PreventCacheCleanupCheckBox.TabIndex = 0;
            PreventCacheCleanupCheckBox.Text = "Disable Automatic Cache Cleanup (Autorun = 0)";
            PreventCacheCleanupCheckBox.UseVisualStyleBackColor = true;
            PreventCacheCleanupCheckBox.CheckedChanged += AnyOptimizationCheckBox_CheckedChanged;
            // 
            // PreventCacheCleanupHelpLabel
            // 
            PreventCacheCleanupHelpLabel.Location = new Point(340, 26);
            PreventCacheCleanupHelpLabel.Name = "PreventCacheCleanupHelpLabel";
            PreventCacheCleanupHelpLabel.Size = new Size(18, 18);
            PreventCacheCleanupHelpLabel.TabIndex = 1;
            PreventCacheCleanupHelpLabel.Text = "?";
            // 
            // PreventCacheCleanupStatusLabel
            // 
            PreventCacheCleanupStatusLabel.AutoSize = true;
            PreventCacheCleanupStatusLabel.Location = new Point(366, 28);
            PreventCacheCleanupStatusLabel.Name = "PreventCacheCleanupStatusLabel";
            PreventCacheCleanupStatusLabel.Size = new Size(45, 15);
            PreventCacheCleanupStatusLabel.TabIndex = 2;
            PreventCacheCleanupStatusLabel.Text = "Default";
            // 
            // ClearDiskCleanupFlagsCheckBox
            // 
            ClearDiskCleanupFlagsCheckBox.AutoSize = true;
            ClearDiskCleanupFlagsCheckBox.Location = new Point(12, 60);
            ClearDiskCleanupFlagsCheckBox.Name = "ClearDiskCleanupFlagsCheckBox";
            ClearDiskCleanupFlagsCheckBox.Size = new Size(318, 19);
            ClearDiskCleanupFlagsCheckBox.TabIndex = 3;
            ClearDiskCleanupFlagsCheckBox.Text = "Clear Disk Cleanup Thumbnail Flags (StateFlags)";
            ClearDiskCleanupFlagsCheckBox.UseVisualStyleBackColor = true;
            ClearDiskCleanupFlagsCheckBox.CheckedChanged += AnyOptimizationCheckBox_CheckedChanged;
            // 
            // ClearDiskCleanupFlagsHelpLabel
            // 
            ClearDiskCleanupFlagsHelpLabel.Location = new Point(340, 60);
            ClearDiskCleanupFlagsHelpLabel.Name = "ClearDiskCleanupFlagsHelpLabel";
            ClearDiskCleanupFlagsHelpLabel.Size = new Size(18, 18);
            ClearDiskCleanupFlagsHelpLabel.TabIndex = 4;
            ClearDiskCleanupFlagsHelpLabel.Text = "?";
            // 
            // ClearDiskCleanupFlagsStatusLabel
            // 
            ClearDiskCleanupFlagsStatusLabel.AutoSize = true;
            ClearDiskCleanupFlagsStatusLabel.Location = new Point(366, 62);
            ClearDiskCleanupFlagsStatusLabel.Name = "ClearDiskCleanupFlagsStatusLabel";
            ClearDiskCleanupFlagsStatusLabel.Size = new Size(45, 15);
            ClearDiskCleanupFlagsStatusLabel.TabIndex = 5;
            ClearDiskCleanupFlagsStatusLabel.Text = "Default";
            // 
            // DisableSilentCleanupTaskCheckBox
            // 
            DisableSilentCleanupTaskCheckBox.AutoSize = true;
            DisableSilentCleanupTaskCheckBox.Location = new Point(12, 94);
            DisableSilentCleanupTaskCheckBox.Name = "DisableSilentCleanupTaskCheckBox";
            DisableSilentCleanupTaskCheckBox.Size = new Size(318, 19);
            DisableSilentCleanupTaskCheckBox.TabIndex = 6;
            DisableSilentCleanupTaskCheckBox.Text = "Disable 'SilentCleanup' Scheduled Task (Admin)";
            DisableSilentCleanupTaskCheckBox.UseVisualStyleBackColor = true;
            DisableSilentCleanupTaskCheckBox.CheckedChanged += AnyOptimizationCheckBox_CheckedChanged;
            // 
            // DisableSilentCleanupHelpLabel
            // 
            DisableSilentCleanupHelpLabel.Location = new Point(340, 94);
            DisableSilentCleanupHelpLabel.Name = "DisableSilentCleanupHelpLabel";
            DisableSilentCleanupHelpLabel.Size = new Size(18, 18);
            DisableSilentCleanupHelpLabel.TabIndex = 7;
            DisableSilentCleanupHelpLabel.Text = "?";
            // 
            // DisableSilentCleanupStatusLabel
            // 
            DisableSilentCleanupStatusLabel.AutoSize = true;
            DisableSilentCleanupStatusLabel.Location = new Point(366, 96);
            DisableSilentCleanupStatusLabel.Name = "DisableSilentCleanupStatusLabel";
            DisableSilentCleanupStatusLabel.Size = new Size(45, 15);
            DisableSilentCleanupStatusLabel.TabIndex = 8;
            DisableSilentCleanupStatusLabel.Text = "Default";
            // 
            // NetworkPolicyGroupBox
            // 
            NetworkPolicyGroupBox.Controls.Add(DisableNetworkThumbsDbCheckBox);
            NetworkPolicyGroupBox.Controls.Add(DisableNetworkThumbsDbHelpLabel);
            NetworkPolicyGroupBox.Controls.Add(DisableNetworkThumbsDbStatusLabel);
            NetworkPolicyGroupBox.Controls.Add(ForceThumbnailPreviewsCheckBox);
            NetworkPolicyGroupBox.Controls.Add(ForceThumbnailPreviewsHelpLabel);
            NetworkPolicyGroupBox.Controls.Add(ForceThumbnailPreviewsStatusLabel);
            NetworkPolicyGroupBox.Location = new Point(8, 146);
            NetworkPolicyGroupBox.Name = "NetworkPolicyGroupBox";
            NetworkPolicyGroupBox.Size = new Size(472, 114);
            NetworkPolicyGroupBox.TabIndex = 1;
            NetworkPolicyGroupBox.TabStop = false;
            NetworkPolicyGroupBox.Text = "SMB / NAS Network && Explorer Thumbnail Policies";
            // 
            // DisableNetworkThumbsDbCheckBox
            // 
            DisableNetworkThumbsDbCheckBox.AutoSize = true;
            DisableNetworkThumbsDbCheckBox.Location = new Point(12, 30);
            DisableNetworkThumbsDbCheckBox.Name = "DisableNetworkThumbsDbCheckBox";
            DisableNetworkThumbsDbCheckBox.Size = new Size(320, 19);
            DisableNetworkThumbsDbCheckBox.TabIndex = 0;
            DisableNetworkThumbsDbCheckBox.Text = "Disable Thumbs.db on Network Folders (SMB/NAS)";
            DisableNetworkThumbsDbCheckBox.UseVisualStyleBackColor = true;
            DisableNetworkThumbsDbCheckBox.CheckedChanged += AnyOptimizationCheckBox_CheckedChanged;
            // 
            // DisableNetworkThumbsDbHelpLabel
            // 
            DisableNetworkThumbsDbHelpLabel.Location = new Point(340, 30);
            DisableNetworkThumbsDbHelpLabel.Name = "DisableNetworkThumbsDbHelpLabel";
            DisableNetworkThumbsDbHelpLabel.Size = new Size(18, 18);
            DisableNetworkThumbsDbHelpLabel.TabIndex = 1;
            DisableNetworkThumbsDbHelpLabel.Text = "?";
            // 
            // DisableNetworkThumbsDbStatusLabel
            // 
            DisableNetworkThumbsDbStatusLabel.AutoSize = true;
            DisableNetworkThumbsDbStatusLabel.Location = new Point(366, 32);
            DisableNetworkThumbsDbStatusLabel.Name = "DisableNetworkThumbsDbStatusLabel";
            DisableNetworkThumbsDbStatusLabel.Size = new Size(45, 15);
            DisableNetworkThumbsDbStatusLabel.TabIndex = 2;
            DisableNetworkThumbsDbStatusLabel.Text = "Default";
            // 
            // ForceThumbnailPreviewsCheckBox
            // 
            ForceThumbnailPreviewsCheckBox.AutoSize = true;
            ForceThumbnailPreviewsCheckBox.Location = new Point(12, 68);
            ForceThumbnailPreviewsCheckBox.Name = "ForceThumbnailPreviewsCheckBox";
            ForceThumbnailPreviewsCheckBox.Size = new Size(310, 19);
            ForceThumbnailPreviewsCheckBox.TabIndex = 3;
            ForceThumbnailPreviewsCheckBox.Text = "Force Thumbnail Previews && Local Cache Active";
            ForceThumbnailPreviewsCheckBox.UseVisualStyleBackColor = true;
            ForceThumbnailPreviewsCheckBox.CheckedChanged += AnyOptimizationCheckBox_CheckedChanged;
            // 
            // ForceThumbnailPreviewsHelpLabel
            // 
            ForceThumbnailPreviewsHelpLabel.Location = new Point(340, 68);
            ForceThumbnailPreviewsHelpLabel.Name = "ForceThumbnailPreviewsHelpLabel";
            ForceThumbnailPreviewsHelpLabel.Size = new Size(18, 18);
            ForceThumbnailPreviewsHelpLabel.TabIndex = 4;
            ForceThumbnailPreviewsHelpLabel.Text = "?";
            // 
            // ForceThumbnailPreviewsStatusLabel
            // 
            ForceThumbnailPreviewsStatusLabel.AutoSize = true;
            ForceThumbnailPreviewsStatusLabel.Location = new Point(366, 70);
            ForceThumbnailPreviewsStatusLabel.Name = "ForceThumbnailPreviewsStatusLabel";
            ForceThumbnailPreviewsStatusLabel.Size = new Size(45, 15);
            ForceThumbnailPreviewsStatusLabel.TabIndex = 5;
            ForceThumbnailPreviewsStatusLabel.Text = "Default";
            // 
            // ExplorerPerfTabPage
            // 
            ExplorerPerfTabPage.BackColor = SystemColors.ControlLightLight;
            ExplorerPerfTabPage.Controls.Add(FolderPerfGroupBox);
            ExplorerPerfTabPage.Controls.Add(IconCacheGroupBox);
            ExplorerPerfTabPage.Location = new Point(4, 24);
            ExplorerPerfTabPage.Name = "ExplorerPerfTabPage";
            ExplorerPerfTabPage.Padding = new Padding(3);
            ExplorerPerfTabPage.Size = new Size(488, 270);
            ExplorerPerfTabPage.TabIndex = 1;
            ExplorerPerfTabPage.Text = "Explorer Performance";
            // 
            // FolderPerfGroupBox
            // 
            FolderPerfGroupBox.Controls.Add(DisableFolderDiscoveryCheckBox);
            FolderPerfGroupBox.Controls.Add(DisableFolderDiscoveryHelpLabel);
            FolderPerfGroupBox.Controls.Add(DisableFolderDiscoveryStatusLabel);
            FolderPerfGroupBox.Controls.Add(ExpandBagMruCheckBox);
            FolderPerfGroupBox.Controls.Add(ExpandBagMruHelpLabel);
            FolderPerfGroupBox.Controls.Add(ExpandBagMruStatusLabel);
            FolderPerfGroupBox.Location = new Point(8, 6);
            FolderPerfGroupBox.Name = "FolderPerfGroupBox";
            FolderPerfGroupBox.Size = new Size(472, 120);
            FolderPerfGroupBox.TabIndex = 0;
            FolderPerfGroupBox.TabStop = false;
            FolderPerfGroupBox.Text = "Folder Enumeration && View Memory (ShellBags)";
            // 
            // DisableFolderDiscoveryCheckBox
            // 
            DisableFolderDiscoveryCheckBox.AutoSize = true;
            DisableFolderDiscoveryCheckBox.Location = new Point(12, 30);
            DisableFolderDiscoveryCheckBox.Name = "DisableFolderDiscoveryCheckBox";
            DisableFolderDiscoveryCheckBox.Size = new Size(320, 19);
            DisableFolderDiscoveryCheckBox.TabIndex = 0;
            DisableFolderDiscoveryCheckBox.Text = "Disable Automatic Folder Type Discovery (Fast)";
            DisableFolderDiscoveryCheckBox.UseVisualStyleBackColor = true;
            DisableFolderDiscoveryCheckBox.CheckedChanged += AnyOptimizationCheckBox_CheckedChanged;
            // 
            // DisableFolderDiscoveryHelpLabel
            // 
            DisableFolderDiscoveryHelpLabel.Location = new Point(340, 30);
            DisableFolderDiscoveryHelpLabel.Name = "DisableFolderDiscoveryHelpLabel";
            DisableFolderDiscoveryHelpLabel.Size = new Size(18, 18);
            DisableFolderDiscoveryHelpLabel.TabIndex = 1;
            DisableFolderDiscoveryHelpLabel.Text = "?";
            // 
            // DisableFolderDiscoveryStatusLabel
            // 
            DisableFolderDiscoveryStatusLabel.AutoSize = true;
            DisableFolderDiscoveryStatusLabel.Location = new Point(366, 32);
            DisableFolderDiscoveryStatusLabel.Name = "DisableFolderDiscoveryStatusLabel";
            DisableFolderDiscoveryStatusLabel.Size = new Size(45, 15);
            DisableFolderDiscoveryStatusLabel.TabIndex = 2;
            DisableFolderDiscoveryStatusLabel.Text = "Default";
            // 
            // ExpandBagMruCheckBox
            // 
            ExpandBagMruCheckBox.AutoSize = true;
            ExpandBagMruCheckBox.Location = new Point(12, 72);
            ExpandBagMruCheckBox.Name = "ExpandBagMruCheckBox";
            ExpandBagMruCheckBox.Size = new Size(315, 19);
            ExpandBagMruCheckBox.TabIndex = 3;
            ExpandBagMruCheckBox.Text = "Expand Folder View Limit (BagMRU = 20,000)";
            ExpandBagMruCheckBox.UseVisualStyleBackColor = true;
            ExpandBagMruCheckBox.CheckedChanged += AnyOptimizationCheckBox_CheckedChanged;
            // 
            // ExpandBagMruHelpLabel
            // 
            ExpandBagMruHelpLabel.Location = new Point(340, 72);
            ExpandBagMruHelpLabel.Name = "ExpandBagMruHelpLabel";
            ExpandBagMruHelpLabel.Size = new Size(18, 18);
            ExpandBagMruHelpLabel.TabIndex = 4;
            ExpandBagMruHelpLabel.Text = "?";
            // 
            // ExpandBagMruStatusLabel
            // 
            ExpandBagMruStatusLabel.AutoSize = true;
            ExpandBagMruStatusLabel.Location = new Point(366, 74);
            ExpandBagMruStatusLabel.Name = "ExpandBagMruStatusLabel";
            ExpandBagMruStatusLabel.Size = new Size(45, 15);
            ExpandBagMruStatusLabel.TabIndex = 5;
            ExpandBagMruStatusLabel.Text = "Default";
            // 
            // IconCacheGroupBox
            // 
            IconCacheGroupBox.Controls.Add(ExpandIconCacheCheckBox);
            IconCacheGroupBox.Controls.Add(ExpandIconCacheHelpLabel);
            IconCacheGroupBox.Controls.Add(ExpandIconCacheStatusLabel);
            IconCacheGroupBox.Controls.Add(IconCacheSizeLabel);
            IconCacheGroupBox.Controls.Add(IconCacheSizeComboBox);
            IconCacheGroupBox.Controls.Add(IconCacheSizeHelpLabel);
            IconCacheGroupBox.Location = new Point(8, 134);
            IconCacheGroupBox.Name = "IconCacheGroupBox";
            IconCacheGroupBox.Size = new Size(472, 126);
            IconCacheGroupBox.TabIndex = 1;
            IconCacheGroupBox.TabStop = false;
            IconCacheGroupBox.Text = "System Icon Cache Capacity";
            // 
            // ExpandIconCacheCheckBox
            // 
            ExpandIconCacheCheckBox.AutoSize = true;
            ExpandIconCacheCheckBox.Location = new Point(12, 30);
            ExpandIconCacheCheckBox.Name = "ExpandIconCacheCheckBox";
            ExpandIconCacheCheckBox.Size = new Size(320, 19);
            ExpandIconCacheCheckBox.TabIndex = 0;
            ExpandIconCacheCheckBox.Text = "Expand Windows Icon Cache (Default: 500 KB)";
            ExpandIconCacheCheckBox.UseVisualStyleBackColor = true;
            ExpandIconCacheCheckBox.CheckedChanged += ExpandIconCacheCheckBox_CheckedChanged;
            // 
            // ExpandIconCacheHelpLabel
            // 
            ExpandIconCacheHelpLabel.Location = new Point(340, 30);
            ExpandIconCacheHelpLabel.Name = "ExpandIconCacheHelpLabel";
            ExpandIconCacheHelpLabel.Size = new Size(18, 18);
            ExpandIconCacheHelpLabel.TabIndex = 1;
            ExpandIconCacheHelpLabel.Text = "?";
            // 
            // ExpandIconCacheStatusLabel
            // 
            ExpandIconCacheStatusLabel.AutoSize = true;
            ExpandIconCacheStatusLabel.Location = new Point(366, 32);
            ExpandIconCacheStatusLabel.Name = "ExpandIconCacheStatusLabel";
            ExpandIconCacheStatusLabel.Size = new Size(45, 15);
            ExpandIconCacheStatusLabel.TabIndex = 2;
            ExpandIconCacheStatusLabel.Text = "Default";
            // 
            // IconCacheSizeLabel
            // 
            IconCacheSizeLabel.AutoSize = true;
            IconCacheSizeLabel.Location = new Point(30, 74);
            IconCacheSizeLabel.Name = "IconCacheSizeLabel";
            IconCacheSizeLabel.Size = new Size(128, 15);
            IconCacheSizeLabel.TabIndex = 3;
            IconCacheSizeLabel.Text = "Target Icon Cache Size:";
            // 
            // IconCacheSizeComboBox
            // 
            IconCacheSizeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            IconCacheSizeComboBox.FormattingEnabled = true;
            IconCacheSizeComboBox.Items.AddRange(new object[]
            {
                "4096 KB (4 MB)",
                "8192 KB (8 MB)",
                "16384 KB (16 MB)"
            });
            IconCacheSizeComboBox.Location = new Point(172, 70);
            IconCacheSizeComboBox.Name = "IconCacheSizeComboBox";
            IconCacheSizeComboBox.Size = new Size(155, 23);
            IconCacheSizeComboBox.TabIndex = 4;
            IconCacheSizeComboBox.SelectedIndexChanged += AnyOptimizationCheckBox_CheckedChanged;
            // 
            // IconCacheSizeHelpLabel
            // 
            IconCacheSizeHelpLabel.Location = new Point(340, 72);
            IconCacheSizeHelpLabel.Name = "IconCacheSizeHelpLabel";
            IconCacheSizeHelpLabel.Size = new Size(18, 18);
            IconCacheSizeHelpLabel.TabIndex = 5;
            IconCacheSizeHelpLabel.Text = "?";
            // 
            // ShellMaintenanceTabPage
            // 
            ShellMaintenanceTabPage.BackColor = SystemColors.ControlLightLight;
            ShellMaintenanceTabPage.Controls.Add(ContextMenuGroupBox);
            ShellMaintenanceTabPage.Controls.Add(QuickActionsGroupBox);
            ShellMaintenanceTabPage.Location = new Point(4, 24);
            ShellMaintenanceTabPage.Name = "ShellMaintenanceTabPage";
            ShellMaintenanceTabPage.Padding = new Padding(3);
            ShellMaintenanceTabPage.Size = new Size(488, 270);
            ShellMaintenanceTabPage.TabIndex = 2;
            ShellMaintenanceTabPage.Text = "Shell && Maintenance";
            // 
            // ContextMenuGroupBox
            // 
            ContextMenuGroupBox.Controls.Add(ContextMenuStatusLabel);
            ContextMenuGroupBox.Controls.Add(RegisterContextMenuButton);
            ContextMenuGroupBox.Controls.Add(UnregisterContextMenuButton);
            ContextMenuGroupBox.Controls.Add(ContextMenuHelpLabel);
            ContextMenuGroupBox.Location = new Point(8, 6);
            ContextMenuGroupBox.Name = "ContextMenuGroupBox";
            ContextMenuGroupBox.Size = new Size(472, 92);
            ContextMenuGroupBox.TabIndex = 0;
            ContextMenuGroupBox.TabStop = false;
            ContextMenuGroupBox.Text = "Explorer Right-Click Context Menu (Portable && Installed)";
            // 
            // ContextMenuStatusLabel
            // 
            ContextMenuStatusLabel.AutoSize = true;
            ContextMenuStatusLabel.Location = new Point(14, 24);
            ContextMenuStatusLabel.Name = "ContextMenuStatusLabel";
            ContextMenuStatusLabel.Size = new Size(130, 15);
            ContextMenuStatusLabel.TabIndex = 0;
            ContextMenuStatusLabel.Text = "Status: Checking...";
            // 
            // RegisterContextMenuButton
            // 
            RegisterContextMenuButton.Location = new Point(14, 50);
            RegisterContextMenuButton.Name = "RegisterContextMenuButton";
            RegisterContextMenuButton.Size = new Size(200, 30);
            RegisterContextMenuButton.TabIndex = 1;
            RegisterContextMenuButton.Text = "Register Context Menu";
            RegisterContextMenuButton.UseVisualStyleBackColor = true;
            RegisterContextMenuButton.Click += RegisterContextMenuButton_Click;
            // 
            // UnregisterContextMenuButton
            // 
            UnregisterContextMenuButton.Location = new Point(226, 50);
            UnregisterContextMenuButton.Name = "UnregisterContextMenuButton";
            UnregisterContextMenuButton.Size = new Size(200, 30);
            UnregisterContextMenuButton.TabIndex = 2;
            UnregisterContextMenuButton.Text = "Remove Context Menu";
            UnregisterContextMenuButton.UseVisualStyleBackColor = true;
            UnregisterContextMenuButton.Click += UnregisterContextMenuButton_Click;
            // 
            // ContextMenuHelpLabel
            // 
            ContextMenuHelpLabel.Location = new Point(438, 56);
            ContextMenuHelpLabel.Name = "ContextMenuHelpLabel";
            ContextMenuHelpLabel.Size = new Size(18, 18);
            ContextMenuHelpLabel.TabIndex = 3;
            ContextMenuHelpLabel.Text = "?";
            // 
            // QuickActionsGroupBox
            // 
            QuickActionsGroupBox.Controls.Add(RebuildIconCacheButton);
            QuickActionsGroupBox.Controls.Add(RebuildIconCacheInfoLabel);
            QuickActionsGroupBox.Controls.Add(RebuildIconCacheHelpLabel);
            QuickActionsGroupBox.Controls.Add(RestartExplorerButton);
            QuickActionsGroupBox.Controls.Add(RestartExplorerInfoLabel);
            QuickActionsGroupBox.Controls.Add(RestartExplorerHelpLabel);
            QuickActionsGroupBox.Controls.Add(RestoreDefaultsButton);
            QuickActionsGroupBox.Controls.Add(RestoreDefaultsInfoLabel);
            QuickActionsGroupBox.Controls.Add(RestoreDefaultsHelpLabel);
            QuickActionsGroupBox.Location = new Point(8, 104);
            QuickActionsGroupBox.Name = "QuickActionsGroupBox";
            QuickActionsGroupBox.Size = new Size(472, 152);
            QuickActionsGroupBox.TabIndex = 1;
            QuickActionsGroupBox.TabStop = false;
            QuickActionsGroupBox.Text = "Quick Cache && Shell Maintenance Actions";
            // 
            // RebuildIconCacheButton
            // 
            RebuildIconCacheButton.Location = new Point(14, 26);
            RebuildIconCacheButton.Name = "RebuildIconCacheButton";
            RebuildIconCacheButton.Size = new Size(200, 30);
            RebuildIconCacheButton.TabIndex = 0;
            RebuildIconCacheButton.Text = "Rebuild Icon Cache Only";
            RebuildIconCacheButton.UseVisualStyleBackColor = true;
            RebuildIconCacheButton.Click += RebuildIconCacheButton_Click;
            // 
            // RebuildIconCacheInfoLabel
            // 
            RebuildIconCacheInfoLabel.AutoSize = true;
            RebuildIconCacheInfoLabel.ForeColor = SystemColors.GrayText;
            RebuildIconCacheInfoLabel.Location = new Point(224, 34);
            RebuildIconCacheInfoLabel.Name = "RebuildIconCacheInfoLabel";
            RebuildIconCacheInfoLabel.Size = new Size(195, 15);
            RebuildIconCacheInfoLabel.TabIndex = 1;
            RebuildIconCacheInfoLabel.Text = "Keeps thumbcache_*.db untouched";
            // 
            // RebuildIconCacheHelpLabel
            // 
            RebuildIconCacheHelpLabel.Location = new Point(438, 32);
            RebuildIconCacheHelpLabel.Name = "RebuildIconCacheHelpLabel";
            RebuildIconCacheHelpLabel.Size = new Size(18, 18);
            RebuildIconCacheHelpLabel.TabIndex = 2;
            RebuildIconCacheHelpLabel.Text = "?";
            // 
            // RestartExplorerButton
            // 
            RestartExplorerButton.Location = new Point(14, 66);
            RestartExplorerButton.Name = "RestartExplorerButton";
            RestartExplorerButton.Size = new Size(200, 30);
            RestartExplorerButton.TabIndex = 3;
            RestartExplorerButton.Text = "Restart Windows Explorer";
            RestartExplorerButton.UseVisualStyleBackColor = true;
            RestartExplorerButton.Click += RestartExplorerButton_Click;
            // 
            // RestartExplorerInfoLabel
            // 
            RestartExplorerInfoLabel.AutoSize = true;
            RestartExplorerInfoLabel.ForeColor = SystemColors.GrayText;
            RestartExplorerInfoLabel.Location = new Point(224, 74);
            RestartExplorerInfoLabel.Name = "RestartExplorerInfoLabel";
            RestartExplorerInfoLabel.Size = new Size(198, 15);
            RestartExplorerInfoLabel.TabIndex = 4;
            RestartExplorerInfoLabel.Text = "Applies ShellBag && Icon tweaks live";
            // 
            // RestartExplorerHelpLabel
            // 
            RestartExplorerHelpLabel.Location = new Point(438, 72);
            RestartExplorerHelpLabel.Name = "RestartExplorerHelpLabel";
            RestartExplorerHelpLabel.Size = new Size(18, 18);
            RestartExplorerHelpLabel.TabIndex = 5;
            RestartExplorerHelpLabel.Text = "?";
            // 
            // RestoreDefaultsButton
            // 
            RestoreDefaultsButton.Location = new Point(14, 106);
            RestoreDefaultsButton.Name = "RestoreDefaultsButton";
            RestoreDefaultsButton.Size = new Size(200, 30);
            RestoreDefaultsButton.TabIndex = 6;
            RestoreDefaultsButton.Text = "Restore Windows Defaults";
            RestoreDefaultsButton.UseVisualStyleBackColor = true;
            RestoreDefaultsButton.Click += RestoreDefaultsButton_Click;
            // 
            // RestoreDefaultsInfoLabel
            // 
            RestoreDefaultsInfoLabel.AutoSize = true;
            RestoreDefaultsInfoLabel.ForeColor = SystemColors.GrayText;
            RestoreDefaultsInfoLabel.Location = new Point(224, 114);
            RestoreDefaultsInfoLabel.Name = "RestoreDefaultsInfoLabel";
            RestoreDefaultsInfoLabel.Size = new Size(190, 15);
            RestoreDefaultsInfoLabel.TabIndex = 7;
            RestoreDefaultsInfoLabel.Text = "Reverts all OS tweaks to defaults";
            // 
            // RestoreDefaultsHelpLabel
            // 
            RestoreDefaultsHelpLabel.Location = new Point(438, 112);
            RestoreDefaultsHelpLabel.Name = "RestoreDefaultsHelpLabel";
            RestoreDefaultsHelpLabel.Size = new Size(18, 18);
            RestoreDefaultsHelpLabel.TabIndex = 8;
            RestoreDefaultsHelpLabel.Text = "?";
            // 
            // RecommendedButton
            // 
            RecommendedButton.Location = new Point(12, 372);
            RecommendedButton.Name = "RecommendedButton";
            RecommendedButton.Size = new Size(118, 28);
            RecommendedButton.TabIndex = 1;
            RecommendedButton.Text = "Recommended";
            RecommendedButton.UseVisualStyleBackColor = true;
            RecommendedButton.Click += RecommendedButton_Click;
            // 
            // ElevateApplyButton
            // 
            ElevateApplyButton.Location = new Point(138, 372);
            ElevateApplyButton.Name = "ElevateApplyButton";
            ElevateApplyButton.Size = new Size(174, 28);
            ElevateApplyButton.TabIndex = 2;
            ElevateApplyButton.Text = "Elevate && Apply (UAC)";
            ElevateApplyButton.UseVisualStyleBackColor = true;
            ElevateApplyButton.Click += ElevateApplyButton_Click;
            // 
            // ApplyButton
            // 
            ApplyButton.Location = new Point(320, 372);
            ApplyButton.Name = "ApplyButton";
            ApplyButton.Size = new Size(100, 28);
            ApplyButton.TabIndex = 3;
            ApplyButton.Text = "Apply";
            ApplyButton.UseVisualStyleBackColor = true;
            ApplyButton.Click += ApplyButton_Click;
            // 
            // CloseButton
            // 
            CloseButton.Location = new Point(428, 372);
            CloseButton.Name = "CloseButton";
            CloseButton.Size = new Size(100, 28);
            CloseButton.TabIndex = 4;
            CloseButton.Text = "Close";
            CloseButton.UseVisualStyleBackColor = true;
            CloseButton.Click += CloseButton_Click;
            // 
            // WindowsOptimizerForm
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(540, 412);
            Controls.Add(OptimizerGroupBox);
            Controls.Add(RecommendedButton);
            Controls.Add(ElevateApplyButton);
            Controls.Add(ApplyButton);
            Controls.Add(CloseButton);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            KeyPreview = true;
            MaximizeBox = false;
            Name = "WindowsOptimizerForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "WinThumbsPreloader - Windows Optimizer";
            Activated += WindowsOptimizerForm_Activated;
            Load += WindowsOptimizerForm_Load;
            KeyDown += WindowsOptimizerForm_KeyDown;
            KeyUp += WindowsOptimizerForm_KeyUp;

            OptimizerGroupBox.ResumeLayout(false);
            OptimizerGroupBox.PerformLayout();
            OptimizerTabControl.ResumeLayout(false);
            CacheNetworkTabPage.ResumeLayout(false);
            CacheProtectionGroupBox.ResumeLayout(false);
            CacheProtectionGroupBox.PerformLayout();
            NetworkPolicyGroupBox.ResumeLayout(false);
            NetworkPolicyGroupBox.PerformLayout();
            ExplorerPerfTabPage.ResumeLayout(false);
            FolderPerfGroupBox.ResumeLayout(false);
            FolderPerfGroupBox.PerformLayout();
            IconCacheGroupBox.ResumeLayout(false);
            IconCacheGroupBox.PerformLayout();
            ShellMaintenanceTabPage.ResumeLayout(false);
            ContextMenuGroupBox.ResumeLayout(false);
            ContextMenuGroupBox.PerformLayout();
            QuickActionsGroupBox.ResumeLayout(false);
            QuickActionsGroupBox.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private ToolTip MainToolTip;
        private GroupBox OptimizerGroupBox;
        private Label PrivilegeStatusLabel;
        private CheckBox EnforceStartupCheckBox;
        private Label EnforceStartupHelpLabel;
        private TabControl OptimizerTabControl;
        private TabPage CacheNetworkTabPage;
        private GroupBox CacheProtectionGroupBox;
        private CheckBox PreventCacheCleanupCheckBox;
        private Label PreventCacheCleanupHelpLabel;
        private Label PreventCacheCleanupStatusLabel;
        private CheckBox ClearDiskCleanupFlagsCheckBox;
        private Label ClearDiskCleanupFlagsHelpLabel;
        private Label ClearDiskCleanupFlagsStatusLabel;
        private CheckBox DisableSilentCleanupTaskCheckBox;
        private Label DisableSilentCleanupHelpLabel;
        private Label DisableSilentCleanupStatusLabel;
        private GroupBox NetworkPolicyGroupBox;
        private CheckBox DisableNetworkThumbsDbCheckBox;
        private Label DisableNetworkThumbsDbHelpLabel;
        private Label DisableNetworkThumbsDbStatusLabel;
        private CheckBox ForceThumbnailPreviewsCheckBox;
        private Label ForceThumbnailPreviewsHelpLabel;
        private Label ForceThumbnailPreviewsStatusLabel;
        private TabPage ExplorerPerfTabPage;
        private GroupBox FolderPerfGroupBox;
        private CheckBox DisableFolderDiscoveryCheckBox;
        private Label DisableFolderDiscoveryHelpLabel;
        private Label DisableFolderDiscoveryStatusLabel;
        private CheckBox ExpandBagMruCheckBox;
        private Label ExpandBagMruHelpLabel;
        private Label ExpandBagMruStatusLabel;
        private GroupBox IconCacheGroupBox;
        private CheckBox ExpandIconCacheCheckBox;
        private Label ExpandIconCacheHelpLabel;
        private Label ExpandIconCacheStatusLabel;
        private Label IconCacheSizeLabel;
        private ComboBox IconCacheSizeComboBox;
        private Label IconCacheSizeHelpLabel;
        private TabPage ShellMaintenanceTabPage;
        private GroupBox ContextMenuGroupBox;
        private Label ContextMenuStatusLabel;
        private Button RegisterContextMenuButton;
        private Button UnregisterContextMenuButton;
        private Label ContextMenuHelpLabel;
        private GroupBox QuickActionsGroupBox;
        private Button RebuildIconCacheButton;
        private Label RebuildIconCacheInfoLabel;
        private Label RebuildIconCacheHelpLabel;
        private Button RestartExplorerButton;
        private Label RestartExplorerInfoLabel;
        private Label RestartExplorerHelpLabel;
        private Button RestoreDefaultsButton;
        private Label RestoreDefaultsInfoLabel;
        private Label RestoreDefaultsHelpLabel;
        private Button RecommendedButton;
        private Button ElevateApplyButton;
        private Button ApplyButton;
        private Button CloseButton;
    }
}
