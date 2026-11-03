using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    [CollectionDefinition("NonParallelIntegration", DisableParallelization = true)]
    public sealed class NonParallelIntegrationCollection { }

    [Collection("NonParallelIntegration")]
    public sealed class PreloaderPipelineIntegrationTests : IDisposable
    {
        private readonly string _tempDir;

        public PreloaderPipelineIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_PipelineTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            Program.formOpen = true; // Prevent EndInstance from calling Application.Exit() during tests
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        private string CreateSampleBitmapFile(string fileName)
        {
            string filePath = Path.Combine(_tempDir, fileName);
            using var bmp = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.CornflowerBlue);
            }
            bmp.Save(filePath, ImageFormat.Bmp);
            return filePath;
        }

        [Fact]
        public void ResetCounters_SetsBothHitAndExtractedCountsToZero()
        {
            // Arrange
            Interlocked.Exchange(ref ThumbnailPreloader.CachedHitsCount, 42);
            Interlocked.Exchange(ref ThumbnailPreloader.ExtractedCount, 99);

            // Act
            ThumbnailPreloader.ResetCounters();

            // Assert
            Assert.Equal(0, Interlocked.Read(ref ThumbnailPreloader.CachedHitsCount));
            Assert.Equal(0, Interlocked.Read(ref ThumbnailPreloader.ExtractedCount));
        }

        [Fact]
        public async Task ThumbnailPreloader_OnStaThread_ExtractsRealImageAndRecreatesCacheAfterCleanup_Regression()
        {
            // Arrange: Regression test verifying that calling CleanupCurrentThreadComObjects()
            // on an STA thread sets ThreadLocalCache.Value = null and a subsequent PreloadThumbnail
            // call on the SAME thread cleanly recreates IThumbnailCache via GetOrCreateThreadLocalCache().
            string img1 = CreateSampleBitmapFile("reuse_test_1.bmp");
            string img2 = CreateSampleBitmapFile("reuse_test_2.bmp");
            ThumbnailPreloader.ResetCounters();

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var staThread = new Thread(() =>
            {
                try
                {
                    var item1 = new ScannedItem(img1, _tempDir, "reuse_test_1.bmp", IsDirectory: false, IsNetworkPath: false);
                    ThumbnailPreloader.PreloadThumbnail(in item1, [96, 256]);

                    // Release COM objects on this STA thread
                    ThumbnailPreloader.CleanupCurrentThreadComObjects();

                    // Immediately reuse the same STA thread to ensure GetOrCreateThreadLocalCache recreates COM instance
                    var item2 = new ScannedItem(img2, _tempDir, "reuse_test_2.bmp", IsDirectory: false, IsNetworkPath: false);
                    ThumbnailPreloader.PreloadThumbnail(in item2, [96, 256]);

                    ThumbnailPreloader.CleanupCurrentThreadComObjects();
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
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();

            // Act
            await tcs.Task;

            // Assert
            long totalProcessed = Interlocked.Read(ref ThumbnailPreloader.CachedHitsCount) +
                                  Interlocked.Read(ref ThumbnailPreloader.ExtractedCount);
            Assert.True(totalProcessed >= 2, $"Expected >= 2 thumbnail operations on STA thread, got {totalProcessed}");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ThumbnailsPreloader_StartPreloaderAsync_SilentDirectoryMode_CompletesStreamingPipeline(bool multiThreaded)
        {
            // Arrange: End-to-end integration test for both single-threaded and multi-threaded STA pipelines
            CreateSampleBitmapFile("img_a.bmp");
            CreateSampleBitmapFile("img_b.bmp");
            CreateSampleBitmapFile("img_c.bmp");

            Interlocked.Exchange(ref Program.activeInstances, 1);
            var preloader = new ThumbnailsPreloader(
                path: _tempDir,
                includeNestedDirectories: true,
                silentMode: true,
                multiThreaded: multiThreaded,
                threadCount: 2);

            // Act
            await preloader.StartPreloaderAsync();

            // Assert
            Assert.Equal(ThumbnailsPreloader.ThumbnailsPreloaderState.Done, preloader.state);
            Assert.True(preloader.processedItemsCount >= 3);
            Assert.Equal(0, Volatile.Read(ref Program.activeInstances));
        }

        [Fact]
        public async Task ThumbnailsPreloader_StartPreloaderAsync_SingleFileMode_Regression_RunsOnStaAndCompletes()
        {
            // Arrange: Regression test for single-file preload path (!isDirectory)
            string singleFile = CreateSampleBitmapFile("single_mode.bmp");
            Interlocked.Exchange(ref Program.activeInstances, 1);

            var preloader = new ThumbnailsPreloader(
                path: singleFile,
                includeNestedDirectories: false,
                silentMode: true,
                multiThreaded: false,
                threadCount: 1);

            // Act
            await preloader.StartPreloaderAsync();

            // Assert
            Assert.Equal(ThumbnailsPreloader.ThumbnailsPreloaderState.Done, preloader.state);
            Assert.Equal(0, Volatile.Read(ref Program.activeInstances));
        }

        [Fact]
        public void ThumbnailPreloader_ConcurrentStaWorkers_SimultaneousShellParse_DoesNotRaceOrFault()
        {
            // Arrange: Regression test for 0xC0000005 access violation when 20 STA threads
            // simultaneously enter GetOrCreateParentShellItem -> SHCreateItemFromParsingName.
            const int workerCount = 20;
            string sampleFile = CreateSampleBitmapFile("concurrent_parse.bmp");
            var item = new ScannedItem(sampleFile, _tempDir, "concurrent_parse.bmp", IsDirectory: false, IsNetworkPath: true);
            using var startGate = new ManualResetEventSlim(false);
            var threads = new Thread[workerCount];
            Exception? capturedError = null;

            for (int i = 0; i < workerCount; i++)
            {
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        startGate.Wait();
                        ThumbnailPreloader.PreloadThumbnail(in item, [96, 256]);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref capturedError, ex, null);
                    }
                    finally
                    {
                        ThumbnailPreloader.CleanupCurrentThreadComObjects();
                    }
                })
                {
                    IsBackground = true
                };
                threads[i].SetApartmentState(ApartmentState.STA);
                threads[i].Start();
            }

            // Act: release all 20 STA threads simultaneously
            startGate.Set();
            foreach (var t in threads)
                t.Join();

            // Assert
            Assert.Null(capturedError);
        }

        [Fact]
        public void ProgressDialog_SetProgress_UpdatesValueAndMaximumAtomically()
        {
            using var dialog = new ProgressDialog();
            dialog.SetProgress(825, 5173);

            Assert.Equal(825, dialog.Value);
            Assert.Equal(5173, dialog.Maximum);
        }

        [Fact]
        public void CacheForm_UpdateTimerTick_AfterFormDisposed_DoesNotThrowNullReferenceException()
        {
            var form = new CacheForm();
            var tickMethod = typeof(CacheForm).GetMethod(
                "UpdateTimer_Tick",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(tickMethod);

            form.Dispose();

            var ex = Record.Exception(() => tickMethod!.Invoke(form, [null, EventArgs.Empty]));
            Assert.Null(ex);
        }

        [Fact]
        public async Task ThumbnailPreloader_InterleavedManyDirectories_ReusesParentShellItemsWithoutThrashing()
        {
            // Regression test guarding against parent IShellItem cache thrashing when > 32 folders
            // are interleaved on an STA worker thread, as well as relative subfolder resolution.
            const int folderCount = 40;
            var items = new ScannedItem[folderCount * 2];
            using (var bmp = new Bitmap(16, 16))
            {
                for (int i = 0; i < folderCount; i++)
                {
                    string subName = $"dir_{i:D3}";
                    string subPath = Path.Combine(_tempDir, subName);
                    Directory.CreateDirectory(subPath);
                    string f1 = Path.Combine(subPath, "a.bmp");
                    string f2 = Path.Combine(subPath, "b.bmp");
                    bmp.Save(f1, ImageFormat.Bmp);
                    bmp.Save(f2, ImageFormat.Bmp);

                    // Interleave across all 40 folders so consecutive items never share the same folder
                    items[i] = new ScannedItem(f1, subPath, "a.bmp", IsDirectory: false, IsNetworkPath: false);
                    items[folderCount + i] = new ScannedItem(f2, subPath, "b.bmp", IsDirectory: false, IsNetworkPath: false);
                }
            }

            ThumbnailPreloader.ResetCounters();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var staThread = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        ThumbnailPreloader.PreloadThumbnail(in items[i], [96]);
                    }
                    ThumbnailPreloader.CleanupCurrentThreadComObjects();
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
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();

            await tcs.Task;

            long totalProcessed = Interlocked.Read(ref ThumbnailPreloader.CachedHitsCount) +
                                  Interlocked.Read(ref ThumbnailPreloader.ExtractedCount);
            Assert.Equal(items.Length, totalProcessed);
        }

        [Fact]
        public void CacheForm_ExplorerCacheSize_ReturnsNonNegativeSizeWithoutException()
        {
            long size = CacheForm.ExplorerCacheSize();
            CacheForm.GetExplorerCacheStats(out long totalBytes, out long maxSingleDbBytes);
            Assert.True(size >= 0);
            Assert.True(totalBytes >= maxSingleDbBytes);
            Assert.True(maxSingleDbBytes >= 0);

            uint[] filtered = ThumbnailsPreloader.FilterSaturatedSecondarySizes([256, 96]);
            Assert.NotEmpty(filtered);
            Assert.Equal(256u, filtered[0]);
        }

        [Fact]
        public void CacheForm_EnsureThumbnailCacheProtection_SetsHkcuAutorunZero()
        {
            CacheForm.EnsureThumbnailCacheProtection();
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache",
                writable: false);
            Assert.NotNull(key);
            Assert.Equal(0, key!.GetValue("Autorun"));

            var prop = typeof(Properties.Settings).GetProperty(nameof(Properties.Settings.StartWithWindows));
            var defaultAttr = (System.Configuration.DefaultSettingValueAttribute?)Attribute.GetCustomAttribute(
                prop!, typeof(System.Configuration.DefaultSettingValueAttribute));
            Assert.NotNull(defaultAttr);
            Assert.Equal("True", defaultAttr!.Value);
        }

        [Fact]
        public void ThumbnailPreloader_ResolveSpilloverSize_RoutesToNextUnsaturatedBucket()
        {
            long softLimit = CacheForm.SingleDbSoftLimitBytes;

            // Normal case: 256.db is below 1.6 GB -> stays 256
            Assert.Equal(256u, ThumbnailPreloader.ResolveSpilloverSize(256, _ => 500L * 1024 * 1024));

            // 256.db is >= 1.6 GB, 768.db is < 1.6 GB -> spills over to 768
            Assert.Equal(768u, ThumbnailPreloader.ResolveSpilloverSize(256, size =>
                size == 256 ? softLimit : 100L * 1024 * 1024));

            // Both 256.db and 768.db are >= 1.6 GB -> spills over to 1280
            Assert.Equal(1280u, ThumbnailPreloader.ResolveSpilloverSize(256, size =>
                (size == 256 || size == 768) ? softLimit + 1024 : 50L * 1024 * 1024));

            // All buckets saturated -> falls back to requested size without throwing
            Assert.Equal(256u, ThumbnailPreloader.ResolveSpilloverSize(256, _ => softLimit + 1024));
        }

        [Fact]
        public void ThumbnailsPreloader_FilterSaturatedSecondarySizes_DropsOnlySaturatedSecondarySizes()
        {
            long softLimit = CacheForm.SingleDbSoftLimitBytes;

            // 96.db is saturated, 256.db and 48.db are not -> drops 96, retains [256, 48]
            uint[] result = ThumbnailsPreloader.FilterSaturatedSecondarySizes(
                [256, 96, 48],
                size => size == 96 ? softLimit : 100L * 1024 * 1024);
            Assert.Equal([256u, 48u], result);

            // Both 256.db and 96.db are saturated -> retains primary [256] (so it can spill over to 768) and drops 96
            uint[] bothSaturated = ThumbnailsPreloader.FilterSaturatedSecondarySizes(
                [256, 96],
                _ => softLimit + 100);
            Assert.Equal([256u], bothSaturated);
        }

        [Fact]
        public async Task ThumbnailPreloader_SpilloverTo768_SatisfiesSubsequent256And96CacheChecksOnRealWindowsCache()
        {
            // Arrange: Create a brand-new 512x512 BMP image that has never been cached
            string fileName = "spillover_real_com_" + Guid.NewGuid().ToString("N") + ".bmp";
            string filePath = Path.Combine(_tempDir, fileName);
            using (var bmp = new Bitmap(512, 512))
            {
                using var g = Graphics.FromImage(bmp);
                g.Clear(Color.MediumSeaGreen);
                bmp.Save(filePath, ImageFormat.Bmp);
            }

            var item = new ScannedItem(filePath, _tempDir, fileName, IsDirectory: false, IsNetworkPath: false);
            var tcs = new TaskCompletionSource<(long firstExtracted, long secondHits, long secondExtracted)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var staThread = new Thread(() =>
            {
                try
                {
                    // Pass 1: Extract ONLY into the 768px spillover bucket
                    ThumbnailPreloader.ResetCounters();
                    ThumbnailPreloader.PreloadThumbnail(in item, [768]);
                    long firstExtracted = Interlocked.Read(ref ThumbnailPreloader.ExtractedCount);

                    // Pass 2: Request [256, 96] on the same item and check if 768px satisfies WTS_INCACHEONLY
                    ThumbnailPreloader.ResetCounters();
                    ThumbnailPreloader.PreloadThumbnail(in item, [256, 96]);
                    long secondHits = Interlocked.Read(ref ThumbnailPreloader.CachedHitsCount);
                    long secondExtracted = Interlocked.Read(ref ThumbnailPreloader.ExtractedCount);

                    ThumbnailPreloader.CleanupCurrentThreadComObjects();
                    tcs.TrySetResult((firstExtracted, secondHits, secondExtracted));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            })
            {
                IsBackground = true
            };
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();

            var (pass1Extracted, pass2Hits, pass2Extracted) = await tcs.Task;
            Assert.Equal(1, pass1Extracted);
            Assert.Equal(2, pass2Hits);
            Assert.Equal(0, pass2Extracted);
        }
    }
}
