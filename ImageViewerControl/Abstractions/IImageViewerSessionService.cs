using System.Threading;
using System.Threading.Tasks;
using ImageViewer.Plugins;
using ImageViewer.Services;

namespace ImageViewer.Abstractions
{
    public interface IImageViewerSessionService
    {
        void SaveToFile(string filePath, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry? pluginRegistry = null);

        Task SaveToFileAsync(string filePath, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default);

        string SerializeSession(string? sessionName, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry? pluginRegistry = null);

        ImageViewerSessionData LoadFromFile(string filePath, RoiPluginRegistry? pluginRegistry = null);

        Task<ImageViewerSessionData> LoadFromFileAsync(string filePath, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default);

        ImageViewerSessionData LoadFromJson(string sessionJson, string? sessionBaseDirectory = null, RoiPluginRegistry? pluginRegistry = null);
    }
}
