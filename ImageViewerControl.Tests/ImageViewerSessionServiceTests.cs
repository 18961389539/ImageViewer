using System;
using System.IO;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class ImageViewerSessionServiceTests
    {
        [Fact]
        public void LoadFromJson_RelativeImagePath_ResolvesAgainstSessionDirectory()
        {
            RoiPluginRegistry registry = RoiPluginRegistry.CreateBuiltIn();
            var service = new ImageViewerSessionService();
            string sessionJson = service.SerializeSession("sample", "assets/source.png", [], 1.0, "px", 1.0, 0, 0, registry);
            string sessionDirectory = Path.Combine(Path.GetTempPath(), "ImageViewerSessionTests", "project");

            ImageViewerSessionData result = service.LoadFromJson(sessionJson, sessionDirectory, registry);

            Assert.Equal(Path.GetFullPath(Path.Combine(sessionDirectory, "assets/source.png")), result.ImagePath);
        }

        [Fact]
        public void LoadFromJson_InvalidJson_ThrowsJsonException()
        {
            var service = new ImageViewerSessionService();

            Assert.Throws<System.Text.Json.JsonException>(() =>
                service.LoadFromJson("{ invalid", null, RoiPluginRegistry.CreateBuiltIn()));
        }

        [Fact]
        public void LoadFromJson_MissingRoiValues_UsesDocumentDefaults()
        {
            var service = new ImageViewerSessionService();
            const string sessionJson = "{\"SessionName\":\"sample\",\"RoiDocumentJson\":\"{}\"}";

            ImageViewerSessionData result = service.LoadFromJson(sessionJson, null, RoiPluginRegistry.CreateBuiltIn());

            Assert.Equal(1.0, result.Scale);
            Assert.Equal(1.0, result.PixelSize);
            Assert.Equal("px", result.PhysicalUnit);
        }
    }
}