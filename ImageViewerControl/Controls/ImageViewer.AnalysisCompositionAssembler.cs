using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImageViewer.Localization;
using ImageViewer.Models;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        internal sealed class AnalysisCompositionAssembler
        {
            private readonly ImageViewer _owner;

            public AnalysisCompositionAssembler(ImageViewer owner)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public ImageViewerAnalysisComposition CreateAnalysisComposition(ImageViewerDialogWorkflowService dialogWorkflowService)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);

                IImageViewerAnalysisHost host = new ImageViewerAnalysisCoordinatorHost(
                    _owner,
                    _owner._analysisState,
                    () => new Point(_owner.translateTransform.X, _owner.translateTransform.Y),
                    () => new Size(_owner.rootGrid.ActualWidth, _owner.rootGrid.ActualHeight));
                IImageViewerAnalysisUiFacade uiFacade = new ImageViewerAnalysisUiFacade(
                    _owner.histogramPanel,
                    _owner.profilePanel,
                    _owner.histogramCanvas,
                    _owner.profileCanvas,
                    _owner.pseudoColorMenuItem.Items.OfType<MenuItem>(),
                    new WpfImageViewerRenderedImageApplier(_owner.imageContainer, _owner.image, _owner.RuntimeServices.RenderService));
                IImageViewerProfileTargetResolver profileTargetResolver = new ImageViewerProfileTargetResolver(() => _owner.ResolveInProgressProfileLine(), () => _owner.ViewerState.SelectedRoi);
                IImageViewerAnalysisErrorSink errorSink = new ImageViewerAnalysisDiagnostics(_owner.HostServices.AnalysisDiagnostics, _owner.Logger);
                var analysisController = new ImageViewerAnalysisCoordinator(host, uiFacade, profileTargetResolver, errorSink);

                return new ImageViewerAnalysisComposition(
                    analysisController,
                    CreateAnalysisCommandController(analysisController, dialogWorkflowService));
            }

            private ImageViewerAnalysisCommandController CreateAnalysisCommandController(
                ImageViewerAnalysisCoordinator analysisController,
                ImageViewerDialogWorkflowService dialogWorkflowService)
            {
                return new ImageViewerAnalysisCommandController(
                    new ImageViewerAnalysisCommandHostAdapter(
                        new ImageViewerAnalysisCommandDependencies
                        {
                            RuntimeOptions = _owner.RuntimeOptions,
                            GetEnableGpuRendering = () => _owner.EnableGpuRendering,
                            SetEnableGpuRendering = value => _owner.EnableGpuRendering = value,
                            UpdateRenderedImage = _owner.UpdateRenderedImage,
                            RefreshAnalysis = analysisController.HandleRefreshAnalysisRequested,
                            ClearAnalysisCache = analysisController.HandleClearAnalysisCacheRequested,
                            ResetPyramidToBaseLevel = () =>
                            {
                                analysisController.ClearRenderCache();
                                _owner._analysisState.ResetPyramidToBaseLevel();
                            },
                            RebuildPyramidIfNeeded = _owner.RebuildPyramidIfNeeded,
                            SetPseudoColorPalette = value => _owner.PseudoColorPalette = value,
                            ShowSmartDisplaySuggestion = () => _owner.ShowSmartDisplaySuggestion(dialogWorkflowService),
                            ShowRenderStatus = () => dialogWorkflowService.ShowReadOnlyText(UiText.Get("DialogRenderStatusTitle"), _owner.BuildRenderStatusSummary())
                        }));
            }
        }
    }
}