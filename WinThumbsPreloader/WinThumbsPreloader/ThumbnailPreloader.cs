using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using static WinThumbsPreloader.Logger;

#nullable enable

namespace WinThumbsPreloader
{
    [SupportedOSPlatform("windows")]
    sealed class ThumbnailPreloader
    {
        const int E_ACCESSDENIED = unchecked((int)0x80070005);
        const int RPC_E_DISCONNECTED = unchecked((int)0x80010108);
        const int MaxCachedParentDirsPerThread = 512;
        const int MaxSharedParentPidls = 4096;

        private static readonly Guid CLSID_LocalThumbnailCache =
            new Guid("50ef4544-ac9f-4a8e-b21b-8a26180db13f");

        private static readonly Guid IID_IShellItem =
            new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        private static readonly Lazy<Type?> ThumbnailCacheComType = new(() =>
            Type.GetTypeFromCLSID(CLSID_LocalThumbnailCache, throwOnError: false));

        // Synchronizes full-path SHCreateItemFromParsingName calls and ensures one-time local drive
        // shell extension initialization (prevents third-party Drive FolderExtensions such as
        // ExplorerPatcher from racing on IAT patching when multiple STA workers start on a network drive).
        private static readonly object ShellParseLock = new();
        private static bool _shellWarmupDone;

        // Cross-thread cache of serialized parent directory ITEMIDLIST (PIDL) byte arrays.
        // Allows STA workers to instantiate thread-local IShellItem instances via SHCreateItemFromIDList
        // in sub-microsecond time without acquiring ShellParseLock or re-invoking Shell namespace parsing.
        private static readonly ConcurrentDictionary<string, byte[]> SharedParentPidls =
            new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        private sealed class ThreadParentCache
        {
            public string? LastPath;
            public IShellItem? LastItem;
            public readonly Dictionary<string, IShellItem> Items = new(StringComparer.OrdinalIgnoreCase);
            public readonly Queue<string> EvictionOrder = new();
        }

        // Per-thread FIFO + 1-slot MRU cache of parent directory IShellItem instances
        private static readonly ThreadLocal<ThreadParentCache> ThreadLocalParentItems =
            new ThreadLocal<ThreadParentCache>(() => new ThreadParentCache());

        // Legacy single base item fallback
        private static readonly ThreadLocal<(string? basePath, IShellItem? baseItem)> ThreadLocalBaseItem =
            new ThreadLocal<(string?, IShellItem?)>(() => (null, null));

        // One thumbnail cache instance per thread (reused across calls)
        private static readonly ThreadLocal<IThumbnailCache?> ThreadLocalCache =
            new ThreadLocal<IThumbnailCache?>(CreateThumbnailCacheInstance);

        private static IThumbnailCache? CreateThumbnailCacheInstance()
        {
            var type = ThumbnailCacheComType.Value;
            if (type == null)
                return null;

            try
            {
                var instance = Activator.CreateInstance(type);
                if (instance == null)
                {
                    WriteLine("Activator.CreateInstance returned null for IThumbnailCache", LoggingFrequency.DebugLogging);
                    return null;
                }
                return (IThumbnailCache)instance;
            }
            catch (Exception ex)
            {
                WriteLine("Failed to create IThumbnailCache: " + ex.Message, LoggingFrequency.DebugLogging);
                return null;
            }
        }

        private static IThumbnailCache? GetOrCreateThreadLocalCache()
        {
            var cache = ThreadLocalCache.Value;
            if (cache == null)
            {
                cache = CreateThumbnailCacheInstance();
                ThreadLocalCache.Value = cache;
            }
            return cache;
        }

        public static long CachedHitsCount = 0;
        public static long ExtractedCount = 0;

        private static readonly uint[] StandardThumbCacheBuckets = [16, 32, 48, 96, 256, 768, 1280, 1920, 2560];
        private static readonly ConcurrentDictionary<uint, uint> ActiveSpilloverMap = new();
        private static long _lastSpilloverCheckTick;

