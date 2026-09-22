using System;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private sealed class CommandControllerAssembler
        {
            private readonly ImageViewer _owner;

            public CommandControllerAssembler(ImageViewer owner)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public ImageViewerFeatureMenuCommandController CreateFeatureMenuCommandController(ImageViewerDialogWorkflowService dialogWorkflowService)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);

                return new ImageViewerFeatureMenuCommandController(
                    new ImageViewerFeatureMenuCommandHostAdapter(
                        new ImageViewerFeatureMenuCommandDependencies
                        {
                            RenderRoot = _owner.rootGrid,
                            GetSelectedRoi = () => _owner.ViewerState.SelectedRoi,
                            GetAnalysisBitmapSource = _owner.GetAnalysisBitmapSource,
                            CreateStateCommand = ImageViewer.CreateStateCommand,
                            ExecuteUndoRedoCommand = command => _owner.ViewerState.UndoRedo.Execute(command),
                            DrawRois = () => _owner.DrawRois(),
                            ShowSaveSnapshotDialog = dialogWorkflowService.ShowSaveSnapshotDialog,
                            ShowSaveAnalysisCsvDialog = dialogWorkflowService.ShowSaveAnalysisCsvDialog,
                            ShowReadOnlyText = dialogWorkflowService.ShowReadOnlyText,
                            GetAllRois = () => _owner.ViewerState.AllRois,
                            GetPixelSize = () => _owner.PixelSize,
                            GetPhysicalUnit = () => _owner.PhysicalUnit,
                            ShowNonCriticalError = _owner.ShowNonCriticalError,
                            ShowStatusHint = message => _owner.ShowStatusHint(message),
                            UpdateContextMenuState = _owner.UpdateContextMenuState
                        }));
            }

            public ImageViewerRoiMenuCommandController CreateRoiMenuCommandController(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                RoiEditController roiEditController,
                CalibrationController calibrationController)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);
                ArgumentNullException.ThrowIfNull(roiEditController);
                ArgumentNullException.ThrowIfNull(calibrationController);

                return new ImageViewerRoiMenuCommandController(
                    new ImageViewerRoiMenuCommandHostAdapter(
                        new ImageViewerRoiMenuCommandDependencies
                        {
                            GetSelectedRoi = () => _owner.ViewerState.SelectedRoi,
                            RoiEditController = roiEditController,
                            CalibrationController = calibrationController,
                            ShowRoiProperties = dialogWorkflowService.ShowRoiProperties,
                            ShowCaliperSettings = dialogWorkflowService.ShowCaliperSettings,
                            UpdateContextMenuState = _owner.UpdateContextMenuState
                        }));
            }

            public ImageViewerViewCommandController CreateViewCommandController(ViewportController viewportController)
            {
                ArgumentNullException.ThrowIfNull(viewportController);
                return new ImageViewerViewCommandController(new ImageViewerViewCommandHostAdapter(new ImageViewerViewCommandDependencies
                {
                    GetShowPixelGrid = () => _owner.ShowPixelGrid,
                    SetShowPixelGrid = value => _owner.ShowPixelGrid = value,
                    GetShowCrosshair = () => _owner.ShowCrosshair,
                    SetShowCrosshair = value => _owner.ShowCrosshair = value,
                    GetShowCaliperScores = () => _owner.ShowCaliperScores,
                    SetShowCaliperScores = value => _owner.ShowCaliperScores = value,
                    GetShowInfoPanel = () => _owner.ShowInfoPanel,
                    SetShowInfoPanel = value => _owner.ShowInfoPanel = value,
                    GetShowHistogram = () => _owner.ShowHistogram,
                    SetShowHistogram = value => _owner.ShowHistogram = value,
                    GetShowProfile = () => _owner.ShowProfile,
                    SetShowProfile = value => _owner.ShowProfile = value,
                    GetShowScaleBar = () => _owner.ShowScaleBar,
                    SetShowScaleBar = value => _owner.ShowScaleBar = value,
                    GetShowRoiList = () => _owner.ShowRoiList,
                    SetShowRoiList = value => _owner.ShowRoiList = value,
                    GetShowSnapGrid = () => _owner.ShowSnapGrid,
                    SetShowSnapGrid = value => _owner.ShowSnapGrid = value,
                    GetEnableSnapToGrid = () => _owner.EnableSnapToGrid,
                    SetEnableSnapToGrid = value => _owner.EnableSnapToGrid = value,
                    FitToView = viewportController.FitToView,
                    ResetView = () =>
                    {
                        viewportController.ResetView();
                        _owner.ResetImageOrientation();
                    },
                    ShowFullImage = viewportController.ShowFullImage,
                    SetActualSize = viewportController.SetActualSize,
                    ZoomIn = () => _owner.ZoomAtViewportCenter(1.25),
                    ZoomOut = () => _owner.ZoomAtViewportCenter(0.8),
                    ZoomToSelection = viewportController.ZoomToSelection,
                    RotateLeft = _owner.RotateImageLeft,
                    RotateRight = _owner.RotateImageRight,
                    FlipHorizontal = _owner.FlipImageHorizontal,
                    FlipVertical = _owner.FlipImageVertical
                }));
            }

            public ImageViewerModeCommandController CreateModeCommandController()
            {
                return new ImageViewerModeCommandController(new ImageViewerModeCommandHostAdapter(new ImageViewerModeCommandDependencies
                {
                    StartRectangleMode = _owner.StartRoiMode,
                    StartEllipseMode = _owner.StartEllipseRoiMode,
                    StartCircleMode = _owner.StartCircleRoiMode,
                    StartPolygonMode = _owner.StartPolygonRoiMode,
                    StartPolylineMode = () => _owner.StartPolylineRoiMode(freehand: false),
                    StartFreehandMode = () => _owner.StartPolylineRoiMode(freehand: true),
                    StartPointAnnotationMode = _owner.StartPointAnnotationMode,
                    StartTextAnnotationMode = _owner.StartTextAnnotationMode,
                    StartLineMeasureMode = _owner.StartLineMeasureMode,
                    StartAngleMeasureMode = _owner.StartAngleMeasureMode
                }));
            }
        }
    }
}