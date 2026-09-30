using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ImageViewer.Abstractions;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;
using ImageViewer.Utils;

namespace ImageViewer.Controls
{
    internal sealed record ImageViewerAnalysisMenuState(
        bool EnableImagePyramidChecked,
        bool AutoSelectPyramidLevelChecked,
        bool AutoSelectPyramidLevelEnabled,
        bool EnableTiledRenderingChecked,
        bool PrefetchAdjacentTilesChecked,
        bool PrefetchAdjacentTilesEnabled,
        bool EnableAsyncAnalysisChecked,
        bool PauseRealtimeHistogramChecked,
        bool PauseRealtimeHistogramEnabled,
        bool PauseRealtimeProfileChecked,
        bool PauseRealtimeProfileEnabled,
        bool EnableRefreshAnalysis,
        bool PreferShaderPseudoColorChecked,
        bool AllowCpuPseudoColorFallbackChecked,
        bool EnableClearPyramidCache,
        bool EnableClearAnalysisCache,
        bool EnableShowRenderStatus,
        bool GpuRenderingChecked,
        bool PseudoColorNoneChecked,
        bool PseudoColorHotChecked,
        bool PseudoColorJetChecked,
        bool PseudoColorViridisChecked)
    {
        public static ImageViewerAnalysisMenuState Empty { get; } =
            new(
                EnableImagePyramidChecked: false,
                AutoSelectPyramidLevelChecked: false,
                AutoSelectPyramidLevelEnabled: false,
                EnableTiledRenderingChecked: false,
                PrefetchAdjacentTilesChecked: false,
                PrefetchAdjacentTilesEnabled: false,
                EnableAsyncAnalysisChecked: false,
                PauseRealtimeHistogramChecked: false,
                PauseRealtimeHistogramEnabled: false,
                PauseRealtimeProfileChecked: false,
                PauseRealtimeProfileEnabled: false,
                EnableRefreshAnalysis: false,
                PreferShaderPseudoColorChecked: false,
                AllowCpuPseudoColorFallbackChecked: false,
                EnableClearPyramidCache: false,
                EnableClearAnalysisCache: false,
                EnableShowRenderStatus: false,
                GpuRenderingChecked: false,
                PseudoColorNoneChecked: true,
                PseudoColorHotChecked: false,
                PseudoColorJetChecked: false,
                PseudoColorViridisChecked: false);
    }

    internal interface IImageViewerAnalysisHost
    {
        ImageViewerAnalysisState AnalysisState { get; }
        IImageViewerRenderService RenderService { get; }
        ImageSource? ImageSource { get; }
        bool EnableGpuRendering { get; }
        bool PreferShaderPseudoColor { get; }
        bool AllowCpuPseudoColorFallback { get; }
        PseudoColorPalette PseudoColorPalette { get; }
        bool EnableImagePyramid { get; }
        bool AutoSelectPyramidLevel { get; }
        bool EnableTiledRendering { get; }
        bool PrefetchAdjacentTiles { get; }
        int TileCacheMaximumMegabytes { get; }
        int TilePrefetchRadius { get; }
        double Scale { get; }
        Point Translation { get; }
        Size ViewportSize { get; }
        int HistogramBinCount { get; }
        bool ShowHistogram { get; }
        bool ShowProfile { get; }
        bool EnableAsyncAnalysis { get; }
        bool PauseRealtimeHistogram { get; }
        bool PauseRealtimeProfile { get; }
    }

    internal sealed class ImageViewerAnalysisCoordinator
    {
        internal const int HistogramBinCount = 256;

        private readonly IImageViewerAnalysisHost _host;
        private readonly IImageViewerAnalysisUiFacade _uiFacade;
        private readonly IImageViewerProfileTargetResolver _profileTargetResolver;
        private readonly IImageViewerAnalysisPipeline _analysisPipeline;
        internal readonly IImageViewerAnalysisErrorSink _errorSink;
        private ImageViewerBackgroundOperationObserver? _backgroundOperationObserver;
        private readonly TimeSpan _asyncAnalysisDebounce;