        /// <summary>
        /// Resolves the target Windows thumbcache_*.db bucket for a requested thumbnail size.
        /// When thumbcache_{requestedSize}.db exceeds <see cref="CacheForm.SingleDbSoftLimitBytes"/> (~1.6 GB),
        /// automatically spills over to the next larger unsaturated Windows bucket (e.g. 256 -> 768 -> 1280)
        /// so Explorer can serve the thumbnail by downscaling in RAM without hitting the 2 GB 32-bit offset reset limit.
        /// </summary>
        internal static uint ResolveSpilloverSize(uint requestedSize, Func<uint, long>? dbSizeProvider = null)
        {
            Func<uint, long> getDbSize = dbSizeProvider ?? CacheForm.GetExplorerDbFileSize;
            if (getDbSize(requestedSize) < CacheForm.SingleDbSoftLimitBytes)
                return requestedSize;

            foreach (uint candidate in StandardThumbCacheBuckets)
            {
                if (candidate <= requestedSize)
                    continue;

                if (getDbSize(candidate) < CacheForm.SingleDbSoftLimitBytes)
                    return candidate;
            }

            return requestedSize;
        }

        private static uint GetActiveExtractionSize(uint requestedSize)
        {
            long now = Environment.TickCount64;
            long last = Volatile.Read(ref _lastSpilloverCheckTick);
            if (now - last >= 2000 && Interlocked.CompareExchange(ref _lastSpilloverCheckTick, now, last) == last)
            {
                ActiveSpilloverMap.Clear();
            }

            return ActiveSpilloverMap.GetOrAdd(requestedSize, static s =>
            {
                uint resolved = ResolveSpilloverSize(s);
                if (resolved != s)
                {
                    WriteLine($"Spillover active: thumbcache_{s}.db reached 1.6 GB soft ceiling; routing new extractions to thumbcache_{resolved}.db", LoggingFrequency.PreloaderLogging);
                }
                return resolved;
            });
        }

        public static void ResetCounters()
        {
            Interlocked.Exchange(ref CachedHitsCount, 0);
            Interlocked.Exchange(ref ExtractedCount, 0);
            Interlocked.Exchange(ref _lastSpilloverCheckTick, 0);
            ActiveSpilloverMap.Clear();
            if (SharedParentPidls.Count > MaxSharedParentPidls)
            {
                SharedParentPidls.Clear();
            }
        }

        /// <summary>
        /// Releases all per-thread cached COM objects on the current worker thread.
        /// </summary>
        public static void CleanupCurrentThreadComObjects()
        {
            try
            {
                if (ThreadLocalParentItems.IsValueCreated && ThreadLocalParentItems.Value is { } state)
                {
                    state.LastPath = null;
                    state.LastItem = null;
                    state.EvictionOrder.Clear();

                    if (state.Items.Count > 0)
                    {
                        foreach (var kvp in state.Items)
                        {
                            try
                            {
                                if (kvp.Value != null && Marshal.IsComObject(kvp.Value))
                                    Marshal.ReleaseComObject(kvp.Value);
                            }
                            catch { }
                        }
                        state.Items.Clear();
                    }
                }

                if (ThreadLocalBaseItem.IsValueCreated)
                {
                    var (_, baseItem) = ThreadLocalBaseItem.Value;
                    if (baseItem != null && Marshal.IsComObject(baseItem))
                    {
                        try { Marshal.ReleaseComObject(baseItem); } catch { }
                    }
                    ThreadLocalBaseItem.Value = (null, null);
                }

                if (ThreadLocalCache.IsValueCreated && ThreadLocalCache.Value is { } cache)
                {
                    if (Marshal.IsComObject(cache))
                    {
                        try { Marshal.ReleaseComObject(cache); } catch { }
                    }
                    ThreadLocalCache.Value = null;
                }
            }
            catch
            {
                // Ignore cleanup errors on thread exit
            }
        }

