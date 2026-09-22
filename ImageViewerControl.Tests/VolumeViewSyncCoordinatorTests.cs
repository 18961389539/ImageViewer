using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Wpf")]
    public class VolumeViewSyncCoordinatorTests
    {
        [Fact]
        public void Coordinator_SelectsAxialSliceAndHandles3DTransition()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer
                {
                    Volume = new VolumeData([CreateBitmap(1), CreateBitmap(2), CreateBitmap(3)])
                };
                using var coordinator = new VolumeViewSyncCoordinator(viewer);

                coordinator.SelectAxialSlice(2);

                Assert.Equal(2, coordinator.CurrentSliceIndex);
                ContextMenu menu = WpfTestRunner.GetPrivateField<ContextMenu>(viewer.Volume3DViewer, "threeDContextMenu");
                menu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Switch to axial slice")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.IsType<VolumeViewer>(viewer.ActiveView);
            });
        }

        private static BitmapSource CreateBitmap(byte value)
        {
            return BitmapSource.Create(2, 2, 96, 96, PixelFormats.Gray8, null, new[] { value, value, value, value }, 2);
        }
    }
}
