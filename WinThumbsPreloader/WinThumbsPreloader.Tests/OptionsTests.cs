using System;
using System.IO;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    public sealed class OptionsTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _tempSubDir;
        private readonly string _tempFile;

        public OptionsTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_OptionsTests_" + Guid.NewGuid().ToString("N"));
            _tempSubDir = Path.Combine(_tempDir, "SubFolder With Spaces");
            Directory.CreateDirectory(_tempSubDir);
            _tempFile = Path.Combine(_tempDir, "sample.jpg");
            File.WriteAllBytes(_tempFile, [0xFF, 0xD8, 0xFF, 0xD9]);
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

        [Fact]
        public void Constructor_WithEmptyArguments_MarksBadOrNoArguments()
        {
            // Arrange & Act
            var options = new Options([]);

            // Assert
            Assert.True(options.badOrNoArguments);
            Assert.Empty(options.paths);
        }

        [Fact]
        public void Constructor_WithWhitespaceOnlyArgument_MarksBadOrNoArguments()
        {
            // Arrange & Act
            var options = new Options(["   "]);

            // Assert
            Assert.True(options.badOrNoArguments);
            Assert.Empty(options.paths);
        }

        [Fact]
        public void Constructor_WithStartMinimizedFlag_SetsStartMinimizedAndStopsParsing()
        {
            // Arrange & Act
            var options = new Options(["-startminimized"]);

            // Assert
            Assert.True(options.startMinimized);
        }

        [Fact]
        public void Constructor_WithReopenSettingsFlag_SetsReopenSettingsAndStopsParsing()
        {
            // Arrange & Act
            var options = new Options(["-reopensettings"]);

            // Assert
            Assert.True(options.reopenSettings);
        }

        [Fact]
        public void Constructor_WithRecursiveSilentAndMultithreadedFlags_ParsesAllFlagsAndPath()
        {
            // Arrange & Act
            var options = new Options(["-r", "-s", "-m:8", "-maxinstances:3", _tempDir]);

            // Assert
            Assert.False(options.badOrNoArguments);
            Assert.True(options.includeNestedDirectories);
            Assert.True(options.silentMode);
            Assert.True(options.multiThreaded);
            Assert.Equal(8, options.threadCount);
            Assert.Equal(3, options.maxParallelInstances);
            Assert.Single(options.paths);
            Assert.Equal(Path.GetFullPath(_tempDir), options.paths[0]);
        }

        [Theory]
        [InlineData("-m", -1)]
        [InlineData("-m:0", 0)]
        [InlineData("-m:16", 16)]
        [InlineData("-m4", 4)]
        [InlineData("-m12", 12)]
        public void Constructor_WithMultithreadedVariants_SetsExpectedThreadCount(string flag, int expectedThreads)
        {
            // Arrange & Act
            var options = new Options([flag, _tempDir]);

            // Assert
            Assert.False(options.badOrNoArguments);
            Assert.True(options.multiThreaded);
            Assert.Equal(expectedThreads, options.threadCount);
        }

        [Fact]
        public void Constructor_WithMultiplePathsAndSpaces_NormalizesAllPaths()
        {
            // Arrange & Act
            var options = new Options([$"\"{_tempDir}\"", $"\"{_tempSubDir}\"", _tempFile]);

            // Assert
            Assert.False(options.badOrNoArguments);
            Assert.Equal(3, options.paths.Count);
            Assert.Contains(Path.GetFullPath(_tempDir), options.paths);
            Assert.Contains(Path.GetFullPath(_tempSubDir), options.paths);
            Assert.Contains(Path.GetFullPath(_tempFile), options.paths);
        }

        [Fact]
        public void Constructor_WithLegacyCommaSeparatedPaths_SplitsAndNormalizesEachPath()
        {
            // Arrange
            string combined = $"{_tempDir},{_tempSubDir}";

            // Act
            var options = new Options([combined]);

            // Assert
            Assert.False(options.badOrNoArguments);
            Assert.Equal(2, options.paths.Count);
            Assert.Equal(Path.GetFullPath(_tempDir), options.paths[0]);
            Assert.Equal(Path.GetFullPath(_tempSubDir), options.paths[1]);
        }

        [Fact]
        public void Constructor_WithDirectoryContainingCommaInName_PreservesSinglePathWithoutSplitting()
        {
            // Arrange
            string dirWithComma = Path.Combine(_tempDir, "Photos, 2026");
            Directory.CreateDirectory(dirWithComma);

            // Act
            var options = new Options([dirWithComma]);

            // Assert
            Assert.False(options.badOrNoArguments);
            Assert.Single(options.paths);
            Assert.Equal(Path.GetFullPath(dirWithComma), options.paths[0]);
        }

        [Fact]
        public void Constructor_WithNonExistentPath_SetsInvalidPathProvidedAndBadArguments()
        {
            // Arrange
            string missingPath = Path.Combine(_tempDir, "DoesNotExist_12345");

            // Act
            var options = new Options(["-r", missingPath]);

            // Assert
            Assert.True(options.badOrNoArguments);
            Assert.True(options.invalidPathProvided);
            Assert.Equal(missingPath, options.invalidPath);
        }

        [Fact]
        public void Constructor_WithOnlyFlagsAndNoPaths_MarksBadOrNoArguments()
        {
            // Arrange & Act
            var options = new Options(["-r", "-s", "-m:4"]);

            // Assert
            Assert.True(options.badOrNoArguments);
            Assert.Empty(options.paths);
        }
    }
}
