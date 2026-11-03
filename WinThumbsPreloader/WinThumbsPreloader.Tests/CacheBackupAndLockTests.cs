using System;
using System.IO;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    [Collection("NonParallelIntegration")]
    public sealed class CacheBackupAndLockTests : IDisposable
    {
        private readonly string _tempDir;

        public CacheBackupAndLockTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_BackupTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            CacheBackupLock.Release();
        }

        public void Dispose()
        {
            CacheBackupLock.Release();
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        [Fact]
        public void CacheBackupLock_TryAcquire_WhenNotHeld_AcquiresAndPreventsReacquisitionUntilReleased()
        {
            // Act 1: First acquisition succeeds
            var firstResult = CacheBackupLock.TryAcquire(_tempDir, out Exception firstEx);

            // Assert 1
            Assert.Equal(CacheBackupLock.AcquireResult.Acquired, firstResult);
            Assert.Null(firstEx);

            // Act 2: Second acquisition in same process returns AlreadyHeldByThisProcess
            var secondResult = CacheBackupLock.TryAcquire(_tempDir, out _);
            Assert.Equal(CacheBackupLock.AcquireResult.AlreadyHeldByThisProcess, secondResult);

            // Act 3: Release and re-acquire succeeds
            CacheBackupLock.Release();
            var thirdResult = CacheBackupLock.TryAcquire(_tempDir, out _);
            Assert.Equal(CacheBackupLock.AcquireResult.Acquired, thirdResult);
        }

        [Fact]
        public void CacheBackupLock_TryAcquire_WithEmptyPath_ReturnsInvalidPath()
        {
            // Act
            var result = CacheBackupLock.TryAcquire("", out Exception ex);

            // Assert
            Assert.Equal(CacheBackupLock.AcquireResult.InvalidPath, result);
            Assert.NotNull(ex);
        }

        [Fact]
        public void GetBackupFolderStatusInfo_WithExplorerThumbcacheFolder_ReturnsInvalidPath()
        {
            // Arrange: Using the actual Explorer cache directory as a backup target must be rejected
            string explorerCacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Explorer");

            // Act
            var status = CacheForm.GetBackupFolderStatusInfo(explorerCacheDir);

            // Assert
            Assert.Equal(CacheForm.BackupFolderStatus.InvalidPath, status.Status);
            Assert.False(status.CanUseForBackup);
        }

        [Fact]
        public void GetBackupFolderStatusInfo_WithWritableTempDirectory_ReturnsWritable()
        {
            // Act
            var status = CacheForm.GetBackupFolderStatusInfo(_tempDir);

            // Assert
            Assert.True(status.CanUseForBackup);
            Assert.Equal(Path.GetFullPath(_tempDir), status.NormalizedPath);
        }

        [Fact]
        public void GetLastBackupDateTimeForFolder_WhenInfoFileAndDbFileExist_ReturnsSavedTimestamp()
        {
            // Arrange
            File.WriteAllBytes(Path.Combine(_tempDir, "thumbcache_256.db"), [1, 2, 3, 4]);
            var expectedTime = new DateTimeOffset(2026, 10, 29, 12, 30, 0, TimeSpan.Zero);
            File.WriteAllText(
                Path.Combine(_tempDir, ".WinThumbsPreloader_BackupInfo.txt"),
                expectedTime.ToString("O"));

            // Act
            DateTimeOffset? actual = CacheForm.GetLastBackupDateTimeForFolder(_tempDir);

            // Assert
            Assert.NotNull(actual);
            Assert.Equal(expectedTime.ToLocalTime(), actual!.Value);
        }

        [Fact]
        public void GetLastBackupDateTimeForFolder_WhenOnlyDbFilesExist_InfersTimestampFromLastWriteTime()
        {
            // Arrange
            string dbFile = Path.Combine(_tempDir, "thumbcache_96.db");
            File.WriteAllBytes(dbFile, [1, 2, 3, 4]);
            DateTime expectedUtc = new DateTime(2026, 5, 15, 8, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(dbFile, expectedUtc);

            // Act
            DateTimeOffset? actual = CacheForm.GetLastBackupDateTimeForFolder(_tempDir);

            // Assert
            Assert.NotNull(actual);
            Assert.Equal(new DateTimeOffset(expectedUtc, TimeSpan.Zero).ToLocalTime(), actual!.Value);
        }

        [Fact]
        public void CopyFileSharedRead_And_CopyFileToDestinationSharedReadWrite_SucceedWhileLockedForReadWrite()
        {
            // Arrange: Simulate thumbcache.dll holding both source and destination open with GENERIC_READ | GENERIC_WRITE and FILE_SHARE_READ | FILE_SHARE_WRITE
            string srcPath = Path.Combine(_tempDir, "thumbcache_256_src.db");
            string stagedPath = Path.Combine(_tempDir, "thumbcache_256_staged.db");
            string dstPath = Path.Combine(_tempDir, "thumbcache_256_dst.db");

            byte[] expectedPayload = [0x43, 0x4D, 0x4D, 0x4D, 0x20, 0x00, 0x00, 0x00, 1, 2, 3, 4, 5, 6, 7, 8];
            File.WriteAllBytes(srcPath, expectedPayload);
            File.WriteAllBytes(dstPath, [0x00, 0x01, 0x02]);

            using var srcHolder = new FileStream(srcPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var dstHolder = new FileStream(dstPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

            // Act
            CacheForm.CopyFileSharedRead(srcPath, stagedPath);
            CacheForm.CopyFileToDestinationSharedReadWrite(stagedPath, dstPath);

            // Assert
            Assert.Equal(expectedPayload, File.ReadAllBytes(stagedPath));

            byte[] dstActual = new byte[dstHolder.Length];
            dstHolder.Position = 0;
            int read = dstHolder.Read(dstActual, 0, dstActual.Length);
            Assert.Equal(expectedPayload.Length, read);
            Assert.Equal(expectedPayload, dstActual);
        }

        [Fact]
        public void SanitizeThumbCacheDirectory_RepairsStaleFreeOffsetTornIdxPointersAndUnreferencedBlocks()
        {
            // Arrange: Build a minimal synthetic thumbcache_256.db (stream index 4) with:
            // - Block 0 at 0x18 (size 0x38 = 56): active block (hash = 0x1122334455667788, referenced by slot 0)
            // - Block 1 at 0x50 (size 0x38 = 56): active block (hash = 0x99AABBCCDDEEFF00, unreferenced in idx -> should be reclaimed as free with valid CRC64)
            // - Block 2 at 0x88 (size 0x38 = 56): trailing free block (hash = 0, cbId = 0)
            // Header FirstFreeEntryOffset (offset 20) is intentionally stale (0x18 instead of 0x88).
            string stream256Path = Path.Combine(_tempDir, "thumbcache_256.db");
            byte[] streamBytes = new byte[24 + 0x38 * 3];
            "CMMM"u8.CopyTo(streamBytes.AsSpan(0, 4));
            BitConverter.TryWriteBytes(streamBytes.AsSpan(4, 4), 32u); // version
            BitConverter.TryWriteBytes(streamBytes.AsSpan(8, 4), 4u);  // stream 4 = 256
            BitConverter.TryWriteBytes(streamBytes.AsSpan(16, 4), 24u); // FirstEntryOffset = 0x18
            BitConverter.TryWriteBytes(streamBytes.AsSpan(20, 4), 24u); // Stale FirstFreeEntryOffset = 0x18 (points to active block!)

            static void WriteCmmmBlock(Span<byte> fileBuf, int offset, uint cbEntry, ulong hash, uint cbId)
            {
                Span<byte> blk = fileBuf.Slice(offset, (int)cbEntry);
                blk.Clear();
                "CMMM"u8.CopyTo(blk.Slice(0, 4));
                BitConverter.TryWriteBytes(blk.Slice(4, 4), cbEntry);
                BitConverter.TryWriteBytes(blk.Slice(8, 8), hash);
                BitConverter.TryWriteBytes(blk.Slice(16, 4), cbId);
                ulong hdrCrc = CacheForm.ComputeThumbCacheCrc64(blk.Slice(0, 0x30));
                BitConverter.TryWriteBytes(blk.Slice(0x30, 8), hdrCrc);
            }

            const ulong validHash = 0x1122334455667788UL;
            const ulong orphanHash = 0x99AABBCCDDEEFF00UL;
            const ulong tornSlotHash = 0xCAFEBABE12345678UL;

            WriteCmmmBlock(streamBytes, 0x18, 0x38, validHash, cbId: 16);
            WriteCmmmBlock(streamBytes, 0x50, 0x38, orphanHash, cbId: 16);
            WriteCmmmBlock(streamBytes, 0x88, 0x38, 0UL, cbId: 0);
            File.WriteAllBytes(stream256Path, streamBytes);

            // Build thumbcache_idx.db with 2 total slots:
            // Slot 0: hash = validHash, stream[4] = 0x18 (valid)
            // Slot 1: hash = tornSlotHash, stream[4] = 0x88 (points to free block -> torn pointer, must be cleared to 0xFFFFFFFF)
            string idxPath = Path.Combine(_tempDir, "thumbcache_idx.db");
            byte[] idxBytes = new byte[144 + 72 * 2];
            BitConverter.TryWriteBytes(idxBytes.AsSpan(0, 4), 0x2030000Cu);
            "IMMM"u8.CopyTo(idxBytes.AsSpan(4, 4));
            BitConverter.TryWriteBytes(idxBytes.AsSpan(8, 4), 32u);
            BitConverter.TryWriteBytes(idxBytes.AsSpan(20, 4), 99u); // stale UsedSlots
            BitConverter.TryWriteBytes(idxBytes.AsSpan(24, 4), 2u);  // TotalSlots = 2

            for (int i = 0; i < 2; i++)
            {
                int slotBase = 144 + i * 72;
                for (int s = 0; s < 14; s++)
                    BitConverter.TryWriteBytes(idxBytes.AsSpan(slotBase + 16 + s * 4, 4), uint.MaxValue);
            }

            // Slot 0 (valid pointer to 0x18 in stream 4)
            BitConverter.TryWriteBytes(idxBytes.AsSpan(144, 8), validHash);
            BitConverter.TryWriteBytes(idxBytes.AsSpan(144 + 16 + 4 * 4, 4), 0x18u);

            // Slot 1 (torn pointer to 0x88 in stream 4)
            BitConverter.TryWriteBytes(idxBytes.AsSpan(144 + 72, 8), tornSlotHash);
            BitConverter.TryWriteBytes(idxBytes.AsSpan(144 + 72 + 16 + 4 * 4, 4), 0x88u);

            File.WriteAllBytes(idxPath, idxBytes);

            // Act
            bool modified = CacheForm.SanitizeThumbCacheDirectory(_tempDir);

            // Assert
            Assert.True(modified);

            byte[] repairedStream = File.ReadAllBytes(stream256Path);
            // 1. Header FirstFreeEntryOffset (offset 20) updated from stale 0x18 to trailing free block 0x88
            Assert.Equal(0x88u, BitConverter.ToUInt32(repairedStream, 20));

            // 2. Unreferenced Block 1 at 0x50 marked as free (hash = 0, cbId = 0) with valid HeaderCrc64
            Assert.Equal(0UL, BitConverter.ToUInt64(repairedStream, 0x50 + 8));
            Assert.Equal(0u, BitConverter.ToUInt32(repairedStream, 0x50 + 16));
            ulong expectedFreeCrc = CacheForm.ComputeThumbCacheCrc64(repairedStream.AsSpan(0x50, 0x30));
            Assert.Equal(expectedFreeCrc, BitConverter.ToUInt64(repairedStream, 0x50 + 0x30));

            byte[] repairedIdx = File.ReadAllBytes(idxPath);
            // 3. UsedSlots = 2, StreamSize[4] = streamBytes.Length, StreamEntryCount[4] = 1
            Assert.Equal(2u, BitConverter.ToUInt32(repairedIdx, 20));
            Assert.Equal((uint)streamBytes.Length, BitConverter.ToUInt32(repairedIdx, 28 + 4 * 4));
            Assert.Equal(1u, BitConverter.ToUInt32(repairedIdx, 84 + 4 * 4));

            // 4. Slot 0 stream[4] preserved (0x18), Slot 1 torn stream[4] cleared to 0xFFFFFFFF
            Assert.Equal(0x18u, BitConverter.ToUInt32(repairedIdx, 144 + 16 + 4 * 4));
            Assert.Equal(uint.MaxValue, BitConverter.ToUInt32(repairedIdx, 144 + 72 + 16 + 4 * 4));
        }

        [Fact]
        public void SumDbFilesSize_IncludesOnlyThumbCacheDb_IgnoresIconCacheDb()
        {
            File.WriteAllBytes(Path.Combine(_tempDir, "thumbcache_256.db"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(_tempDir, "thumbcache_idx.db"), new byte[512]);
            File.WriteAllBytes(Path.Combine(_tempDir, "iconcache_256.db"), new byte[4096]);
            File.WriteAllBytes(Path.Combine(_tempDir, "iconcache_idx.db"), new byte[2048]);

            long total = CacheForm.SumDbFilesSize(_tempDir, out long maxSingle);

            Assert.Equal(1536L, total);
            Assert.Equal(1024L, maxSingle);
        }

        [Fact]
        public void RemoveLegacyIconCacheFilesFromBackup_DeletesIconCacheAndPreservesThumbCache()
        {
            string thumbFile = Path.Combine(_tempDir, "thumbcache_256.db");
            string iconFile = Path.Combine(_tempDir, "iconcache_256.db");
            File.WriteAllBytes(thumbFile, new byte[128]);
            File.WriteAllBytes(iconFile, new byte[128]);

            CacheForm.RemoveLegacyIconCacheFilesFromBackup(_tempDir);

            Assert.True(File.Exists(thumbFile));
            Assert.False(File.Exists(iconFile));
        }

        [Fact]
        public void GetLastBackupDateTimeForFolder_IgnoresFolderWithOnlyIconCacheFiles()
        {
            File.WriteAllBytes(Path.Combine(_tempDir, "iconcache_256.db"), new byte[128]);

            Assert.Null(CacheForm.GetLastBackupDateTimeForFolder(_tempDir));

            File.WriteAllBytes(Path.Combine(_tempDir, "thumbcache_256.db"), new byte[128]);

            Assert.NotNull(CacheForm.GetLastBackupDateTimeForFolder(_tempDir));
        }

        [Theory]
        [InlineData("thumbcache_16.db", true)]
        [InlineData("THUMBCACHE_IDX.DB", true)]
        [InlineData("thumbcache_sr.db", true)]
        [InlineData("iconcache_16.db", false)]
        [InlineData("iconcache_idx.db", false)]
        [InlineData("thumbcache_256.db.tmp", false)]
        [InlineData("custom.db", false)]
        public void IsThumbCacheDbFileName_MatchesOnlyThumbCacheDbFiles(string fileName, bool expected)
        {
            Assert.Equal(expected, CacheForm.IsThumbCacheDbFileName(fileName.AsSpan()));
        }

        [Fact]
        public void CacheBackupLock_IsHeldByThisProcess_ReflectsLockLifecycle()
        {
            Assert.False(CacheBackupLock.IsHeldByThisProcess);

            var result = CacheBackupLock.TryAcquire(_tempDir, out _);
            Assert.Equal(CacheBackupLock.AcquireResult.Acquired, result);
            Assert.True(CacheBackupLock.IsHeldByThisProcess);

            CacheBackupLock.Release();
            Assert.False(CacheBackupLock.IsHeldByThisProcess);
        }

        [Fact]
        public async System.Threading.Tasks.Task CheckForCacheReset_WhenResetRecognizedIsTrue_ReturnsEarlyAcrossRepeatedTicks()
        {
            bool originalResetRecognized = Properties.Settings.Default.ResetRecognized;
            string originalBackupPath = Properties.Settings.Default.BackupCacheFolderPath;
            try
            {
                // Arrange: Create a backup folder larger than current Explorer cache so a reset would otherwise be detected
                string fakeThumb = Path.Combine(_tempDir, "thumbcache_256.db");
                using (var fs = new FileStream(fakeThumb, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.SetLength(CacheForm.ExplorerCacheSize() + (10L * 1024 * 1024));
                }

                Properties.Settings.Default.BackupCacheFolderPath = _tempDir;
                Properties.Settings.Default.ResetRecognized = true;
                AboutForm.ResetCacheResetFlags();

                // Act: Call CheckForCacheReset multiple times (simulating 250ms timer ticks after recognizedLogged becomes true)
                await AboutForm.CheckForCacheReset(true, false);
                await AboutForm.CheckForCacheReset(true, false);
                await AboutForm.CheckForCacheReset(true, false);

                // Assert: ResetRecognized remains true and no dialog blocked execution
                Assert.True(Properties.Settings.Default.ResetRecognized);
                Assert.False(AboutForm.IsCacheResetCheckInProgress);
            }
            finally
            {
                Properties.Settings.Default.ResetRecognized = originalResetRecognized;
                Properties.Settings.Default.BackupCacheFolderPath = originalBackupPath;
                Properties.Settings.Default.Save();
                AboutForm.ResetCacheResetFlags();
            }
        }

        [Fact]
        public void CompareThumbsCacheSize_WhileBackupLockHeld_DoesNotClearResetRecognized()
        {
            bool originalResetRecognized = Properties.Settings.Default.ResetRecognized;
            string originalBackupPath = Properties.Settings.Default.BackupCacheFolderPath;
            try
            {
                // Arrange: Backup folder has 0 bytes (so ExplorerCacheSize >= BackupCacheSize is true),
                // but CacheBackupLock is held (simulating an in-progress restore/backup).
                Properties.Settings.Default.BackupCacheFolderPath = _tempDir;
                Properties.Settings.Default.ResetRecognized = true;

                var lockResult = CacheBackupLock.TryAcquire(_tempDir, out _);
                Assert.Equal(CacheBackupLock.AcquireResult.Acquired, lockResult);

                // Act: CompareThumbsCacheSize should see Explorer >= Backup, but must NOT clear ResetRecognized while lock is held
                bool isLargerOrEqual = CacheForm.CompareThumbsCacheSize();
                Assert.True(isLargerOrEqual);
                Assert.True(Properties.Settings.Default.ResetRecognized);

                // Once lock is released, CompareThumbsCacheSize clears ResetRecognized normally
                CacheBackupLock.Release();
                isLargerOrEqual = CacheForm.CompareThumbsCacheSize();
                Assert.True(isLargerOrEqual);
                Assert.False(Properties.Settings.Default.ResetRecognized);
            }
            finally
            {
                CacheBackupLock.Release();
                Properties.Settings.Default.ResetRecognized = originalResetRecognized;
                Properties.Settings.Default.BackupCacheFolderPath = originalBackupPath;
                Properties.Settings.Default.Save();
            }
        }
    }
}
