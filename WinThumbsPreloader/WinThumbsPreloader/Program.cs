using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
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
        public static Options AppOptions { get; private set; }

        public static int activeInstances = 0;
        public static bool adminElevationAttempted = false;
        public static bool standardElevationAttempted = false;
        public static bool formOpen = false;

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
            AppOptions = options;

            WriteLine($"Options: Bad or no arguments = {options.badOrNoArguments}, Include nested directories = {options.includeNestedDirectories}, Multithreaded = {options.multiThreaded}, Silent mode = {options.silentMode}, Start minimized = {options.startMinimized}, Thread count = {options.threadCount}, Max parallel instances = {options.maxParallelInstances}", LoggingFrequency.PreloaderLogging);
            WriteLine($"Paths: {string.Join(Environment.NewLine, options.paths)}", LoggingFrequency.PreloaderLogging);

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

        private static void StartMinimized()
        {
            formOpen = true;
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CacheForm());
        }

        private static void OpenAboutForm()
        {
            formOpen = true;
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AboutForm());
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

            try
            {
                WriteLine("Attempting to restart as administrator.", LoggingFrequency.GUILogging);
                Process.Start(psi);
                Environment.Exit(0);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
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
