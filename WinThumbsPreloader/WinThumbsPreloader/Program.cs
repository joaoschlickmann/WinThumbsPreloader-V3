using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader
{
    static class Program
    {
        public static Options AppOptions { get; internal set; }
        public static AboutForm MainAboutForm { get; internal set; }
        public static CacheForm TrayCacheForm { get; internal set; }

        public static int activeInstances = 0;
        public static bool adminElevationAttempted = false;
        public static bool standardElevationAttempted = false;
        public static bool formOpen = false;

        private static SingleInstanceCoordinator _singleInstanceCoordinator;

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] arguments)
        {
            InitializeLogger();
            WriteLine("New instance started - Main(string[])", LoggingFrequency.PreloaderLogging);
            WriteLine("Arguments: " + string.Join(" ", arguments), LoggingFrequency.PreloaderLogging);

            Options options = new Options(arguments);
            if ((arguments == null || arguments.Length == 0) && AutoUpdater.ConsumePostUpdateLaunchMinimized())
            {
                options.startMinimized = true;
            }
            AppOptions = options;

            WriteLine($"Options: Bad or no arguments = {options.badOrNoArguments}, Include nested directories = {options.includeNestedDirectories}, Multithreaded = {options.multiThreaded}, Silent mode = {options.silentMode}, Start minimized = {options.startMinimized}, Thread count = {options.threadCount}, Max parallel instances = {options.maxParallelInstances}", LoggingFrequency.PreloaderLogging);
            WriteLine($"Paths: {string.Join(Environment.NewLine, options.paths)}", LoggingFrequency.PreloaderLogging);

            if (options.shutdownExisting)
            {
                WriteLine("Signaling existing GUI instance to shut down cleanly.", LoggingFrequency.AllLogging);
                SingleInstanceCoordinator.SignalShutdown();
                return;
            }

            if (options.unknownFlagProvided)
            {
                WriteLine("Exiting quietly due to unrecognized CLI flag.", LoggingFrequency.AllLogging);
                return;
            }

            if (options.applyOptimizations)
            {
                WriteLine("Applying Windows optimizations via CLI helper.", LoggingFrequency.AllLogging);
                var profile = options.hasExplicitOptimizationProfile
                    ? WindowsOptimizer.OptimizationProfile.FromBitMask(options.optimizationProfileMask, options.optimizationIconCacheKb)
                    : WindowsOptimizer.OptimizationProfile.LoadFromSettings();
                WindowsOptimizer.ApplyOptimizations(profile, broadcastShellChange: true);
                return;
            }

            if (options.restoreDefaults)
            {
                WriteLine("Restoring Windows optimization defaults via CLI helper.", LoggingFrequency.AllLogging);
                WindowsOptimizer.RestoreWindowsDefaults(broadcastShellChange: true);
                return;
            }

            if (options.IsGuiLaunch)
            {
                if (!SingleInstanceCoordinator.TryAcquireOrNotifyExisting(
                    options,
                    out _singleInstanceCoordinator,
                    OnExternalActivationSignal))
                {
                    WriteLine("Another GUI instance of WinThumbsPreloader is already open; delegating and exiting.", LoggingFrequency.AllLogging);
                    return;
                }
            }

            try
            {
                if (!HandleElevationState(arguments))
                    return;

                CacheForm.EnsureThumbnailCacheProtection();

                if (options.startMinimized)
                {
                    WriteLine("Starting GUI cache form minimized", LoggingFrequency.GUILogging);

                    StartMinimized();
                }
                else if (options.badOrNoArguments || options.paths == null || options.paths.Count == 0)
                {
                    WriteLine("Starting GUI", LoggingFrequency.AllLogging);

                    OpenAboutForm();
                }
                else if (options.paths.Count >= 1)
                {
                    WriteLine($"Active Instances: {activeInstances}", LoggingFrequency.DebugLogging);
                    WriteLine("Starting preloader", LoggingFrequency.PreloaderLogging);

                    RunPreloaderMode(options);
                }
            }
            finally
            {
                CleanupTrayIconsOnExit();
                ReleaseSingleInstanceCoordinator();
            }
        }

        private static void OnExternalActivationSignal(SingleInstanceActivationKind kind)
        {
            if (kind == SingleInstanceActivationKind.Shutdown)
            {
                WriteLine("Received shutdown signal from external process.", LoggingFrequency.AllLogging);
                try
                {
                    InvokeOnUiThread(() => ExitApplication(0));
                }
                catch
                {
                    ExitApplication(0);
                }
                return;
            }

            InvokeOnUiThread(() => RestoreAndActivateExistingInstance(kind));
        }

        private static void InvokeOnUiThread(Action action)
        {
            Form target = GetUiInvokeTargetForm();
            if (target != null && !target.IsDisposed && target.IsHandleCreated)
            {
                if (target.InvokeRequired)
                {
                    target.BeginInvoke(action);
                }
                else
                {
                    action();
                }
                return;
            }

            action();
        }

        private static Form GetUiInvokeTargetForm()
        {
            if (MainAboutForm != null && !MainAboutForm.IsDisposed && MainAboutForm.IsHandleCreated)
                return MainAboutForm;

            if (TrayCacheForm != null && !TrayCacheForm.IsDisposed && TrayCacheForm.IsHandleCreated)
                return TrayCacheForm;

            foreach (Form form in Application.OpenForms)
            {
                if (form != null && !form.IsDisposed && form.IsHandleCreated)
                    return form;
            }

            return null;
        }

        public static void RestoreAndActivateExistingInstance(SingleInstanceActivationKind kind = SingleInstanceActivationKind.ShowMainWindow)
        {
            WriteLine($"Restoring and activating existing instance (Kind: {kind})", LoggingFrequency.GUILogging);

            Form visibleDesktopForm = FindActiveVisibleDesktopForm();

            if (visibleDesktopForm == null)
            {
                // Currently minimized to the system tray (no desktop window is visible).
                ShowMainAboutForm();
                visibleDesktopForm = MainAboutForm;
            }
            else
            {
                if (TrayCacheForm != null && !TrayCacheForm.IsDisposed)
                {
                    TrayCacheForm.SetTrayIconVisible(false);
                }

                // Restore any minimized parent/owner in the chain first.
                Form rootOwner = visibleDesktopForm;
                while (rootOwner.Owner != null && !rootOwner.Owner.IsDisposed)
                {
                    rootOwner = rootOwner.Owner;
                }

                if (rootOwner != visibleDesktopForm && rootOwner.Visible && rootOwner.WindowState == FormWindowState.Minimized)
                {
                    RestoreFormWindowState(rootOwner);
                }

                RestoreFormWindowState(visibleDesktopForm);
                BringWindowToFront(visibleDesktopForm);
            }

            if (kind == SingleInstanceActivationKind.ReopenOptimizer)
            {
                bool optimizerAlreadyOpen = Application.OpenForms
                    .Cast<Form>()
                    .Any(f => f is WindowsOptimizerForm && !f.IsDisposed && f.Visible);

                if (!optimizerAlreadyOpen && visibleDesktopForm != null && !visibleDesktopForm.IsDisposed)
                {
                    visibleDesktopForm.OpenSecondaryFormCentered(new WindowsOptimizerForm());
                }
            }
            else if (kind == SingleInstanceActivationKind.ReopenSettings)
            {
                bool settingsAlreadyOpen = Application.OpenForms
                    .Cast<Form>()
                    .Any(f => f is SettingsForm && !f.IsDisposed && f.Visible);

                if (!settingsAlreadyOpen && visibleDesktopForm is AboutForm about && !about.IsDisposed && !about.Modal)
                {
                    about.BeginInvoke(new Action(() =>
                    {
                        if (!about.IsDisposed)
                            about.OpenFormCentered(new SettingsForm());
                    }));
                }
            }
        }

        private static Form FindActiveVisibleDesktopForm()
        {
            Form bestModalOrChild = null;
            Form bestTopLevel = null;

            foreach (Form form in Application.OpenForms)
            {
                if (form == null || form.IsDisposed || !form.Visible)
                    continue;

                // Ignore TrayCacheForm when it is hidden in the system tray
                if (ReferenceEquals(form, TrayCacheForm) &&
                    form.WindowState == FormWindowState.Minimized &&
                    !form.ShowInTaskbar)
                {
                    continue;
                }

                if (form.Modal || form.Owner != null)
                {
                    bestModalOrChild = form;
                }
                else if (form.ShowInTaskbar)
                {
                    bestTopLevel = form;
                }
            }

            return bestModalOrChild ?? bestTopLevel;
        }

        private static void RestoreFormWindowState(Form form)
        {
            if (form == null || form.IsDisposed)
                return;

            if (!form.Visible)
                form.Show();

            if (form.WindowState == FormWindowState.Minimized)
            {
                if (form.IsHandleCreated)
                    ShowWindow(form.Handle, SW_RESTORE);

                form.WindowState = FormWindowState.Normal;
            }

            if (form.Owner == null && !form.Modal)
            {
                form.ShowInTaskbar = true;
            }
        }

        public static void BringWindowToFront(Form form)
        {
            if (form == null || form.IsDisposed)
                return;

            try
            {
                RestoreFormWindowState(form);

                if (form.IsHandleCreated)
                {
                    if (IsIconic(form.Handle))
                        ShowWindow(form.Handle, SW_RESTORE);
                    else
                        ShowWindow(form.Handle, SW_SHOW);

                    BringWindowToTop(form.Handle);
                    SetForegroundWindow(form.Handle);
                }

                bool wasTopMost = form.TopMost;
                form.TopMost = true;
                form.Activate();
                form.BringToFront();
                form.TopMost = wasTopMost;
                form.Focus();
            }
            catch (Exception ex)
            {
                WriteLine("Failed to bring window to front: " + ex.Message, LoggingFrequency.DebugLogging);
            }
        }

        public static void ExitApplication(int exitCode = 0)
        {
            CleanupTrayIconsOnExit();
            ReleaseSingleInstanceCoordinator();
            Environment.Exit(exitCode);
        }

        private static void CleanupTrayIconsOnExit()
        {
            try
            {
                if (TrayCacheForm != null && !TrayCacheForm.IsDisposed)
                {
                    TrayCacheForm.SetTrayIconVisible(false);
                }

                foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
                {
                    if (form is CacheForm cacheForm && !cacheForm.IsDisposed)
                    {
                        cacheForm.SetTrayIconVisible(false);
                    }
                }
            }
            catch
            {
                // Best effort tray icon cleanup
            }
        }

        private static void ReleaseSingleInstanceCoordinator()
        {
            try
            {
                _singleInstanceCoordinator?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
            finally
            {
                _singleInstanceCoordinator = null;
            }
        }

        private static void StartMinimized()
        {
            formOpen = true;
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetCompatibleTextRenderingDefault(false);
            TrayCacheForm = new CacheForm();
            _ = TrayCacheForm.Handle;
            Application.Run(TrayCacheForm);
        }

        private static void OpenAboutForm()
        {
            formOpen = true;
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetCompatibleTextRenderingDefault(false);
            MainAboutForm = new AboutForm();
            Application.Run(MainAboutForm);
        }

        public static void MinimizeAboutFormToTray(AboutForm aboutForm)
        {
            if (aboutForm != null && !aboutForm.IsDisposed)
            {
                MainAboutForm = aboutForm;
                aboutForm.ShowInTaskbar = false;
                aboutForm.Hide();
            }

            if (TrayCacheForm == null || TrayCacheForm.IsDisposed)
            {
                TrayCacheForm = new CacheForm();
            }

            _ = TrayCacheForm.Handle;
            TrayCacheForm.RefreshSettingsState();
            TrayCacheForm.MinimizeToTray();
        }

        public static void ShowMainAboutForm()
        {
            if (TrayCacheForm != null && !TrayCacheForm.IsDisposed)
            {
                if (TrayCacheForm.Visible)
                {
                    TrayCacheForm.ShowInTaskbar = false;
                    TrayCacheForm.Hide();
                }
                TrayCacheForm.SetTrayIconVisible(false);
            }

            if (MainAboutForm == null || MainAboutForm.IsDisposed)
            {
                MainAboutForm = new AboutForm();
            }

            MainAboutForm.Show();
            MainAboutForm.WindowState = FormWindowState.Normal;
            MainAboutForm.ShowInTaskbar = true;
            BringWindowToFront(MainAboutForm);
        }

        private static void RunPreloaderMode(Options options)
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var context = new PreloaderApplicationContext(options);
            Application.Run(context);
        }

        public static async Task StartPreloadersAsync(Options options)
        {
            if (options.paths == null || options.paths.Count == 0)
                return;

            // Reserve all active instances upfront so sequential/throttled execution does not exit early
            Interlocked.Add(ref activeInstances, options.paths.Count);
            WriteLine($"Active Instances reserved: {activeInstances}", LoggingFrequency.DebugLogging);

            int maxParallel = options.maxParallelInstances > 0
                ? options.maxParallelInstances
                : options.paths.Count;

            using var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
            var preloaderTasks = new List<Task>(options.paths.Count);

            foreach (string path in options.paths)
            {
                string currentPath = path;
                preloaderTasks.Add(RunSinglePathWithThrottleAsync(currentPath, options, semaphore));
            }

            await Task.WhenAll(preloaderTasks);
        }

        private static async Task RunSinglePathWithThrottleAsync(string path, Options options, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                WriteLine($"exePath: {path}", LoggingFrequency.PreloaderLogging);
                var preloader = new ThumbnailsPreloader(
                    path,
                    options.includeNestedDirectories,
                    options.silentMode,
                    options.multiThreaded,
                    options.threadCount);

                await preloader.StartPreloaderAsync();
            }
            finally
            {
                semaphore.Release();
            }
        }

        private static bool HandleElevationState(string[] args)
        {
            bool shouldBeAdmin = Settings.Default.Admin;
            bool isAdmin = IsRunningAsAdministrator();

            if (shouldBeAdmin && !isAdmin)
            {
                RestartAsAdmin(args);
                return true;
            }

            return true;
        }

        public static bool IsRunningAsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            var runningAsAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);
            WriteLine("Running as administrator: " + runningAsAdministrator, LoggingFrequency.DebugLogging);
            return runningAsAdministrator;
        }

        public static bool RestartAsAdmin(string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.CurrentDirectory
            };

            if (args != null)
            {
                foreach (string arg in args)
                {
                    psi.ArgumentList.Add(arg);
                }
            }

            bool releasedCoordinator = _singleInstanceCoordinator != null;
            if (releasedCoordinator)
            {
                ReleaseSingleInstanceCoordinator();
            }

            try
            {
                WriteLine("Attempting to restart as administrator.", LoggingFrequency.GUILogging);
                Process.Start(psi);
                ExitApplication(0);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                if (releasedCoordinator)
                {
                    _singleInstanceCoordinator = SingleInstanceCoordinator.TryAcquire(onActivate: OnExternalActivationSignal);
                }

                // User cancelled the UAC prompt
                WriteLine("User cancelled admin elevation: " + ex.Message, LoggingFrequency.GUILogging);
                MessageBox.Show(
                    "Administrator elevation was cancelled.\nThe program will continue without elevation.",
                    "Elevation Cancelled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }
            catch (Exception ex)
            {
                if (releasedCoordinator)
                {
                    _singleInstanceCoordinator = SingleInstanceCoordinator.TryAcquire(onActivate: OnExternalActivationSignal);
                }

                WriteLine("Failed to restart as administrator: " + ex.Message, LoggingFrequency.GUILogging);
                MessageBox.Show(
                    "Failed to restart as administrator.\n" + ex.Message,
                    "Elevation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }
        }

        public static void OpenFormCentered(this Form currentForm, Form newForm)
        {
            WriteLine("Opening form centered", LoggingFrequency.GUILogging);
            currentForm.Hide();
            newForm.FormClosed += (s, args) =>
            {
                currentForm.Location = new Point(newForm.Location.X + (newForm.Width - currentForm.Width) / 2,
                                                 newForm.Location.Y + (newForm.Height - currentForm.Height) / 2);
                currentForm.Show();
            };

            newForm.StartPosition = FormStartPosition.Manual;
            newForm.Location = new Point(currentForm.Location.X + (currentForm.Width - newForm.Width) / 2,
                                         currentForm.Location.Y + (currentForm.Height - newForm.Height) / 2);
            newForm.Owner = currentForm;
            try
            {
                newForm.ShowDialog();
            }
            finally
            {
                newForm.Dispose();
            }
        }

        public static void OpenSecondaryFormCentered(this Form currentForm, Form newForm)
        {
            WriteLine("Opening secondary form centered", LoggingFrequency.GUILogging);
            newForm.FormClosed += (s, args) =>
            {
                currentForm.Focus();
            };

            newForm.StartPosition = FormStartPosition.Manual;
            newForm.Location = new Point(currentForm.Location.X + (currentForm.Width - newForm.Width) / 2,
                                         currentForm.Location.Y + (currentForm.Height - newForm.Height) / 2);
            newForm.Owner = currentForm;
            newForm.Show();
        }

        private sealed class PreloaderApplicationContext : ApplicationContext
        {
            private readonly Options _options;
            private bool _started;

            public PreloaderApplicationContext(Options options)
            {
                _options = options;

                // Idle will occur after Application.Run has started pumping messages.
                Application.Idle += Application_Idle;
            }

            private async void Application_Idle(object sender, EventArgs e)
            {
                if (_started)
                    return;

                _started = true;
                Application.Idle -= Application_Idle;

                try
                {
                    await StartPreloadersAsync(_options);
                }
                catch (Exception ex)
                {
                    WriteLine("Unhandled preloader exception: " + ex, LoggingFrequency.PreloaderLogging);

                    MessageBox.Show(
                        "An error occurred while preloading thumbnails.\n\n" + ex.Message,
                        "Preloader Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    // Stops Application.Run(context).
                    ExitThread();
                }
            }

            protected override void Dispose(bool disposing)
            {
                Application.Idle -= Application_Idle;
                base.Dispose(disposing);
            }
        }
    }
}
