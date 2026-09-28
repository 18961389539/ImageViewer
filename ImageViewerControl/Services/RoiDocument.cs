using System.Collections.Generic;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    internal sealed class RoiDocument
    {
        public int Version { get; set; } = 1;
        // Null means the legacy document omitted the field. An explicit zero,
        // negative or non-finite value is invalid and must not silently become 1 px.
        public double? PixelSize { get; set; } = 1.0;
        public string PhysicalUnit { get; set; } = "px";
        public ImageAnalysisQualityProfile? QualityProfile { get; set; }
        public List<RoiPersistenceData> Items { get; set; } = new();
    }
}
