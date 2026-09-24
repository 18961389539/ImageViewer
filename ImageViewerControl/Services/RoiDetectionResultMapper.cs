using ImageViewer.Models;
using ImageViewer.Rendering;
using ImageViewer.Utils;

namespace ImageViewer.Services
{
    internal static class RoiDetectionResultMapper
    {
        public static void Apply(CaliperMeasureRoi line, LineMeasureGradientDetectionResult detectionResult)
        {
            line.P1 = detectionResult.DetectedP1.ToPointD();
            line.P2 = detectionResult.DetectedP2.ToPointD();
            line.SetDetectionVisualization(CaliperDetectionDisplayStateFactory.Create(line, detectionResult));
        }

        public static void Apply(LineCaliperMeasureRoi line, LineCaliperDetectionResult detectionResult)
        {
            line.P1 = detectionResult.DetectedP1.ToPointD();
            line.P2 = detectionResult.DetectedP2.ToPointD();
            line.SetDetectionVisualization(CaliperDetectionDisplayStateFactory.Create(line, detectionResult));
            line.AngleDegrees = detectionResult.AngleDegrees;
        }

        public static void Apply(CircularCaliperMeasureRoi caliper, CircularCaliperDetectionResult detectionResult)
        {
            caliper.Center = detectionResult.DetectedCenter.ToPointD();
            caliper.Radius = detectionResult.DetectedRadius;
            caliper.SetDetectionVisualization(CaliperDetectionDisplayStateFactory.Create(caliper, detectionResult));
        }
    }
}
