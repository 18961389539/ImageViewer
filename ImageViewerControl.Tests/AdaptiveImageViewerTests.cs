using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Controls;
using ImageViewer.Models;
using Xunit;
using ImageViewerControl2D = ImageViewer.Controls.ImageViewer;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Wpf")]
    public class AdaptiveImageViewerTests
    {
        [Fact]
        public void AutoMode_SelectsTwoDimensionalViewForImage()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer();
                viewer.ImageSource = CreateBitmap(9);

                Assert.IsType<ImageViewerControl2D>(viewer.ActiveView);
            });
        }

        [Fact]
        public void AutoMode_SelectsThreeDimensionalViewForVolume()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer
                {
                    Volume = new VolumeData([CreateBitmap(9), CreateBitmap(18)])
                };

                Assert.IsType<Volume3DViewer>(viewer.ActiveView);
            });
        }

        [Fact]
        public void ExplicitMode_CanSwitchVolumeBetween3DAndAxialSlice()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer
                {
                    Volume = new VolumeData([CreateBitmap(9), CreateBitmap(18)]),
                    DisplayMode = AdaptiveDisplayMode.AxialSlice
                };

                Assert.IsType<VolumeViewer>(viewer.ActiveView);
                viewer.DisplayMode = AdaptiveDisplayMode.ThreeDimensional;
                Assert.IsType<Volume3DViewer>(viewer.ActiveView);
            });
        }

        [Fact]
        public void SelectingAxialSlice_SynchronizesThreeDimensionalPlane()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer
                {
                    Volume = new VolumeData([CreateBitmap(9), CreateBitmap(18), CreateBitmap(27)]),
                    DisplayMode = AdaptiveDisplayMode.AxialSlice
                };

                viewer.VolumeViewer.SelectSlice(2);

                Assert.Equal(2, viewer.Volume3DViewer.CurrentSliceIndex);
            });
        }

        [Fact]
        public void SteppingCoronalSlice_UpdatesThreeDimensionalPlane()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer
                {
                    Volume = new VolumeData([CreateBitmap(9), CreateBitmap(18), CreateBitmap(27)])
                };

                viewer.DisplayMode = AdaptiveDisplayMode.Coronal;
                viewer.StepMprSlice(1);

                Assert.Equal(1, viewer.Volume3DViewer.CurrentCoronalSliceIndex);
            });
        }

        [Fact]
        public void SteppingSagittalSlice_UpdatesThreeDimensionalPlane()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer
                {
                    Volume = new VolumeData([CreateBitmap(9), CreateBitmap(18), CreateBitmap(27)])
                };

                viewer.DisplayMode = AdaptiveDisplayMode.Sagittal;
                viewer.StepMprSlice(1);

                Assert.Equal(1, viewer.Volume3DViewer.CurrentSagittalSliceIndex);
            });
        }

        private static BitmapSource CreateBitmap(byte value)
        {
            return BitmapSource.Create(
                2,
                2,
                96,
                96,
                PixelFormats.Gray8,
                null,
                new[] { value, value, value, value },
                2);
        }
    }
}
