using System;
using System.Threading.Tasks;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerFileMenuCommandDependencies
    {
        public required Func<Task> ShowOpenImageDialogAsync { get; init; }
        public required ImageViewerSessionController SessionController { get; init; }
        public required ImageViewerRoiPersistenceController RoiPersistenceController { get; init; }
        public required Action UpdateContextMenuState { get; init; }
    }

    internal sealed class ImageViewerFileMenuCommandHostAdapter : IImageViewerFileMenuCommandHost
    {
        private readonly ImageViewerFileMenuCommandDependencies _dependencies;

        public ImageViewerFileMenuCommandHostAdapter(ImageViewerFileMenuCommandDependencies dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public Task ShowOpenImageDialogAsync() => _dependencies.ShowOpenImageDialogAsync();

        public Task OpenRecentProjectAsync(string filePath) => _dependencies.SessionController.OpenRecentProjectAsync(filePath);

        public Task SaveRoisAsync() => _dependencies.RoiPersistenceController.SaveRoisAsync();

        public Task LoadRoisAsync() => _dependencies.RoiPersistenceController.LoadRoisAsync();

        public Task SaveSessionAsync() => _dependencies.SessionController.SaveSessionAsync();

        public Task LoadSessionAsync() => _dependencies.SessionController.LoadProjectAsync();

        public Task ExportProjectPackageAsync() => _dependencies.SessionController.ExportProjectPackageAsync();

        public void ToggleAutoSave() => _dependencies.SessionController.ToggleAutoSave();

        public void UpdateContextMenuState() => _dependencies.UpdateContextMenuState();
    }
}