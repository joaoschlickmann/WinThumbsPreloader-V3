using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinThumbsPreloader.Forms;
using WinThumbsPreloader.Properties;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    [Collection("NonParallelIntegration")]
    public sealed class VisualAndUserSimulationE2ETests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _screenshotsDir;
        private readonly bool _originalThumbsResetAlert;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        public VisualAndUserSimulationE2ETests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_VisualE2E_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            string repoRoot = LocateRepositoryRoot();
            _screenshotsDir = Path.Combine(repoRoot, "artifacts", "e2e-visual");
            Directory.CreateDirectory(_screenshotsDir);

            _originalThumbsResetAlert = Settings.Default.ThumbsResetAlert;
            Settings.Default.ThumbsResetAlert = false;

            Program.formOpen = true;
            if (Program.AppOptions == null)
            {
                Program.AppOptions = new Options([]);
            }
        }

        public void Dispose()
        {
            Settings.Default.ThumbsResetAlert = _originalThumbsResetAlert;
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        private static string LocateRepositoryRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "README.md")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "WinThumbsPreloader")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return Path.Combine(Path.GetTempPath(), "WTP_Artifacts");
        }

        private static Task RunOnStaThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    Application.EnableVisualStyles();
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        private string CaptureFormScreenshot(Form form, string fileName)
        {
            form.PerformLayout();
            form.Invalidate(invalidateChildren: true);
            form.Update();
            Application.DoEvents();
            Thread.Sleep(50);
            form.Refresh();
            Application.DoEvents();

            int width = Math.Max(form.Width, 320);
            int height = Math.Max(form.Height, 240);

            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            bool capturedViaPrintWindow = false;

            if (form.IsHandleCreated)
            {
                using Graphics g = Graphics.FromImage(bmp);
                IntPtr hdc = g.GetHdc();
                try
                {
                    capturedViaPrintWindow = PrintWindow(form.Handle, hdc, PW_RENDERFULLCONTENT);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            if (!capturedViaPrintWindow)
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, width, height));
            }

            string fullPath = Path.Combine(_screenshotsDir, fileName);
            bmp.Save(fullPath, ImageFormat.Png);

            FileInfo fi = new FileInfo(fullPath);
            Assert.True(fi.Exists && fi.Length > 1024, $"Screenshot {fileName} was not generated properly.");
            return fullPath;
        }

        private static void AssertNoSiblingButtonsOrCheckBoxesOverlap(Control parent, List<string> violations)
        {
            var interactiveSiblings = parent.Controls
                .Cast<Control>()
                .Where(c => c.Visible && (c is Button || c is CheckBox || c is ComboBox || c is NumericUpDown || c.Name.EndsWith("HelpLabel", StringComparison.Ordinal)))
                .ToList();

            for (int i = 0; i < interactiveSiblings.Count; i++)
            {
                Control a = interactiveSiblings[i];
                if (a.Right > parent.ClientSize.Width + 6 || a.Bottom > parent.ClientSize.Height + 6)
                {
                    violations.Add($"{parent.Name}.{a.Name} ({a.Bounds}) overflows parent ClientSize ({parent.ClientSize})");
                }

                for (int j = i + 1; j < interactiveSiblings.Count; j++)
                {
                    Control b = interactiveSiblings[j];
                    Rectangle intersection = Rectangle.Intersect(a.Bounds, b.Bounds);
                    if (intersection.Width > 2 && intersection.Height > 2)
                    {
                        violations.Add($"Overlap in {parent.Name}: {a.Name} ({a.Bounds}) overlaps {b.Name} ({b.Bounds})");
                    }
                }
            }

            foreach (Control child in parent.Controls)
            {
                if (child.Visible && child.HasChildren)
                {
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(child, violations);
                }
            }
        }

        [Fact]
        public async Task AllApplicationForms_RenderVisuallyWithoutClippedOrOverlappingControls_AndCaptureScreenshots()
        {
            await RunOnStaThreadAsync(() =>
            {
                var layoutViolations = new List<string>();

                // 1. AboutForm (verifying OptimizeButton is present & aligned)
                using (var aboutForm = new AboutForm())
                {
                    aboutForm.StartPosition = FormStartPosition.Manual;
                    aboutForm.Location = new Point(100, 100);
                    aboutForm.Show();
                    Application.DoEvents();

                    Control[] optBtn = aboutForm.Controls.Find("OptimizeButton", searchAllChildren: true);
                    Assert.Single(optBtn);
                    Assert.True(optBtn[0].Visible);
                    Assert.Equal("Optimize OS", optBtn[0].Text);

                    CaptureFormScreenshot(aboutForm, "01_AboutForm.png");
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(aboutForm, layoutViolations);
                    aboutForm.Hide();
                }

                // 2. SettingsForm (verifying WindowsOptimizerButton is present & aligned)
                using (var settingsForm = new SettingsForm())
                {
                    settingsForm.StartPosition = FormStartPosition.Manual;
                    settingsForm.Location = new Point(100, 100);
                    settingsForm.Show();
                    Application.DoEvents();

                    Control[] optBtn = settingsForm.Controls.Find("WindowsOptimizerButton", searchAllChildren: true);
                    Assert.Single(optBtn);
                    Assert.True(optBtn[0].Visible);
                    Assert.Equal("Optimize OS", optBtn[0].Text);

                    CaptureFormScreenshot(settingsForm, "02_SettingsForm.png");
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(settingsForm, layoutViolations);
                    settingsForm.Hide();
                }

                // 3. CacheForm (verifying OptimizeButton & Tray Menu item)
                using (var cacheForm = new CacheForm())
                {
                    cacheForm.StartPosition = FormStartPosition.Manual;
                    cacheForm.Location = new Point(100, 100);
                    cacheForm.Show();
                    Application.DoEvents();

                    Control[] optBtn = cacheForm.Controls.Find("OptimizeButton", searchAllChildren: true);
                    Assert.Single(optBtn);
                    Assert.True(optBtn[0].Visible);
                    Assert.Equal("Optimize OS", optBtn[0].Text);

                    FieldInfo? trayMenuField = typeof(CacheForm).GetField("contextMenuStrip1", BindingFlags.Instance | BindingFlags.NonPublic);
                    var trayMenu = trayMenuField?.GetValue(cacheForm) as ContextMenuStrip;
                    Assert.NotNull(trayMenu);
                    bool hasTrayOptimizerItem = trayMenu!.Items
                        .Cast<ToolStripItem>()
                        .Any(item => string.Equals(item.Text, "Windows Optimizer...", StringComparison.Ordinal));
                    Assert.True(hasTrayOptimizerItem, "System tray menu must contain 'Windows Optimizer...'");

                    CaptureFormScreenshot(cacheForm, "03_CacheForm.png");
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(cacheForm, layoutViolations);
                    cacheForm.Hide();
                }

                // 4, 5, 6. WindowsOptimizerForm across all 3 Tabs
                using (var optimizerForm = new WindowsOptimizerForm())
                {
                    optimizerForm.StartPosition = FormStartPosition.Manual;
                    optimizerForm.Location = new Point(100, 100);
                    optimizerForm.Show();
                    Application.DoEvents();

                    var tabControl = (TabControl)optimizerForm.Controls.Find("OptimizerTabControl", searchAllChildren: true).Single();
                    Assert.Equal(3, tabControl.TabPages.Count);
                    Assert.Equal(14, optimizerForm.HelpTexts.Count);
                    foreach (var kvp in optimizerForm.HelpTexts)
                    {
                        Assert.Equal(Cursors.Help, kvp.Key.Cursor);
                        Assert.Contains("WHAT IT DOES:", kvp.Value);
                        Assert.Contains("[+] PROS:", kvp.Value);
                        Assert.Contains("[-] CONS / NOTES:", kvp.Value);
                    }

                    // Tab 1: Cache & Network
                    tabControl.SelectedIndex = 0;
                    Application.DoEvents();
                    CaptureFormScreenshot(optimizerForm, "04_WindowsOptimizer_Tab1_CacheNetwork.png");
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(optimizerForm, layoutViolations);

                    // Tab 2: Explorer Performance
                    tabControl.SelectedIndex = 1;
                    Application.DoEvents();
                    CaptureFormScreenshot(optimizerForm, "05_WindowsOptimizer_Tab2_ExplorerPerformance.png");
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(optimizerForm, layoutViolations);

                    // Tab 3: Shell & Maintenance
                    tabControl.SelectedIndex = 2;
                    Application.DoEvents();
                    CaptureFormScreenshot(optimizerForm, "06_WindowsOptimizer_Tab3_ShellMaintenance.png");
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(optimizerForm, layoutViolations);

                    optimizerForm.Hide();
                }

                // 7. AdvancedSettingsForm
                using (var advSettings = new AdvancedSettingsForm())
                {
                    advSettings.StartPosition = FormStartPosition.Manual;
                    advSettings.Location = new Point(100, 100);
                    advSettings.Show();
                    Application.DoEvents();
                    CaptureFormScreenshot(advSettings, "07_AdvancedSettingsForm.png");
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(advSettings, layoutViolations);
                    advSettings.Hide();
                }

                // 8. AdvancedCacheForm
                using (var advCache = new AdvancedCacheForm())
                {
                    advCache.StartPosition = FormStartPosition.Manual;
                    advCache.Location = new Point(100, 100);
                    advCache.Show();
                    Application.DoEvents();
                    CaptureFormScreenshot(advCache, "08_AdvancedCacheForm.png");
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(advCache, layoutViolations);
                    advCache.Hide();
                }

                // 9. BulkPreloaderForm
                using (var bulkForm = new BulkPreloaderForm())
                {
                    bulkForm.StartPosition = FormStartPosition.Manual;
                    bulkForm.Location = new Point(100, 100);
                    bulkForm.Show();
                    Application.DoEvents();
                    CaptureFormScreenshot(bulkForm, "09_BulkPreloaderForm.png");
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(bulkForm, layoutViolations);
                    bulkForm.Hide();
                }

                // 10. ScheduleForm
                using (var scheduleForm = new ScheduleForm())
                {
                    scheduleForm.StartPosition = FormStartPosition.Manual;
                    scheduleForm.Location = new Point(100, 100);
                    scheduleForm.Show();
                    Application.DoEvents();
                    CaptureFormScreenshot(scheduleForm, "10_ScheduleForm.png");
                    AssertNoSiblingButtonsOrCheckBoxesOverlap(scheduleForm, layoutViolations);
                    scheduleForm.Hide();
                }

                Assert.Empty(layoutViolations);
            });
        }

        [Fact]
        public async Task SimulatedUserFlow_WindowsOptimizerForm_ClickRecommended_ToggleControls_AndVerifyShiftKeyExit()
        {
            var originalProfile = WindowsOptimizer.OptimizationProfile.LoadFromSettings();
            try
            {
                await RunOnStaThreadAsync(() =>
                {
                    using var form = new WindowsOptimizerForm();
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(120, 120);
                    form.Show();
                    Application.DoEvents();

                    var recommendedBtn = (Button)form.Controls.Find("RecommendedButton", true).Single();
                    var closeBtn = (Button)form.Controls.Find("CloseButton", true).Single();
                    var preventCleanupCb = (CheckBox)form.Controls.Find("PreventCacheCleanupCheckBox", true).Single();
                    var clearFlagsCb = (CheckBox)form.Controls.Find("ClearDiskCleanupFlagsCheckBox", true).Single();
                    var disableSilentTaskCb = (CheckBox)form.Controls.Find("DisableSilentCleanupTaskCheckBox", true).Single();
                    var netThumbsCb = (CheckBox)form.Controls.Find("DisableNetworkThumbsDbCheckBox", true).Single();
                    var forcePreviewsCb = (CheckBox)form.Controls.Find("ForceThumbnailPreviewsCheckBox", true).Single();
                    var folderDiscoveryCb = (CheckBox)form.Controls.Find("DisableFolderDiscoveryCheckBox", true).Single();
                    var bagMruCb = (CheckBox)form.Controls.Find("ExpandBagMruCheckBox", true).Single();
                    var iconCacheCb = (CheckBox)form.Controls.Find("ExpandIconCacheCheckBox", true).Single();
                    var iconSizeCombo = (ComboBox)form.Controls.Find("IconCacheSizeComboBox", true).Single();
                    var tabControl = (TabControl)form.Controls.Find("OptimizerTabControl", true).Single();

                    // Simulate user clicking "Recommended"
                    recommendedBtn.PerformClick();
                    Application.DoEvents();

                    Assert.True(preventCleanupCb.Checked);
                    Assert.True(clearFlagsCb.Checked);
                    Assert.False(disableSilentTaskCb.Checked);
                    Assert.True(netThumbsCb.Checked);
                    Assert.True(forcePreviewsCb.Checked);
                    Assert.True(folderDiscoveryCb.Checked);
                    Assert.True(bagMruCb.Checked);
                    Assert.True(iconCacheCb.Checked);
                    Assert.True(iconSizeCombo.Enabled);
                    Assert.Equal(1, iconSizeCombo.SelectedIndex); // 8192 KB (8 MB)

                    CaptureFormScreenshot(form, "11_WindowsOptimizer_AfterRecommendedClick.png");

                    // Switch to Tab 2 (Explorer Performance) and simulate user toggling Icon Cache checkbox & dropdown
                    tabControl.SelectedIndex = 1;
                    Application.DoEvents();

                    iconCacheCb.Checked = false;
                    Application.DoEvents();
                    Assert.False(iconSizeCombo.Enabled);

                    iconCacheCb.Checked = true;
                    iconSizeCombo.SelectedIndex = 2; // 16384 KB (16 MB)
                    Application.DoEvents();
                    Assert.True(iconSizeCombo.Enabled);
                    Assert.Equal("16384 KB (16 MB)", iconSizeCombo.SelectedItem?.ToString());

                    CaptureFormScreenshot(form, "12_WindowsOptimizer_Tab2_AfterUserCustomization.png");

                    // Simulate user pressing and releasing Shift key to verify Close <-> Exit button behavior
                    MethodInfo? keyDownMethod = typeof(WindowsOptimizerForm).GetMethod("WindowsOptimizerForm_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
                    MethodInfo? keyUpMethod = typeof(WindowsOptimizerForm).GetMethod("WindowsOptimizerForm_KeyUp", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(keyDownMethod);
                    Assert.NotNull(keyUpMethod);

                    keyDownMethod!.Invoke(form, [form, new KeyEventArgs(Keys.ShiftKey)]);
                    Assert.Equal("Exit", closeBtn.Text);

                    keyUpMethod!.Invoke(form, [form, new KeyEventArgs(Keys.ShiftKey)]);
                    Assert.Equal("Close", closeBtn.Text);

                    form.Close();
                });
            }
            finally
            {
                originalProfile.SaveToSettings();
            }
        }

        [Fact]
        public void WindowsOptimizer_RegressionSuite_IndividualTweaksAndSettingsPersistenceRoundTrip()
        {
            string regRoot = @"Software\WinThumbsPreloader_UnitTests\E2E_" + Guid.NewGuid().ToString("N");
            var originalProfile = WindowsOptimizer.OptimizationProfile.LoadFromSettings();

            try
            {
                bool silentTaskDisabled = false;
                var scope = WindowsOptimizer.RegistryHiveScope.CreateIsolatedForTesting(
                    Microsoft.Win32.Registry.CurrentUser,
                    regRoot,
                    simulateMachineWritable: true,
                    querySilentCleanupDisabled: () => silentTaskDisabled,
                    setSilentCleanupDisabled: disable =>
                    {
                        silentTaskDisabled = disable;
                        return true;
                    });

                // 1. Settings persistence round-trip
                var customProfile = new WindowsOptimizer.OptimizationProfile
                {
                    EnforceOnStartup = true,
                    PreventThumbCacheCleanup = true,
                    ClearDiskCleanupFlags = true,
                    DisableSilentCleanupTask = false,
                    DisableNetworkThumbsDb = true,
                    ForceThumbnailPreviews = true,
                    DisableFolderTypeDiscovery = true,
                    ExpandBagMruSize = true,
                    ExpandIconCache = true,
                    IconCacheSizeKb = 16384
                };
                customProfile.SaveToSettings();
                var loadedProfile = WindowsOptimizer.OptimizationProfile.LoadFromSettings();

                Assert.Equal(customProfile.EnforceOnStartup, loadedProfile.EnforceOnStartup);
                Assert.Equal(customProfile.PreventThumbCacheCleanup, loadedProfile.PreventThumbCacheCleanup);
                Assert.Equal(customProfile.ClearDiskCleanupFlags, loadedProfile.ClearDiskCleanupFlags);
                Assert.Equal(customProfile.DisableSilentCleanupTask, loadedProfile.DisableSilentCleanupTask);
                Assert.Equal(customProfile.DisableNetworkThumbsDb, loadedProfile.DisableNetworkThumbsDb);
                Assert.Equal(customProfile.ForceThumbnailPreviews, loadedProfile.ForceThumbnailPreviews);
                Assert.Equal(customProfile.DisableFolderTypeDiscovery, loadedProfile.DisableFolderTypeDiscovery);
                Assert.Equal(customProfile.ExpandBagMruSize, loadedProfile.ExpandBagMruSize);
                Assert.Equal(customProfile.ExpandIconCache, loadedProfile.ExpandIconCache);
                Assert.Equal(16384, loadedProfile.IconCacheSizeKb);

                // 2. Startup enforcement hook test
                WindowsOptimizer.EnsureStartupOptimizations(scope);
                var stateAfterStartup = WindowsOptimizer.InspectSystemState(scope);
                Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, stateAfterStartup.PreventThumbCacheCleanup);
                Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, stateAfterStartup.DisableFolderTypeDiscovery);
                Assert.Equal(WindowsOptimizer.OptimizationState.Optimized, stateAfterStartup.ExpandBagMruSize);
                Assert.Equal(16384, stateAfterStartup.CurrentIconCacheSizeKb);

                // 3. Verify all 3 BagMRU Shell keys received 20000
                string[] bagKeys =
                [
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell",
                    @"Software\Microsoft\Windows\Shell",
                    @"Software\Microsoft\Windows\ShellNoRoam"
                ];
                foreach (string k in bagKeys)
                {
                    using var key = scope.OpenUserKey(k, writable: false);
                    Assert.NotNull(key);
                    Assert.Equal(20000, key!.GetValue("BagMRU Size"));
                }
            }
            finally
            {
                originalProfile.SaveToSettings();
                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(regRoot, throwOnMissingSubKey: false);
                }
                catch { }
            }
        }

        [Fact]
        public void EndToEndExecutableCli_SilentRecursiveMultithreadedPreload_ExitsZeroAndPreloadsRealMedia()
        {
            // Arrange: Create real sample media files (.bmp, .png, .jpg) in nested directories
            string nestedDir = Path.Combine(_tempDir, "SubAlbum_2026");
            Directory.CreateDirectory(nestedDir);

            string bmpPath = Path.Combine(_tempDir, "e2e_photo1.bmp");
            string pngPath = Path.Combine(nestedDir, "e2e_photo2.png");
            string jpgPath = Path.Combine(nestedDir, "e2e_photo3.jpg");

            using (var bmp = new Bitmap(128, 128))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.DarkSlateBlue);
                    g.FillEllipse(Brushes.Gold, 16, 16, 96, 96);
                }
                bmp.Save(bmpPath, ImageFormat.Bmp);
                bmp.Save(pngPath, ImageFormat.Png);
                bmp.Save(jpgPath, ImageFormat.Jpeg);
            }

            string exePath = typeof(Program).Assembly.Location;
            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                string candidateExe = Path.ChangeExtension(exePath, ".exe");
                if (File.Exists(candidateExe))
                    exePath = candidateExe;
            }
            Assert.True(File.Exists(exePath), $"Executable not found at {exePath}");

            // Ensure Admin auto-restart is false during non-interactive CLI E2E test
            bool prevAdmin = Settings.Default.Admin;
            Settings.Default.Admin = false;
            Settings.Default.Save();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-r");
                psi.ArgumentList.Add("-s");
                psi.ArgumentList.Add("-m:2");
                psi.ArgumentList.Add(_tempDir);

                using Process? proc = Process.Start(psi);
                Assert.NotNull(proc);
                bool exited = proc!.WaitForExit(20000);
                if (!exited)
                {
                    try { proc.Kill(); } catch { }
                }

                Assert.True(exited, "WinThumbsPreloader.exe E2E CLI process did not exit within 20 seconds.");
                Assert.Equal(0, proc.ExitCode);
            }
            finally
            {
                Settings.Default.Admin = prevAdmin;
                Settings.Default.Save();
            }
        }
    }
}
