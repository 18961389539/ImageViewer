using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Controls;
using ImageViewer.Localization;
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

        [Fact]
        public void GestureHint_UsesModeSpecificConventionsFor2D_MprAnd3D()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer
                {
                    ImageSource = CreateBitmap(9),
                    Volume = new VolumeData([CreateBitmap(9), CreateBitmap(18)])
                };

                viewer.DisplayMode = AdaptiveDisplayMode.TwoDimensional;
                Assert.Equal(UiText.Get("AdaptiveGestureImage"), viewer.gestureHintText.Text);

                viewer.DisplayMode = AdaptiveDisplayMode.Coronal;
                Assert.Equal(UiText.Get("AdaptiveGestureVolume"), viewer.gestureHintText.Text);

                viewer.DisplayMode = AdaptiveDisplayMode.ThreeDimensional;
                Assert.Equal(UiText.Get("AdaptiveGesture3D"), viewer.gestureHintText.Text);
            });
        }

        [Fact]
        public void NestedImageViewer_EscapeCancelsDrawingBeforeAdaptiveHandler()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer();
                var window = new Window
                {
                    Width = 320,
                    Height = 240,
                    Content = viewer
                };

                try
                {
                    window.Show();
                    WpfTestRunner.DrainDispatcher();

                    var tool = viewer.ImageViewer.AvailableDrawingTools[0];
                    tool.Activate(viewer.ImageViewer);
                    Assert.False(string.IsNullOrWhiteSpace(viewer.ImageViewer.ActiveToolName));

                    viewer.ImageViewer.Focus();
                    RaiseKeyDown(viewer.ImageViewer, Key.Escape);
                    WpfTestRunner.DrainDispatcher();

                    Assert.True(string.IsNullOrEmpty(viewer.ImageViewer.ActiveToolName));
                }
                finally
                {
                    window.Close();
                    WpfTestRunner.DrainDispatcher();
                }
            });
        }

        [Fact]
        public void NestedImageViewer_ArrowKeyMovesSelectedRoiInMprView()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer
                {
                    Volume = new VolumeData([CreateBitmap(9), CreateBitmap(18)])
                };
                viewer.DisplayMode = AdaptiveDisplayMode.Coronal;
                viewer.ImageViewer.ImageSource = CreateBitmap(9);

                var roi = new LineMeasureRoi
                {
                    P1 = new PointD(2, 3),
                    P2 = new PointD(5, 3)
                };
                Assert.True(viewer.ImageViewer.AddRoi(roi));

                var window = new Window
                {
                    Width = 320,
                    Height = 240,
                    Content = viewer
                };

                try
                {
                    window.Show();
                    WpfTestRunner.DrainDispatcher();
                    viewer.ImageViewer.Focus();

                    RaiseKeyDown(viewer.ImageViewer, Key.Right);
                    WpfTestRunner.DrainDispatcher();

                    Assert.Equal(3, roi.P1.X);
                    Assert.Equal(6, roi.P2.X);
                    Assert.Equal(AdaptiveDisplayMode.Coronal, viewer.DisplayMode);
                }
                finally
                {
                    window.Close();
                    WpfTestRunner.DrainDispatcher();
                }
            });
        }

        [Fact]
        public void AdaptiveShortcuts_DoNotRunWhileTextInputHasFocus()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer
                {
                    Volume = new VolumeData([CreateBitmap(9), CreateBitmap(18)])
                };
                viewer.DisplayMode = AdaptiveDisplayMode.ThreeDimensional;

                var editor = new TextBox();
                var host = new Grid();
                host.Children.Add(viewer);
                host.Children.Add(editor);
                var window = new Window
                {
                    Width = 320,
                    Height = 240,
                    Content = host
                };

                try
                {
                    window.Show();
                    WpfTestRunner.DrainDispatcher();
                    editor.Focus();

                    KeyEventArgs args = RaiseKeyDown(viewer, Key.D1);

                    Assert.False(args.Handled);
                    Assert.Equal(AdaptiveDisplayMode.ThreeDimensional, viewer.DisplayMode);
                }
                finally
                {
                    window.Close();
                    WpfTestRunner.DrainDispatcher();
                }
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

        private static KeyEventArgs RaiseKeyDown(FrameworkElement source, Key key)
        {
            var args = new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(source),
                0,
                key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            source.RaiseEvent(args);
            return args;
        }
    }
}
