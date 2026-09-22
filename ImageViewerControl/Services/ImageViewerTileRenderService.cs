using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ImageViewer.Services
{
    internal sealed class ImageViewerTileRenderService : IDisposable, IAsyncDisposable
    {
        private const long LargeImageThresholdPixels = 4_000_000;
        private const double TileMarginScreenPixels = 192;
        private readonly ImageViewerRenderTileCache _tileCache = new();

        public void ClearTileCache() => _tileCache.Clear();

        public void Dispose() => _tileCache.Dispose();

        public ValueTask DisposeAsync() => _tileCache.DisposeAsync();

        public ImageViewerRenderFrame BuildFrame(
            BitmapSource source,
            IReadOnlyList<ImagePyramidLevel> levels,
            Size viewport,
            double scale,
            Point translation,
            bool enableTiledRendering,
            bool autoSelectPyramidLevel,
            bool prefetchAdjacentTiles,
            int tileCacheMaximumMegabytes,
            int tilePrefetchRadius)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(levels);
            _tileCache.SetMaximumBytes(Math.Max(1, tileCacheMaximumMegabytes) * 1024L * 1024L);

            IReadOnlyList<ImagePyramidLevel> effectiveLevels = levels.Count > 0
                ? levels
                : [new ImagePyramidLevel(source, 1.0)];
            BitmapSource workingSource = effectiveLevels[0].Bitmap;
            ImagePyramidLevel level = SelectLevel(effectiveLevels, scale, autoSelectPyramidLevel);
            bool useTiledRendering = enableTiledRendering && IsLargeImage(workingSource) && IsValid(viewport);

            if (!useTiledRendering)
            {
                return new ImageViewerRenderFrame(workingSource, 0, 0, workingSource.PixelWidth, workingSource.PixelHeight, 1.0, false);
            }

            Rect visibleRegion = GetVisibleRegion(workingSource, viewport, scale, translation, prefetchAdjacentTiles);
            if (visibleRegion.IsEmpty)
            {
                return new ImageViewerRenderFrame(level.Bitmap, 0, 0, workingSource.PixelWidth, workingSource.PixelHeight, level.ScaleFactor, false);
            }

            Int32Rect sourceCrop = ToCropRect(level.Bitmap, visibleRegion, level.ScaleFactor);
            if (_tileCache.TryGet(level.Bitmap, sourceCrop, out BitmapSource? cachedFrame))
            {
                Prefetch(level.Bitmap, sourceCrop, prefetchAdjacentTiles, tilePrefetchRadius);
                return new ImageViewerRenderFrame(cachedFrame, visibleRegion.X, visibleRegion.Y, visibleRegion.Width, visibleRegion.Height, level.ScaleFactor, true);
            }

            Int32Rect cacheCrop = ImageViewerRenderTileCache.ExpandToTileGrid(sourceCrop, level.Bitmap.PixelWidth, level.Bitmap.PixelHeight);
            BitmapSource cachedTile = _tileCache.GetOrCreate(level.Bitmap, cacheCrop);
            Int32Rect cropWithinTile = new(sourceCrop.X - cacheCrop.X, sourceCrop.Y - cacheCrop.Y, sourceCrop.Width, sourceCrop.Height);
            BitmapSource tiledSource = cropWithinTile.X == 0 && cropWithinTile.Y == 0 && cropWithinTile.Width == cachedTile.PixelWidth && cropWithinTile.Height == cachedTile.PixelHeight
                ? cachedTile
                : new CroppedBitmap(cachedTile, cropWithinTile);

            if (tiledSource.CanFreeze)
            {
                tiledSource.Freeze();
            }

            _tileCache.Store(level.Bitmap, sourceCrop, tiledSource);
            Prefetch(level.Bitmap, cacheCrop, prefetchAdjacentTiles, tilePrefetchRadius);
            return new ImageViewerRenderFrame(tiledSource, visibleRegion.X, visibleRegion.Y, visibleRegion.Width, visibleRegion.Height, level.ScaleFactor, true);
        }

        private void Prefetch(BitmapSource source, Int32Rect rect, bool enabled, int radius)
        {
            int effectiveRadius = enabled ? Math.Max(0, radius) : 0;
            if (effectiveRadius > 0)
            {
                _tileCache.Prefetch(source, ImageViewerRenderTileCache.BuildPrefetchRects(rect, source.PixelWidth, source.PixelHeight, effectiveRadius));
            }
        }

        private static bool IsLargeImage(BitmapSource source) => (long)source.PixelWidth * source.PixelHeight >= LargeImageThresholdPixels;

        private static bool IsValid(Size size) => !double.IsNaN(size.Width) && !double.IsNaN(size.Height) && size.Width > 0 && size.Height > 0;

        private static ImagePyramidLevel SelectLevel(IReadOnlyList<ImagePyramidLevel> levels, double scale, bool autoSelectPyramidLevel)
        {
            if (!autoSelectPyramidLevel || levels.Count == 0)
            {
                return levels[0];
            }

            ImagePyramidLevel selected = levels[0];
            double target = Math.Clamp(scale, levels[^1].ScaleFactor, 1.0);
            double bestDistance = double.MaxValue;
            foreach (ImagePyramidLevel level in levels)
            {
                double distance = Math.Abs(Math.Log(level.ScaleFactor, 2) - Math.Log(target, 2));
                if (distance < bestDistance)
                {
                    selected = level;
                    bestDistance = distance;
                }
            }

            return selected;
        }

        private static Rect GetVisibleRegion(BitmapSource source, Size viewport, double scale, Point translation, bool prefetchAdjacentTiles)
        {
            if (!IsValid(viewport) || scale <= 0)
            {
                return new Rect(0, 0, source.PixelWidth, source.PixelHeight);
            }

            double left = Math.Max(0, -translation.X / scale);
            double top = Math.Max(0, -translation.Y / scale);
            double right = Math.Min(source.PixelWidth, (viewport.Width - translation.X) / scale);
            double bottom = Math.Min(source.PixelHeight, (viewport.Height - translation.Y) / scale);
            if (right <= left || bottom <= top)
            {
                return Rect.Empty;
            }

            double margin = (prefetchAdjacentTiles ? TileMarginScreenPixels : 32) / Math.Max(scale, 0.1);
            return ClampRect(new Rect(left - margin, top - margin, (right - left) + margin * 2, (bottom - top) + margin * 2), source.PixelWidth, source.PixelHeight);
        }

        private static Rect ClampRect(Rect rect, int maxWidth, int maxHeight)
        {
            double x = Math.Clamp(rect.X, 0, maxWidth);
            double y = Math.Clamp(rect.Y, 0, maxHeight);
            double right = Math.Clamp(rect.Right, x, maxWidth);
            double bottom = Math.Clamp(rect.Bottom, y, maxHeight);
            return new Rect(x, y, right - x, bottom - y);
        }

        private static Int32Rect ToCropRect(BitmapSource source, Rect visibleRegion, double scaleFactor)
        {
            int x = Math.Clamp((int)Math.Floor(visibleRegion.X * scaleFactor), 0, source.PixelWidth - 1);
            int y = Math.Clamp((int)Math.Floor(visibleRegion.Y * scaleFactor), 0, source.PixelHeight - 1);
            int width = Math.Max(1, Math.Min(source.PixelWidth - x, (int)Math.Ceiling(visibleRegion.Width * scaleFactor)));
            int height = Math.Max(1, Math.Min(source.PixelHeight - y, (int)Math.Ceiling(visibleRegion.Height * scaleFactor)));
            return new Int32Rect(x, y, width, height);
        }
    }
}