using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;

#nullable enable

namespace WinThumbsPreloader
{
    public readonly record struct ScannedItem(
        string FullPath,
        string? ParentDirectory,
        string? RelativeName,
        bool IsDirectory,
        bool IsNetworkPath
    );

    [SupportedOSPlatform("windows")]
    sealed class DirectoryScanner
    {
        public volatile bool cancelled = false;
        public int totalItemsCount = 0;
        public volatile bool scanCompleted = false;

        private readonly List<ScannedItem> scannedList = new List<ScannedItem>();
        private readonly bool includeNestedDirectories;
        private readonly bool multiThreaded;
        private readonly string path;
        private readonly bool isNetworkPath;
        private readonly bool preloadAllFolders;
        private readonly bool preloadFolderIcons;
        private readonly int threadCount;
        private readonly HashSet<string> thumbnailExtensions;
        private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> thumbnailExtensionsSpanLookup;
        private readonly FileSystemEnumerable<bool>.FindPredicate matchFileOnlyPredicate;
        private readonly FileSystemEnumerable<(string Name, bool IsDirectory)>.FindPredicate includeDirsPredicate;
        private readonly FileSystemEnumerable<(string Name, bool IsDirectory)>.FindPredicate filesOnlyPredicate;

        public DirectoryScanner(string path, bool includeNestedDirectories, bool multiThreaded, int threadCount)
        {
            WriteLine("Initializing Directory Scanner - DirectoryScanner(string, bool, bool, int)", LoggingFrequency.PreloaderLogging);
            thumbnailExtensions = LoadExtensions();
            thumbnailExtensionsSpanLookup = thumbnailExtensions.GetAlternateLookup<ReadOnlySpan<char>>();
            matchFileOnlyPredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && ShouldIncludeFileName(entry.FileName);
            includeDirsPredicate = (ref FileSystemEntry entry) => entry.IsDirectory || ShouldIncludeFileName(entry.FileName);
            filesOnlyPredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && ShouldIncludeFileName(entry.FileName);
            this.path = NetworkPathResolver.NormalizeAndResolvePath(path) ?? path;
            this.isNetworkPath = NetworkPathResolver.IsNetworkOrMappedPath(this.path);
            this.includeNestedDirectories = includeNestedDirectories;
            this.multiThreaded = multiThreaded;
            this.threadCount = Math.Max(1, threadCount);
            this.preloadFolderIcons = Settings.Default.PreloadFolderIcons;
            this.preloadAllFolders = Settings.Default.PreloadAllFolders;
            WriteLine($"Resolved Scanner Path: {this.path} (Network/Mapped: {this.isNetworkPath})", LoggingFrequency.PreloaderLogging);
            WriteLine("IncludeNestedDirectories: " + includeNestedDirectories, LoggingFrequency.PreloaderLogging);
            WriteLine("preloadFolderIcons: " + preloadFolderIcons, LoggingFrequency.PreloaderLogging);
            WriteLine("preloadAllFolders: " + preloadAllFolders, LoggingFrequency.PreloaderLogging);
        }

