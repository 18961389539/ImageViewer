using System;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerAnalysisCommandHostAdapter : IImageViewerAnalysisCommandHost
    {
        private readonly ImageViewerAnalysisCommandDependencies _dependencies;
        private readonly ImageViewerRuntimeOptions _runtimeOptions;

        public ImageViewerAnalysisCommandHostAdapter(ImageViewerAnalysisCommandDependencies dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _runtimeOptions = dependencies.RuntimeOptions;
        }

        public bool EnableAsyncAnalysis
        {
            get => _runtimeOptions.EnableAsyncAnalysis;
            set => _runtimeOptions.EnableAsyncAnalysis = value;
        }

        public bool PauseRealtimeHistogram
        {
            get => _runtimeOptions.PauseRealtimeHistogram;
            set => _runtimeOptions.PauseRealtimeHistogram = value;
        }

        public bool PauseRealtimeProfile
        {
            get => _runtimeOptions.PauseRealtimeProfile;
            set => _runtimeOptions.PauseRealtimeProfile = value;
        }

        public bool EnableImagePyramid
        {
            get => _runtimeOptions.EnableImagePyramid;
            set => _runtimeOptions.EnableImagePyramid = value;
        }

        public bool AutoSelectPyramidLevel
        {
            get => _runtimeOptions.AutoSelectPyramidLevel;
            set => _runtimeOptions.AutoSelectPyramidLevel = value;
        }

        public bool EnableTiledRendering
        {
            get => _runtimeOptions.EnableTiledRendering;
            set => _runtimeOptions.EnableTiledRendering = value;
        }

        public bool PrefetchAdjacentTiles
        {
            get => _runtimeOptions.PrefetchAdjacentTiles;
            set => _runtimeOptions.PrefetchAdjacentTiles = value;
        }

        public int TileCacheMaximumMegabytes
        {
            get => _runtimeOptions.TileCacheMaximumMegabytes;
            set => _runtimeOptions.TileCacheMaximumMegabytes = value;
        }

        public int TilePrefetchRadius
        {
            get => _runtimeOptions.TilePrefetchRadius;
            set => _runtimeOptions.TilePrefetchRadius = value;
        }

        public bool EnableGpuRendering
        {
            get => _dependencies.GetEnableGpuRendering();
            set => _dependencies.SetEnableGpuRendering(value);
        }

        public bool PreferShaderPseudoColor
        {
            get => _runtimeOptions.PreferShaderPseudoColor;
            set => _runtimeOptions.PreferShaderPseudoColor = value;
        }

        public bool AllowCpuPseudoColorFallback
        {
            get => _runtimeOptions.AllowCpuPseudoColorFallback;
            set => _runtimeOptions.AllowCpuPseudoColorFallback = value;
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