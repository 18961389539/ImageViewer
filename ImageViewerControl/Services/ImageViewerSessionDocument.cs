using System;

namespace ImageViewer.Services
{
    internal sealed class ImageViewerSessionDocument
    {
        public string? SessionName { get; set; }
        public DateTimeOffset SavedAtUtc { get; set; }
        public string? ImagePath { get; set; }
        public string RoiDocumentJson { get; set; } = string.Empty;
        public double Scale { get; set; } = 1.0;
        public double TranslateX { get; set; }
        public double TranslateY { get; set; }
    }
}