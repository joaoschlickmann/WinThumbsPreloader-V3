using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

#nullable enable

namespace WinThumbsPreloader.Forms
{
    [SupportedOSPlatform("windows")]
    public partial class BulkPreloaderForm : Form
    {
        public sealed class BulkPreset
        {
            public string Name { get; set; } = string.Empty;
            public List<string> Paths { get; set; } = new List<string>();
            public bool Recursive { get; set; } = true;
            public bool MultiThreaded { get; set; } = true;
            public int Threads { get; set; } = 0;
            public int MaximumInstances { get; set; } = 0;
            public string Method { get; set; } = "Sequentially";
        }

        private bool presetsVisible = false;
        private List<BulkPreset> _presets = new List<BulkPreset>();

        private static string PresetsFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinThumbsPreloader",
                "BulkPreloaderPresets.json");

        public BulkPreloaderForm()
        {
            InitializeComponent();
            this.KeyDown += BulkPreloaderForm_KeyDown;
            this.KeyUp += BulkPreloaderForm_KeyUp;
            this.Activated += BulkPreloaderForm_Activated;
            this.KeyPreview = true;

            PathsTextBox.DragEnter += PathsTextBox_DragEnter;
            PathsTextBox.DragDrop += PathsTextBox_DragDrop;

            TogglePresetsVisibility();
        }

        private void BulkPreloaderForm_Load(object? sender, EventArgs e)
        {
            this.Icon = Resources.MainIcon;

            MultithreadedCheckBox.Checked = Settings.Default.Multithreaded;
            ThreadsNumericUpDown.Value = Math.Clamp(Settings.Default.ThreadCount, (int)ThreadsNumericUpDown.Minimum, (int)ThreadsNumericUpDown.Maximum);
            ThreadsNumericUpDown.Enabled = MultithreadedCheckBox.Checked;
            MaximumInstancesNumericUpDown.Value = 0;
            BulkPreloaderMethodComboBox.SelectedItem = "Sequentially";
            UpdateMaxInstancesControlState();

            LoadPresetsFromDisk();
            RefreshPresetsListBox();
        }

        private void BulkPreloaderForm_Activated(object? sender, EventArgs e)
        {
            CloseButton.Text = Control.ModifierKeys == Keys.Shift ? "Exit" : "Close";
        }

