using System;
using System.IO;
using ImageViewer.Models;
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

        [Fact]
        public void SerializeSession_WithCalibration_RoundTripsDistortionParameters()
        {
            var registry = RoiPluginRegistry.CreateBuiltIn();
            var service = new ImageViewerSessionService();
            var calibration = new CameraCalibration { K1 = 0.05, K2 = -0.003, PrincipalX = 200, PrincipalY = 150, NormalizationRadius = 100 };

            string sessionJson = service.SerializeSession("sample", null, [], 0.02, "mm", 1.0, 0, 0, registry, calibration);
            ImageViewerSessionData result = service.LoadFromJson(sessionJson, null, registry);

            Assert.NotNull(result.Calibration);
            Assert.Equal(0.05, result.Calibration!.K1, 6);
            Assert.Equal(-0.003, result.Calibration.K2, 6);
            Assert.Equal(200, result.Calibration.PrincipalX, 6);
            Assert.Equal(150, result.Calibration.PrincipalY, 6);
            Assert.Equal(100, result.Calibration.NormalizationRadius, 6);
        }

        [Fact]
        public void LoadFromJson_WithoutCalibration_ReturnsNullCalibration()
        {
            var service = new ImageViewerSessionService();
            const string sessionJson = "{\"SessionName\":\"sample\",\"RoiDocumentJson\":\"{}\"}";

            ImageViewerSessionData result = service.LoadFromJson(sessionJson, null, RoiPluginRegistry.CreateBuiltIn());

            Assert.Null(result.Calibration);
        }
    }
}