        private HashSet<string> LoadExtensions()
        {
            try
            {
                var exts = Settings.Default.ExtensionsText
                    .Split([',', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(ext => ext.Trim())
                    .Where(ext => ext.Length > 0)
                    .Select(ext =>
                    {
                        ext = ext.TrimStart('.');
                        return "." + ext.ToLowerInvariant();
                    })
                    .Distinct();

                return new HashSet<string>(exts, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                WriteLine("Failed to load extensions, using defaults: " + ex.Message, LoggingFrequency.PreloaderLogging);

                return new HashSet<string>(
                [
                    ".avi",".avif",".bmp",".gif",".heic",".heif",".jpg",".jpeg",
                    ".mkv",".mov",".mp4",".png",".svg",".tif",".tiff",".webp"
                ], StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Streams discovered items directly into a bounded/unbounded ChannelWriter so consumers can start
        /// extracting thumbnails immediately while directory enumeration is still running.
        /// </summary>
        public void ScanToChannel(ChannelWriter<ScannedItem> writer)
        {
            try
            {
                if (!includeNestedDirectories)
                {
                    ScanFirstLevelToChannel(writer);
                }
                else if (multiThreaded)
                {
                    ScanNestedParallelToChannel(writer);
                }
                else
                {
                    ScanNestedSequentialToChannel(writer);
                }
            }
            finally
            {
                scanCompleted = true;
                writer.TryComplete();
            }
        }

        public string[] GetItems()
        {
            ScannedItem[] items = GetScannedItems();
            string[] result = new string[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                result[i] = items[i].FullPath;
            }
            return result;
        }

        public ScannedItem[] GetScannedItems()
        {
            var channel = Channel.CreateUnbounded<ScannedItem>(new UnboundedChannelOptions
            {
                SingleWriter = !multiThreaded || !includeNestedDirectories,
                SingleReader = true
            });

            ScanToChannel(channel.Writer);

            scannedList.Clear();
            while (channel.Reader.TryRead(out ScannedItem item))
            {
                scannedList.Add(item);
            }

            if (multiThreaded && includeNestedDirectories)
            {
                scannedList.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.FullPath, b.FullPath));
            }

            ScannedItem[] result = scannedList.ToArray();
            scannedList.Clear();
            scannedList.Capacity = 0;
            return result;
        }

        private bool ShouldIncludeFileName(ReadOnlySpan<char> fileName)
        {
            ReadOnlySpan<char> ext = Path.GetExtension(fileName);

            return !ext.IsEmpty &&
                   (thumbnailExtensions.Count == 0 || thumbnailExtensionsSpanLookup.Contains(ext));
        }

        private bool DirectoryContainsMatchingFile(string directoryPath, EnumerationOptions enumOptions)
        {
            var enumerable = new FileSystemEnumerable<bool>(
                directoryPath,
                static (ref FileSystemEntry _) => true,
                enumOptions)
            {
                ShouldIncludePredicate = matchFileOnlyPredicate
            };

            foreach (bool _ in enumerable)
            {
                if (cancelled)
                    return false;

                return true;
            }

            return false;
        }

        private FileSystemEnumerable<(string Name, bool IsDirectory)> CreateDirectoryEnumerable(
            string directoryPath,
            EnumerationOptions enumOptions,
            bool includeDirectories)
        {
            return new FileSystemEnumerable<(string Name, bool IsDirectory)>(
                directoryPath,
                static (ref FileSystemEntry entry) => (entry.FileName.ToString(), entry.IsDirectory),
                enumOptions)
            {
                ShouldIncludePredicate = includeDirectories ? includeDirsPredicate : filesOnlyPredicate
            };
        }

        private void ScanFirstLevelToChannel(ChannelWriter<ScannedItem> writer)
        {
            WriteLine("Scanning items for only first level", LoggingFrequency.PreloaderLogging);

            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                RecurseSubdirectories = false
            };

            try
            {
                string rootFullName = Path.GetFullPath(path);

                foreach (var (name, isDirectory) in CreateDirectoryEnumerable(rootFullName, enumOptions, preloadFolderIcons))
                {
                    if (cancelled)
                        break;

                    string entryFullName = Path.Join(rootFullName, name);

                    if (isDirectory)
                    {
                        if (preloadAllFolders)
                        {
                            writer.TryWrite(new ScannedItem(entryFullName, rootFullName, name, true, isNetworkPath));
                            Interlocked.Increment(ref totalItemsCount);
                            continue;
                        }

                        try
                        {
                            if (DirectoryContainsMatchingFile(entryFullName, enumOptions))
                            {
                                writer.TryWrite(new ScannedItem(entryFullName, rootFullName, name, true, isNetworkPath));
                                Interlocked.Increment(ref totalItemsCount);
                            }
                        }
                        catch (Exception e)
                        {
                            WriteLine("Exception thrown while scanning subdirectory: " + e.Message, LoggingFrequency.DebugLogging);
                        }
                    }
                    else
                    {
                        writer.TryWrite(new ScannedItem(entryFullName, rootFullName, name, false, isNetworkPath));
                        Interlocked.Increment(ref totalItemsCount);
                    }
                }
            }
            catch (Exception e)
            {
                WriteLine("Exception thrown while scanning directory: " + e.Message, LoggingFrequency.DebugLogging);
            }
        }

        private void ScanNestedSequentialToChannel(ChannelWriter<ScannedItem> writer)
        {
            WriteLine("Scanning items for nested directories sequentially", LoggingFrequency.PreloaderLogging);

            var enumOptions = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
            var queue = new Queue<(string FullPath, string? ParentPath, string? FolderName)>();
            string rootFullName = Path.GetFullPath(path);
            queue.Enqueue((rootFullName, Path.GetDirectoryName(rootFullName), Path.GetFileName(rootFullName)));

            while (queue.Count > 0 && !cancelled)
            {
                var (currentPath, parentPath, folderName) = queue.Dequeue();
                bool directoryContainsThumbnail = false;

                try
                {
                    foreach (var (name, isDirectory) in CreateDirectoryEnumerable(currentPath, enumOptions, includeDirectories: true))
                    {
                        if (cancelled) break;

                        string entryFullName = Path.Join(currentPath, name);

                        if (isDirectory)
                        {
                            queue.Enqueue((entryFullName, currentPath, name));
                        }
                        else
                        {
                            writer.TryWrite(new ScannedItem(entryFullName, currentPath, name, false, isNetworkPath));
                            Interlocked.Increment(ref totalItemsCount);
                            directoryContainsThumbnail = true;
                        }
                    }

                    if (!cancelled && preloadFolderIcons && (directoryContainsThumbnail || preloadAllFolders))
                    {
                        writer.TryWrite(new ScannedItem(currentPath, parentPath, folderName, true, isNetworkPath));
                        Interlocked.Increment(ref totalItemsCount);
                    }
                }
                catch (Exception e)
                {
                    WriteLine("Exception thrown while scanning directory: " + e.Message, LoggingFrequency.DebugLogging);
                }
            }
        }

        private void ScanNestedParallelToChannel(ChannelWriter<ScannedItem> writer)
        {
            // Cap SMB directory enumeration concurrency to avoid overwhelming network shares while keeping local NVMe at full speed
            int scannerDegree = isNetworkPath ? Math.Clamp(threadCount, 2, 8) : threadCount;
            WriteLine($"Scanning items for nested directories in parallel (degree={scannerDegree})", LoggingFrequency.PreloaderLogging);

            var enumOptions = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
            string rootFullName = Path.GetFullPath(path);
            using var directoriesToProcess = new BlockingCollection<(string FullPath, string? ParentPath, string? FolderName)>
            {
                (rootFullName, Path.GetDirectoryName(rootFullName), Path.GetFileName(rootFullName))
            };
            int pendingWork = 1;

            void AbortRemainingQueue()
            {
                try
                {
                    directoriesToProcess.CompleteAdding();
                    while (directoriesToProcess.TryTake(out _))
                    {
                        Interlocked.Decrement(ref pendingWork);
                    }
                }
                catch { }
            }

            Parallel.ForEach(
                Partitioner.Create(directoriesToProcess.GetConsumingEnumerable(), EnumerablePartitionerOptions.NoBuffering),
                new ParallelOptions { MaxDegreeOfParallelism = scannerDegree },
                (currentDir, loopState) =>
                {
                    try
                    {
                        if (cancelled || loopState.ShouldExitCurrentIteration)
                        {
                            AbortRemainingQueue();
                            loopState.Stop();
                            return;
                        }

                        bool directoryContainsThumbnail = false;

                        try
                        {
                            string dirFullName = currentDir.FullPath;

                            foreach (var (name, isDirectory) in CreateDirectoryEnumerable(dirFullName, enumOptions, includeDirectories: true))
                            {
                                if (cancelled || loopState.ShouldExitCurrentIteration)
                                {
                                    AbortRemainingQueue();
                                    loopState.Stop();
                                    return;
                                }

                                string entryFullName = Path.Join(dirFullName, name);

                                if (isDirectory)
                                {
                                    Interlocked.Increment(ref pendingWork);
                                    try
                                    {
                                        directoriesToProcess.Add((entryFullName, dirFullName, name));
                                    }
                                    catch (InvalidOperationException)
                                    {
                                        Interlocked.Decrement(ref pendingWork);
                                    }
                                }
                                else
                                {
                                    writer.TryWrite(new ScannedItem(entryFullName, dirFullName, name, false, isNetworkPath));
                                    Interlocked.Increment(ref totalItemsCount);
                                    directoryContainsThumbnail = true;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            WriteLine("Exception thrown while scanning directory: " + e.Message, LoggingFrequency.DebugLogging);
                        }

                        if (!cancelled && preloadFolderIcons && (directoryContainsThumbnail || preloadAllFolders))
                        {
                            writer.TryWrite(new ScannedItem(currentDir.FullPath, currentDir.ParentPath, currentDir.FolderName, true, isNetworkPath));
                            Interlocked.Increment(ref totalItemsCount);
                        }
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref pendingWork) == 0)
                        {
                            try
                            {
                                directoriesToProcess.CompleteAdding();
                            }
                            catch { }
                        }
                    }
                });
        }
    }
}
