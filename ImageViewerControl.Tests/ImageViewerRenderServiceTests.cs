using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using ImageViewer.Services;
using ImageViewer.Models;
using ImageViewer.Rendering;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Unit")]
    [Trait("Category", "Wpf")]
    public class ImageViewerRenderServiceTests
    {
        [Fact]
        public void DualEdgeCaliperSummaryAnchor_FollowsDetectedSegmentMidpoint()
        {
            var caliper = new CaliperMeasureRoi
            {
                CaliperCenter = new PointD(100, 100),
                P1 = new PointD(10, 20),
                P2 = new PointD(30, 40),
                HasDetectedEdges = true
            };

            Point anchor = DualEdgeCaliperRenderHelper.GetSummaryAnchor(caliper);

            Assert.Equal(new Point(20, 30), anchor);
        }

        [Fact]
        public void DualEdgeCaliperSummaryAnchor_IsOffsetAboveMeasuredSegment()
        {
            WpfTestRunner.Run(() =>
            {
                var caliper = new CaliperMeasureRoi
                {
                    P1 = new PointD(10, 100),
                    P2 = new PointD(90, 100),
                    HasDetectedEdges = true
                };
                var context = new RoiRenderContext(
                    new Canvas(),
                    new Canvas(),
                    static point => point,
                    scale: 1,
                    pixelSize: 1,
                    physicalUnit: "px",
                    showCaliperScores: false,
                    handleSize: 8,
                    infoTextOffset: 20,
                    angleArcRadius: 20,
                    pointAnnotationSize: 10,
                    polygonResizeHandlePadding: 4,
                    polygonCloseHighlightPadding: 6);

                Point anchor = DualEdgeCaliperRenderHelper.GetSummaryAnchor(context, caliper);

                Assert.Equal(new Point(50, 80), anchor);
            });
        }

        [Fact]
        public void DualEdgeCaliperSummaryAnchor_UsesRoiCenterBeforeDetection()
        {
            var caliper = new CaliperMeasureRoi
            {
                CaliperCenter = new PointD(100, 100),
                P1 = new PointD(10, 20),
                P2 = new PointD(30, 40)
            };

            Point anchor = DualEdgeCaliperRenderHelper.GetSummaryAnchor(caliper);

            Assert.Equal(new Point(100, 100), anchor);
        }

        [Fact]
        public void BuildRenderFrame_ReusesCachedTileForRepeatedRequests()
        {
            WpfTestRunner.Run(() =>
            {
                var service = new ImageViewerRenderService();
                BitmapSource source = CreateLargeBitmap();
                var pyramid = new[] { new ImagePyramidLevel(source, 1.0) };

                ImageViewerRenderFrame first = service.BuildRenderFrame(
                    source,
                    pyramid,
                    new Size(512, 512),
                    1.0,
                    new Point(0, 0),
                    PseudoColorPalette.None,
                    enableTiledRendering: true,
                    autoSelectPyramidLevel: true,
                    prefetchAdjacentTiles: false,
                    tileCacheMaximumMegabytes: 64,
                    tilePrefetchRadius: 0);

                ImageViewerRenderFrame second = service.BuildRenderFrame(
                    source,
                    pyramid,
                    new Size(512, 512),
                    1.0,
                    new Point(0, 0),
                    PseudoColorPalette.None,
                    enableTiledRendering: true,
                    autoSelectPyramidLevel: true,
                    prefetchAdjacentTiles: false,
                    tileCacheMaximumMegabytes: 64,
                    tilePrefetchRadius: 0);

                service.ClearTileCache();

                ImageViewerRenderFrame third = service.BuildRenderFrame(
                    source,
                    pyramid,
                    new Size(512, 512),
                    1.0,
                    new Point(0, 0),
                    PseudoColorPalette.None,
                    enableTiledRendering: true,
                    autoSelectPyramidLevel: true,
                    prefetchAdjacentTiles: false,
                    tileCacheMaximumMegabytes: 64,
                    tilePrefetchRadius: 0);

                Assert.True(first.IsTiled);
                Assert.Same(first.Source, second.Source);
                Assert.NotSame(second.Source, third.Source);
            });
        }

        [Fact]
        public void BuildRenderFrame_WhenSourceIsNull_ReturnsEmptyFrame()
        {
            WpfTestRunner.Run(() =>
            {
                var service = new ImageViewerRenderService();

                ImageViewerRenderFrame frame = service.BuildRenderFrame(
                    null,
                    null,
                    new Size(512, 512),
                    1.0,
                    new Point(0, 0),
                    PseudoColorPalette.None,
                    enableTiledRendering: true,
                    autoSelectPyramidLevel: true,
                    prefetchAdjacentTiles: true,
                    tileCacheMaximumMegabytes: 64,
                    tilePrefetchRadius: 2);

                Assert.Null(frame.Source);
                Assert.False(frame.IsTiled);
            });
        }

        [Fact]
        public void BuildRenderFrame_WithNonFrozenSource_ReturnsUsableFrame()
        {
            WpfTestRunner.Run(() =>
            {
                var service = new ImageViewerRenderService();
                BitmapSource source = CreateLargeBitmap();
                Assert.False(source.IsFrozen);

                ImageViewerRenderFrame frame = service.BuildRenderFrame(
                    source,
                    [new ImagePyramidLevel(source, 1.0)],
                    new Size(512, 512),
                    1.0,
                    new Point(0, 0),
                    PseudoColorPalette.None,
                    enableTiledRendering: true,
                    autoSelectPyramidLevel: true,
                    prefetchAdjacentTiles: false,
                    tileCacheMaximumMegabytes: 64,
                    tilePrefetchRadius: 0);

                Assert.NotNull(frame.Source);
                Assert.True(frame.IsTiled);
            });
        }

        [Fact]
        public void BuildRenderFrame_TiledCropKeepsImageAndOverlayCoordinatesAlignedAtHighZoom()
        {
            WpfTestRunner.Run(() =>
            {
                const int edgeX = 120;
                const double zoom = 12;
                const double translationX = -1242.8;
                var service = new ImageViewerRenderService();
                BitmapSource source = CreateStepBitmap(2100, 2000, edgeX);

                ImageViewerRenderFrame frame = service.BuildRenderFrame(
                    source,
                    [new ImagePyramidLevel(source, 1.0)],
                    new Size(631, 1240),
                    zoom,
                    new Point(translationX, 0),
                    PseudoColorPalette.None,
                    enableTiledRendering: true,
                    autoSelectPyramidLevel: false,
                    prefetchAdjacentTiles: false,
                    tileCacheMaximumMegabytes: 128,
                    tilePrefetchRadius: 0);

                BitmapSource renderedTile = Assert.IsAssignableFrom<BitmapSource>(frame.Source);
                byte[] pixels = new byte[renderedTile.PixelWidth * renderedTile.PixelHeight];
                renderedTile.CopyPixels(pixels, renderedTile.PixelWidth, 0);
                int localEdgeX = Array.FindIndex(pixels, value => value == 48);
                Assert.True(localEdgeX >= 0);

                double imageEdgeScreenX = (frame.Left + localEdgeX * frame.Width / renderedTile.PixelWidth) * zoom + translationX;
                double overlayEdgeScreenX = edgeX * zoom + translationX;
                Assert.InRange(Math.Abs(imageEdgeScreenX - overlayEdgeScreenX), 0, 0.1);
            });
        }

        [Fact]
        public void RenderTileCache_SetSmallBudget_EvictsOlderEntries()
        {
            WpfTestRunner.Run(() =>
            {
                var cache = new ImageViewerRenderTileCache();
                BitmapSource source = CreateLargeBitmap();
                Int32Rect firstRect = new(0, 0, 512, 512);
                Int32Rect secondRect = new(512, 0, 512, 512);
                BitmapSource first = cache.GetOrCreate(source, firstRect);
                cache.SetMaximumBytes(512L * 512L + 1);
                cache.GetOrCreate(source, secondRect);

                Assert.False(cache.TryGet(source, firstRect, out _));
                Assert.True(cache.TryGet(source, secondRect, out BitmapSource? current));
                Assert.Same(current, cache.GetOrCreate(source, secondRect));
                Assert.NotSame(first, current);
            });
        }

        [Fact]
        public void RenderTileCache_PrefetchRects_RespectRadiusAndImageBounds()
        {
            IReadOnlyList<Int32Rect> none = ImageViewerRenderTileCache.BuildPrefetchRects(
                new Int32Rect(0, 0, 512, 512), 1024, 1024, 0);
            IReadOnlyList<Int32Rect> rects = ImageViewerRenderTileCache.BuildPrefetchRects(
                new Int32Rect(0, 0, 512, 512), 1024, 1024, 1);

            Assert.Empty(none);
            Assert.NotEmpty(rects);
            Assert.All(rects, rect =>
            {
                Assert.InRange(rect.X, 0, 1023);
                Assert.InRange(rect.Y, 0, 1023);
                Assert.True(rect.X + rect.Width <= 1024);
                Assert.True(rect.Y + rect.Height <= 1024);
            });
        }

        [Fact]
        public void RenderTileCache_Dispose_PreventsNewPrefetchWork()
        {
            WpfTestRunner.Run(() =>
            {
                using var cache = new ImageViewerRenderTileCache();
                BitmapSource source = CreateLargeBitmap();

                cache.Dispose();
                cache.Prefetch(source, [new Int32Rect(0, 0, 512, 512)]);

                Assert.False(cache.TryGet(source, new Int32Rect(0, 0, 512, 512), out _));
            });
        }

        [Fact]
        public void RenderTileCache_DisposeAsync_IsIdempotent()
        {
            WpfTestRunner.Run(() =>
            {
                var cache = new ImageViewerRenderTileCache();
                cache.Dispose();

                ValueTask first = cache.DisposeAsync();
                ValueTask second = cache.DisposeAsync();

                first.AsTask().GetAwaiter().GetResult();
                second.AsTask().GetAwaiter().GetResult();
                Assert.False(cache.TryGet(CreateLargeBitmap(), new Int32Rect(0, 0, 512, 512), out _));
            });
        }

        private static BitmapSource CreateLargeBitmap()
        {
            int width = 2001;
            int height = 2001;
            byte[] pixels = new byte[width * height];
            return BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Gray8,
                null,
                pixels,
                width);
        }

        private static BitmapSource CreateStepBitmap(int width, int height, int edgeX)
        {
            byte[] pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = (byte)(x < edgeX ? 255 : 48);
                }
            }

            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
        }
    }
}
