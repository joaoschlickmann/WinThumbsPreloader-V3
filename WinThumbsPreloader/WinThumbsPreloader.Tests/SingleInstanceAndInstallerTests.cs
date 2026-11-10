using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinThumbsPreloader.Properties;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    [Collection("NonParallelIntegration")]
    public sealed class SingleInstanceAndInstallerTests
    {
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

        [Fact]
        public void Options_WithShutdownFlag_SetsShutdownExistingAndNotGuiMode()
        {
            var options = new Options(["-shutdown"]);

            Assert.True(options.shutdownExisting);
            Assert.False(options.IsGuiLaunch);
        }

        [Fact]
        public void Options_IsGuiLaunch_DistinguishesGuiFromCliAndFolderPreload()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "WTP_GuiCheck_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                Assert.True(new Options([]).IsGuiLaunch);
                Assert.True(new Options(["-startminimized"]).IsGuiLaunch);
                Assert.True(new Options(["-reopensettings"]).IsGuiLaunch);
                Assert.True(new Options(["-reopenoptimizer"]).IsGuiLaunch);

                Assert.False(new Options(["-apply-optimizations"]).IsGuiLaunch);
                Assert.False(new Options(["-restore-defaults"]).IsGuiLaunch);
                Assert.False(new Options(["-shutdown"]).IsGuiLaunch);
                Assert.False(new Options(["-m", tempDir]).IsGuiLaunch);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void SingleInstanceCoordinator_SecondGuiLaunch_SignalsPrimaryInstanceToActivateAndReturnsFalse()
        {
            string scopeId = "WTP_SingleInstanceTest_" + Guid.NewGuid().ToString("N");
            using var activatedSignal = new ManualResetEventSlim(false);
            SingleInstanceActivationKind receivedKind = SingleInstanceActivationKind.None;

            using var primary = SingleInstanceCoordinator.TryAcquire(
                scopeId,
                kind =>
                {
                    receivedKind = kind;
                    activatedSignal.Set();
                });

            Assert.NotNull(primary);
            Assert.True(primary.IsPrimaryInstance);

            // Second normal launch (e.g., from Start Menu with no arguments)
            var secondOptions = new Options([]);
            bool canContinueAsNewInstance = SingleInstanceCoordinator.TryAcquireOrNotifyExisting(
                secondOptions,
                out var secondaryCoordinator,
                onActivate: null,
                scopeId: scopeId);

            Assert.False(canContinueAsNewInstance);
            Assert.Null(secondaryCoordinator);

            bool signaled = activatedSignal.Wait(TimeSpan.FromSeconds(5));
            Assert.True(signaled, "Primary instance did not receive activation signal from second launch.");
            Assert.Equal(SingleInstanceActivationKind.ShowMainWindow, receivedKind);
        }

        [Fact]
        public void SingleInstanceCoordinator_SecondStartMinimizedLaunch_ExitsQuietlyWithoutActivatingWindow()
        {
            string scopeId = "WTP_SingleInstanceMinTest_" + Guid.NewGuid().ToString("N");
            using var activatedSignal = new ManualResetEventSlim(false);

            using var primary = SingleInstanceCoordinator.TryAcquire(
                scopeId,
                _ => activatedSignal.Set());

            Assert.NotNull(primary);

            // Second launch with -startminimized should NOT pop up the window
            var secondOptions = new Options(["-startminimized"]);
            bool canContinue = SingleInstanceCoordinator.TryAcquireOrNotifyExisting(
                secondOptions,
                out var secondaryCoordinator,
                onActivate: null,
                scopeId: scopeId);

            Assert.False(canContinue);
            Assert.Null(secondaryCoordinator);
            Assert.False(activatedSignal.Wait(TimeSpan.FromMilliseconds(250)));
        }

        [Fact]
        public void SingleInstanceCoordinator_ReleaseAllowsSubsequentInstanceToAcquirePrimaryLock()
        {
            string scopeId = "WTP_SingleInstanceReleaseTest_" + Guid.NewGuid().ToString("N");

            var first = SingleInstanceCoordinator.TryAcquire(scopeId, null);
            Assert.NotNull(first);
            first.Dispose();

            var second = SingleInstanceCoordinator.TryAcquire(scopeId, null);
            Assert.NotNull(second);
            Assert.True(second.IsPrimaryInstance);
            second.Dispose();
        }

        [Fact]
        public async Task RestoreAndActivateExistingInstance_WhenMinimizedToTray_RestoresMainAboutFormAndHidesTrayIcon()
        {
            bool previousAlert = Settings.Default.ThumbsResetAlert;
            Settings.Default.ThumbsResetAlert = false;

            try
            {
                await RunOnStaThreadAsync(() =>
                {
                    Program.AppOptions = new Options([]);
                    using var aboutForm = new AboutForm();
                    try
                    {
                        aboutForm.StartPosition = FormStartPosition.Manual;
                        aboutForm.Show();
                        Application.DoEvents();

                        Program.MinimizeAboutFormToTray(aboutForm);
                        Application.DoEvents();

                        Assert.False(aboutForm.Visible);
                        Assert.NotNull(Program.TrayCacheForm);

                        FieldInfo? notifyField = typeof(CacheForm).GetField("notifyIcon1", BindingFlags.Instance | BindingFlags.NonPublic);
                        var notifyIcon = notifyField?.GetValue(Program.TrayCacheForm) as NotifyIcon;
                        Assert.NotNull(notifyIcon);
                        Assert.True(notifyIcon!.Visible);

                        // Act: Simulate Start Menu launch signaling existing instance
                        Program.RestoreAndActivateExistingInstance(SingleInstanceActivationKind.ShowMainWindow);
                        Application.DoEvents();

                        // Assert: MainAboutForm is visible and restored, and tray icon is hidden
                        Assert.True(aboutForm.Visible);
                        Assert.Equal(FormWindowState.Normal, aboutForm.WindowState);
                        Assert.True(aboutForm.ShowInTaskbar);
                        Assert.False(notifyIcon.Visible);

                        // Also verify single tray icon enforcement if a secondary CacheForm is opened
                        Program.AppOptions = new Options(["-startminimized"]);
                        using var primaryTrayForm = new CacheForm();
                        Program.TrayCacheForm = primaryTrayForm;
                        var primaryNotify = notifyField?.GetValue(primaryTrayForm) as NotifyIcon;
                        Assert.NotNull(primaryNotify);
                        Assert.True(primaryNotify!.Visible);
                        Assert.False(Program.AppOptions.startMinimized);

                        using var secondaryCacheForm = new CacheForm();
                        var secondaryNotify = notifyField?.GetValue(secondaryCacheForm) as NotifyIcon;
                        Assert.NotNull(secondaryNotify);
                        Assert.False(secondaryNotify!.Visible);
                        primaryTrayForm.SetTrayIconVisible(false);
                    }
                    finally
                    {
                        Program.TrayCacheForm?.SetTrayIconVisible(false);
                        Program.TrayCacheForm?.Dispose();
                        Program.TrayCacheForm = null;
                        Program.MainAboutForm = null;
                    }
                });
            }
            finally
            {
                Settings.Default.ThumbsResetAlert = previousAlert;
            }
        }

        [Fact]
        public void InnoSetupScript_EnforcesSingleCanonicalInstallAndCleansLegacyLocations()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InnoSetup", "installer.iss")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            string issPath = Path.Combine(dir!.FullName, "InnoSetup", "installer.iss");
            string issContent = File.ReadAllText(issPath);

            Assert.Contains("PrivilegesRequired=admin", issContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DisableDirPage=yes", issContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UsePreviousAppDir=no", issContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SignalRunningInstanceShutdown", issContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Exec(AppExe, '-shutdown'", issContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("{localappdata}\\Programs\\{#MyAppName}", issContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("{userprograms}\\{#MyAppName}.lnk", issContent, StringComparison.OrdinalIgnoreCase);

            var unknownFlagOptions = new Options(["-future-unknown-flag"]);
            Assert.True(unknownFlagOptions.unknownFlagProvided);
            Assert.False(unknownFlagOptions.invalidPathProvided);
            Assert.False(unknownFlagOptions.IsGuiLaunch);
        }

        [Fact]
        public void EndToEndMultiProcess_StartMenuLaunchWhileMinimized_RestoresFirstProcessAndExitsSecondProcess()
        {
            string exePath = typeof(Program).Assembly.Location;
            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                string candidateExe = Path.ChangeExtension(exePath, ".exe");
                if (File.Exists(candidateExe))
                    exePath = candidateExe;
            }
            Assert.True(File.Exists(exePath), $"Executable not found at {exePath}");

            string testScope = "WTP_E2E_MultiProc_" + Guid.NewGuid().ToString("N");
            bool prevAdmin = Settings.Default.Admin;
            bool prevAlert = Settings.Default.ThumbsResetAlert;
            Settings.Default.Admin = false;
            Settings.Default.ThumbsResetAlert = false;
            Settings.Default.Save();

            Process? proc1 = null;
            Process? proc2 = null;
            Process? proc3 = null;

            try
            {
                // 1. Start primary instance minimized to tray (-startminimized)
                var psi1 = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false
                };
                psi1.Environment["WTP_SINGLE_INSTANCE_SCOPE"] = testScope;
                psi1.ArgumentList.Add("-startminimized");

                proc1 = Process.Start(psi1);
                Assert.NotNull(proc1);

                // Give proc1 time to acquire mutex and create tray form
                Thread.Sleep(1200);
                Assert.False(proc1!.HasExited, "Primary -startminimized process exited unexpectedly.");

                // 2. Launch second instance with NO arguments (simulating Start Menu launch)
                var psi2 = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false
                };
                psi2.Environment["WTP_SINGLE_INSTANCE_SCOPE"] = testScope;

                proc2 = Process.Start(psi2);
                Assert.NotNull(proc2);

                bool secondExited = proc2!.WaitForExit(8000);
                Assert.True(secondExited, "Second GUI process should delegate to primary instance and exit immediately.");
                Assert.Equal(0, proc2.ExitCode);

                // Verify primary instance is still alive and its main window became visible
                bool mainWindowRestored = false;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 5000 && !proc1.HasExited)
                {
                    proc1.Refresh();
                    if (proc1.MainWindowHandle != IntPtr.Zero)
                    {
                        mainWindowRestored = true;
                        break;
                    }
                    Thread.Sleep(100);
                }

                Assert.False(proc1.HasExited);
                Assert.True(mainWindowRestored, "Primary instance should have restored its main window when second instance launched.");

                // 3. Signal graceful shutdown via -shutdown CLI flag (simulating installer pre-install step)
                var psi3 = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false
                };
                psi3.Environment["WTP_SINGLE_INSTANCE_SCOPE"] = testScope;
                psi3.ArgumentList.Add("-shutdown");

                proc3 = Process.Start(psi3);
                Assert.NotNull(proc3);
                Assert.True(proc3!.WaitForExit(5000));

                bool primaryExitedCleanly = proc1.WaitForExit(8000);
                Assert.True(primaryExitedCleanly, "Primary instance should shut down cleanly when -shutdown is invoked.");
                Assert.Equal(0, proc1.ExitCode);
            }
            finally
            {
                foreach (var p in new[] { proc3, proc2, proc1 })
                {
                    if (p != null)
                    {
                        try
                        {
                            if (!p.HasExited)
                            {
                                p.Kill(entireProcessTree: true);
                                p.WaitForExit(3000);
                            }
                        }
                        catch { }
                        try { p.Dispose(); } catch { }
                    }
                }

                Settings.Default.Admin = prevAdmin;
                Settings.Default.ThumbsResetAlert = prevAlert;
                Settings.Default.Save();
            }
        }
    }
}
