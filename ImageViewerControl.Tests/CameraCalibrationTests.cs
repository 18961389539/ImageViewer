using System.Windows;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    /// <summary>
    /// 相机畸变标定模型及信息面板校正应用测试。
    /// English: Camera distortion calibration model and info-panel application tests.
    /// </summary>
    public class CameraCalibrationTests
    {
        [Fact]
        public void UndistortScaleFactor_NoCoefficients_ReturnsOne()
        {
            var calibration = new CameraCalibration { PrincipalX = 100, PrincipalY = 100, NormalizationRadius = 100 };

            Assert.Equal(1.0, calibration.UndistortScaleFactor(new PointD(150, 150)), 9);
        }

        [Fact]
        public void UndistortScaleFactor_AtPrincipalPoint_ReturnsOne()
        {
            var calibration = new CameraCalibration { PrincipalX = 100, PrincipalY = 80, NormalizationRadius = 100, K1 = 0.1 };

            Assert.Equal(1.0, calibration.UndistortScaleFactor(new PointD(100, 80)), 9);
        }

        [Fact]
        public void UndistortScaleFactor_PositiveK1_ReducesOffCenterMagnification()
        {
            // dx=100, R=100 → r²=1; local scale=1+K1=1.25 → factor=0.8
            var calibration = new CameraCalibration { PrincipalX = 100, PrincipalY = 100, NormalizationRadius = 100, K1 = 0.25 };

            double factor = calibration.UndistortScaleFactor(new PointD(200, 100));

            Assert.Equal(0.8, factor, 9);
        }

        [Fact]
        public void CorrectPixelLength_AppliesFactorAtMidpoint()
        {
            var calibration = new CameraCalibration { PrincipalX = 0, PrincipalY = 0, NormalizationRadius = 100, K1 = 0.25 };

            double corrected = calibration.CorrectPixelLength(100, new PointD(100, 0));

            Assert.Equal(80.0, corrected, 9);
        }

        [Fact]
        public void RoiCalibrationHelper_UsesEndpointCorrectionForBasicRadialModel()
        {
            var calibration = new CameraCalibration
            {
                PrincipalX = 0,
                PrincipalY = 0,
                NormalizationRadius = 100,
                K1 = 0.25
            };
            var line = new LineMeasureRoi
            {
                P1 = new PointD(100, 0),
                P2 = new PointD(200, 0)
            };

            double rawLength = line.P1.DistanceTo(line.P2);
            double endpointCorrection = calibration.CorrectPixelLength(line.P1, line.P2) / rawLength;
            double midpointCorrection = calibration.UndistortScaleFactor(new PointD(150, 0));

            Assert.Equal(endpointCorrection, RoiCalibrationHelper.GetLengthCorrection(line, calibration), 9);
            Assert.NotEqual(midpointCorrection, endpointCorrection);
        }

        [Fact]
        public void RoiCalibrationHelper_UsesJacobianDeterminantForAreaCorrection()
        {
            var calibration = new CameraCalibration
            {
                PrincipalX = 0,
                PrincipalY = 0,
                NormalizationRadius = 100,
                K1 = 0.25
            };
            var circle = new CircleRoi { Center = new PointD(150, 0), Radius = 10 };

            double areaCorrection = RoiCalibrationHelper.GetAreaCorrection(circle, calibration);
            double lengthSquareApproximation = Math.Pow(calibration.LocalLengthScaleFactor(circle.Center), 2);

            Assert.Equal(calibration.LocalAreaScaleFactor(circle.Center), areaCorrection, 9);
            Assert.NotEqual(lengthSquareApproximation, areaCorrection);
        }

        [Fact]
        public void UndistortScaleFactor_ExtremeCoefficients_AreClamped()
        {
            var calibration = new CameraCalibration { PrincipalX = 100, PrincipalY = 100, NormalizationRadius = 100, K1 = 10 };

            double factor = calibration.UndistortScaleFactor(new PointD(200, 100));

            Assert.InRange(factor, 0.0, 5.0);
            Assert.True(factor > 0);
        }

        [Fact]
        public void NormalizedRadiusSquared_AtPrincipalPoint_IsZero()
        {
            var calibration = new CameraCalibration { PrincipalX = 50, PrincipalY = 60, NormalizationRadius = 10 };

            Assert.Equal(0.0, calibration.NormalizedRadiusSquared(new PointD(50, 60)), 9);
        }

        [Fact]
        public void NormalizedRadiusSquared_UsesPrincipalOffset()
        {
            var calibration = new CameraCalibration { PrincipalX = 90, PrincipalY = 80, NormalizationRadius = 10 };

            Assert.Equal(1.0, calibration.NormalizedRadiusSquared(new PointD(100, 80)), 9);
        }

        [Fact]
        public void CreateForImage_SetsPrincipalToCenter_AndRadiusToHalfShortSide()
        {
            var calibration = CameraCalibration.CreateForImage(0.1, 0.01, 1920, 1080);

            Assert.Equal(960, calibration.PrincipalX, 9);
            Assert.Equal(540, calibration.PrincipalY, 9);
            Assert.Equal(540, calibration.NormalizationRadius, 9);
        }

        [Fact]
        public void RoiInfoService_BuildInfo_AppliesDistortionCorrectionAtRoiCenter()
        {
            var registry = RoiPluginRegistry.CreateBuiltIn();
            var calibration = CameraCalibration.CreateForImage(0.25, 0, 400, 200); // 主点 (200,100), R=100
            var circle = new CircleRoi { Center = new PointD(300, 100), Radius = 10 }; // dx=100 → r²=1

            string uncorrected = RoiInfoService.BuildInfo(circle, null, 0.01, "mm", registry, includeStatistics: false);
            string corrected = RoiInfoService.BuildInfo(circle, null, 0.01, "mm", registry, includeStatistics: false, calibration);

            // 无畸变: 10px * 0.01 = 0.10mm; 有畸变: 10px * 0.8 * 0.01 = 0.08mm
            Assert.Contains("0.10 mm", uncorrected);
            Assert.Contains("0.08 mm", corrected);
            Assert.DoesNotContain("0.10 mm", corrected);
        }

        [Fact]
        public void RoiInfoService_BuildInfo_WithoutCalibration_ShowsNoDistortionLine()
        {
            var registry = RoiPluginRegistry.CreateBuiltIn();
            var circle = new CircleRoi { Center = new PointD(100, 100), Radius = 5 };

            string info = RoiInfoService.BuildInfo(circle, null, 0.01, "mm", registry, includeStatistics: false);

            Assert.DoesNotContain("畸变校正", info);
        }

        [Fact]
        public void ExtendedBrownConradyModel_RoundTripsDistortedPoint()
        {
            var calibration = new CameraCalibration
            {
                PrincipalX = 320,
                PrincipalY = 240,
                NormalizationRadius = 320,
                K1 = 0.08,
                K2 = -0.015,
                K3 = 0.002,
                TangentialP1 = 0.001,
                TangentialP2 = -0.0008
            };
            PointD ideal = new(410, 285);

            PointD distorted = calibration.DistortPoint(ideal);
            PointD restored = calibration.UndistortPoint(distorted);

            Assert.InRange(Math.Abs(restored.X - ideal.X), 0, 1e-6);
            Assert.InRange(Math.Abs(restored.Y - ideal.Y), 0, 1e-6);
            Assert.True(calibration.IsValid);
            Assert.True(calibration.HasExtendedDistortionModel);
        }

        [Fact]
        public void Calibration_WithNonFiniteParameter_IsInvalidAndDoesNotTransform()
        {
            var calibration = new CameraCalibration
            {
                PrincipalX = 10,
                PrincipalY = 10,
                NormalizationRadius = 10,
                K1 = double.NaN
            };
            PointD point = new(14, 12);

            Assert.False(calibration.IsValid);
            Assert.Equal(point, calibration.DistortPoint(point));
            Assert.Equal(point, calibration.UndistortPoint(point));
        }
    }
}