        /// <summary>
        /// High-performance V3 entry point using a ScannedItem with direct parent directory IShellItem caching
        /// and network share fallback via IShellItemImageFactory.
        /// </summary>
        public static void PreloadThumbnail(in ScannedItem item, uint[] sizes)
        {
            if (string.IsNullOrWhiteSpace(item.FullPath) || sizes == null || sizes.Length == 0)
                return;

            // If the item is itself a directory, reuse/populate the cached directory IShellItem directly
            if (item.IsDirectory)
            {
                IShellItem? dirItem = GetOrCreateParentShellItem(item.FullPath, item.ParentDirectory, item.RelativeName);
                if (dirItem != null)
                {
                    ExtractSizesForShellItem(dirItem, item.FullPath, sizes, item.IsNetworkPath);
                    return;
                }
            }

            IShellItem? shellItem = null;
            int hrCreate = 0;

            if (!string.IsNullOrWhiteSpace(item.ParentDirectory) && !string.IsNullOrWhiteSpace(item.RelativeName))
            {
                IShellItem? parentItem = GetOrCreateParentShellItem(item.ParentDirectory);
                if (parentItem != null)
                {
                    TryCreateShellItemFromRelativeName(parentItem, item.RelativeName, out shellItem, out hrCreate);
                }
            }

            // Fallback to full parsing name if relative resolution didn't succeed
            if (shellItem == null)
            {
                if (!TryCreateShellItem(item.FullPath, out shellItem, out hrCreate) || shellItem == null)
                {
                    if (currentLoggingFrequency == LoggingFrequency.DebugLogging)
                    {
                        string msg = Marshal.GetExceptionForHR(hrCreate)?.Message ?? "Unknown COM error";
                        WriteLine($"SHCreateItemFromParsingName failed (0x{hrCreate:X8}) for '{item.FullPath}': {msg}", LoggingFrequency.DebugLogging);
                    }
                    return;
                }
            }

            try
            {
                ExtractSizesForShellItem(shellItem, item.FullPath, sizes, item.IsNetworkPath);
            }
            finally
            {
                if (shellItem != null)
                    Marshal.ReleaseComObject(shellItem);
            }
        }

        /// <summary>
        /// Preloads thumbnails for a given file path and a set of sizes.
        /// </summary>
        public static void PreloadThumbnail(string filePath, uint[] sizes)
        {
            if (string.IsNullOrWhiteSpace(filePath) || sizes == null || sizes.Length == 0)
                return;

            string? resolved = NetworkPathResolver.NormalizeAndResolvePath(filePath) ?? filePath;
            bool isNetwork = NetworkPathResolver.IsNetworkOrMappedPath(resolved);

            if (!TryCreateShellItem(resolved, out IShellItem? shellItem, out int hrCreate) || shellItem == null)
            {
                if (currentLoggingFrequency == LoggingFrequency.DebugLogging)
                {
                    string msg = Marshal.GetExceptionForHR(hrCreate)?.Message ?? "Unknown COM error";
                    WriteLine($"SHCreateItemFromParsingName failed (0x{hrCreate:X8}) for '{resolved}': {msg}", LoggingFrequency.DebugLogging);
                }
                return;
            }

            try
            {
                ExtractSizesForShellItem(shellItem, resolved, sizes, isNetwork);
            }
            finally
            {
                Marshal.ReleaseComObject(shellItem);
            }
        }

        /// <summary>
        /// Preloads thumbnails using a base folder path and a relative file name.
        /// </summary>
        public static void PreloadThumbnail(string basePath, string relativePath, uint[] sizes)
        {
            if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(relativePath) || sizes == null || sizes.Length == 0)
                return;

            bool isNetwork = NetworkPathResolver.IsNetworkOrMappedPath(basePath);
            IShellItem? baseItem = GetOrCreateBaseShellItem(basePath);
            if (baseItem == null)
                return;

            if (!TryCreateShellItemFromRelativeName(baseItem, relativePath, out IShellItem? shellItem, out int hrCreate) || shellItem == null)
            {
                if (currentLoggingFrequency == LoggingFrequency.DebugLogging)
                {
                    string msg = Marshal.GetExceptionForHR(hrCreate)?.Message ?? "Unknown COM error";
                    WriteLine($"SHCreateItemFromRelativeName failed (0x{hrCreate:X8}) for '{relativePath}': {msg}", LoggingFrequency.DebugLogging);
                }
                return;
            }

            try
            {
                ExtractSizesForShellItem(shellItem, relativePath, sizes, isNetwork);
            }
            finally
            {
                Marshal.ReleaseComObject(shellItem);
            }
        }