        public ImageViewerAnalysisCoordinator(
            IImageViewerAnalysisHost host,
            IImageViewerAnalysisUiFacade uiFacade,
            IImageViewerProfileTargetResolver profileTargetResolver,
            IImageViewerAnalysisErrorSink errorSink,
            IImageViewerAnalysisPipeline? analysisPipeline = null,
            TimeSpan? asyncAnalysisDebounce = null)
        {
            _host = host;
            _uiFacade = uiFacade;
            _profileTargetResolver = profileTargetResolver;
            _analysisPipeline = analysisPipeline ?? new ImageViewerAnalysisPipeline();
            _errorSink = errorSink;
            _asyncAnalysisDebounce = asyncAnalysisDebounce ?? TimeSpan.FromMilliseconds(120);
        }

        public void UpdateRenderedImage()
        {
            ImageViewerRenderedImagePlan renderPlan = BuildRenderedImagePlan();
            _uiFacade.ApplyRenderedImagePlan(renderPlan);
            _host.AnalysisState.IsShaderPseudoColorActive = renderPlan.IsShaderPseudoColorActive;
            _host.AnalysisState.LastRenderFrame = renderPlan.RenderFrame;
        }

        public void ClearRenderCache()
        {
            _host.RenderService.ClearTileCache();
        }

        private ImageViewerRenderedImagePlan BuildRenderedImagePlan()
        {
            Effect? pseudoColorEffect = _host.PreferShaderPseudoColor ? _host.RenderService.CreatePseudoColorEffect(_host.PseudoColorPalette) : null;
            PseudoColorPalette cpuPseudoColorPalette = pseudoColorEffect == null && _host.AllowCpuPseudoColorFallback ? _host.PseudoColorPalette : PseudoColorPalette.None;

            if (_host.AnalysisState.AnalysisBitmapSource == null)
            {
                double imageWidth = _host.ImageSource is BitmapSource bitmap ? bitmap.PixelWidth : _host.ImageSource?.Width ?? 0;
                double imageHeight = _host.ImageSource is BitmapSource bitmapSource ? bitmapSource.PixelHeight : _host.ImageSource?.Height ?? 0;
                ImageSource? source = pseudoColorEffect == null ? _host.RenderService.BuildDisplaySource(_host.ImageSource, cpuPseudoColorPalette) : _host.ImageSource;
                return new ImageViewerRenderedImagePlan(
                    new ImageViewerRenderFrame(source, 0, 0, imageWidth, imageHeight, 1.0, false),
                    pseudoColorEffect,
                    _host.EnableGpuRendering);
            }

            IReadOnlyList<ImagePyramidLevel> activePyramidLevels = _host.EnableImagePyramid
                ? _host.AnalysisState.PyramidLevels
                : [new ImagePyramidLevel(_host.AnalysisState.AnalysisBitmapSource, 1.0)];

            ImageViewerRenderFrame frame = _host.RenderService.BuildRenderFrame(
                _host.AnalysisState.AnalysisBitmapSource,
                activePyramidLevels,
                _host.ViewportSize,
                _host.Scale,
                _host.Translation,
                cpuPseudoColorPalette,
                _host.EnableTiledRendering,
                _host.AutoSelectPyramidLevel,
                _host.PrefetchAdjacentTiles,
                _host.TileCacheMaximumMegabytes,
                _host.TilePrefetchRadius);

            return new ImageViewerRenderedImagePlan(frame, pseudoColorEffect, _host.EnableGpuRendering);
        }

        public BitmapSource? GetAnalysisBitmapSource()
        {
            return _host.AnalysisState.AnalysisBitmapSource;
        }

        public async Task PrepareAnalysisResourcesAsync(ImageSource? source)
        {
            _host.RenderService.ClearTileCache();

            // ResetForSource cancels every in-flight request and bumps the source generation,
            // so nothing computed for the previous image can be committed afterwards.
            BitmapSource? analysisBitmap = _host.RenderService.GetAnalysisBitmap(source);
            _host.AnalysisState.ResetForSource(analysisBitmap);
            ClearAnalysisDisplays();
            UpdateRenderedImage();

            if (analysisBitmap == null || !_host.EnableImagePyramid)
            {
                await RefreshAnalysisDisplaysAsync();
                return;
            }

            ImageViewerAnalysisTicket ticket = _host.AnalysisState.BeginPyramidTicket();
            long generation = _host.AnalysisState.SourceGeneration;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                IReadOnlyList<ImagePyramidLevel> pyramidLevels = await _host.RenderService.BuildPyramidAsync(analysisBitmap, ticket.Token);
                if (!_host.AnalysisState.IsCurrentPyramidTicket(ticket))
                {
                    return;
                }

                if (!_host.AnalysisState.SetPyramidLevels(generation, pyramidLevels, stopwatch.Elapsed))
                {
                    return;
                }

                UpdateRenderedImage();
                await RefreshAnalysisDisplaysAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                if (_host.AnalysisState.IsCurrentPyramidTicket(ticket))
                {
                    _host.AnalysisState.ClearPyramidBuildWork();
                    ClearAnalysisDisplays();
                }

                throw;
            }
        }

