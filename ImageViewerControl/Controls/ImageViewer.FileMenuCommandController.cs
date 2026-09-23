using System;
using System.Threading.Tasks;

namespace ImageViewer.Controls
{
    internal interface IImageViewerFileMenuCommandHost
    {
        Task ShowOpenImageDialogAsync();

        Task OpenRecentProjectAsync(string filePath);

        Task SaveRoisAsync();

        Task LoadRoisAsync();

        Task SaveSessionAsync();

        Task LoadSessionAsync();

        Task ExportProjectPackageAsync();

        void ToggleAutoSave();

        void UpdateContextMenuState();
    }

    internal sealed class ImageViewerFileMenuCommandController : ImageViewerMenuCommandControllerBase<IImageViewerFileMenuCommandHost>
    {
        public ImageViewerFileMenuCommandController(IImageViewerFileMenuCommandHost host)
            : base(host, host.UpdateContextMenuState)
        {
        }

        public async Task ExecuteAsync(ImageViewerFileMenuCommand command)
        {
            switch (command)
            {
                case ImageViewerFileMenuCommand.OpenImage:
                    await Host.ShowOpenImageDialogAsync();
                    break;
                case ImageViewerFileMenuCommand.SaveRois:
                    await Host.SaveRoisAsync();
                    break;
                case ImageViewerFileMenuCommand.LoadRois:
                    await Host.LoadRoisAsync();
                    break;
                case ImageViewerFileMenuCommand.SaveSession:
                    await Host.SaveSessionAsync();
                    break;
                case ImageViewerFileMenuCommand.LoadSession:
                    await Host.LoadSessionAsync();
                    break;
                case ImageViewerFileMenuCommand.ExportProjectPackage:
                    await Host.ExportProjectPackageAsync();
                    break;
                case ImageViewerFileMenuCommand.ToggleAutoSave:
                    Host.ToggleAutoSave();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }

            RefreshMenuState();
        }

        public async Task OpenRecentProjectAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await Host.OpenRecentProjectAsync(filePath);
            RefreshMenuState();
        }
    }
}