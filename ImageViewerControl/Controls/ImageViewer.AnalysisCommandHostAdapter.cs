using System;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerAnalysisCommandHostAdapter : IImageViewerAnalysisCommandHost
    {
        private readonly ImageViewerAnalysisCommandDependencies _dependencies;

        public ImageViewerAnalysisCommandHostAdapter(ImageViewerAnalysisCommandDependencies dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public bool EnableAsyncAnalysis
        {
            get => _dependencies.GetEnableAsyncAnalysis();
            set => _dependencies.SetEnableAsyncAnalysis(value);
        }

        public bool PauseRealtimeHistogram
        {
            get => _dependencies.GetPauseRealtimeHistogram();
            set => _dependencies.SetPauseRealtimeHistogram(value);
        }

        public bool PauseRealtimeProfile
        {
            get => _dependencies.GetPauseRealtimeProfile();
            set => _dependencies.SetPauseRealtimeProfile(value);
        }

        public bool EnableImagePyramid
        {
            get => _dependencies.GetEnableImagePyramid();
            set => _dependencies.SetEnableImagePyramid(value);
        }

        public bool AutoSelectPyramidLevel
        {
            get => _dependencies.GetAutoSelectPyramidLevel();
            set => _dependencies.SetAutoSelectPyramidLevel(value);
        }

        public bool EnableTiledRendering
        {
            get => _dependencies.GetEnableTiledRendering();
            set => _dependencies.SetEnableTiledRendering(value);
        }

        public bool PrefetchAdjacentTiles
        {
            get => _dependencies.GetPrefetchAdjacentTiles();
            set => _dependencies.SetPrefetchAdjacentTiles(value);
        }

        public int TileCacheMaximumMegabytes
        {
            get => _dependencies.GetTileCacheMaximumMegabytes();
            set => _dependencies.SetTileCacheMaximumMegabytes(value);
        }

        public int TilePrefetchRadius
        {
            get => _dependencies.GetTilePrefetchRadius();
            set => _dependencies.SetTilePrefetchRadius(value);
        }

        public bool EnableGpuRendering
        {
            get => _dependencies.GetEnableGpuRendering();
            set => _dependencies.SetEnableGpuRendering(value);
        }

        public bool PreferShaderPseudoColor
        {
            get => _dependencies.GetPreferShaderPseudoColor();
            set => _dependencies.SetPreferShaderPseudoColor(value);
        }

        public bool AllowCpuPseudoColorFallback
        {
            get => _dependencies.GetAllowCpuPseudoColorFallback();
            set => _dependencies.SetAllowCpuPseudoColorFallback(value);
        }

        public void UpdateRenderedImage() => _dependencies.UpdateRenderedImage();

        public void RefreshAnalysis() => _dependencies.RefreshAnalysis();

        public void ClearAnalysisCache() => _dependencies.ClearAnalysisCache();

        public void ResetPyramidToBaseLevel() => _dependencies.ResetPyramidToBaseLevel();

        public void RebuildPyramidIfNeeded() => _dependencies.RebuildPyramidIfNeeded();

        public void SetPseudoColorPalette(PseudoColorPalette palette) => _dependencies.SetPseudoColorPalette(palette);

        public void ShowSmartDisplaySuggestion() => _dependencies.ShowSmartDisplaySuggestion();

        public void ShowRenderStatus() => _dependencies.ShowRenderStatus();
    }
}