        public void HandleAsyncAnalysisChanged()
        {
            _ = BackgroundOperationObserver.ObserveAsync(RefreshAnalysisDisplaysAsync(force: true), "Refresh analysis after async mode changed");
        }

        public void HandleRealtimeHistogramPauseChanged()
        {
            if (!_host.PauseRealtimeHistogram)
            {
                _ = BackgroundOperationObserver.ObserveAsync(UpdateHistogram(force: true), "Refresh histogram after pause changed");
            }
        }

        public void HandleRealtimeProfilePauseChanged()
        {
            if (!_host.PauseRealtimeProfile)
            {
                _ = BackgroundOperationObserver.ObserveAsync(UpdateProfile(force: true), "Refresh profile after pause changed");
            }
        }

        public void HandleRefreshAnalysisRequested()
        {
            _ = BackgroundOperationObserver.ObserveAsync(RefreshAnalysisDisplaysAsync(force: true), "Refresh analysis requested");
        }

        public void HandleClearAnalysisCacheRequested()
        {
            ClearAnalysisCaches();
            _ = BackgroundOperationObserver.ObserveAsync(RefreshAnalysisDisplaysAsync(force: true), "Refresh analysis after cache clear");
        }

        /// <summary>
        /// 调色板变化。
        /// Chinese: 只需要重绘图像。直方图与剖面的**计算**基于原始强度图（`request.Bitmap`），**绘制**用固定颜色
        /// （直方图半透明灰、剖面 Gray/Cyan），都与调色板无关——原先的两处刷新是纯浪费。
        /// 若将来柱状/曲线改成跟随调色板着色，正确做法是"重新呈现上一次的输出"，仍然不重算。
        /// English: Only the rendered image needs refreshing. Histogram/profile computation uses the raw intensity bitmap and
        /// their drawing uses fixed colors, so neither depends on the palette.
        /// </summary>
        public void HandlePseudoColorPaletteChanged()
        {
            UpdateRenderedImage();
        }

        public void HandleRenderingOptionChanged()
        {
            UpdateRenderedImage();
        }

        public void HandleHistogramVisibilityChanged(bool isVisible)
        {
            if (isVisible)
            {
                _uiFacade.SetHistogramPanelVisibility(true);
                _ = BackgroundOperationObserver.ObserveAsync(UpdateHistogram(), "Refresh histogram after visibility changed");
                return;
            }

            _host.AnalysisState.ClearHistogramWork();
            _uiFacade.PresentHistogram(null);
            _uiFacade.SetHistogramPanelVisibility(false);
        }

        public void HandleProfileVisibilityChanged(bool isVisible)
        {
            if (isVisible)
            {
                _uiFacade.SetProfilePanelVisibility(true);
                _ = BackgroundOperationObserver.ObserveAsync(UpdateProfile(), "Refresh profile after visibility changed");
                return;
            }

            _host.AnalysisState.ClearProfileWork();
            _uiFacade.PresentProfile(null);
            _uiFacade.SetProfilePanelVisibility(false);
        }

        public Task RefreshAnalysisDisplays(bool force = false)
        {
            return RefreshAnalysisDisplaysAsync(force);
        }

        private async Task RefreshAnalysisDisplaysAsync(bool force = false)
        {
            if (_host.ShowHistogram)
            {
                await UpdateHistogram(force);
            }

            if (_host.ShowProfile)
            {
                await UpdateProfile(force);
            }
        }

