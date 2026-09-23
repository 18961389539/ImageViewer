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
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Unit")]
    [Trait("Category", "Wpf")]
    public class ImageViewerAnalysisCoordinatorTests
    {
        [Fact]
        public void HandleRenderingOptionChanged_UsesShaderEffectWhenAvailable()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var renderService = new FakeRenderService
                {
                    PseudoColorEffect = new BlurEffect()
                };
                var host = CreateHost(renderService, bitmap);
                host.PreferShaderPseudoColor = true;
                host.PseudoColorPalette = PseudoColorPalette.Hot;
                var uiFacade = new FakeAnalysisUiFacade();
                var coordinator = CreateCoordinator(host, uiFacade, new FakeProfileTargetResolver());

                coordinator.HandleRenderingOptionChanged();

                Assert.NotNull(uiFacade.LastRenderedImagePlan);
                Assert.Same(renderService.PseudoColorEffect, uiFacade.LastRenderedImagePlan!.Effect);
                Assert.True(host.AnalysisState.IsShaderPseudoColorActive);
                Assert.Equal(PseudoColorPalette.None, renderService.LastBuildRenderFramePalette);
            });
        }

        [Fact]
        public void BuildRenderStatus_ReturnsPureRenderStatusModel()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var renderService = new FakeRenderService();
                var host = CreateHost(renderService, bitmap);
                host.EnableGpuRendering = true;
                host.EnableImagePyramid = true;
                host.EnableTiledRendering = true;
                host.PrefetchAdjacentTiles = true;
                host.TileCacheMaximumMegabytes = 256;
                host.TilePrefetchRadius = 3;
                host.PseudoColorPalette = PseudoColorPalette.Jet;
                host.EnableAsyncAnalysis = true;
                host.PauseRealtimeHistogram = true;
                host.PauseRealtimeProfile = true;
                host.AnalysisState.LastRenderFrame = new ImageViewerRenderFrame(new DrawingImage(), 1, 2, 3, 4, 0.5, true);
                host.AnalysisState.LastPyramidBuildDuration = TimeSpan.FromMilliseconds(12);
                host.AnalysisState.LastHistogramDuration = TimeSpan.FromMilliseconds(21);
                host.AnalysisState.LastProfileDuration = TimeSpan.FromMilliseconds(34);
                host.AnalysisState.IsShaderPseudoColorActive = true;
                var coordinator = CreateCoordinator(host, new FakeAnalysisUiFacade(), new FakeProfileTargetResolver());

                ImageViewerRenderStatus status = coordinator.BuildRenderStatus();

                Assert.Equal(2, status.SourcePixelWidth);
                Assert.Equal(2, status.SourcePixelHeight);
                Assert.Equal(1, status.PyramidLevelCount);
                Assert.True(status.EnableGpuRendering);
                Assert.True(status.IsShaderPseudoColorActive);
                Assert.Equal(PseudoColorPalette.Jet, status.PseudoColorPalette);
                Assert.Equal(256, status.TileCacheMaximumMegabytes);
                Assert.Equal(3, status.TilePrefetchRadius);
                Assert.True(status.EnableAsyncAnalysis);
                Assert.True(status.PauseRealtimeHistogram);
                Assert.True(status.PauseRealtimeProfile);
                Assert.Equal(12, status.PyramidBuildDuration.TotalMilliseconds, 1);
                Assert.Equal(21, status.LastHistogramDuration.TotalMilliseconds, 1);
                Assert.Equal(34, status.LastProfileDuration.TotalMilliseconds, 1);
            });
        }

        [Fact]
        public void BuildRenderStatusSummary_FormatsStatusWithoutUiFacadeFormatting()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var host = CreateHost(new FakeRenderService(), bitmap);
                host.PseudoColorPalette = PseudoColorPalette.Hot;
                host.EnableImagePyramid = true;
                host.AnalysisState.LastRenderFrame = new ImageViewerRenderFrame(new DrawingImage(), 1, 2, 3, 4, 0.5, false);
                var coordinator = CreateCoordinator(host, new FakeAnalysisUiFacade(), new FakeProfileTargetResolver());

                string summary = coordinator.BuildRenderStatusSummary();

                Assert.Contains("Pseudo color: Hot", summary);
                Assert.Contains("Pyramid: On", summary);
            });
        }

        [Fact]
        public void HandlePseudoColorPaletteChanged_RefreshesHistogramAndProfile()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var renderService = new FakeRenderService();
                var host = CreateHost(renderService, bitmap);
                host.ShowHistogram = true;
                host.ShowProfile = true;
                host.PseudoColorPalette = PseudoColorPalette.Hot;
                var uiFacade = new FakeAnalysisUiFacade();
                var coordinator = CreateCoordinator(host, uiFacade, new FakeProfileTargetResolver { TargetLine = CreateProfileLine() });

                coordinator.HandlePseudoColorPaletteChanged();

                Assert.Equal(1, renderService.BuildRenderFrameCallCount);
                Assert.NotNull(uiFacade.LastHistogramOutput);
                Assert.NotNull(uiFacade.LastProfileOutput);
            });
        }

        [Fact]
        public void UpdatePseudoColorMenuState_AppliesPureMenuState()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var host = CreateHost(new FakeRenderService(), bitmap);
                host.PseudoColorPalette = PseudoColorPalette.Viridis;
                var uiFacade = new FakeAnalysisUiFacade();
                var coordinator = CreateCoordinator(host, uiFacade, new FakeProfileTargetResolver());

                coordinator.UpdatePseudoColorMenuState();

                Assert.NotNull(uiFacade.LastPseudoColorMenuState);
                Assert.Equal(PseudoColorPalette.Viridis, uiFacade.LastPseudoColorMenuState!.SelectedPalette);
            });
        }

        [Fact]
        public void HandleProfileVisibilityChanged_WhenVisible_RefreshesProfilePanel()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var host = CreateHost(new FakeRenderService(), bitmap);
                host.ShowProfile = true;
                var uiFacade = new FakeAnalysisUiFacade();
                var coordinator = CreateCoordinator(host, uiFacade, new FakeProfileTargetResolver { TargetLine = CreateProfileLine() });

                coordinator.HandleProfileVisibilityChanged(true);

                Assert.True(uiFacade.IsProfilePanelVisible);
                Assert.NotNull(uiFacade.LastProfileOutput);
            });
        }

        [Fact]
        public void HandleHistogramVisibilityChanged_WhenHidden_CancelsOutstandingWorkAndClearsCanvas()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var host = CreateHost(new FakeRenderService(), bitmap);
                var histogramWork = new CancellationTokenSource();
                host.AnalysisState.HistogramUpdateCancellationTokenSource = histogramWork;
                var uiFacade = new FakeAnalysisUiFacade();
                uiFacade.LastHistogramOutput = new ImageViewerHistogramOutput([1, 2, 3], 3);
                var coordinator = CreateCoordinator(host, uiFacade, new FakeProfileTargetResolver());

                coordinator.HandleHistogramVisibilityChanged(false);

                Assert.True(histogramWork.IsCancellationRequested);
                Assert.Null(host.AnalysisState.HistogramUpdateCancellationTokenSource);
                Assert.Null(uiFacade.LastHistogramOutput);
                Assert.False(uiFacade.IsHistogramPanelVisible);
            });
        }

        [Fact]
        public void UpdateHistogram_WhenPausedAndNotForced_DoesNotRefresh()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var renderService = new FakeRenderService();
                var host = CreateHost(renderService, bitmap);
                host.ShowHistogram = true;
                host.EnableAsyncAnalysis = false;
                host.PauseRealtimeHistogram = true;
                var uiFacade = new FakeAnalysisUiFacade();
                var coordinator = CreateCoordinator(host, uiFacade, new FakeProfileTargetResolver());

                coordinator.UpdateHistogram().GetAwaiter().GetResult();

                Assert.Null(uiFacade.LastHistogramOutput);
            });
        }

        [Fact]
        public void UpdateProfile_WhenNoTargetLine_ClearsProfileOutput()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var host = CreateHost(new FakeRenderService(), bitmap);
                host.ShowProfile = true;
                var uiFacade = new FakeAnalysisUiFacade
                {
                    LastProfileOutput = new ImageViewerProfileOutput([1, 2, 3])
                };
                var coordinator = CreateCoordinator(host, uiFacade, new FakeProfileTargetResolver());

                coordinator.UpdateProfile().GetAwaiter().GetResult();

                Assert.Null(uiFacade.LastProfileOutput);
            });
        }

        [Fact]
        public void PrepareAnalysisResourcesAsync_BuildsPyramidAndResetsExistingWork()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var renderService = new FakeRenderService
                {
                    AnalysisBitmap = bitmap,
                    PyramidLevelsResult =
                    [
                        new ImagePyramidLevel(bitmap, 1.0),
                        new ImagePyramidLevel(bitmap, 0.5)
                    ]
                };
                var host = CreateHost(renderService, bitmap);
                var oldHistogramWork = new CancellationTokenSource();
                var oldProfileWork = new CancellationTokenSource();
                var oldPyramidWork = new CancellationTokenSource();
                host.AnalysisState.HistogramUpdateCancellationTokenSource = oldHistogramWork;
                host.AnalysisState.ProfileUpdateCancellationTokenSource = oldProfileWork;
                host.AnalysisState.PyramidBuildCancellationTokenSource = oldPyramidWork;
                var coordinator = CreateCoordinator(host, new FakeAnalysisUiFacade(), new FakeProfileTargetResolver());

                coordinator.PrepareAnalysisResourcesAsync(bitmap).GetAwaiter().GetResult();

                Assert.True(oldHistogramWork.IsCancellationRequested);
                Assert.True(oldProfileWork.IsCancellationRequested);
                Assert.True(oldPyramidWork.IsCancellationRequested);
                Assert.Equal(1, renderService.ClearTileCacheCallCount);
                Assert.Same(bitmap, host.AnalysisState.AnalysisBitmapSource);
                Assert.Equal(2, host.AnalysisState.PyramidLevels.Count);
                Assert.Equal(1, renderService.BuildPyramidAsyncCallCount);
                Assert.Equal(2, renderService.BuildRenderFrameCallCount);
            });
        }

        [Fact]
        public void ClearAnalysisCaches_CancelsOutstandingWorkAndClearsCanvases()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource bitmap = CreateBitmap();
                var host = CreateHost(new FakeRenderService(), bitmap);
                var histogramWork = new CancellationTokenSource();
                var profileWork = new CancellationTokenSource();
                host.AnalysisState.HistogramUpdateCancellationTokenSource = histogramWork;
                host.AnalysisState.ProfileUpdateCancellationTokenSource = profileWork;
                var uiFacade = new FakeAnalysisUiFacade();
                uiFacade.LastHistogramOutput = new ImageViewerHistogramOutput([1, 2, 3], 3);
                uiFacade.LastProfileOutput = new ImageViewerProfileOutput([0, 127, 255]);
                var coordinator = CreateCoordinator(host, uiFacade, new FakeProfileTargetResolver());

                coordinator.ClearAnalysisCaches();

                Assert.True(histogramWork.IsCancellationRequested);
                Assert.True(profileWork.IsCancellationRequested);
                Assert.Null(host.AnalysisState.HistogramUpdateCancellationTokenSource);
                Assert.Null(host.AnalysisState.ProfileUpdateCancellationTokenSource);
                Assert.Null(uiFacade.LastHistogramOutput);
                Assert.Null(uiFacade.LastProfileOutput);
            });
        }

        private static ImageViewerAnalysisCoordinator CreateCoordinator(
            FakeAnalysisHost host,
            FakeAnalysisUiFacade uiFacade,
            FakeProfileTargetResolver profileTargetResolver)
        {
            return new ImageViewerAnalysisCoordinator(host, uiFacade, profileTargetResolver, new FakeErrorSink());
        }

        private static FakeAnalysisHost CreateHost(FakeRenderService renderService, BitmapSource bitmap)
        {
            var host = new FakeAnalysisHost(renderService)
            {
                ImageSource = bitmap
            };

            host.AnalysisState.AnalysisBitmapSource = bitmap;
            host.AnalysisState.PyramidLevels = [new ImagePyramidLevel(bitmap, 1.0)];
            return host;
        }

        private static BitmapSource CreateBitmap()
        {
            return BitmapSource.Create(
                pixelWidth: 2,
                pixelHeight: 2,
                dpiX: 96,
                dpiY: 96,
                pixelFormat: PixelFormats.Gray8,
                palette: null,
                pixels: new byte[] { 0, 64, 128, 255 },
                stride: 2);
        }

        private static LineMeasureRoi CreateProfileLine()
        {
            return new LineMeasureRoi
            {
                P1 = new Point(0, 0),
                P2 = new Point(1, 0)
            };
        }

        private sealed class FakeAnalysisHost : IImageViewerAnalysisHost
        {
            public FakeAnalysisHost(IImageViewerRenderService renderService)
            {
                RenderService = renderService;
            }

            public ImageViewerAnalysisState AnalysisState { get; } = new();

            public IImageViewerRenderService RenderService { get; }

            public ImageSource? ImageSource { get; set; }

            public bool EnableGpuRendering { get; set; }

            public bool PreferShaderPseudoColor { get; set; }

            public bool AllowCpuPseudoColorFallback { get; set; } = true;

            public PseudoColorPalette PseudoColorPalette { get; set; }

            public bool EnableImagePyramid { get; set; } = true;

            public bool AutoSelectPyramidLevel { get; set; } = true;

            public bool EnableTiledRendering { get; set; }

            public bool PrefetchAdjacentTiles { get; set; }

            public int TileCacheMaximumMegabytes { get; set; } = 128;

            public int TilePrefetchRadius { get; set; } = 1;

            public double Scale { get; set; } = 1.0;

            public Point Translation { get; set; }

            public Size ViewportSize { get; set; } = new(320, 240);

            public int HistogramBinCount { get; set; } = ImageViewerAnalysisCoordinator.HistogramBinCount;

            public bool ShowHistogram { get; set; }

            public bool ShowProfile { get; set; }

            public bool EnableAsyncAnalysis { get; set; }

            public bool PauseRealtimeHistogram { get; set; }

            public bool PauseRealtimeProfile { get; set; }
        }

        private sealed class FakeAnalysisUiFacade : IImageViewerAnalysisUiFacade
        {
            public bool IsHistogramPanelVisible { get; private set; }

            public bool IsProfilePanelVisible { get; private set; }

            public ImageViewerHistogramOutput? LastHistogramOutput { get; set; }

            public ImageViewerProfileOutput? LastProfileOutput { get; set; }

            public ImageViewerPseudoColorMenuState? LastPseudoColorMenuState { get; private set; }

            public ImageViewerRenderedImagePlan? LastRenderedImagePlan { get; private set; }

            public void SetHistogramPanelVisibility(bool isVisible)
            {
                IsHistogramPanelVisible = isVisible;
            }

            public void SetProfilePanelVisibility(bool isVisible)
            {
                IsProfilePanelVisible = isVisible;
            }

            public void ApplyRenderedImagePlan(ImageViewerRenderedImagePlan plan)
            {
                LastRenderedImagePlan = plan;
            }

            public void ApplyPseudoColorMenuState(ImageViewerPseudoColorMenuState state)
            {
                LastPseudoColorMenuState = state;
            }

            public void PresentHistogram(ImageViewerHistogramOutput? output)
            {
                LastHistogramOutput = output;
            }

            public void PresentProfile(ImageViewerProfileOutput? output)
            {
                LastProfileOutput = output;
            }

            public void PresentHistogramError(string message)
            {
                LastHistogramOutput = null;
            }

            public void PresentProfileError(string message)
            {
                LastProfileOutput = null;
            }
        }

        private sealed class FakeProfileTargetResolver : IImageViewerProfileTargetResolver
        {
            public LineMeasureRoi? TargetLine { get; set; }

            public LineMeasureRoi? GetProfileTargetLine() => TargetLine;
        }

        private sealed class FakeErrorSink : IImageViewerAnalysisErrorSink
        {
            public void LogNonCriticalError(string message, Exception ex)
            {
            }
        }

        private sealed class FakeRenderService : IImageViewerRenderService
        {
            public BitmapSource? AnalysisBitmap { get; set; }

            public Effect? PseudoColorEffect { get; set; }

            public IReadOnlyList<ImagePyramidLevel> PyramidLevelsResult { get; set; } = [];

            public int BuildRenderFrameCallCount { get; private set; }

            public int BuildPyramidAsyncCallCount { get; private set; }

            public int ClearTileCacheCallCount { get; private set; }

            public PseudoColorPalette LastBuildRenderFramePalette { get; private set; }

            public ImageSource? BuildDisplaySource(ImageSource? source, PseudoColorPalette palette)
            {
                return source;
            }

            public void ApplyGpuCaching(Canvas imageContainer, bool enableGpuRendering)
            {
            }

            public void ClearTileCache()
            {
                ClearTileCacheCallCount++;
            }

            public BitmapSource? GetAnalysisBitmap(ImageSource? source)
            {
                return AnalysisBitmap ?? source as BitmapSource;
            }

            public Effect? CreatePseudoColorEffect(PseudoColorPalette palette)
            {
                return PseudoColorEffect;
            }

            public Task<IReadOnlyList<ImagePyramidLevel>> BuildPyramidAsync(BitmapSource? source, CancellationToken cancellationToken)
            {
                BuildPyramidAsyncCallCount++;
                return Task.FromResult(PyramidLevelsResult);
            }

            public ImageViewerRenderFrame BuildRenderFrame(BitmapSource? source, IReadOnlyList<ImagePyramidLevel>? pyramid, Size viewport, double scale, Point translation, PseudoColorPalette palette, bool enableTiledRendering, bool autoSelectPyramidLevel, bool prefetchAdjacentTiles, int tileCacheMaximumMegabytes, int tilePrefetchRadius)
            {
                BuildRenderFrameCallCount++;
                LastBuildRenderFramePalette = palette;
                return new ImageViewerRenderFrame(new DrawingImage(), 10, 20, 30, 40, 1.0, enableTiledRendering);
            }

            public Task<int[]?> CreateHistogramAsync(BitmapSource? source, int binCount, CancellationToken cancellationToken)
            {
                int[] histogram = new int[binCount];
                Array.Fill(histogram, 1);
                return Task.FromResult<int[]?>(histogram);
            }

            public Task<byte[]?> CreateProfileAsync(ImageViewerAnalysisRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult<byte[]?>([0, 127, 255]);
            }
        }
    }
}
