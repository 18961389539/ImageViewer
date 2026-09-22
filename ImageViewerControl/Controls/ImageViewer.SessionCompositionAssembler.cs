using System;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private sealed class SessionCompositionAssembler
        {
            private readonly ImageViewer _owner;

            public SessionCompositionAssembler(ImageViewer owner)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public ImageViewerSessionComposition CreateSessionComposition(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                ViewportController viewportController)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);
                ArgumentNullException.ThrowIfNull(viewportController);

                var sessionController = new ImageViewerSessionController(
                    new ImageViewerSessionControllerDependencies
                    {
                        Persistence = new ImageViewerSessionPersistenceWorkflow
                        {
                            ShowSaveSessionDialog = dialogWorkflowService.ShowSaveSessionDialog,
                            ShowOpenSessionDialog = dialogWorkflowService.ShowOpenSessionDialog,
                            ShowSaveProjectPackageDialog = dialogWorkflowService.ShowSaveProjectPackageDialog,
                            SessionService = _owner.RuntimeServices.SessionService,
                            RecentProjectService = _owner.RuntimeServices.RecentProjectService,
                            ProjectPackageService = _owner.RuntimeServices.ProjectPackageService,
                            GetPluginRegistry = () => _owner.PluginRegistry,
                            GetAllRois = () => _owner.ViewerState.AllRois,
                            GetPixelSize = () => _owner.PixelSize,
                            SetPixelSize = value => _owner.PixelSize = value,
                            GetPhysicalUnit = () => _owner.PhysicalUnit,
                            SetPhysicalUnit = value => _owner.PhysicalUnit = value,
                            GetCurrentViewportState = () => viewportController.CurrentState,
                            TryGetCurrentImagePath = viewportController.TryGetCurrentImagePath,
                            LoadImageFromFile = viewportController.LoadImageFromFile,
                            ReplaceAllRois = _owner.ViewerState.ReplaceAllRois,
                            ApplyViewportState = state => viewportController.ApplyViewportState(state),
                            DrawRois = () => _owner.DrawRois(),
                            ShowNonCriticalError = _owner.ShowNonCriticalError,
                            ShowWarning = dialogWorkflowService.ShowWarning,
                            ShowStatusHint = message => _owner.ShowStatusHint(message),
                            ClearUndoHistory = () => _owner.ViewerState.UndoRedo.Clear(),
                            UpdateContextMenuState = _owner.UpdateContextMenuState
                        },
                        AutoSave = new ImageViewerAutoSaveWorkflow
                        {
                            Dispatcher = _owner.Dispatcher,
                            HasContent = () => _owner.ImageSource != null || _owner.ViewerState.AllRois.Count > 0,
                            GetCurrentViewportState = () => viewportController.CurrentState,
                            TryGetCurrentImagePath = viewportController.TryGetCurrentImagePath,
                            GetAllRois = () => _owner.ViewerState.AllRois,
                            GetPixelSize = () => _owner.PixelSize,
                            GetPhysicalUnit = () => _owner.PhysicalUnit,
                            SessionService = _owner.RuntimeServices.SessionService,
                            GetPluginRegistry = () => _owner.PluginRegistry,
                            LogNonCriticalError = _owner.LogNonCriticalError
                        }
                    },
                    _owner.HostServices.PeriodicTaskSchedulerFactory,
                    _owner.HostServices.SessionStoragePolicy);

                ImageViewerRoiPersistenceController roiPersistenceController = CreateRoiPersistenceController(dialogWorkflowService);
                ImageViewerFileMenuCommandController fileMenuCommandController = CreateFileMenuCommandController(dialogWorkflowService, sessionController, roiPersistenceController);

                return new ImageViewerSessionComposition(sessionController, roiPersistenceController, fileMenuCommandController);
            }

            private ImageViewerRoiPersistenceController CreateRoiPersistenceController(ImageViewerDialogWorkflowService dialogWorkflowService)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);

                return new ImageViewerRoiPersistenceController(
                    new ImageViewerRoiPersistenceControllerHostAdapter(
                        _owner,
                        dialogWorkflowService,
                        _owner.RefreshAllCaliperDetections,
                        () => _owner.DrawRois(),
                        () => _owner._roiSelectionStateController.RefreshPropertyPanel(),
                        _owner.ShowNonCriticalError));
            }

            private ImageViewerFileMenuCommandController CreateFileMenuCommandController(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                ImageViewerSessionController sessionController,
                ImageViewerRoiPersistenceController roiPersistenceController)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);
                ArgumentNullException.ThrowIfNull(sessionController);
                ArgumentNullException.ThrowIfNull(roiPersistenceController);

                return new ImageViewerFileMenuCommandController(
                    new ImageViewerFileMenuCommandHostAdapter(
                        new ImageViewerFileMenuCommandDependencies
                        {
                            ShowOpenImageDialogAsync = dialogWorkflowService.OpenImageAsync,
                            SessionController = sessionController,
                            RoiPersistenceController = roiPersistenceController,
                            UpdateContextMenuState = _owner.UpdateContextMenuState
                        }));
            }
        }
    }
}