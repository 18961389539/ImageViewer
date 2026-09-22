using System.Threading.Tasks;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    public class ImageViewerCommandHostAdapterTests
    {
        [Fact]
        public void ViewCommandHostAdapter_ToggleInfoPanel_UpdatesViewerProperty()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                ImageViewerControlComposition composition = GetComposition(viewer);
                var controller = new ImageViewerViewCommandController(new ImageViewerViewCommandHostAdapter(new ImageViewerViewCommandDependencies
                {
                    GetShowPixelGrid = () => viewer.ShowPixelGrid,
                    SetShowPixelGrid = value => viewer.ShowPixelGrid = value,
                    GetShowCrosshair = () => viewer.ShowCrosshair,
                    SetShowCrosshair = value => viewer.ShowCrosshair = value,
                    GetShowCaliperScores = () => viewer.ShowCaliperScores,
                    SetShowCaliperScores = value => viewer.ShowCaliperScores = value,
                    GetShowInfoPanel = () => viewer.ShowInfoPanel,
                    SetShowInfoPanel = value => viewer.ShowInfoPanel = value,
                    GetShowHistogram = () => viewer.ShowHistogram,
                    SetShowHistogram = value => viewer.ShowHistogram = value,
                    GetShowProfile = () => viewer.ShowProfile,
                    SetShowProfile = value => viewer.ShowProfile = value,
                    GetShowScaleBar = () => viewer.ShowScaleBar,
                    SetShowScaleBar = value => viewer.ShowScaleBar = value,
                    GetShowRoiList = () => viewer.ShowRoiList,
                    SetShowRoiList = value => viewer.ShowRoiList = value,
                    GetShowToolbar = () => viewer.ShowToolbar,
                    SetShowToolbar = value => viewer.ShowToolbar = value,
                    GetShowSnapGrid = () => viewer.ShowSnapGrid,
                    SetShowSnapGrid = value => viewer.ShowSnapGrid = value,
                    GetEnableSnapToGrid = () => viewer.EnableSnapToGrid,
                    SetEnableSnapToGrid = value => viewer.EnableSnapToGrid = value,
                    FitToView = composition.ViewportController.FitToView,
                    ResetView = composition.ViewportController.ResetView,
                    ShowFullImage = composition.ViewportController.ShowFullImage,
                    SetActualSize = composition.ViewportController.SetActualSize,
                    ZoomIn = static () => { },
                    ZoomOut = static () => { },
                    ZoomToSelection = composition.ViewportController.ZoomToSelection,
                    RotateLeft = static () => { },
                    RotateRight = static () => { },
                    FlipHorizontal = static () => { },
                    FlipVertical = static () => { }
                }));
                bool initial = viewer.ShowInfoPanel;

                controller.Execute(ImageViewerViewCommand.ToggleInfoPanel);

                Assert.NotEqual(initial, viewer.ShowInfoPanel);
            });
        }

        [Fact]
        public void AnalysisCommandHostAdapter_CommandsUpdateViewerState()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                ImageViewerControlComposition composition = GetComposition(viewer);
                ImageViewerAnalysisState analysisState = viewer._analysisState;
                int updateRenderedImageCount = 0;
                int smartDisplaySuggestionCount = 0;
                var controller = new ImageViewerAnalysisCommandController(
                    new ImageViewerAnalysisCommandHostAdapter(
                        new ImageViewerAnalysisCommandDependencies
                        {
                            GetEnableAsyncAnalysis = () => viewer.EnableAsyncAnalysis,
                            SetEnableAsyncAnalysis = value => viewer.EnableAsyncAnalysis = value,
                            GetPauseRealtimeHistogram = () => viewer.PauseRealtimeHistogram,
                            SetPauseRealtimeHistogram = value => viewer.PauseRealtimeHistogram = value,
                            GetPauseRealtimeProfile = () => viewer.PauseRealtimeProfile,
                            SetPauseRealtimeProfile = value => viewer.PauseRealtimeProfile = value,
                            GetEnableImagePyramid = () => viewer.EnableImagePyramid,
                            SetEnableImagePyramid = value => viewer.EnableImagePyramid = value,
                            GetAutoSelectPyramidLevel = () => viewer.AutoSelectPyramidLevel,
                            SetAutoSelectPyramidLevel = value => viewer.AutoSelectPyramidLevel = value,
                            GetEnableTiledRendering = () => viewer.EnableTiledRendering,
                            SetEnableTiledRendering = value => viewer.EnableTiledRendering = value,
                            GetPrefetchAdjacentTiles = () => viewer.PrefetchAdjacentTiles,
                            SetPrefetchAdjacentTiles = value => viewer.PrefetchAdjacentTiles = value,
                            GetTileCacheMaximumMegabytes = () => viewer.TileCacheMaximumMegabytes,
                            SetTileCacheMaximumMegabytes = value => viewer.TileCacheMaximumMegabytes = value,
                            GetTilePrefetchRadius = () => viewer.TilePrefetchRadius,
                            SetTilePrefetchRadius = value => viewer.TilePrefetchRadius = value,
                            GetEnableGpuRendering = () => viewer.EnableGpuRendering,
                            SetEnableGpuRendering = value => viewer.EnableGpuRendering = value,
                            GetPreferShaderPseudoColor = () => viewer.PreferShaderPseudoColor,
                            SetPreferShaderPseudoColor = value => viewer.PreferShaderPseudoColor = value,
                            GetAllowCpuPseudoColorFallback = () => viewer.AllowCpuPseudoColorFallback,
                            SetAllowCpuPseudoColorFallback = value => viewer.AllowCpuPseudoColorFallback = value,
                            UpdateRenderedImage = () => updateRenderedImageCount++,
                            RefreshAnalysis = composition.AnalysisController.HandleRefreshAnalysisRequested,
                            ClearAnalysisCache = composition.AnalysisController.HandleClearAnalysisCacheRequested,
                            ResetPyramidToBaseLevel = () => { composition.AnalysisController.ClearRenderCache(); analysisState.ResetPyramidToBaseLevel(); },
                            RebuildPyramidIfNeeded = () => { },
                            SetPseudoColorPalette = value => viewer.PseudoColorPalette = value,
                            ShowSmartDisplaySuggestion = () => smartDisplaySuggestionCount++,
                            ShowRenderStatus = () => { }
                        }));
                bool initialGpuRendering = viewer.EnableGpuRendering;

                controller.Execute(ImageViewerAnalysisCommand.ToggleGpuRendering);
                controller.Execute(ImageViewerAnalysisCommand.SetPseudoColorPaletteHot);
                controller.Execute(ImageViewerAnalysisCommand.ShowSmartDisplaySuggestion);

                Assert.NotEqual(initialGpuRendering, viewer.EnableGpuRendering);
                Assert.Equal(1, updateRenderedImageCount);
                Assert.Equal(PseudoColorPalette.Hot, viewer.PseudoColorPalette);
                Assert.Equal(1, smartDisplaySuggestionCount);
            });
        }

        [Fact]
        public void RoiMenuCommandHostAdapter_EditProperties_UsesSelectedRoi()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                ImageViewerControlComposition composition = GetComposition(viewer);
                var selectedRoi = new CircleRoi();
                RoiBase? shownRoi = null;
                var controller = new ImageViewerRoiMenuCommandController(
                    new ImageViewerRoiMenuCommandHostAdapter(
                        new ImageViewerRoiMenuCommandDependencies
                        {
                            GetSelectedRoi = () => viewer.ViewerState.SelectedRoi,
                            RoiEditController = composition.RoiEditController,
                            CalibrationController = composition.CalibrationController,
                            ShowRoiProperties = roi => shownRoi = roi,
                            ShowCaliperSettings = _ => { },
                            UpdateContextMenuState = () => { }
                        }));

                viewer.ViewerState.SelectedRoi = selectedRoi;
                controller.Execute(ImageViewerRoiMenuCommand.EditProperties);

                Assert.Same(selectedRoi, shownRoi);
            });
        }

        [Fact]
        public void RoiMenuCommandHostAdapter_EditCaliperSettings_UsesSelectedRoi()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                ImageViewerControlComposition composition = GetComposition(viewer);
                var selectedRoi = new LineCaliperMeasureRoi();
                RoiBase? shownRoi = null;
                var controller = new ImageViewerRoiMenuCommandController(
                    new ImageViewerRoiMenuCommandHostAdapter(
                        new ImageViewerRoiMenuCommandDependencies
                        {
                            GetSelectedRoi = () => viewer.ViewerState.SelectedRoi,
                            RoiEditController = composition.RoiEditController,
                            CalibrationController = composition.CalibrationController,
                            ShowRoiProperties = _ => { },
                            ShowCaliperSettings = roi => shownRoi = roi,
                            UpdateContextMenuState = () => { }
                        }));

                viewer.ViewerState.SelectedRoi = selectedRoi;
                controller.Execute(ImageViewerRoiMenuCommand.EditCaliperSettings);

                Assert.Same(selectedRoi, shownRoi);
            });
        }

        [Fact]
        public void FileMenuCommandHostAdapter_ToggleAutoSave_UsesSessionController()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                ImageViewerControlComposition composition = GetComposition(viewer);
                var controller = new ImageViewerFileMenuCommandController(
                    new ImageViewerFileMenuCommandHostAdapter(
                        new ImageViewerFileMenuCommandDependencies
                        {
                            ShowOpenImageDialogAsync = composition.DialogWorkflowService.OpenImageAsync,
                            SessionController = composition.SessionController,
                            RoiPersistenceController = composition.RoiPersistenceController,
                            UpdateContextMenuState = () => { }
                        }));
                bool initial = composition.SessionController.IsAutoSaveEnabled;

                controller.ExecuteAsync(ImageViewerFileMenuCommand.ToggleAutoSave).GetAwaiter().GetResult();

                Assert.NotEqual(initial, composition.SessionController.IsAutoSaveEnabled);
            });
        }

        private static ImageViewerControlComposition GetComposition(ImageViewer.Controls.ImageViewer viewer)
        {
            return viewer._controlComposition;
        }
    }
}