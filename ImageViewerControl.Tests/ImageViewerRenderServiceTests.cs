using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Unit")]
    [Trait("Category", "Wpf")]
    public class ImageViewerRenderServiceTests
    {
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
    }
}