        public async Task UpdateHistogram(bool force = false)
        {
            if (!_host.ShowHistogram || _host.AnalysisState.AnalysisBitmapSource is not BitmapSource bitmap)
            {
                _host.AnalysisState.CancelHistogramWork();
                _uiFacade.PresentHistogram(null);
                return;
            }

            if (_host.PauseRealtimeHistogram && !force)
            {
                return;
            }

            // Registered after the visibility/pause gates so that a suppressed refresh does not spawn work,
            // but registered before anything is awaited so that any later refresh supersedes this one.
            ImageViewerAnalysisTicket ticket = _host.AnalysisState.BeginHistogramTicket();

            if (!_host.EnableAsyncAnalysis)
            {
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    int[] histogram = await _analysisPipeline.CreateHistogramAsync(bitmap, _host.HistogramBinCount, ticket.Token);
                    if (!_host.AnalysisState.IsCurrentHistogramTicket(ticket))
                    {
                        return;
                    }

                    _host.AnalysisState.LastHistogramDuration = stopwatch.Elapsed;
                    _uiFacade.PresentHistogram(new ImageViewerHistogramOutput(histogram, _host.HistogramBinCount));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _errorSink.LogNonCriticalError("Failed to update histogram", ex);
                    _uiFacade.PresentHistogramError(UiText.Get("StatusHistogramFailed"));
                }

                return;
            }

