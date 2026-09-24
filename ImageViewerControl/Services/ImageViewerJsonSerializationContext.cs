using System.Collections.Generic;
using System.Text.Json.Serialization;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    // 注意：禁止把 GenerationMode 改为 Serialization（仅快路径）。
    // ImageViewerSessionDocument.RoiDocument 使用了属性级 [JsonConverter]，该特性只在 metadata 模式下生效；
    // 缺少 metadata 模式会在序列化会话时直接抛异常。
    // Note: do not set GenerationMode to Serialization (fast-path only). The property-level [JsonConverter] on
    // ImageViewerSessionDocument.RoiDocument is only honored in metadata mode; without it session serialization throws.
    [JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(List<RecentImageViewerProject>))]
    [JsonSerializable(typeof(RoiPluginDiscoveryOptions))]
    [JsonSerializable(typeof(ImageViewerSessionDocument))]
    [JsonSerializable(typeof(RoiDocument))]
    [JsonSerializable(typeof(CameraCalibration))]
    internal partial class ImageViewerJsonSerializationContext : JsonSerializerContext
    {
    }
}