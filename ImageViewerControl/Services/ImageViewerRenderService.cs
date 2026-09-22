using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ImageViewer.Abstractions;

namespace ImageViewer.Services
{
    public sealed class ImageViewerRenderService : IImageViewerRenderService, IDisposable, IAsyncDisposable
    {
        private readonly ImageViewerTileRenderService _tileRenderService = new();

        public ImageSource? BuildDisplaySource(ImageSource? source, PseudoColorPalette palette)
        {
            return ImageViewerDisplaySourceService.BuildDisplaySource(source, palette);
        }

        public void ApplyGpuCaching(Canvas imageContainer, bool enableGpuRendering)
        {
            ImageViewerDisplaySourceService.ApplyGpuCaching(imageContainer, enableGpuRendering);
        }

        public void ClearTileCache()
        {
            _tileRenderService.ClearTileCache();
        }

        public void Dispose() => _tileRenderService.Dispose();

        public ValueTask DisposeAsync() => _tileRenderService.DisposeAsync();

        public BitmapSource? GetAnalysisBitmap(ImageSource? source)
        {
            return ImageViewerDisplaySourceService.GetAnalysisBitmap(source);
        }

        public Effect? CreatePseudoColorEffect(PseudoColorPalette palette)
        {
            return ImageViewerDisplaySourceService.CreatePseudoColorEffect(palette);
        }

        public Task<IReadOnlyList<ImagePyramidLevel>> BuildPyramidAsync(BitmapSource? source, CancellationToken cancellationToken)
        {
            return ImageViewerPyramidService.BuildAsync(source, cancellationToken);
        }

        public ImageViewerRenderFrame BuildRenderFrame(
            BitmapSource? source,
            IReadOnlyList<ImagePyramidLevel>? pyramid,
            Size viewport,
            double scale,
            Point translation,
            PseudoColorPalette palette,
            bool enableTiledRendering,
            bool autoSelectPyramidLevel,
            bool prefetchAdjacentTiles,
            int tileCacheMaximumMegabytes,
            int tilePrefetchRadius)
        {
            if (source == null)
            {
                return new ImageViewerRenderFrame(null, 0, 0, 0, 0, 1.0, false);
            }

            BitmapSource workingSource = source.IsFrozen ? source : Freeze(source);
            return _tileRenderService.BuildFrame(
                workingSource,
                pyramid ?? [],
                viewport,
                scale,
                translation,
                enableTiledRendering,
                autoSelectPyramidLevel,
                prefetchAdjacentTiles,
                tileCacheMaximumMegabytes,
                tilePrefetchRadius);
        }

        public Task<int[]?> CreateHistogramAsync(BitmapSource? source, int binCount, CancellationToken cancellationToken)
        {
            return ImageViewerAnalysisBitmapService.CreateHistogramAsync(source, binCount, cancellationToken);
        }

        public Task<byte[]?> CreateProfileAsync(ImageViewerAnalysisRequest request, CancellationToken cancellationToken)
        {
            return ImageViewerAnalysisBitmapService.CreateProfileAsync(request, cancellationToken);
        }

        private static BitmapSource Freeze(BitmapSource source)
        {
            BitmapSource clone = source.Clone();
            if (clone.CanFreeze)
            {
                clone.Freeze();
            }

            return clone;
        }
    }
}
