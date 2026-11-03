using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WinThumbsPreloader.Properties;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    public sealed class DirectoryScannerTests : IDisposable
    {
        private readonly string _rootDir;

        public DirectoryScannerTests()
        {
            _rootDir = Path.Combine(Path.GetTempPath(), "WTP_ScannerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_rootDir))
                    Directory.Delete(_rootDir, recursive: true);
            }
            catch { }
        }

        [Fact]
        public void ScanToChannel_FirstLevelOnly_StreamsTopLevelMatchingFilesAndIgnoresNestedFiles()
        {
            // Arrange
            string topJpg = Path.Combine(_rootDir, "top1.jpg");
            string topPng = Path.Combine(_rootDir, "top2.png");
            string topTxt = Path.Combine(_rootDir, "ignore.txt");
            File.WriteAllBytes(topJpg, [1, 2, 3]);
            File.WriteAllBytes(topPng, [1, 2, 3]);
            File.WriteAllText(topTxt, "not an image");

            string subDir = Path.Combine(_rootDir, "Nested");
            Directory.CreateDirectory(subDir);
            File.WriteAllBytes(Path.Combine(subDir, "nested.jpg"), [1, 2, 3]);

            var channel = Channel.CreateUnbounded<ScannedItem>();
            var scanner = new DirectoryScanner(_rootDir, includeNestedDirectories: false, multiThreaded: false, threadCount: 1);

            // Act
            scanner.ScanToChannel(channel.Writer);

            var items = new List<ScannedItem>();
            while (channel.Reader.TryRead(out ScannedItem item))
            {
                items.Add(item);
            }

            // Assert
            Assert.True(scanner.scanCompleted);
            var filePaths = items.Where(i => !i.IsDirectory).Select(i => i.FullPath).ToList();
            Assert.Equal(2, filePaths.Count);
            Assert.Contains(topJpg, filePaths);
            Assert.Contains(topPng, filePaths);
            Assert.DoesNotContain(topTxt, filePaths);
        }

        [Fact]
        public void ScanToChannel_SequentialNested_StreamsAllLevelsOfMediaFiles()
        {
            // Arrange
            string level1 = Path.Combine(_rootDir, "L1");
            string level2 = Path.Combine(level1, "L2");
            Directory.CreateDirectory(level2);

            string f0 = Path.Combine(_rootDir, "root.webp");
            string f1 = Path.Combine(level1, "mid.jpg");
            string f2 = Path.Combine(level2, "deep.png");
            File.WriteAllBytes(f0, [1]);
            File.WriteAllBytes(f1, [1]);
            File.WriteAllBytes(f2, [1]);

            var channel = Channel.CreateUnbounded<ScannedItem>();
            var scanner = new DirectoryScanner(_rootDir, includeNestedDirectories: true, multiThreaded: false, threadCount: 1);

            // Act
            scanner.ScanToChannel(channel.Writer);

            var files = new List<string>();
            while (channel.Reader.TryRead(out ScannedItem item))
            {
                if (!item.IsDirectory)
                    files.Add(item.FullPath);
            }

            // Assert
            Assert.Equal(3, files.Count);
            Assert.Contains(f0, files);
            Assert.Contains(f1, files);
            Assert.Contains(f2, files);
        }

        [Fact]
        public void ScanToChannel_ParallelNested_Regression_DiscoversAllSubdirectoriesWithoutPrematureCompletionRace()
        {
            // Arrange: Regression test for Critical bug where activeThreads == 0 && directoriesToProcess.Count == 0
            // caused ScanNestedParallelToChannel to call CompleteAdding() while another thread had dequeued a folder
            // but not yet incremented activeThreads, dropping entire subtrees.
            const int branchCount = 12;
            const int depthPerBranch = 5;
            int expectedFileCount = 0;

            for (int b = 0; b < branchCount; b++)
            {
                string current = _rootDir;
                for (int d = 0; d < depthPerBranch; d++)
                {
                    current = Path.Combine(current, $"Branch_{b}_Depth_{d}");
                    Directory.CreateDirectory(current);
                    string imgFile = Path.Combine(current, $"img_{b}_{d}.jpg");
                    File.WriteAllBytes(imgFile, [0xFF, 0xD8]);
                    expectedFileCount++;
                }
            }

            var channel = Channel.CreateUnbounded<ScannedItem>();
            var scanner = new DirectoryScanner(_rootDir, includeNestedDirectories: true, multiThreaded: true, threadCount: 8);

            // Act
            scanner.ScanToChannel(channel.Writer);

            var discoveredFiles = new List<ScannedItem>();
            while (channel.Reader.TryRead(out ScannedItem item))
            {
                if (!item.IsDirectory)
                    discoveredFiles.Add(item);
            }

            // Assert
            Assert.Equal(expectedFileCount, discoveredFiles.Count);
            Assert.All(discoveredFiles, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.ParentDirectory));
                Assert.False(string.IsNullOrWhiteSpace(item.RelativeName));
            });
        }

        [Fact]
        public async Task ScanToChannel_ParallelNestedWhenCancelled_Regression_ExitsCleanlyWithoutDeadlock()
        {
            // Arrange: Regression test for cancellation deadlock in BlockingCollection.GetConsumingEnumerable()
            for (int i = 0; i < 30; i++)
            {
                string sub = Path.Combine(_rootDir, $"Folder_{i}");
                Directory.CreateDirectory(sub);
                for (int j = 0; j < 10; j++)
                {
                    File.WriteAllBytes(Path.Combine(sub, $"pic_{j}.jpg"), [1, 2]);
                }
            }

            var channel = Channel.CreateUnbounded<ScannedItem>();
            var scanner = new DirectoryScanner(_rootDir, includeNestedDirectories: true, multiThreaded: true, threadCount: 8)
            {
                cancelled = true // Pre-cancelled before entering loop
            };

            // Act
            Task scanTask = Task.Run(() => scanner.ScanToChannel(channel.Writer));
            Task completed = await Task.WhenAny(scanTask, Task.Delay(TimeSpan.FromSeconds(5)));

            // Assert
            Assert.Same(scanTask, completed);
            await scanTask;
            Assert.True(scanner.scanCompleted);
        }
    }
}
