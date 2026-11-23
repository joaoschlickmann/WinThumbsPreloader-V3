using System.Drawing;
using System.Windows.Forms;

namespace WinThumbsPreloader
{
    partial class CodecsForm
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
            CodecsToolTip = new ToolTip(components);
            CodecsGroupBox = new GroupBox();
            SubtitleLabel = new Label();
            PrivilegeAndBundleLabel = new Label();
            CodecsListView = new ListView();
            FormatColumnHeader = new ColumnHeader();
            ExtensionsColumnHeader = new ColumnHeader();
            HandlerColumnHeader = new ColumnHeader();
            StatusColumnHeader = new ColumnHeader();
            SelectAllButton = new Button();
            SelectMissingButton = new Button();
            SelectNoneButton = new Button();
            SyncExtensionsCheckBox = new CheckBox();
            UninstallAllButton = new Button();
            ClearCacheButton = new Button();
            ElevateApplyButton = new Button();
            ApplyButton = new Button();
            CloseButton = new Button();

            CodecsGroupBox.SuspendLayout();
            SuspendLayout();

            // 
            // CodecsToolTip
            // 
            CodecsToolTip.AutoPopDelay = 20000;
            CodecsToolTip.InitialDelay = 200;
            CodecsToolTip.ReshowDelay = 50;
            CodecsToolTip.ShowAlways = true;

            // 
            // CodecsGroupBox
            // 
            CodecsGroupBox.BackColor = SystemColors.ControlLightLight;
            CodecsGroupBox.Controls.Add(SubtitleLabel);
            CodecsGroupBox.Controls.Add(PrivilegeAndBundleLabel);
            CodecsGroupBox.Controls.Add(CodecsListView);
            CodecsGroupBox.Controls.Add(SelectAllButton);
            CodecsGroupBox.Controls.Add(SelectMissingButton);
            CodecsGroupBox.Controls.Add(SelectNoneButton);
            CodecsGroupBox.Controls.Add(SyncExtensionsCheckBox);
            CodecsGroupBox.Location = new Point(12, 12);
            CodecsGroupBox.Name = "CodecsGroupBox";
            CodecsGroupBox.Size = new Size(688, 444);
            CodecsGroupBox.TabIndex = 0;
            CodecsGroupBox.TabStop = false;
            CodecsGroupBox.Text = "Built-in Open-Source Thumbnail Codecs (WinThumbsCodecs + PDFium)";

            // 
            // SubtitleLabel
            // 
            SubtitleLabel.Location = new Point(12, 20);
            SubtitleLabel.Name = "SubtitleLabel";
            SubtitleLabel.Size = new Size(664, 32);
            SubtitleLabel.TabIndex = 0;
            SubtitleLabel.Text = "Registers native Windows 10/11 Explorer & Preloader thumbnail providers (isolated in dllhost.exe). Existing handlers (Ardfry, Acrobat, PowerToys) are backed up and restored automatically on uninstall.";
            SubtitleLabel.UseMnemonic = false;

            // 
            // PrivilegeAndBundleLabel
            // 
            PrivilegeAndBundleLabel.AutoEllipsis = true;
            PrivilegeAndBundleLabel.Font = new Font(Font, FontStyle.Bold);
            PrivilegeAndBundleLabel.Location = new Point(12, 54);
            PrivilegeAndBundleLabel.Name = "PrivilegeAndBundleLabel";
            PrivilegeAndBundleLabel.Size = new Size(664, 18);
            PrivilegeAndBundleLabel.TabIndex = 1;
            PrivilegeAndBundleLabel.Text = "Checking codecs status...";

            // 
            // CodecsListView
            // 
            CodecsListView.CheckBoxes = true;
            CodecsListView.Columns.AddRange(
            [
                FormatColumnHeader,
                ExtensionsColumnHeader,
                HandlerColumnHeader,
                StatusColumnHeader
            ]);
            CodecsListView.FullRowSelect = true;
            CodecsListView.GridLines = true;
            CodecsListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            CodecsListView.HideSelection = false;
            CodecsListView.Location = new Point(12, 76);
            CodecsListView.Name = "CodecsListView";
            CodecsListView.ShowItemToolTips = true;
            CodecsListView.Size = new Size(664, 322);
            CodecsListView.TabIndex = 2;
            CodecsListView.UseCompatibleStateImageBehavior = false;
            CodecsListView.View = View.Details;
            CodecsListView.ItemCheck += CodecsListView_ItemCheck;

            // 
            // ColumnHeaders
            // 
            FormatColumnHeader.Text = "Format Family";
            FormatColumnHeader.Width = 262;
            ExtensionsColumnHeader.Text = "Extensions";
            ExtensionsColumnHeader.Width = 152;
            HandlerColumnHeader.Text = "Current Handler";
            HandlerColumnHeader.Width = 148;
            StatusColumnHeader.Text = "Status";
            StatusColumnHeader.Width = 96;

            // 
            // SelectAllButton
            // 
            SelectAllButton.Location = new Point(12, 406);
            SelectAllButton.Name = "SelectAllButton";
            SelectAllButton.Size = new Size(88, 27);
            SelectAllButton.TabIndex = 3;
            SelectAllButton.Text = "Select All";
            SelectAllButton.UseVisualStyleBackColor = true;
            SelectAllButton.Click += SelectAllButton_Click;

            // 
            // SelectMissingButton
            // 
            SelectMissingButton.Location = new Point(106, 406);
            SelectMissingButton.Name = "SelectMissingButton";
            SelectMissingButton.Size = new Size(136, 27);
            SelectMissingButton.TabIndex = 4;
            SelectMissingButton.Text = "Select Missing Only";
            CodecsToolTip.SetToolTip(SelectMissingButton, "Checks already-active WinThumbsPreloader codecs and formats that currently have no handler ('None').");
            SelectMissingButton.UseVisualStyleBackColor = true;
            SelectMissingButton.Click += SelectMissingButton_Click;