        private static void ExtractSizesForShellItem(IShellItem shellItem, string displayPath, uint[] sizes, bool isNetworkPath)
        {
            IThumbnailCache? tbCache = GetOrCreateThreadLocalCache();

            foreach (uint size in sizes)
            {
                if (size == 0)
                    continue;

                bool succeeded = false;

                if (tbCache != null)
                {
                    // 1. Fast check if high-quality thumbnail is already in Windows Thumbnail Cache
                    int hrCheck = tbCache.GetThumbnail(
                        shellItem,
                        size,
                        WTS_FLAGS.WTS_INCACHEONLY,
                        IntPtr.Zero,
                        out WTS_CACHEFLAGS outFlags,
                        IntPtr.Zero);

                    if (hrCheck >= 0 &&
                        (outFlags & WTS_CACHEFLAGS.WTS_CACHED) != 0 &&
                        (outFlags & WTS_CACHEFLAGS.WTS_LOWQUALITY) == 0)
                    {
                        Interlocked.Increment(ref CachedHitsCount);
                        continue;
                    }

                    // 2. Resolve target bucket (spills over e.g. 256 -> 768 if thumbcache_256.db >= 1.6 GB)
                    uint extractSize = GetActiveExtractionSize(size);

                    // 3. Try fast in-proc extraction first with WTS_SLOWRECLAIM (high retention in thumbcache_idx.db);
                    // if in-proc fails, fall back to out-of-proc WTS_EXTRACT | WTS_SLOWRECLAIM
                    int hrInProc = tbCache.GetThumbnail(
                        shellItem,
                        extractSize,
                        WTS_FLAGS.WTS_EXTRACTINPROC | WTS_FLAGS.WTS_SLOWRECLAIM,
                        IntPtr.Zero,
                        out _,
                        IntPtr.Zero);

                    if (hrInProc >= 0)
                    {
                        succeeded = true;
                        Interlocked.Increment(ref ExtractedCount);
                    }
                    else if (hrInProc == E_ACCESSDENIED || hrInProc == RPC_E_DISCONNECTED)
                    {
                        break;
                    }

                    if (!succeeded)
                    {
                        int hrExtract = tbCache.GetThumbnail(
                            shellItem,
                            extractSize,
                            WTS_FLAGS.WTS_EXTRACT | WTS_FLAGS.WTS_SLOWRECLAIM,
                            IntPtr.Zero,
                            out _,
                            IntPtr.Zero);

                        if (hrExtract >= 0)
                        {
                            succeeded = true;
                            Interlocked.Increment(ref ExtractedCount);
                        }
                        else if (hrExtract == E_ACCESSDENIED)
                        {
                            break;
                        }
                    }
                }

                // 4. Fallback via IShellItemImageFactory (critical for network shares \\server\share, mapped drives, and out-of-proc handlers)
                if (!succeeded)
                {
                    uint fallbackSize = GetActiveExtractionSize(size);
                    if (TryExtractViaShellItemImageFactory(shellItem, fallbackSize, isNetworkPath, out bool wasCacheHit))
                    {
                        if (wasCacheHit)
                            Interlocked.Increment(ref CachedHitsCount);
                        else
                            Interlocked.Increment(ref ExtractedCount);
                    }
                    else if (currentLoggingFrequency == LoggingFrequency.DebugLogging)
                    {
                        WriteLine($"GetThumbnail failed on path '{displayPath}' for size {size}", LoggingFrequency.DebugLogging);
                    }
                }
            }
        }

        private static bool TryExtractViaShellItemImageFactory(IShellItem shellItem, uint size, bool isNetworkPath, out bool wasCacheHit)
        {
            wasCacheHit = false;
            if (shellItem is not IShellItemImageFactory imageFactory)
                return false;

            var requestedSize = new SIZE((int)size, (int)size);

            // Check cache first via SIIGBF_INCACHEONLY | SIIGBF_THUMBNAILONLY
            int hrCache = imageFactory.GetImage(
                requestedSize,
                SIIGBF.SIIGBF_INCACHEONLY | SIIGBF.SIIGBF_THUMBNAILONLY,
                out IntPtr hbmCached);

            if (hbmCached != IntPtr.Zero)
            {
                DeleteObject(hbmCached);
                if (hrCache >= 0)
                {
                    wasCacheHit = true;
                    return true;
                }
            }

            // Extract and populate Explorer / network folder cache
            SIIGBF extractFlags = isNetworkPath
                ? SIIGBF.SIIGBF_RESIZETOFIT
                : SIIGBF.SIIGBF_THUMBNAILONLY;

            int hr = imageFactory.GetImage(requestedSize, extractFlags, out IntPtr hbm);
            if (hbm != IntPtr.Zero)
            {
                DeleteObject(hbm);
                if (hr >= 0)
                    return true;
            }

            if (!isNetworkPath)
            {
                hr = imageFactory.GetImage(requestedSize, SIIGBF.SIIGBF_RESIZETOFIT, out hbm);
                if (hbm != IntPtr.Zero)
                {
                    DeleteObject(hbm);
                    if (hr >= 0)
                        return true;
                }
            }

            return false;
        }

