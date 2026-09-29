using System;
using System.Threading.Tasks;

namespace ImageViewer.Controls
{
    internal interface IImageViewerFileDialogCapability
    {
        Task ShowOpenImageDialogAsync();
    }

    internal interface IImageViewerFileOperationsCapability
    {
        Task OpenRecentProjectAsync(string filePath);
        Task SaveRoisAsync();
        Task LoadRoisAsync();
        Task SaveSessionAsync();
        Task LoadSessionAsync();
        Task ExportProjectPackageAsync();
        void ToggleAutoSave();
    }

    internal sealed class ImageViewerFileMenuCommandHostAdapter : IImageViewerFileMenuCommandHost
    {
        private readonly IImageViewerFileDialogCapability _dialog;
        private readonly IImageViewerFileOperationsCapability _operations;
        private readonly IImageViewerContextMenuCapability _contextMenu;

        public ImageViewerFileMenuCommandHostAdapter(
            IImageViewerFileDialogCapability dialog,
            IImageViewerFileOperationsCapability operations,
            IImageViewerContextMenuCapability contextMenu)
        {
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
            _operations = operations ?? throw new ArgumentNullException(nameof(operations));
            _contextMenu = contextMenu ?? throw new ArgumentNullException(nameof(contextMenu));
        }

        public Task ShowOpenImageDialogAsync() => _dialog.ShowOpenImageDialogAsync();

        public Task OpenRecentProjectAsync(string filePath) => _operations.OpenRecentProjectAsync(filePath);

        public Task SaveRoisAsync() => _operations.SaveRoisAsync();

        public Task LoadRoisAsync() => _operations.LoadRoisAsync();

        public Task SaveSessionAsync() => _operations.SaveSessionAsync();

        public Task LoadSessionAsync() => _operations.LoadSessionAsync();

        public Task ExportProjectPackageAsync() => _operations.ExportProjectPackageAsync();

        public void ToggleAutoSave() => _operations.ToggleAutoSave();

        public void UpdateContextMenuState() => _contextMenu.UpdateContextMenuState();
    }

    internal sealed class ImageViewerFileDialogCapability : IImageViewerFileDialogCapability
    {
        private readonly ImageViewerDialogWorkflowService _dialogWorkflowService;

        public ImageViewerFileDialogCapability(ImageViewerDialogWorkflowService dialogWorkflowService) => _dialogWorkflowService = dialogWorkflowService ?? throw new ArgumentNullException(nameof(dialogWorkflowService));

        public Task ShowOpenImageDialogAsync() => _dialogWorkflowService.OpenImageAsync();
    }

    internal sealed class ImageViewerFileOperationsCapability : IImageViewerFileOperationsCapability
    {
        private readonly ImageViewerSessionController _sessionController;
        private readonly ImageViewerRoiPersistenceController _roiPersistenceController;

        public ImageViewerFileOperationsCapability(
            ImageViewerSessionController sessionController,
            ImageViewerRoiPersistenceController roiPersistenceController)
        {
            _sessionController = sessionController ?? throw new ArgumentNullException(nameof(sessionController));
            _roiPersistenceController = roiPersistenceController ?? throw new ArgumentNullException(nameof(roiPersistenceController));
        }

        public Task OpenRecentProjectAsync(string filePath) => _sessionController.OpenRecentProjectAsync(filePath);
        public Task SaveRoisAsync() => _roiPersistenceController.SaveRoisAsync();
        public Task LoadRoisAsync() => _roiPersistenceController.LoadRoisAsync();
        public Task SaveSessionAsync() => _sessionController.SaveSessionAsync();
        public Task LoadSessionAsync() => _sessionController.LoadProjectAsync();
        public Task ExportProjectPackageAsync() => _sessionController.ExportProjectPackageAsync();
        public void ToggleAutoSave() => _sessionController.ToggleAutoSave();
    }
}