            try
            {
                await Task.Delay(_asyncAnalysisDebounce, ticket.Token);
                var stopwatch = Stopwatch.StartNew();
                int[]? histogram = await _host.RenderService.CreateHistogramAsync(bitmap, _host.HistogramBinCount, ticket.Token);
                if (histogram == null || !_host.AnalysisState.IsCurrentHistogramTicket(ticket))
                {
                    return;
                }

                _host.AnalysisState.LastHistogramDuration = stopwatch.Elapsed;
                _uiFacade.PresentHistogram(new ImageViewerHistogramOutput(histogram, _host.HistogramBinCount));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _errorSink.LogNonCriticalError("Failed to update histogram", ex);
                _uiFacade.PresentHistogramError(UiText.Get("StatusHistogramFailed"));
            }
        }

        public async Task UpdateProfile(bool force = false)
        {
            if (!_host.ShowProfile)
            {
                _host.AnalysisState.CancelProfileWork();
                return;
            }

            // Registered before the target-line lookup so that losing the line (or switching the async mode)
            // supersedes any request that is still in flight instead of racing with it.
            ImageViewerAnalysisTicket ticket = _host.AnalysisState.BeginProfileTicket();

            LineMeasureRoi? targetLine = _profileTargetResolver.GetProfileTargetLine();
            if (targetLine == null)
            {
                _uiFacade.PresentProfile(null);
                return;
            }

            if (_host.AnalysisState.AnalysisBitmapSource is not BitmapSource bitmap)
            {
                _uiFacade.PresentProfile(null);
                return;
            }

            if (_host.PauseRealtimeProfile && !force)
            {
                return;
            }

            if (!_host.EnableAsyncAnalysis)
            {
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    ushort[] profileData = await _analysisPipeline.CreateProfile16Async(
                        bitmap,
                        targetLine.P1.ToWpfPoint(),
                        targetLine.P2.ToWpfPoint(),
                        ticket.Token);
                    if (!_host.AnalysisState.IsCurrentProfileTicket(ticket))
                    {
                        return;
                    }

                    _host.AnalysisState.LastProfileDuration = stopwatch.Elapsed;
                    _uiFacade.PresentProfile(new ImageViewerProfileOutput(profileData, GetProfileMaximum(bitmap)));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _errorSink.LogNonCriticalError("Failed to update profile", ex);
                    _uiFacade.PresentProfileError(UiText.Get("StatusProfileFailed"));
                }

                return;
            }

            try
            {
                await Task.Delay(_asyncAnalysisDebounce, ticket.Token);
                var stopwatch = Stopwatch.StartNew();
                ImageViewerAnalysisRequest request = new(bitmap, targetLine.P1.ToWpfPoint(), targetLine.P2.ToWpfPoint());
                ushort[]? profileData;
                ushort maximumValue;
                if (_host.RenderService is IImageViewerHighBitDepthAnalysisRenderService highBitDepthService)
                {
                    profileData = await highBitDepthService.CreateProfile16Async(request, ticket.Token);
                    maximumValue = GetProfileMaximum(bitmap);
                }
                else
                {
                    byte[]? legacyProfile = await _host.RenderService.CreateProfileAsync(request, ticket.Token);
                    profileData = ConvertLegacyProfile(legacyProfile);
                    maximumValue = byte.MaxValue;
                }

                if (profileData == null || !_host.AnalysisState.IsCurrentProfileTicket(ticket))
                {
                    return;
                }

                _host.AnalysisState.LastProfileDuration = stopwatch.Elapsed;
                _uiFacade.PresentProfile(new ImageViewerProfileOutput(profileData, maximumValue));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _errorSink.LogNonCriticalError("Failed to update profile", ex);
                _uiFacade.PresentProfileError(UiText.Get("StatusProfileFailed"));
            }
        }

        private static ushort GetProfileMaximum(BitmapSource bitmap)
        {
            BitmapSource normalized = ImageViewerPixelAccess.NormalizeForIntensity(bitmap);
            return (ushort)ImageViewerPixelAccess.GetIntensityMaximum(normalized.Format);
        }

        private static ushort[]? ConvertLegacyProfile(byte[]? profile)
        {
            if (profile == null)
            {
                return null;
            }

            ushort[] converted = new ushort[profile.Length];
            for (int index = 0; index < profile.Length; index++)
            {
                converted[index] = profile[index];
            }

            return converted;
        }

        public void RebuildPyramidIfNeeded()
        {
            if (_host.ImageSource == null)
            {
                UpdateRenderedImage();
                return;
            }

            _ = BackgroundOperationObserver.ObserveAsync(PrepareAnalysisResourcesAsync(_host.ImageSource), "Rebuild image pyramid");
        }

        private ImageViewerBackgroundOperationObserver BackgroundOperationObserver =>
            _backgroundOperationObserver ??= new ImageViewerBackgroundOperationObserver(_errorSink.LogNonCriticalError);

        public void ClearAnalysisCaches()
        {
            _host.AnalysisState.ClearAnalysisCaches();
            ClearAnalysisDisplays();
        }

        private void ClearAnalysisDisplays()
        {
            _uiFacade.PresentHistogram(null);
            _uiFacade.PresentProfile(null);
        }

        public ImageViewerRenderStatus BuildRenderStatus()
        {
            BitmapSource? analysisBitmap = _host.AnalysisState.AnalysisBitmapSource;

            return new ImageViewerRenderStatus(
                analysisBitmap?.PixelWidth,
                analysisBitmap?.PixelHeight,
                _host.EnableImagePyramid,
                _host.AnalysisState.PyramidLevels.Count,
                _host.AnalysisState.LastPyramidBuildDuration,
                _host.AutoSelectPyramidLevel,
                _host.EnableTiledRendering,
                _host.PrefetchAdjacentTiles,
                _host.TileCacheMaximumMegabytes,
                _host.TilePrefetchRadius,
                _host.AnalysisState.LastRenderFrame,
                _host.EnableGpuRendering,
                _host.PseudoColorPalette,
                _host.AnalysisState.IsShaderPseudoColorActive,
                _host.EnableAsyncAnalysis,
                _host.PauseRealtimeHistogram,
                _host.AnalysisState.LastHistogramDuration,
                _host.PauseRealtimeProfile,
                _host.AnalysisState.LastProfileDuration);
        }

        public string BuildRenderStatusSummary()
        {
            return ImageViewerRenderStatusFormatter.Format(BuildRenderStatus());
        }

        public void UpdatePseudoColorMenuState()
        {
            _uiFacade.ApplyPseudoColorMenuState(BuildPseudoColorMenuState());
        }

        public ImageViewerPseudoColorMenuState BuildPseudoColorMenuState()
        {
            return new ImageViewerPseudoColorMenuState(_host.PseudoColorPalette);
        }

        public ImageViewerAnalysisMenuState BuildMenuState()
        {
            bool hasAnalysisBitmap = _host.AnalysisState.AnalysisBitmapSource != null;
            return new ImageViewerAnalysisMenuState(
                _host.EnableImagePyramid,
                _host.AutoSelectPyramidLevel,
                _host.EnableImagePyramid,
                _host.EnableTiledRendering,
                _host.PrefetchAdjacentTiles,
                _host.EnableTiledRendering,
                _host.EnableAsyncAnalysis,
                _host.PauseRealtimeHistogram,
                _host.ShowHistogram,
                _host.PauseRealtimeProfile,
                _host.ShowProfile,
                hasAnalysisBitmap,
                _host.PreferShaderPseudoColor,
                _host.AllowCpuPseudoColorFallback,
                hasAnalysisBitmap,
                _host.ShowHistogram || _host.ShowProfile,
                hasAnalysisBitmap || _host.ImageSource != null,
                _host.EnableGpuRendering,
                _host.PseudoColorPalette == PseudoColorPalette.None,
                _host.PseudoColorPalette == PseudoColorPalette.Hot,
                _host.PseudoColorPalette == PseudoColorPalette.Jet,
                _host.PseudoColorPalette == PseudoColorPalette.Viridis);
        }

    }
}
