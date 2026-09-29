using System.Text.Json.Serialization;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Persistence
{
    /// <summary>
    /// Source-generated JSON metadata for framework-neutral session and ROI schema types.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(ImageViewerSessionDocument))]
    [JsonSerializable(typeof(RoiDocument))]
    [JsonSerializable(typeof(CameraCalibration))]
    [JsonSerializable(typeof(ImageAnalysisQualityProfile))]
    public partial class ImageViewerCoreJsonSerializationContext : JsonSerializerContext
    {
    }
}
