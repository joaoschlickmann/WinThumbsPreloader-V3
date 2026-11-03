using System.Text.Json;
using WinThumbsPreloader.Forms;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    public sealed class BulkPresetTests
    {
        [Fact]
        public void BulkPreset_DefaultValues_MatchExpectedV3Defaults()
        {
            // Arrange & Act
            var preset = new BulkPreloaderForm.BulkPreset();

            // Assert
            Assert.Equal(string.Empty, preset.Name);
            Assert.NotNull(preset.Paths);
            Assert.Empty(preset.Paths);
            Assert.True(preset.Recursive);
            Assert.True(preset.MultiThreaded);
            Assert.Equal(0, preset.Threads);
            Assert.Equal(0, preset.MaximumInstances);
            Assert.Equal("Sequentially", preset.Method);
        }

        [Fact]
        public void BulkPreset_JsonRoundTrip_PreservesAllPropertiesIncludingUncAndLocalPaths()
        {
            // Arrange
            var original = new BulkPreloaderForm.BulkPreset
            {
                Name = "NAS & Local Photo Libraries",
                Paths = [@"D:\Photos\2026", @"\\nas\Media\Wallpapers", @"Z:\Exports"],
                Recursive = true,
                MultiThreaded = true,
                Threads = 12,
                MaximumInstances = 4,
                Method = "Simultaneously"
            };

            // Act
            string json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<BulkPreloaderForm.BulkPreset>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Name, deserialized!.Name);
            Assert.Equal(original.Paths, deserialized.Paths);
            Assert.Equal(original.Recursive, deserialized.Recursive);
            Assert.Equal(original.MultiThreaded, deserialized.MultiThreaded);
            Assert.Equal(original.Threads, deserialized.Threads);
            Assert.Equal(original.MaximumInstances, deserialized.MaximumInstances);
            Assert.Equal(original.Method, deserialized.Method);
        }
    }
}
