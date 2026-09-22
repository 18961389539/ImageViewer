using System.Collections.Generic;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    internal sealed class RoiDocument
    {
        public int Version { get; set; } = 1;
        public double PixelSize { get; set; } = 1.0;
        public string PhysicalUnit { get; set; } = "px";
        public List<RoiPersistenceData> Items { get; set; } = new();
    }
}