            // 
            // SelectNoneButton
            // 
            SelectNoneButton.Location = new Point(248, 406);
            SelectNoneButton.Name = "SelectNoneButton";
            SelectNoneButton.Size = new Size(94, 27);
            SelectNoneButton.TabIndex = 5;
            SelectNoneButton.Text = "Select None";
            SelectNoneButton.UseVisualStyleBackColor = true;
            SelectNoneButton.Click += SelectNoneButton_Click;

            // 
            // SyncExtensionsCheckBox
            // 
            SyncExtensionsCheckBox.AutoSize = true;
            SyncExtensionsCheckBox.Checked = true;
            SyncExtensionsCheckBox.CheckState = CheckState.Checked;
            SyncExtensionsCheckBox.Location = new Point(354, 410);
            SyncExtensionsCheckBox.Name = "SyncExtensionsCheckBox";
            SyncExtensionsCheckBox.Size = new Size(254, 19);
            SyncExtensionsCheckBox.TabIndex = 6;
            SyncExtensionsCheckBox.Text = "Add enabled formats to Preload Extensions";
            CodecsToolTip.SetToolTip(SyncExtensionsCheckBox, "Automatically adds checked codec extensions (e.g. pdf, eps, indd, xd, cdr, raw) to Settings -> Extensions.");
            SyncExtensionsCheckBox.UseVisualStyleBackColor = true;

            // 
            // UninstallAllButton
            // 
            UninstallAllButton.Location = new Point(12, 468);
            UninstallAllButton.Name = "UninstallAllButton";
            UninstallAllButton.Size = new Size(110, 30);
            UninstallAllButton.TabIndex = 1;
            UninstallAllButton.Text = "Uninstall All";
            CodecsToolTip.SetToolTip(UninstallAllButton, "Removes WinThumbsPreloader codec registrations and restores backed-up external handlers.");
            UninstallAllButton.UseVisualStyleBackColor = true;
            UninstallAllButton.Click += UninstallAllButton_Click;

            // 
            // ClearCacheButton
            // 
            ClearCacheButton.Location = new Point(128, 468);
            ClearCacheButton.Name = "ClearCacheButton";
            ClearCacheButton.Size = new Size(110, 30);
            ClearCacheButton.TabIndex = 2;
            ClearCacheButton.Text = "Clear Cache...";
            CodecsToolTip.SetToolTip(ClearCacheButton, "Open Cache Manager to clear stale/blank Explorer thumbnail cache entries after installing codecs.");
            ClearCacheButton.UseVisualStyleBackColor = true;
            ClearCacheButton.Click += ClearCacheButton_Click;

            // 
            // ElevateApplyButton
            // 
            ElevateApplyButton.Location = new Point(330, 468);
            ElevateApplyButton.Name = "ElevateApplyButton";
            ElevateApplyButton.Size = new Size(156, 30);
            ElevateApplyButton.TabIndex = 3;
            ElevateApplyButton.Text = "Elevate && Apply (UAC)";
            CodecsToolTip.SetToolTip(ElevateApplyButton, "Registers checked codecs system-wide (HKLM + HKCU) using a UAC-elevated helper so both standard and Admin preloaders work.");
            ElevateApplyButton.UseVisualStyleBackColor = true;
            ElevateApplyButton.Click += ElevateApplyButton_Click;

            // 
            // ApplyButton
            // 
            ApplyButton.Location = new Point(492, 468);
            ApplyButton.Name = "ApplyButton";
            ApplyButton.Size = new Size(100, 30);
            ApplyButton.TabIndex = 4;
            ApplyButton.Text = "Apply";
            ApplyButton.UseVisualStyleBackColor = true;
            ApplyButton.Click += ApplyButton_Click;

            // 
            // CloseButton
            // 
            CloseButton.Location = new Point(598, 468);
            CloseButton.Name = "CloseButton";
            CloseButton.Size = new Size(102, 30);
            CloseButton.TabIndex = 5;
            CloseButton.Text = "Close";
            CloseButton.UseVisualStyleBackColor = true;
            CloseButton.Click += CloseButton_Click;

            // 
            // CodecsForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(712, 510);
            Controls.Add(CodecsGroupBox);
            Controls.Add(UninstallAllButton);
            Controls.Add(ClearCacheButton);
            Controls.Add(ElevateApplyButton);
            Controls.Add(ApplyButton);
            Controls.Add(CloseButton);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "CodecsForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "WinThumbsPreloader - Built-in Codecs";
            Load += CodecsForm_Load;

            CodecsGroupBox.ResumeLayout(false);
            CodecsGroupBox.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private ToolTip CodecsToolTip;
        private GroupBox CodecsGroupBox;
        private Label SubtitleLabel;
        private Label PrivilegeAndBundleLabel;
        private ListView CodecsListView;
        private ColumnHeader FormatColumnHeader;
        private ColumnHeader ExtensionsColumnHeader;
        private ColumnHeader HandlerColumnHeader;
        private ColumnHeader StatusColumnHeader;
        private Button SelectAllButton;
        private Button SelectMissingButton;
        private Button SelectNoneButton;
        private CheckBox SyncExtensionsCheckBox;
        private Button UninstallAllButton;
        private Button ClearCacheButton;
        private Button ElevateApplyButton;
        private Button ApplyButton;
        private Button CloseButton;
    }
}
