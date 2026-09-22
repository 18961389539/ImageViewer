using System.Collections.Generic;
using System.Text.Json.Serialization;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    [JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(List<RecentImageViewerProject>))]
    [JsonSerializable(typeof(RoiPluginDiscoveryOptions))]
    [JsonSerializable(typeof(ImageViewerSessionDocument))]
    [JsonSerializable(typeof(RoiDocument))]
    internal partial class ImageViewerJsonSerializationContext : JsonSerializerContext
    {
    }
}