        private static bool TryCreateShellItem(string filePath, out IShellItem? shellItem, out int hr)
        {
            shellItem = null;
            hr = 0;

            if (SharedParentPidls.TryGetValue(filePath, out byte[]? cachedPidl) &&
                SHCreateItemFromIDList(cachedPidl, IID_IShellItem, out shellItem) >= 0 &&
                shellItem != null)
            {
                return true;
            }

            lock (ShellParseLock)
            {
                if (!_shellWarmupDone)
                {
                    _shellWarmupDone = true;
                    string warmupPath = Environment.SystemDirectory;
                    if (!string.IsNullOrWhiteSpace(warmupPath) &&
                        SHCreateItemFromParsingName(warmupPath, IntPtr.Zero, IID_IShellItem, out IShellItem? warmupItem) >= 0 &&
                        warmupItem != null)
                    {
                        try { Marshal.ReleaseComObject(warmupItem); } catch { }
                    }
                }

                if (SharedParentPidls.TryGetValue(filePath, out cachedPidl) &&
                    SHCreateItemFromIDList(cachedPidl, IID_IShellItem, out shellItem) >= 0 &&
                    shellItem != null)
                {
                    return true;
                }

                hr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, IID_IShellItem, out shellItem);
                if (hr >= 0 && shellItem != null)
                {
                    TryCacheSharedPidl(filePath, shellItem);
                }
            }
            return hr >= 0 && shellItem != null;
        }

        private static void TryCacheSharedPidl(string path, IShellItem item)
        {
            if (SharedParentPidls.ContainsKey(path))
                return;

            if (SHGetIDListFromObject(item, out IntPtr pidl) >= 0 && pidl != IntPtr.Zero)
            {
                try
                {
                    uint size = ILGetSize(pidl);
                    if (size > 0 && size <= 65536)
                    {
                        byte[] bytes = new byte[size];
                        Marshal.Copy(pidl, bytes, 0, (int)size);
                        SharedParentPidls.TryAdd(path, bytes);
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }

        private static bool TryCreateShellItemFromRelativeName(IShellItem baseItem, string relativePath, out IShellItem? shellItem, out int hr)
        {
            shellItem = null;
            hr = SHCreateItemFromRelativeName(baseItem, relativePath, IntPtr.Zero, IID_IShellItem, out shellItem);
            return hr >= 0 && shellItem != null;
        }

        private static IShellItem? GetOrCreateParentShellItem(
            string parentPath,
            string? grandParentPath = null,
            string? relativeFolderName = null,
            int depth = 0)
        {
            var state = ThreadLocalParentItems.Value;
            if (state == null)
                return null;

            // 1. Fast 1-slot MRU check for consecutive files in the same folder
            if (state.LastItem != null && string.Equals(state.LastPath, parentPath, StringComparison.OrdinalIgnoreCase))
                return state.LastItem;

            // 2. Per-thread dictionary lookup
            if (state.Items.TryGetValue(parentPath, out IShellItem? existing) && existing != null)
            {
                state.LastPath = parentPath;
                state.LastItem = existing;
                return existing;
            }

            IShellItem? newItem = null;
            int hr = 0;

            // 3. Cross-thread shared PIDL lookup (zero lock, zero filesystem parse)
            if (SharedParentPidls.TryGetValue(parentPath, out byte[]? pidlBytes))
            {
                hr = SHCreateItemFromIDList(pidlBytes, IID_IShellItem, out newItem);
            }

            // 4. Hierarchical relative creation from parent directory (avoids full path parsing per subfolder)
            if (newItem == null && depth < 2)
            {
                grandParentPath ??= Path.GetDirectoryName(parentPath);
                relativeFolderName ??= Path.GetFileName(parentPath);

                if (!string.IsNullOrWhiteSpace(grandParentPath) &&
                    !string.IsNullOrWhiteSpace(relativeFolderName) &&
                    !string.Equals(grandParentPath, parentPath, StringComparison.OrdinalIgnoreCase) &&
                    (depth == 0 || state.Items.ContainsKey(grandParentPath) || SharedParentPidls.ContainsKey(grandParentPath)))
                {
                    IShellItem? grandParentItem = GetOrCreateParentShellItem(grandParentPath, null, null, depth + 1);
                    if (grandParentItem != null &&
                        TryCreateShellItemFromRelativeName(grandParentItem, relativeFolderName, out newItem, out hr) &&
                        newItem != null)
                    {
                        TryCacheSharedPidl(parentPath, newItem);
                    }
                }
            }

            // 5. Fallback to full path parse (with double-checked SharedParentPidls inside ShellParseLock)
            if (newItem == null)
            {
                if (!TryCreateShellItem(parentPath, out newItem, out hr) || newItem == null)
                {
                    if (currentLoggingFrequency == LoggingFrequency.DebugLogging)
                    {
                        string msg = Marshal.GetExceptionForHR(hr)?.Message ?? "Unknown COM error";
                        WriteLine($"SHCreateItemFromParsingName failed for parent folder (0x{hr:X8}) '{parentPath}': {msg}", LoggingFrequency.DebugLogging);
                    }
                    return null;
                }
            }

            CacheParentItemInThreadState(state, parentPath, newItem);
            return newItem;
        }

        private static void CacheParentItemInThreadState(ThreadParentCache state, string path, IShellItem item)
        {
            while (state.Items.Count >= MaxCachedParentDirsPerThread && state.EvictionOrder.TryDequeue(out string? oldestKey))
            {
                if (state.Items.Remove(oldestKey, out IShellItem? oldestVal))
                {
                    if (ReferenceEquals(state.LastItem, oldestVal))
                    {
                        state.LastPath = null;
                        state.LastItem = null;
                    }
                    try
                    {
                        if (oldestVal != null && Marshal.IsComObject(oldestVal))
                            Marshal.ReleaseComObject(oldestVal);
                    }
                    catch { }
                }
            }

            if (state.Items.TryAdd(path, item))
            {
                state.EvictionOrder.Enqueue(path);
            }
            else
            {
                state.Items[path] = item;
            }

            state.LastPath = path;
            state.LastItem = item;
        }

        private static IShellItem? GetOrCreateBaseShellItem(string basePath)
        {
            var (currentPath, currentItem) = ThreadLocalBaseItem.Value;

            if (string.Equals(currentPath, basePath, StringComparison.OrdinalIgnoreCase) && currentItem != null)
                return currentItem;

            if (currentItem != null)
            {
                try { Marshal.ReleaseComObject(currentItem); } catch { }
            }

            if (!TryCreateShellItem(basePath, out IShellItem? newItem, out int hr))
            {
                if (currentLoggingFrequency == LoggingFrequency.DebugLogging)
                {
                    string msg = Marshal.GetExceptionForHR(hr)?.Message ?? "Unknown COM error";
                    WriteLine($"SHCreateItemFromParsingName failed for base path (0x{hr:X8}) '{basePath}': {msg}", LoggingFrequency.DebugLogging);
                }
                ThreadLocalBaseItem.Value = (basePath, null);
                return null;
            }

            ThreadLocalBaseItem.Value = (basePath, newItem);
            return newItem;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(
            [In][MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            [In] IntPtr pbc,
            [In][MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out][MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] out IShellItem? ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromRelativeName(
            [In][MarshalAs(UnmanagedType.Interface)] IShellItem psiParent,
            [In][MarshalAs(UnmanagedType.LPWStr)] string pszName,
            [In] IntPtr pbc,
            [In][MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out][MarshalAs(UnmanagedType.Interface, IidParameterIndex = 3)] out IShellItem? ppv);

        [DllImport("shell32.dll")]
        private static extern int SHGetIDListFromObject(
            [In, MarshalAs(UnmanagedType.IUnknown)] object punk,
            out IntPtr ppidl);

        [DllImport("shell32.dll")]
        private static extern uint ILGetSize(IntPtr pidl);

        [DllImport("shell32.dll")]
        private static extern int SHCreateItemFromIDList(
            [In] byte[] pidl,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface, IidParameterIndex = 1)] out IShellItem? ppv);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("F676C15D-596A-4ce2-8234-33996F445DB1")]
        private interface IThumbnailCache
        {
            [PreserveSig]
            int GetThumbnail(
                [In] IShellItem pShellItem,
                [In] uint cxyRequestedThumbSize,
                [In] WTS_FLAGS flags,
                [In] IntPtr ppvThumb,
                [Out] out WTS_CACHEFLAGS pOutFlags,
                [In] IntPtr pThumbnailID
            );
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(
                [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
                [In] SIIGBF flags,
                [Out] out IntPtr phbm);
        }

        [Flags]
        private enum SIIGBF : int
        {
            SIIGBF_RESIZETOFIT = 0x00000000,
            SIIGBF_BIGGERSIZEOK = 0x00000001,
            SIIGBF_MEMORYONLY = 0x00000002,
            SIIGBF_ICONONLY = 0x00000004,
            SIIGBF_THUMBNAILONLY = 0x00000008,
            SIIGBF_INCACHEONLY = 0x00000010,
            SIIGBF_CROPTOSQUARE = 0x00000020,
            SIIGBF_WIDETHUMBNAILS = 0x00000040,
            SIIGBF_ICONBACKGROUND = 0x00000080,
            SIIGBF_SCALEUP = 0x00000100
        }

        [Flags]
        private enum WTS_FLAGS : uint
        {
            WTS_EXTRACT = 0x00000000,
            WTS_INCACHEONLY = 0x00000001,
            WTS_FASTEXTRACT = 0x00000002,
            WTS_FORCEEXTRACTION = 0x00000004,
            WTS_SLOWRECLAIM = 0x00000008,
            WTS_EXTRACTDONOTCACHE = 0x00000020,
            WTS_SCALETOREQUESTEDSIZE = 0x00000040,
            WTS_SKIPFASTEXTRACT = 0x00000080,
            WTS_EXTRACTINPROC = 0x00000100
        }

        [Flags]
        private enum WTS_CACHEFLAGS : uint
        {
            WTS_DEFAULT = 0x00000000,
            WTS_LOWQUALITY = 0x00000001,
            WTS_CACHED = 0x00000002
        }

        [InlineArray(16)]
        private struct Byte16
        {
            private byte _element0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_THUMBNAILID
        {
            public Byte16 rgbKey;
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        public interface IShellItem
        {
            void BindToHandler(
                IntPtr pbc,
                [MarshalAs(UnmanagedType.LPStruct)] Guid bhid,
                [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                out IntPtr ppv);

            void GetParent(out IShellItem ppsi);

            void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);

            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        public enum SIGDN : uint
        {
            NORMALDISPLAY = 0,
            PARENTRELATIVEPARSING = 0x80018001,
            PARENTRELATIVEFORADDRESSBAR = 0x8001c001,
            DESKTOPABSOLUTEPARSING = 0x80028000,
            PARENTRELATIVEEDITING = 0x80031001,
            DESKTOPABSOLUTEEDITING = 0x8004c000,
            FILESYSPATH = 0x80058000,
            URL = 0x80068000
        }

        [ComImport]
        [Guid("091162a4-bc96-411f-aae8-c5122cd03363")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ISharedBitmap
        {
            uint Detach([Out] out IntPtr phbm);
            uint GetFormat([Out] out WTS_ALPHATYPE pat);
            uint GetSharedBitmap([Out] out IntPtr phbm);
            uint GetSize([Out, MarshalAs(UnmanagedType.Struct)] out SIZE pSize);
            uint InitializeBitmap([In] IntPtr hbm, [In] WTS_ALPHATYPE wtsAT);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;

            public SIZE(int cx, int cy)
            {
                this.cx = cx;
                this.cy = cy;
            }
        }

        public enum WTS_ALPHATYPE : uint
        {
            WTSAT_UNKNOWN = 0,
            WTSAT_RGB = 1,
            WTSAT_ARGB = 2
        }
    }
}