        private void BulkPreloaderForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey)
            {
                CloseButton.Text = "Exit";
            }
        }

        private void BulkPreloaderForm_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey)
            {
                CloseButton.Text = "Close";
            }
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            if (CloseButton.Text == "Close")
            {
                Close();
            }
            else if (CloseButton.Text == "Exit")
            {
                Environment.Exit(0);
            }
        }

        private void PresetsListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (PresetsListBox.SelectedItem is not string selectedName)
                return;

            var preset = _presets.FirstOrDefault(p => string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase));
            if (preset == null)
                return;

            PresetsTextBox.Text = preset.Name;
            PathsTextBox.Text = string.Join(Environment.NewLine, preset.Paths ?? []);
            RecursiveCheckBox.Checked = preset.Recursive;
            MultithreadedCheckBox.Checked = preset.MultiThreaded;
            ThreadsNumericUpDown.Value = Math.Clamp(preset.Threads, (int)ThreadsNumericUpDown.Minimum, (int)ThreadsNumericUpDown.Maximum);
            MaximumInstancesNumericUpDown.Value = Math.Clamp(preset.MaximumInstances, (int)MaximumInstancesNumericUpDown.Minimum, (int)MaximumInstancesNumericUpDown.Maximum);

            if (BulkPreloaderMethodComboBox.Items.Contains(preset.Method))
                BulkPreloaderMethodComboBox.SelectedItem = preset.Method;
            else
                BulkPreloaderMethodComboBox.SelectedItem = "Sequentially";
        }

        private void PathsTextBox_TextChanged(object? sender, EventArgs e)
        {
        }

        private void PathsTextBox_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text)))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void PathsTextBox_DragDrop(object? sender, DragEventArgs e)
        {
            try
            {
                var currentPaths = GetRawPathsFromTextBox();

                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    if (e.Data.GetData(DataFormats.FileDrop) is string[] droppedFiles)
                    {
                        foreach (string item in droppedFiles)
                        {
                            if (!string.IsNullOrWhiteSpace(item) && !currentPaths.Contains(item, StringComparer.OrdinalIgnoreCase))
                            {
                                currentPaths.Add(item);
                            }
                        }
                    }
                }
                else if (e.Data != null && e.Data.GetDataPresent(DataFormats.Text))
                {
                    string? droppedText = e.Data.GetData(DataFormats.Text) as string;
                    if (!string.IsNullOrWhiteSpace(droppedText))
                    {
                        foreach (string line in droppedText.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries))
                        {
                            string cleaned = line.Trim().Trim('"');
                            if (!string.IsNullOrWhiteSpace(cleaned) && !currentPaths.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                            {
                                currentPaths.Add(cleaned);
                            }
                        }
                    }
                }

                PathsTextBox.Text = string.Join(Environment.NewLine, currentPaths);
            }
            catch (Exception ex)
            {
                WriteLine("Error dropping paths into BulkPreloaderForm: " + ex.Message, LoggingFrequency.GUILogging);
            }
        }

        private void MultithreadedCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            ThreadsNumericUpDown.Enabled = MultithreadedCheckBox.Checked;
        }

        private void ThreadsNumericUpDown_ValueChanged(object? sender, EventArgs e)
        {
        }

        private void DefaultThreadsButton_Click(object? sender, EventArgs e)
        {
            MultithreadedCheckBox.Checked = true;
            ThreadsNumericUpDown.Value = 0;
        }

        private void MaximumInstancesNumericUpDown_ValueChanged(object? sender, EventArgs e)
        {
        }

        private void DefaultMaximumInstancesButton_Click(object? sender, EventArgs e)
        {
            MaximumInstancesNumericUpDown.Value = 0;
        }

        private void BulkPreloaderMethodComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateMaxInstancesControlState();
        }

        private void UpdateMaxInstancesControlState()
        {
            bool isSimultaneous = string.Equals(BulkPreloaderMethodComboBox.SelectedItem?.ToString(), "Simultaneously", StringComparison.OrdinalIgnoreCase);
            MaximumInstancesNumericUpDown.Enabled = isSimultaneous;
            DefaultMaximumInstancesButton.Enabled = isSimultaneous;
        }

        private void ClearButton_Click(object? sender, EventArgs e)
        {
            PathsTextBox.Clear();
        }

        private void ExportPathsButton_Click(object? sender, EventArgs e)
        {
            using var saveDialog = new SaveFileDialog
            {
                FileName = "BulkPreloaderPaths.txt",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = "txt",
                Title = "Export Paths"
            };

            if (saveDialog.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    File.WriteAllText(saveDialog.FileName, PathsTextBox.Text);
                }
                catch (Exception ex)
                {
                    WriteLine("Failed to export paths: " + ex.Message, LoggingFrequency.GUILogging);
                    MessageBox.Show(this, $"Failed to export paths:\n{ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportPresetsButton_Click(object? sender, EventArgs e)
        {
            using var saveDialog = new SaveFileDialog
            {
                FileName = "BulkPreloaderPresets.json",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json",
                Title = "Export Presets"
            };

            if (saveDialog.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    string json = JsonSerializer.Serialize(_presets, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(saveDialog.FileName, json);
                }
                catch (Exception ex)
                {
                    WriteLine("Failed to export presets: " + ex.Message, LoggingFrequency.GUILogging);
                    MessageBox.Show(this, $"Failed to export presets:\n{ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void PresetsTextBox_TextChanged(object? sender, EventArgs e)
        {
        }

        private void SavePresetsButton_Click(object? sender, EventArgs e)
        {
            string presetName = PresetsTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(presetName))
            {
                MessageBox.Show(this, "Please enter a name for the preset.", "Preset Name Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                PresetsTextBox.Focus();
                return;
            }

            List<string> rawPaths = GetRawPathsFromTextBox();
            if (rawPaths.Count == 0)
            {
                MessageBox.Show(this, "Please add at least one path before saving a preset.", "No Paths", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                PathsTextBox.Focus();
                return;
            }

            var existing = _presets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Name = presetName;
                existing.Paths = rawPaths;
                existing.Recursive = RecursiveCheckBox.Checked;
                existing.MultiThreaded = MultithreadedCheckBox.Checked;
                existing.Threads = (int)ThreadsNumericUpDown.Value;
                existing.MaximumInstances = (int)MaximumInstancesNumericUpDown.Value;
                existing.Method = BulkPreloaderMethodComboBox.SelectedItem?.ToString() ?? "Sequentially";
            }
            else
            {
                _presets.Add(new BulkPreset
                {
                    Name = presetName,
                    Paths = rawPaths,
                    Recursive = RecursiveCheckBox.Checked,
                    MultiThreaded = MultithreadedCheckBox.Checked,
                    Threads = (int)ThreadsNumericUpDown.Value,
                    MaximumInstances = (int)MaximumInstancesNumericUpDown.Value,
                    Method = BulkPreloaderMethodComboBox.SelectedItem?.ToString() ?? "Sequentially"
                });
            }

            SavePresetsToDisk();
            RefreshPresetsListBox(presetName);
        }

        private void DeleteButton_Click(object? sender, EventArgs e)
        {
            string? targetName = PresetsListBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(targetName))
                targetName = PresetsTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(targetName))
                return;

            int removed = _presets.RemoveAll(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                SavePresetsToDisk();
                RefreshPresetsListBox();
                PresetsTextBox.Clear();
            }
        }

        private void PresetsGroupBox_Enter(object? sender, EventArgs e)
        {
        }

        private void TogglePresetsVisibility()
        {
            if (!presetsVisible)
            {
                PresetsGroupBox.Visible = false;
                PresetsGroupBox.Enabled = false;
                Size = new Size(450, 450);
                CloseButton.Location = new Point(334, 371);
                StartButton.Location = new Point(12, 371);
            }
            else
            {
                PresetsGroupBox.Visible = true;
                PresetsGroupBox.Enabled = true;
                Size = new Size(750, 450);
                CloseButton.Location = new Point(634, 371);
                StartButton.Location = new Point(12, 371);
            }
        }

        private void PresetsButton_Click(object? sender, EventArgs e)
        {
            presetsVisible = !presetsVisible;
            TogglePresetsVisibility();
        }

        private void StartButton_Click(object? sender, EventArgs e)
        {
            List<string> rawPaths = GetRawPathsFromTextBox();
            if (rawPaths.Count == 0)
            {
                MessageBox.Show(this, "Please add at least one folder or file path to preload.", "No Paths Specified", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
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
                    MessageBox.Show(
                        this,
                        "None of the provided paths exist or could be accessed:\n\n" + string.Join(Environment.NewLine, invalidPaths),
                        "Invalid Paths",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                DialogResult proceed = MessageBox.Show(
                    this,
                    "The following paths could not be found or accessed:\n\n" +
                    string.Join(Environment.NewLine, invalidPaths) +
                    "\n\nDo you want to continue with the remaining " + validPaths.Count + " valid path(s)?",
                    "Some Paths Invalid",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (proceed != DialogResult.Yes)
                    return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                if (RecursiveCheckBox.Checked)
                    psi.ArgumentList.Add("-r");

                if (MultithreadedCheckBox.Checked)
                {
                    int threads = (int)ThreadsNumericUpDown.Value;
                    psi.ArgumentList.Add(threads > 0 ? $"-m:{threads}" : "-m");
                }

                bool isSequential = string.Equals(BulkPreloaderMethodComboBox.SelectedItem?.ToString(), "Sequentially", StringComparison.OrdinalIgnoreCase);
                if (isSequential)
                {
                    psi.ArgumentList.Add("-maxinstances:1");
                }
                else
                {
                    int maxInst = (int)MaximumInstancesNumericUpDown.Value;
                    if (maxInst > 0)
                        psi.ArgumentList.Add($"-maxinstances:{maxInst}");
                }

                foreach (string validPath in validPaths)
                {
                    psi.ArgumentList.Add(validPath);
                }

                WriteLine($"Launching Bulk Preloader for {validPaths.Count} path(s)", LoggingFrequency.GUILogging);
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                WriteLine("Failed to start Bulk Preloader: " + ex.Message, LoggingFrequency.GUILogging);
                MessageBox.Show(this, $"Failed to start Bulk Preloader: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BrowseButton_Click(object? sender, EventArgs e)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Select Folder to Add (Local, Mapped Drive, or Network Share)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
            {
                var currentPaths = GetRawPathsFromTextBox();
                if (!currentPaths.Contains(folderDialog.SelectedPath, StringComparer.OrdinalIgnoreCase))
                {
                    currentPaths.Add(folderDialog.SelectedPath);
                    PathsTextBox.Text = string.Join(Environment.NewLine, currentPaths);
                }
            }
        }

        private List<string> GetRawPathsFromTextBox()
        {
            if (string.IsNullOrWhiteSpace(PathsTextBox.Text))
                return new List<string>();

            return PathsTextBox.Text
                .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim().Trim('"'))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void LoadPresetsFromDisk()
        {
            try
            {
                string filePath = PresetsFilePath;
                if (!File.Exists(filePath))
                    return;

                string json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<List<BulkPreset>>(json);
                if (loaded != null)
                {
                    _presets = loaded;
                }
            }
            catch (Exception ex)
            {
                WriteLine("Failed to load Bulk Preloader presets: " + ex.Message, LoggingFrequency.GUILogging);
            }
        }

        private void SavePresetsToDisk()
        {
            try
            {
                string filePath = PresetsFilePath;
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(_presets, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                WriteLine("Failed to save Bulk Preloader presets: " + ex.Message, LoggingFrequency.GUILogging);
            }
        }

        private void RefreshPresetsListBox(string? selectPresetName = null)
        {
            PresetsListBox.Items.Clear();
            foreach (var preset in _presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                PresetsListBox.Items.Add(preset.Name);
            }

            if (!string.IsNullOrWhiteSpace(selectPresetName))
            {
                int idx = PresetsListBox.Items.IndexOf(selectPresetName);
                if (idx >= 0)
                    PresetsListBox.SelectedIndex = idx;
            }
        }
    }
}
