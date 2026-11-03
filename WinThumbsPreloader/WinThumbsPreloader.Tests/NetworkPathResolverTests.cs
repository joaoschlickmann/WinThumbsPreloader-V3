using System;
using System.IO;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    public sealed class NetworkPathResolverTests : IDisposable
    {
        private readonly string _tempDir;

        public NetworkPathResolverTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_NetResolverTests_" + Guid.NewGuid().ToString("N"));
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

        [Theory]
        [InlineData(@"\\server\share", true)]
        [InlineData(@"\\192.168.1.50\Photos\2026", true)]
        [InlineData(@"C:\Users\Public", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsUncPath_DetectsUncPrefixCorrectly(string? input, bool expected)
        {
            // Act
            bool result = NetworkPathResolver.IsUncPath(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsNetworkOrMappedPath_WithUncPath_ReturnsTrue()
        {
            // Act & Assert
            Assert.True(NetworkPathResolver.IsNetworkOrMappedPath(@"\\nas-server\media\photos"));
        }

        [Fact]
        public void IsNetworkOrMappedPath_WithLocalSystemDrive_ReturnsFalse()
        {
            // Arrange
            string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);

            // Act & Assert
            Assert.False(NetworkPathResolver.IsNetworkOrMappedPath(systemDir));
        }

        [Fact]
        public void NormalizeAndResolvePath_WithBareDriveLetter_AppendsBackslashAndResolvesDriveRoot()
        {
            // Arrange:Regression test for bare drive syntax "C:" -> "C:\"
            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'); // e.g. "C:"

            // Act
            string? resolved = NetworkPathResolver.NormalizeAndResolvePath(systemDrive);

            // Assert
            Assert.NotNull(resolved);
            Assert.Equal(systemDrive + @"\", resolved, ignoreCase: true);
        }

        [Fact]
        public void NormalizeAndResolvePath_WithForwardSlashesAndQuotes_NormalizesToCanonicalWindowsPath()
        {
            // Arrange
            string messyPath = $"\"{_tempDir.Replace('\\', '/')}\"";

            // Act
            string? resolved = NetworkPathResolver.NormalizeAndResolvePath(messyPath);

            // Assert
            Assert.Equal(Path.GetFullPath(_tempDir), resolved);
        }

        [Fact]
        public void NormalizeAndResolvePath_WithEnvironmentVariables_ExpandsAndResolvesExistingDirectory()
        {
            // Arrange (TDD test: %TEMP% should expand and resolve to the user's temp directory)
            string envPath = "%TEMP%";

            // Act
            string? resolved = NetworkPathResolver.NormalizeAndResolvePath(envPath);

            // Assert
            Assert.NotNull(resolved);
            Assert.True(Directory.Exists(resolved));
        }

        [Fact]
        public void NormalizeAndResolvePath_WithLoopbackUncAdminShareOrLocalhost_HandlesGracefully()
        {
            // Arrange: Non-existent UNC share must return null without throwing
            string fakeUnc = @"\\127.0.0.1\NonExistentShare_" + Guid.NewGuid().ToString("N");

            // Act
            string? resolved = NetworkPathResolver.NormalizeAndResolvePath(fakeUnc);

            // Assert
            Assert.Null(resolved);
        }

        [Fact]
        public void GetUserMappedDrives_ExecutesWithoutThrowing()
        {
            // Act
            var mappedDrives = NetworkPathResolver.GetUserMappedDrives();

            // Assert
            Assert.NotNull(mappedDrives);
        }
    }
}
