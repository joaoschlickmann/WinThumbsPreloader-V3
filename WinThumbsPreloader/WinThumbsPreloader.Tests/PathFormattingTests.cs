using System;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    public sealed class PathFormattingTests
    {
        [Fact]
        public void ShortenPath_WhenPathFitsWithinMaxLength_ReturnsFullPathUnchanged()
        {
            // Arrange
            string path = @"C:\Photos\2026\img.jpg";

            // Act
            string shortened = ThumbnailsPreloader.ShortenPath(path, maxLength: 56);

            // Assert
            Assert.Equal(path, shortened);
        }

        [Fact]
        public void ShortenPath_WithDeepLocalPath_PreservesDriveRootAndTrailingSegmentsWithEllipsis()
        {
            // Arrange
            string deepPath = @"C:\Users\Administrator\Pictures\ Vacations \2026\Summer\Europe\Italy\Rome\Colosseum_Sunset_HDR_001.jpg";

            // Act
            string shortened = ThumbnailsPreloader.ShortenPath(deepPath, maxLength: 56);

            // Assert
            Assert.True(shortened.Length <= 56, $"Expected length <= 56, got {shortened.Length}: '{shortened}'");
            Assert.StartsWith(@"C:\...\", shortened);
            Assert.EndsWith("Colosseum_Sunset_HDR_001.jpg", shortened);
        }

        [Fact]
        public void ShortenPath_WithDeepUncNetworkPath_Regression_PreservesSeparatorAfterUncShareRoot()
        {
            // Arrange: Regression test for V3 UNC path shortening bug where Path.GetPathRoot("\\server\share\...")
            // returns "\\server\share" without a trailing backslash, previously producing "\\server\share...\file.jpg".
            string uncPath = @"\\nas-storage\MediaLibrary\Photography\Archive\2026\RawExports\Portrait_Session_042.tiff";

            // Act
            string shortened = ThumbnailsPreloader.ShortenPath(uncPath, maxLength: 56);

            // Assert
            Assert.True(shortened.Length <= 56, $"Expected length <= 56, got {shortened.Length}: '{shortened}'");
            Assert.StartsWith(@"\\nas-storage\MediaLibrary\...\", shortened);
            Assert.DoesNotContain(@"MediaLibrary...", shortened);
        }

        [Fact]
        public void ShortenPath_WhenFileNameAloneExceedsBudget_TruncatesToTrailingCharactersWithinMaxLength()
        {
            // Arrange
            string longFilePath = @"C:\Folder\VeryLongFileName_ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789_Final.png";

            // Act
            string shortened = ThumbnailsPreloader.ShortenPath(longFilePath, maxLength: 32);

            // Assert
            Assert.True(shortened.Length <= 32, $"Expected length <= 32, got {shortened.Length}: '{shortened}'");
            Assert.EndsWith("Final.png", shortened);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ShortenPath_WithEmptyOrWhitespace_ReturnsInputUnchanged(string input)
        {
            // Act & Assert
            Assert.Equal(input, ThumbnailsPreloader.ShortenPath(input, maxLength: 20));
        }
    }
}
