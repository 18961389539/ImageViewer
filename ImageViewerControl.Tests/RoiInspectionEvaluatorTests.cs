using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class RoiInspectionEvaluatorTests
    {
        [Fact]
        public void DetectedWidthUsesDetectionAndSpecificationStatusesIndependently()
        {
            var roi = new CaliperMeasureRoi
            {
                P1 = new PointD(0, 0),
                P2 = new PointD(20, 0),
                HasDetectedEdges = true,
                Tolerance = new MeasurementTolerance { Nominal = 10, TolerancePlus = 0.5, ToleranceMinus = 0.5 }
            };
            roi.ApplyWidthMeasurement(new CaliperWidthMeasurementResult([9.9, 10.0, 10.1], centerDistance: 10));

            RoiInspectionResult result = RoiInspectionEvaluator.Evaluate(roi, pixelSize: 1);

            Assert.Equal(RoiDetectionStatus.Passed, result.DetectionStatus);
            Assert.Equal(RoiSpecificationStatus.Passed, result.SpecificationStatus);
            Assert.Equal(10, result.MeasuredValue);
            Assert.Equal(10, result.NominalValue);
        }

        [Fact]
        public void UnmeasuredCircularCaliperCannotProduceSpecificationDecision()
        {
            var roi = new CircularCaliperMeasureRoi
            {
                Radius = 5,
                Tolerance = new MeasurementTolerance { Nominal = 5, TolerancePlus = 0.1, ToleranceMinus = 0.1 }
            };

            RoiInspectionResult result = RoiInspectionEvaluator.Evaluate(roi, pixelSize: 1);

            Assert.Equal(RoiDetectionStatus.NotMeasured, result.DetectionStatus);
            Assert.Equal(RoiSpecificationStatus.NotEvaluable, result.SpecificationStatus);
            Assert.Null(result.MeasuredValue);
        }

        [Fact]
        public void GroundTruthLineMeasurement_AppliesPixelScaleAndTolerance()
        {
            var roi = new LineMeasureRoi
            {
                P1 = new PointD(10, 20),
                P2 = new PointD(13, 24),
                Tolerance = new MeasurementTolerance { Nominal = 2.5, TolerancePlus = 0.01, ToleranceMinus = 0.01 }
            };

            RoiInspectionResult result = RoiInspectionEvaluator.Evaluate(roi, pixelSize: 0.5);

            Assert.Equal(2.5, result.MeasuredValue!.Value, precision: 10);
            Assert.Equal(RoiDetectionStatus.NotApplicable, result.DetectionStatus);
            Assert.Equal(RoiSpecificationStatus.Passed, result.SpecificationStatus);
        }

        [Fact]
    public void CsvExportsBothStatusesAsSeparateColumns()
        {
            var roi = new LineMeasureRoi
            {
                P1 = new PointD(0, 0),
                P2 = new PointD(10, 0),
                Tolerance = new MeasurementTolerance { Nominal = 10, TolerancePlus = 0, ToleranceMinus = 0 }
            };

            string csv = RoiAnalysisExportService.BuildCsv([roi], bitmap: null, pixelSize: 1, physicalUnit: "mm");

            Assert.Contains("DetectionStatus,SpecificationStatus,MeasuredValue,NominalValue", csv);
            Assert.Contains("\"NotApplicable\",\"Passed\",10,10", csv);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void CsvRejectsInvalidPixelSize(double pixelSize)
        {
            var roi = new LineMeasureRoi { P1 = new PointD(0, 0), P2 = new PointD(1, 0) };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RoiAnalysisExportService.BuildCsv([roi], bitmap: null, pixelSize, physicalUnit: "mm"));
        }

        [Fact]
        public void InfoRejectsInvalidPixelSize()
        {
            var roi = new LineMeasureRoi { P1 = new PointD(0, 0), P2 = new PointD(1, 0) };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RoiInfoService.BuildInfo(roi, bitmap: null, pixelSize: 0, physicalUnit: "mm", pluginRegistry: RoiPluginRegistry.CreateBuiltIn(), includeStatistics: false));
        }

        [Fact]
        public void InfoAppendsToleranceJudgementWhenPluginSuppliesCustomLines()
        {
            var roi = new LineMeasureRoi
            {
                P1 = new PointD(0, 0),
                P2 = new PointD(10, 0),
                Tolerance = new MeasurementTolerance { Nominal = 10, TolerancePlus = 0.1, ToleranceMinus = 0.1 }
            };
            var registry = new RoiPluginRegistry();
            registry.Register(new CustomInfoPlugin());

            string info = RoiInfoService.BuildInfo(roi, bitmap: null, pixelSize: 1, physicalUnit: "mm", pluginRegistry: registry, includeStatistics: false);

            Assert.Contains("custom plugin info", info);
            Assert.Contains("合格", info);
        }

        [Fact]
        public void InvalidEnabledCalibrationCannotProduceSpecificationDecision()
        {
            var roi = new LineMeasureRoi
            {
                P1 = new PointD(0, 0),
                P2 = new PointD(10, 0),
                Tolerance = new MeasurementTolerance { Nominal = 10, TolerancePlus = 0.1, ToleranceMinus = 0.1 }
            };
            var calibration = new CameraCalibration
            {
                PrincipalX = 0,
                PrincipalY = 0,
                NormalizationRadius = 1,
                K1 = 0.1,
                ReprojectionErrorRms = double.NaN
            };

            RoiInspectionResult result = RoiInspectionEvaluator.Evaluate(roi, 1, calibration);

            Assert.Null(result.MeasuredValue);
            Assert.Equal(RoiSpecificationStatus.NotEvaluable, result.SpecificationStatus);
        }

        private sealed class CustomInfoPlugin : IRoiPlugin
        {
            public string TypeKey => "custom-line-info";

            public Type RoiType => typeof(LineMeasureRoi);

            public int HitTestOrder => 0;

            public IReadOnlyList<RoiToolDescriptor> DrawingTools => Array.Empty<RoiToolDescriptor>();

            public ImageViewer.Abstractions.IRoiBehavior Behavior => null!;

            public ImageViewer.Rendering.IRoiRenderer Renderer => null!;

            public IEnumerable<RoiBase> GetRois(ImageViewer.ViewModels.ImageViewerViewModel viewModel) => throw new NotSupportedException();

            public void ClearCollection(ImageViewer.ViewModels.ImageViewerViewModel viewModel) => throw new NotSupportedException();

            public bool AddToCollection(ImageViewer.ViewModels.ImageViewerViewModel viewModel, RoiBase roi) => throw new NotSupportedException();

            public bool RemoveFromCollection(ImageViewer.ViewModels.ImageViewerViewModel viewModel, RoiBase roi) => throw new NotSupportedException();

            public RoiBase CreateRoi(RoiPersistenceData data) => throw new NotSupportedException();

            public void PopulatePersistenceData(RoiBase roi, RoiPersistenceData data) => throw new NotSupportedException();

            public IReadOnlyList<string> BuildInfoLines(RoiBase roi, BitmapSource? bitmap, double pixelSize, string? physicalUnit)
                => ["custom plugin info"];

            public FrameworkElement? CreatePropertyEditor(RoiBase roi) => null;
        }
    }
}
