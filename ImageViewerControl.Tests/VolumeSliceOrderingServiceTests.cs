using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class VolumeSliceOrderingServiceTests
    {
        [Fact]
        public void OrderPaths_UsesNumericNaturalOrderForSliceNames()
        {
            var ordered = VolumeSliceOrderingService.OrderPaths([
                @"C:\scan\slice_10.png",
                @"C:\scan\slice_2.png",
                @"C:\scan\slice_1.png"]);

            Assert.Equal(
                [
                    @"C:\scan\slice_1.png",
                    @"C:\scan\slice_2.png",
                    @"C:\scan\slice_10.png"],
                ordered);
        }

        [Fact]
        public void HasOrderChanged_DetectsReorderingWithoutChangingEqualCase()
        {
            Assert.True(VolumeSliceOrderingService.HasOrderChanged(
                ["slice_1.png", "slice_10.png", "slice_2.png"],
                ["slice_1.png", "slice_2.png", "slice_10.png"]));
            Assert.False(VolumeSliceOrderingService.HasOrderChanged(
                ["slice_1.png", "slice_2.png"],
                ["SLICE_1.PNG", "slice_2.png"]));
        }

        [Fact]
        public void OrderPaths_RejectsDuplicatePathsInsteadOfSilentlyDroppingOne()
        {
            Assert.Throws<ArgumentException>(() => VolumeSliceOrderingService.OrderPaths([
                "slice_1.png",
                "slice_1.png"]));
        }

        [Fact]
        public void OrderDescriptors_PrefersPhysicalPositionOverFilename()
        {
            VolumeSliceOrderingResult result = VolumeSliceOrderingService.OrderDescriptors([
                new VolumeSliceDescriptor("slice_1.png", Position: 2.0),
                new VolumeSliceDescriptor("slice_2.png", Position: -1.0),
                new VolumeSliceDescriptor("slice_10.png", Position: 0.5)]);

            Assert.Equal(VolumeSliceOrderSource.ExplicitPosition, result.Source);
            Assert.True(result.IsAuthoritative);
            Assert.Equal(["slice_2.png", "slice_10.png", "slice_1.png"], result.OrderedPaths);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public async Task OrderPathsWithMetadataAsync_UsesSliceSidecars()
        {
            string root = Path.Combine(Path.GetTempPath(), $"image-viewer-order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                string first = Path.Combine(root, "slice_10.png");
                string second = Path.Combine(root, "slice_2.png");
                await File.WriteAllTextAsync(first + ".slice.json", "{\"zPosition\": 10}");
                await File.WriteAllTextAsync(second + ".slice.json", "{\"zPosition\": 2}");

                VolumeSliceOrderingResult result = await VolumeSliceOrderingService.OrderPathsWithMetadataAsync([first, second]);

                Assert.Equal(VolumeSliceOrderSource.ExplicitPosition, result.Source);
                Assert.Equal([second, first], result.OrderedPaths);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void OrderDescriptors_WarnsWhenFallingBackToFilename()
        {
            VolumeSliceOrderingResult result = VolumeSliceOrderingService.OrderDescriptors([
                new VolumeSliceDescriptor("slice_10.png"),
                new VolumeSliceDescriptor("slice_2.png")]);

            Assert.Equal(VolumeSliceOrderSource.NaturalFileName, result.Source);
            Assert.False(result.IsAuthoritative);
            Assert.Contains(result.Warnings, warning => warning.Contains("元数据"));
        }
    }
}
