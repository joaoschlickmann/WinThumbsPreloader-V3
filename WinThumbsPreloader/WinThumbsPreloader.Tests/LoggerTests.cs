using System;
using System.IO;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    public sealed class LoggerTests : IDisposable
    {
        private readonly string _tempDir;

        public LoggerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_LoggerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
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
        public void GetLogFolderStatusInfo_WithEmptyString_ReturnsNotSelected()
        {
            // Act
            var status = Logger.GetLogFolderStatusInfo("");

            // Assert
            Assert.Equal(Logger.LogFolderStatus.NotSelected, status.Status);
            Assert.False(status.CanUseForLogging);
        }

        [Fact]
        public void GetLogFolderStatusInfo_WithWritableTempFolder_ReturnsWritableAndCanUseForLogging()
        {
            // Act
            var status = Logger.GetLogFolderStatusInfo(_tempDir);

            // Assert
            Assert.True(status.CanUseForLogging);
            Assert.Equal(Path.GetFullPath(_tempDir), status.NormalizedPath);
        }

        [Fact]
        public void GetLogFolderStatusInfo_WithNonExistentChildUnderWritableParent_ReturnsWillBeCreated()
        {
            // Arrange
            string newChild = Path.Combine(_tempDir, "NewLogsSubfolder");

            // Act
            var status = Logger.GetLogFolderStatusInfo(newChild);

            // Assert
            Assert.True(
                status.Status == Logger.LogFolderStatus.WillBeCreated ||
                status.Status == Logger.LogFolderStatus.WillBeCreatedAdmin);
            Assert.True(status.CanUseForLogging);
        }

        [Fact]
        public void GetRecommendedDefaultLogPath_ReturnsValidRootedPath()
        {
            // Act
            string path = Logger.GetRecommendedDefaultLogPath();

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.True(Path.IsPathRooted(path));
        }
    }
}
