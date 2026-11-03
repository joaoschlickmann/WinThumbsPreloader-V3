using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader
{
    class ThumbnailsPreloader
    {
        public enum ThumbnailsPreloaderState
        {
            New,
            GettingNumberOfItems,
            Processing,
            Canceled,
            Done
        }

        private DirectoryScanner directoryScanner;
        private System.Windows.Forms.Timer cacheCheckTimer;
        private ProgressDialog progressDialog;
        private System.Windows.Forms.Timer progressDialogUpdateTimer;

        private bool hasDecrementedActiveInstances = false;
        private bool includeNestedDirectories;
        private bool multiThreaded;
        private bool silentMode;
        private int threadCount;
        private int threads;
        private string path;
        private readonly TaskCompletionSource<bool> _instanceCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ThumbnailsPreloaderState state = ThumbnailsPreloaderState.GettingNumberOfItems;
        public ThumbnailsPreloaderState prevState = ThumbnailsPreloaderState.New;
        public int totalItemsCount = 0;
        public int processedItemsCount = 0;
        public uint[] thumbnailSizes;
        public string currentFile = "";

        public ThumbnailsPreloader(string path, bool includeNestedDirectories, bool silentMode, bool multiThreaded, int threadCount)
        {
            WriteLine("Initializing Preloader - ThumbnailsPreloader(string, bool, bool, bool, int)", LoggingFrequency.PreloaderLogging);

            thumbnailSizes = ParseThumbnailSizes();
            WriteLine("thumbnailSizes: " + string.Join(", ", thumbnailSizes), LoggingFrequency.PreloaderLogging);

            this.includeNestedDirectories = includeNestedDirectories;
            WriteLine("IncludeNestedDirectories: " + includeNestedDirectories, LoggingFrequency.PreloaderLogging);

            this.multiThreaded = multiThreaded;
            WriteLine("MultiThreaded: " + multiThreaded, LoggingFrequency.PreloaderLogging);

            this.threadCount = threadCount;
            WriteLine("ThreadCount: " + threadCount, LoggingFrequency.PreloaderLogging);

            threads = DetermineThreadCount();
            WriteLine("Resolved Thread Count: " + threads, LoggingFrequency.PreloaderLogging);

            this.silentMode = silentMode;
            WriteLine("SilentMode: " + silentMode, LoggingFrequency.PreloaderLogging);

            this.path = NetworkPathResolver.NormalizeAndResolvePath(path) ?? path;
            WriteLine("Path: " + this.path, LoggingFrequency.PreloaderLogging);
        }

        public async Task StartPreloaderAsync()
        {
            try
            {
                WriteLine("Starting Preloader - StartPreloaderAsync()", LoggingFrequency.PreloaderLogging);

                FileAttributes attributes = File.GetAttributes(path);
                bool isDirectory = attributes.HasFlag(FileAttributes.Directory);

                WriteLine("Path is a directory: " + isDirectory, LoggingFrequency.PreloaderLogging);

                if (!isDirectory)
                {
                    WriteLine("Preloading single file: " + path, LoggingFrequency.PreloaderLogging);

                    await RunOnStaThreadAsync(() =>
                    {
                        try
                        {
                            ThumbnailPreloader.PreloadThumbnail(path, thumbnailSizes);
                        }
                        finally
                        {
                            ThumbnailPreloader.CleanupCurrentThreadComObjects();
                        }
                    });

                    WriteLine("Preloading single file done: " + path, LoggingFrequency.PreloaderLogging);

                    state = ThumbnailsPreloaderState.Done;
                    FinalizeInstance();
                    EndInstance();
                    await _instanceCompletion.Task;
                    return;
                }

                directoryScanner = new DirectoryScanner(path, includeNestedDirectories, multiThreaded, threads);

                if (!silentMode)
                {
                    InitProgressDialog();
                    InitProgressDialogUpdateTimer();

                    if (Settings.Default.ThumbsResetAlert && currentLoggingFrequency != LoggingFrequency.NoLogging)
                    {
                        InitializeCacheCheckTimer();
                    }
                }

                await Run();

                if (silentMode || state == ThumbnailsPreloaderState.Canceled)
                {
                    FinalizeInstance();
                    EndInstance();
                }

                await _instanceCompletion.Task;
            }
            catch (Exception ex)
            {
                WriteLine($"Preloader failed for '{path}': {ex}", LoggingFrequency.PreloaderLogging);

                state = ThumbnailsPreloaderState.Canceled;
                FinalizeInstance();
                EndInstance();

                throw;
            }
        }

        private void InitProgressDialog()
        {
            WriteLine("Initializing progress dialog - InitProgressDialog()", LoggingFrequency.PreloaderLogging);
            progressDialog = new ProgressDialog();
            progressDialog.ShowTimeRemaining = false;
            progressDialog.Title = Resources.ThumbnailsPreloader_Title;
            progressDialog.CancelMessage = Resources.ThumbnailsPreloader_CancelMessage;
            progressDialog.Show();
            WriteLine("Progress dialog initialized", LoggingFrequency.PreloaderLogging);
            UpdateProgressDialog(null, null);
        }

        private void InitProgressDialogUpdateTimer()
        {
            WriteLine("Initializing progress dialog update timer", LoggingFrequency.PreloaderLogging);
            progressDialogUpdateTimer = new System.Windows.Forms.Timer();
            progressDialogUpdateTimer.Interval = Settings.Default.ProgressDialogUpdateSpeed; // Default: 250ms
            progressDialogUpdateTimer.Tick += new EventHandler(UpdateProgressDialog);
            progressDialogUpdateTimer.Start();
            WriteLine("Progress dialog update timer initialized", LoggingFrequency.PreloaderLogging);
        }

        bool statusLogged = false;
        bool statusLogged2 = false;

        private DateTime doneCompletedAt;
        private bool doneCompletedAtSet = false;

        private async void UpdateProgressDialog(object sender, EventArgs e)
        {
            if (progressDialog == null)
                return;

            if (!statusLogged)
            {
                WriteLine("Updating progress dialog - UpdateProgressDialog(object, EventArgs)", LoggingFrequency.DebugLogging);
                statusLogged = true;
            }
            if (progressDialog.HasUserCancelled)
            {
                if (!statusLogged2)
                {
                    WriteLine("Cancelling preloader and progress dialog", LoggingFrequency.PreloaderLogging);
                    statusLogged2 = true;
                }
                finishDelayCts.Cancel();
                state = ThumbnailsPreloaderState.Canceled;
                if (directoryScanner != null)
                    directoryScanner.cancelled = true;

                progressDialog.Close();
                progressDialog?.Dispose();
                progressDialog = null;
                progressDialogUpdateTimer?.Stop();
                if (progressDialogUpdateTimer != null)
                    progressDialogUpdateTimer.Tick -= UpdateProgressDialog;
                progressDialogUpdateTimer?.Dispose();
                progressDialogUpdateTimer = null;
                return;
            }
            else if (state == ThumbnailsPreloaderState.GettingNumberOfItems)
            {
                if (prevState != state)
                {
                    WriteLine("Updating number of items in progress dialog", LoggingFrequency.DebugLogging);
                    prevState = state;
                    progressDialog.Line1 = "Scanning directory for items...";
                    progressDialog.Line3 = Resources.ThumbnailsPreloader_CalculatingNumberOfItems;
                    progressDialog.Marquee = true;
                }
                totalItemsCount = directoryScanner.totalItemsCount;
                progressDialog.Line2 = String.Format(Resources.ThumbnailsPreloader_Discovered0Items, totalItemsCount);
            }
            else if (state == ThumbnailsPreloaderState.Processing)
            {
                totalItemsCount = Math.Max(directoryScanner.totalItemsCount, processedItemsCount);
                int safeTotal = Math.Max(1, totalItemsCount);

                if (prevState != state)
                {
                    WriteLine("Updating progress dialog for thumbnail processing", LoggingFrequency.DebugLogging);
                    prevState = state;
                    progressDialog.Marquee = false;
                }

                progressDialog.Line1 = String.Format(Resources.ThumbnailsPreloader_PreloadingThumbnailsFor0Items, totalItemsCount);
                int percent = Math.Clamp((int)((processedItemsCount * 100L) / safeTotal), 0, 100);
                progressDialog.Title = String.Format(Resources.ThumbnailsPreloader_Processing, percent);
                progressDialog.Line2 = Resources.ThumbnailsPreloader_Name + ": " + Path.GetFileName(Volatile.Read(ref currentFile));
                progressDialog.Line3 = String.Format(Resources.ThumbnailsPreloader_ItemsRemaining, Math.Max(0, totalItemsCount - processedItemsCount));
                progressDialog.SetProgress(Math.Min(processedItemsCount, safeTotal), safeTotal);
            }
            else if (state == ThumbnailsPreloaderState.Done)
            {
                progressDialogUpdateTimer?.Stop();
                if (progressDialogUpdateTimer != null)
                    progressDialogUpdateTimer.Tick -= UpdateProgressDialog;
                progressDialogUpdateTimer?.Dispose();
                progressDialogUpdateTimer = null;

                if (prevState != state)
                {
                    WriteLine("Finalizing progress dialog", LoggingFrequency.DebugLogging);
                    if (!doneCompletedAtSet)
                    {
                        doneCompletedAt = DateTime.Now;
                        doneCompletedAtSet = true;
                    }
                    totalItemsCount = directoryScanner.totalItemsCount;
                    int safeDoneTotal = Math.Max(1, totalItemsCount);
                    progressDialog.Marquee = false;
                    progressDialog.Title = String.Format(Resources.ThumbnailsPreloader_Processing, 100);
                    progressDialog.Line1 = $"Preloading completed for {totalItemsCount:N0} items";
                    string displayPath = ShortenPath(path, 56);
                    progressDialog.Line2 = "Path: \"" + displayPath + "\"";
                    progressDialog.Line3 = $"Finished preloading {processedItemsCount:N0} items in {preloaderElapsedSeconds:N2} seconds.";
                    progressDialog.SetProgress(safeDoneTotal, safeDoneTotal);

                    bool backupAttempted = false;
                    bool waitedAfterBackup = false;
                    bool isLastActiveInstance = Volatile.Read(ref Program.activeInstances) <= 1;
                    if (isLastActiveInstance &&
                        Settings.Default.AutoBackupAfterPreload &&
                        Settings.Default.AutoBackupThumbs &&
                        CacheForm.ExplorerCacheSize() > CacheForm.BackupCacheSize())
                    {
                        WriteLine("AutoBackupThumbs is enabled and the cache size has grown, backing up thumbs cache", LoggingFrequency.PreloaderLogging);
                        prevState = state;
                        progressDialog.Line1 = Resources.ThumbnailsPreloader_BackingUpThumbsCache;

                        CacheForm.CacheOperationResult backupResult = await CacheForm.BackupThumbsCacheDetailedAsync(null);

                        if (backupResult.Succeeded)
                        {
                            WriteLine("Thumbs cache backed up successfully", LoggingFrequency.PreloaderLogging);
                            progressDialog.Line1 = "Thumbnail cache backed up successfully";
                            backupAttempted = true;
                        }
                        else if (backupResult.PartiallySucceeded)
                        {
                            WriteLine("Thumbs cache partially backed up", LoggingFrequency.PreloaderLogging);
                            progressDialog.Line1 = "Thumbnail cache partially backed up";
                            backupAttempted = true;
                        }
                        else if (backupResult.FailureReason == CacheForm.CacheOperationFailureReason.AlreadyInProgress)
                        {
                            WriteLine("Thumbs cache backup skipped, already running in another instance", LoggingFrequency.PreloaderLogging);
                        }
                        else
                        {
                            WriteLine("Thumbs cache backup failed", LoggingFrequency.PreloaderLogging);
                            WriteLine("Failure reason: " + backupResult.FailureReason, LoggingFrequency.PreloaderLogging);
                            progressDialog.Line1 = "Thumbnail cache backup failed";
                            backupAttempted = true;
                        }

                        if (backupAttempted && Settings.Default.WaitAfterCacheBackup)
                        {
                            waitedAfterBackup = true;
                            TimeSpan delay = GetDelayTimeSpan(Settings.Default.WaitTimeAfterCacheBackup, Settings.Default.WaitAfterCacheUnit);

                            await DelayWithCancellation(delay, updateDoneTitle: true);
                        }
                    }
                    if (InstanceCompleted() && !waitedAfterBackup)
                    {
                        if (Settings.Default.WaitAfterPreloading)
                        {
                            if (Settings.Default.WaitTimeAfterPreloading == 0)
                            {
                                while (progressDialog != null && !progressDialog.HasUserCancelled)
                                {
                                    await DelayWithCancellation(TimeSpan.Zero, updateDoneTitle: true);
                                }
                            }
                            else
                            {
                                WriteLine("Waiting " + Settings.Default.WaitTimeAfterPreloading + " " + Settings.Default.WaitAfterPreloadingUnit.ToLower() + " before closing preloader", LoggingFrequency.PreloaderLogging);
                                TimeSpan delay = GetDelayTimeSpan(Settings.Default.WaitTimeAfterPreloading, Settings.Default.WaitAfterPreloadingUnit);

                                await DelayWithCancellation(delay, updateDoneTitle: true);
                            }
                        }
                    }
                    WriteLine("Instance completed, ending progress dialog", LoggingFrequency.PreloaderLogging);

                    progressDialog?.Close();
                    progressDialog?.Dispose();
                    progressDialog = null;

                    finishDelayCts.Cancel();
                    finishDelayCts.Dispose();

                    EndInstance();
                    return;
                }
            }
        }

        private CancellationTokenSource finishDelayCts = new();

        private async Task DelayWithCancellation(TimeSpan delay, bool updateDoneTitle = false)
        {
            const int stepMs = 100;

            if (delay == TimeSpan.Zero)
            {
                if (updateDoneTitle && progressDialog != null)
                    progressDialog.Title = $"Finished at {doneCompletedAt:yyyy-MM-dd h:mm:ss tt}";

                while (!finishDelayCts.IsCancellationRequested && progressDialog != null && !progressDialog.HasUserCancelled)
                {
                    await Task.Delay(stepMs);
                }

                return;
            }

            DateTime closeAt = DateTime.Now + delay;
            int lastSecondsShown = -1;

            while (true)
            {
                if (finishDelayCts.IsCancellationRequested || progressDialog == null || progressDialog.HasUserCancelled)
                    return;

                TimeSpan remaining = closeAt - DateTime.Now;

                if (remaining <= TimeSpan.Zero)
                    break;

                if (updateDoneTitle)
                {
                    int secondsShown = (int)Math.Ceiling(remaining.TotalSeconds);

                    if (secondsShown != lastSecondsShown)
                    {
                        UpdateDoneTitleForCountdown(closeAt);
                        lastSecondsShown = secondsShown;
                    }
                }

                await Task.Delay(stepMs);
            }

            if (updateDoneTitle && progressDialog != null)
                progressDialog.Title = "Closing...";
        }

        private static TimeSpan GetDelayTimeSpan(int amount, string unit)
        {
            if (amount <= 0)
                return TimeSpan.Zero;

            if (unit.Equals("Hours", StringComparison.OrdinalIgnoreCase))
                return TimeSpan.FromHours(amount);

            if (unit.Equals("Minutes", StringComparison.OrdinalIgnoreCase))
                return TimeSpan.FromMinutes(amount);

            return TimeSpan.FromSeconds(amount);
        }

        private static string FormatRemainingTime(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            int hours = (int)Math.Floor(remaining.TotalHours);

            return $"{hours}h {remaining.Minutes:D2}m {remaining.Seconds:D2}s";
        }

        private void UpdateDoneTitleForCountdown(DateTime closeAt)
        {
            if (progressDialog == null)
                return;

            TimeSpan remaining = closeAt - DateTime.Now;

            if (remaining <= TimeSpan.Zero)
            {
                progressDialog.Title = "Closing...";
                return;
            }

            progressDialog.Title = $"Closing in {FormatRemainingTime(remaining)}";
        }

        public static string ShortenPath(string path, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                // Keep raw path if UNC root cannot be transformed by GetFullPath
            }

            if (path.Length <= maxLength)
                return path;

            string root = Path.GetPathRoot(path) ?? "";
            string sep = Path.DirectorySeparatorChar.ToString();
            string relative = path.Substring(root.Length).Trim(Path.DirectorySeparatorChar);

            // Ensure UNC roots (e.g. "\\server\share") include a trailing separator before ellipsis
            if (root.Length > 0 && !root.EndsWith(Path.DirectorySeparatorChar))
            {
                root += sep;
            }

            string[] segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            const string ellipsis = "...";

            for (int keep = segments.Length; keep >= 1; keep--)
            {
                string tail = string.Join(sep, segments.Skip(segments.Length - keep));
                string candidate = $"{root}{ellipsis}{sep}{tail}";
                if (candidate.Length <= maxLength)
                    return candidate;
            }

            string fileName = segments.Length > 0 ? segments[^1] : relative;
            string prefix = root.Length + ellipsis.Length + sep.Length < maxLength
                ? root + ellipsis + sep
                : "";

            int budget = Math.Max(1, maxLength - prefix.Length);
            string truncatedName = fileName.Length > budget ? fileName[^budget..] : fileName;

            return prefix + truncatedName;
        }

        private void InitializeCacheCheckTimer()
        {
            WriteLine("Initializing cache check timer", LoggingFrequency.PreloaderLogging);
            cacheCheckTimer = new System.Windows.Forms.Timer();
            cacheCheckTimer.Interval = 1000;
            cacheCheckTimer.Tick += CacheCheckTimer_Tick;
            WriteLine("Cache check timer initialized", LoggingFrequency.PreloaderLogging);
        }

        private void CacheCheckTimer_Tick(object sender, EventArgs e)
        {
            cacheCheckTimer.Stop();
            CheckCacheReset(currentFile);
            cacheCheckTimer.Start();
        }

        private void CheckCacheReset(string item)
        {
            CacheForm.GetExplorerCacheStats(out long currentCacheSize, out long currentMaxSingleDbSize);

            if (currentMaxSingleDbSize >= CacheForm.SingleDbSoftLimitBytes && thumbnailSizes.Length > 1)
            {
                Volatile.Write(ref thumbnailSizes, FilterSaturatedSecondarySizes(thumbnailSizes));
            }

            if (Settings.Default.ThumbsResetAlert && currentLoggingFrequency != LoggingFrequency.NoLogging)
            {
                if (initialCacheSize > currentCacheSize || initialMaxSingleDbSize > currentMaxSingleDbSize)
                {
                    if (!cacheReset)
                    {
                        WriteLine("WARNING: Thumbnail cache has been reset at file: " + item, LoggingFrequency.PreloaderLogging);
                    }
                    cacheReset = true;
                }
                else if (currentCacheSize > initialCacheSize)
                {
                    cacheReset = false;
                }
            }
        }

        private static Task RunOnStaThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "ThumbsSingleFile-STA"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        public long initialCacheSize = 0;
        public long initialMaxSingleDbSize = 0;
        public bool cacheReset = false;
        public double preloaderElapsedSeconds;

        private async Task Run()
        {
            WriteLine("Running Preloader V3 Streaming Pipeline - Run()", LoggingFrequency.PreloaderLogging);
            await Task.Run(() =>
            {
                state = ThumbnailsPreloaderState.GettingNumberOfItems;
                WriteLine("Preloader state: " + state, LoggingFrequency.PreloaderLogging);

                using (Process p = Process.GetCurrentProcess())
                {
                    if (Settings.Default.PreloaderProcessPriority.Equals("Realtime")) { p.PriorityClass = ProcessPriorityClass.RealTime; }
                    else if (Settings.Default.PreloaderProcessPriority.Equals("High")) { p.PriorityClass = ProcessPriorityClass.High; }
                    else if (Settings.Default.PreloaderProcessPriority.Equals("Above Normal")) { p.PriorityClass = ProcessPriorityClass.AboveNormal; }
                    else if (Settings.Default.PreloaderProcessPriority.Equals("Normal")) { p.PriorityClass = ProcessPriorityClass.Normal; }
                    else if (Settings.Default.PreloaderProcessPriority.Equals("Below Normal")) { p.PriorityClass = ProcessPriorityClass.BelowNormal; }
                    else if (Settings.Default.PreloaderProcessPriority.Equals("Idle")) { p.PriorityClass = ProcessPriorityClass.Idle; }
                }
                WriteLine("Process priority set to " + Settings.Default.PreloaderProcessPriority, LoggingFrequency.PreloaderLogging);

                CacheForm.GetExplorerCacheStats(out initialCacheSize, out initialMaxSingleDbSize);
                ThumbnailPreloader.ResetCounters();

                if (Settings.Default.ThumbsResetAlert && currentLoggingFrequency != LoggingFrequency.NoLogging)
                {
                    cacheCheckTimer?.Start();
                    WriteLine("Cache check timer started", LoggingFrequency.PreloaderLogging);
                }

                var preloaderStopWatch = Stopwatch.StartNew();

                int workerCount = multiThreaded ? Math.Max(1, threads) : 1;
                var channel = Channel.CreateUnbounded<ScannedItem>(new UnboundedChannelOptions
                {
                    SingleWriter = !multiThreaded || !includeNestedDirectories,
                    SingleReader = workerCount == 1
                });

                // Start producer scan on background thread so consumer workers can process items immediately as they arrive
                var scanStopwatch = Stopwatch.StartNew();
                Task producerTask = Task.Run(() =>
                {
                    directoryScanner.ScanToChannel(channel.Writer);
                    scanStopwatch.Stop();
                    WriteLine($"Directory scanning completed in {scanStopwatch.Elapsed.TotalSeconds:F2} seconds. Total items: {directoryScanner.totalItemsCount}", LoggingFrequency.PreloaderLogging);
                });

                WriteLine($"Starting {workerCount} dedicated STA worker thread(s) for thumbnail extraction", LoggingFrequency.PreloaderLogging);
                var reader = channel.Reader;
                var workerThreads = new Thread[workerCount];

                for (int i = 0; i < workerCount; i++)
                {
                    var worker = new Thread(() =>
                    {
                        long lastCacheCheckTick = Environment.TickCount64;
                        string lastProcessedPath = null;
                        try
                        {
                            while (state != ThumbnailsPreloaderState.Canceled)
                            {
                                ValueTask<bool> waitTask = reader.WaitToReadAsync(finishDelayCts.Token);
                                bool canRead = waitTask.IsCompletedSuccessfully
                                    ? waitTask.Result
                                    : waitTask.AsTask().GetAwaiter().GetResult();

                                if (!canRead)
                                    break;

                                while (state != ThumbnailsPreloaderState.Canceled &&
                                       reader.TryRead(out ScannedItem item))
                                {
                                    if (state == ThumbnailsPreloaderState.GettingNumberOfItems)
                                    {
                                        state = ThumbnailsPreloaderState.Processing;
                                    }

                                    try
                                    {
                                        lastProcessedPath = item.FullPath;
                                        Volatile.Write(ref currentFile, item.FullPath);
                                        ThumbnailPreloader.PreloadThumbnail(in item, Volatile.Read(ref thumbnailSizes));
                                        if (workerCount == 1 && cacheCheckTimer == null)
                                        {
                                            long nowTick = Environment.TickCount64;
                                            if (nowTick - lastCacheCheckTick >= 1000)
                                            {
                                                lastCacheCheckTick = nowTick;
                                                CheckCacheReset(item.FullPath);
                                            }
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        WriteLine($"Exception thrown while preloading thumbnail '{item.FullPath}': {e.Message}", LoggingFrequency.PreloaderLogging);
                                    }

                                    Interlocked.Increment(ref processedItemsCount);
                                }
                            }

                            if (workerCount == 1 && cacheCheckTimer == null && lastProcessedPath != null && state != ThumbnailsPreloaderState.Canceled)
                            {
                                CheckCacheReset(lastProcessedPath);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Cancelled by user
                        }
                        finally
                        {
                            ThumbnailPreloader.CleanupCurrentThreadComObjects();
                        }
                    })
                    {
                        IsBackground = true,
                        Name = $"ThumbsWorker-STA-{i + 1}"
                    };

                    worker.SetApartmentState(ApartmentState.STA);
                    workerThreads[i] = worker;
                    worker.Start();
                }

                for (int i = 0; i < workerCount; i++)
                {
                    workerThreads[i].Join();
                }

                producerTask.GetAwaiter().GetResult();
                totalItemsCount = directoryScanner.totalItemsCount;

                preloaderStopWatch.Stop();
                preloaderElapsedSeconds = preloaderStopWatch.Elapsed.TotalSeconds;

                if (totalItemsCount == 0)
                {
                    WriteLine("No items found", LoggingFrequency.PreloaderLogging);
                    preloaderElapsedSeconds = 0;
                }
                else
                {
                    WriteLine($"Thumbnail preloading completed in {preloaderElapsedSeconds:F2} seconds (Cached hits: {ThumbnailPreloader.CachedHitsCount}, Extracted: {ThumbnailPreloader.ExtractedCount}).", LoggingFrequency.PreloaderLogging);
                }

                if (state != ThumbnailsPreloaderState.Canceled)
                {
                    state = ThumbnailsPreloaderState.Done;
                }

                if (InstanceCompleted())
                {
                    FinalizeInstance();
                }
            });
        }

        private int DetermineThreadCount()
        {
            // User did NOT provide an explicit count
            if (threadCount < 0)
            {
                int saved = Settings.Default.ThreadCount;

                if (saved > 0)
                    return Math.Min(saved, 512);

                return Environment.ProcessorCount;
            }

            // User explicitly selected auto (0)
            if (threadCount == 0)
                return Environment.ProcessorCount;

            // User entered an exact number
            return Math.Min(threadCount, 512);
        }

        private static uint[] ParseThumbnailSizes()
        {
            try
            {
                uint[] parsed = Settings.Default.PreloaderThumbnailSizes
                    .Split([','], StringSplitOptions.RemoveEmptyEntries)
                    .Select(uint.Parse)
                    .OrderByDescending(x => x)
                    .ToArray();

                return FilterSaturatedSecondarySizes(parsed);
            }
            catch (Exception e)
            {
                WriteLine("Error parsing thumbnail sizes: " + e.Message, LoggingFrequency.PreloaderLogging);
                return [256];
            }
        }

        internal static uint[] FilterSaturatedSecondarySizes(uint[] sizes, Func<uint, long> dbSizeProvider = null)
        {
            if (sizes == null || sizes.Length <= 1)
                return sizes ?? [256];

            Func<uint, long> getDbSize = dbSizeProvider ?? CacheForm.GetExplorerDbFileSize;

            // Always keep the largest requested size (sizes[0], e.g. 256px) because Windows Explorer
            // downscales it in RAM for smaller views. If any secondary smaller database (such as
            // uncompressed thumbcache_96.db) has reached the 1.6 GB soft ceiling, skip it to prevent
            // Windows thumbcache.dll from hitting its 2 GB 32-bit offset limit and resetting the DB.
            var retained = new System.Collections.Generic.List<uint>(sizes.Length) { sizes[0] };
            for (int i = 1; i < sizes.Length; i++)
            {
                uint candidate = sizes[i];
                long dbBytes = getDbSize(candidate);
                if (dbBytes >= CacheForm.SingleDbSoftLimitBytes)
                {
                    WriteLine($"WARNING: Skipping saturated secondary thumbnail size {candidate}px (thumbcache_{candidate}.db is {dbBytes / (1024 * 1024)} MB >= {CacheForm.SingleDbSoftLimitBytes / (1024 * 1024)} MB soft limit) to prevent Windows 2 GB cache reset. Larger size {sizes[0]}px remains active.", LoggingFrequency.PreloaderLogging);
                    continue;
                }
                retained.Add(candidate);
            }

            return retained.Count == sizes.Length ? sizes : retained.ToArray();
        }

        private bool InstanceCompleted() =>
            state == ThumbnailsPreloaderState.Canceled || state == ThumbnailsPreloaderState.Done;

        bool isFinalizing = false;

        private void FinalizeInstance()
        {
            if (isFinalizing) return;
            isFinalizing = true;

            cacheCheckTimer?.Stop();
            if (cacheCheckTimer != null)
                cacheCheckTimer.Tick -= CacheCheckTimer_Tick;
            cacheCheckTimer?.Dispose();
            cacheCheckTimer = null;
            WriteLine("Cache check timer stopped", LoggingFrequency.PreloaderLogging);

            if (state == ThumbnailsPreloaderState.Done)
            {
                WriteLine("Preloader state: " + state, LoggingFrequency.PreloaderLogging);
                WriteLine("Preloader has finished", LoggingFrequency.PreloaderLogging);
            }
            else if (state == ThumbnailsPreloaderState.Canceled)
            {
                WriteLine("Preloader state: " + state, LoggingFrequency.PreloaderLogging);
                WriteLine("Preloader has been canceled", LoggingFrequency.PreloaderLogging);
            }
        }

        private void EndInstance()
        {
            if (hasDecrementedActiveInstances)
                return;

            hasDecrementedActiveInstances = true;

            int remainingInstances =
                Interlocked.Decrement(ref Program.activeInstances);

            WriteLine($"Instance completed. Active instances remaining: {remainingInstances}", LoggingFrequency.PreloaderLogging);

            _instanceCompletion.TrySetResult(true);

            if (remainingInstances == 0)
            {
                if (!Program.formOpen)
                {
                    WriteLine("No forms or preloaders remain; exiting application.", LoggingFrequency.GUILogging);

                    Application.Exit();
                }
            }
        }
    }
}
