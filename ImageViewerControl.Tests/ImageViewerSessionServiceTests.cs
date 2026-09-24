using System;
using System.Collections.Generic;
using System.IO;
using ImageViewer.Abstractions;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class ImageViewerSessionServiceTests
    {
        private static ImageViewerPersistenceSnapshot CreateSnapshot(
            string? imagePath,
            IReadOnlyList<RoiBase> rois,
            double pixelSize = 1.0,
            string physicalUnit = "px",
            CameraCalibration? calibration = null)
        {
            return new ImageViewerPersistenceSnapshot(imagePath, rois, pixelSize, physicalUnit, 1.0, 0, 0, calibration);
        }

        [Fact]
        public void LoadFromJson_RelativeImagePath_ResolvesAgainstSessionDirectory()
        {
            RoiPluginRegistry registry = RoiPluginRegistry.CreateBuiltIn();
            var service = new ImageViewerSessionService();
            string sessionJson = service.SerializeSession("sample", CreateSnapshot("assets/source.png", [], 1.0, "px"), registry);
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
        public void SerializeSession_WithUnresolvedRois_WritesThemBackSoTheySurviveReopen()
        {
            // 场景：工程里含有本机未安装插件才能识别的标注 → 会话重新保存后，这些载荷必须原样保留。
            var registry = RoiPluginRegistry.CreateBuiltIn();
            var service = new ImageViewerSessionService();
            var unresolved = new RoiPersistenceData { Type = "future-plugin-roi", Label = "keep-me" };
            ImageViewerPersistenceSnapshot snapshot = CreateSnapshot(null, [new CircleRoi()]) with
            {
                UnresolvedRois = [unresolved]
            };

            string sessionJson = service.SerializeSession("sample", snapshot, registry);
            ImageViewerSessionData reopened = service.LoadFromJson(sessionJson, null, registry);

            Assert.IsType<CircleRoi>(Assert.Single(reopened.Rois));
            RoiPersistenceData kept = Assert.Single(reopened.UnresolvedRois);
            Assert.Equal("future-plugin-roi", kept.Type);
            Assert.Equal("keep-me", kept.Label);
        }

        [Fact]
        public void SerializeSession_WithCalibration_RoundTripsDistortionParameters()
        {
            var registry = RoiPluginRegistry.CreateBuiltIn();
            var service = new ImageViewerSessionService();
            var calibration = new CameraCalibration { K1 = 0.05, K2 = -0.003, PrincipalX = 200, PrincipalY = 150, NormalizationRadius = 100 };

            string sessionJson = service.SerializeSession("sample", CreateSnapshot(null, [], 0.02, "mm", calibration), registry);
            ImageViewerSessionData result = service.LoadFromJson(sessionJson, null, registry);

            Assert.NotNull(result.Calibration);
            Assert.Equal(0.05, result.Calibration!.K1, 6);
            Assert.Equal(-0.003, result.Calibration.K2, 6);
            Assert.Equal(200, result.Calibration.PrincipalX, 6);
            Assert.Equal(150, result.Calibration.PrincipalY, 6);
            Assert.Equal(100, result.Calibration.NormalizationRadius, 6);
        }

        [Fact]
        public void SerializeSession_WritesNestedRoiDocumentObject()
        {
            var service = new ImageViewerSessionService();
            var registry = RoiPluginRegistry.CreateBuiltIn();

            string sessionJson = service.SerializeSession("sample", CreateSnapshot(null, [new CircleRoi { Radius = 15 }], 0.5, "mm"), registry);

            Assert.DoesNotContain("\"RoiDocumentJson\"", sessionJson, StringComparison.Ordinal);
            Assert.Contains("\"RoiDocument\"", sessionJson, StringComparison.Ordinal);
            Assert.Contains("\"Version\": 2", sessionJson, StringComparison.Ordinal);
            Assert.Contains("\"PixelSize\": 0.5", sessionJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Common\"", sessionJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Geometry\"", sessionJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Measurement\"", sessionJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Options\"", sessionJson, StringComparison.Ordinal);
        }

        [Fact]
        public void SerializeSession_ThenLoad_RoundTripsThroughNestedForm()
        {
            var service = new ImageViewerSessionService();
            var registry = RoiPluginRegistry.CreateBuiltIn();

            string sessionJson = service.SerializeSession(
                "sample",
                CreateSnapshot("source.png", [new CircleRoi { Label = "nested", Center = new PointD(5, 6), Radius = 7 }], 0.5, "mm") with
                {
                    Scale = 1.25,
                    TranslateX = 24,
                    TranslateY = -12
                },
                registry);
            ImageViewerSessionData result = service.LoadFromJson(sessionJson, null, registry);

            Assert.Equal(0.5, result.PixelSize);
            Assert.Equal("mm", result.PhysicalUnit);
            Assert.Equal(1.25, result.Scale);
            Assert.Equal(24, result.TranslateX);
            Assert.Equal(-12, result.TranslateY);
            var circle = Assert.IsType<CircleRoi>(Assert.Single(result.Rois));
            Assert.Equal("nested", circle.Label);
            Assert.Equal(7, circle.Radius);
        }

        /// <summary>
        /// 改造前的会话文件必须继续可读。
        /// Chinese: 该 fixture 由改造前的实现真实生成，ROI 载荷是转义字符串且没有 Version 字段。
        /// English: This fixture was produced by the pre-change implementation; the ROI payload is an escaped
        /// string and the session has no Version field.
        /// </summary>
        [Fact]
        public void LoadFromJson_LegacyEscapedRoiDocumentFixture_RemainsReadable()
        {
            var service = new ImageViewerSessionService();
            string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Session", "legacy-v1-escaped-session.json");
            string sessionBaseDirectory = Path.Combine(Path.GetTempPath(), "ImageViewerSessionTests", "legacy");

            ImageViewerSessionData result = service.LoadFromJson(
                File.ReadAllText(fixturePath),
                sessionBaseDirectory,
                RoiPluginRegistry.CreateBuiltIn());

            Assert.Equal("legacy-session", result.SessionName);
            Assert.Equal(Path.GetFullPath(Path.Combine(sessionBaseDirectory, "assets/source.png")), result.ImagePath);
            Assert.Equal(0.5, result.PixelSize);
            Assert.Equal("mm", result.PhysicalUnit);
            Assert.Equal(1.25, result.Scale);
            Assert.Equal(24, result.TranslateX);
            Assert.Equal(-12, result.TranslateY);
            var circle = Assert.IsType<CircleRoi>(Assert.Single(result.Rois));
            Assert.Equal("legacy-circle", circle.Label);
            Assert.Equal(new PointD(120, 84), circle.Center);
            Assert.Equal(15, circle.Radius);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(99)]
        public void LoadFromJson_FutureSessionVersion_ThrowsNotSupportedException(int version)
        {
            var service = new ImageViewerSessionService();
            string sessionJson = $"{{\"Version\":{version},\"SessionName\":\"sample\",\"RoiDocumentJson\":\"{{}}\"}}";

            Assert.Throws<NotSupportedException>(() =>
                service.LoadFromJson(sessionJson, null, RoiPluginRegistry.CreateBuiltIn()));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void LoadFromJson_SupportedOrNonPositiveSessionVersion_IsAccepted(int version)
        {
            var service = new ImageViewerSessionService();
            string sessionJson = $"{{\"Version\":{version},\"SessionName\":\"sample\",\"RoiDocumentJson\":\"{{}}\"}}";

            ImageViewerSessionData result = service.LoadFromJson(sessionJson, null, RoiPluginRegistry.CreateBuiltIn());

            Assert.Equal("sample", result.SessionName);
        }

        [Fact]
        public void LoadFromJson_MissingSessionVersion_IsAcceptedAsLegacy()
        {
            var service = new ImageViewerSessionService();
            const string sessionJson = "{\"SessionName\":\"sample\",\"RoiDocumentJson\":\"{}\"}";

            ImageViewerSessionData result = service.LoadFromJson(sessionJson, null, RoiPluginRegistry.CreateBuiltIn());

            Assert.Equal("sample", result.SessionName);
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