using ImageViewer.Models;
using CoreCircularDetectionResult = ImageViewer.Core.Analysis.CircularCaliperDetectionResult;
using CoreLineCaliperDetectionResult = ImageViewer.Core.Analysis.LineCaliperDetectionResult;
using CoreLineMeasureDetectionResult = ImageViewer.Core.Analysis.LineMeasureGradientDetectionResult;
using ImageViewer.Rendering;
using ImageViewer.Utils;

namespace ImageViewer.Services
{
    internal static class RoiDetectionResultMapper
    {
        public static void Apply(CaliperMeasureRoi line, CoreLineMeasureDetectionResult detectionResult)
        {
            line.P1 = detectionResult.DetectedP1;
            line.P2 = detectionResult.DetectedP2;
            line.SetDetectionVisualization(CaliperDetectionDisplayStateFactory.Create(line, detectionResult));
            line.ApplyWidthMeasurement(new CaliperWidthMeasurementResult(
                detectionResult.WidthSamples,
                GeometryUtils.Distance(detectionResult.DetectedP1.ToWpfPoint(), detectionResult.DetectedP2.ToWpfPoint())));
        }

        public static void Apply(LineCaliperMeasureRoi line, CoreLineCaliperDetectionResult detectionResult)
        {
            line.P1 = detectionResult.DetectedP1;
            line.P2 = detectionResult.DetectedP2;
            line.SetDetectionVisualization(CaliperDetectionDisplayStateFactory.Create(line, detectionResult));
            line.AngleDegrees = detectionResult.AngleDegrees;
        }

        public static void Apply(CircularCaliperMeasureRoi caliper, CoreCircularDetectionResult detectionResult, ImageAnalysisQualityProfile? profile = null)
        {
            caliper.Center = detectionResult.DetectedCenter;
            caliper.Radius = detectionResult.DetectedRadius;
            caliper.SetDetectionVisualization(CaliperDetectionDisplayStateFactory.Create(caliper, detectionResult));
            if (caliper is not ArcCaliperMeasureRoi)
            {
                caliper.ApplyQualityAssessment(CircularCaliperQualityGate.Evaluate(caliper, detectionResult, profile));
            }
        }
    }
}
