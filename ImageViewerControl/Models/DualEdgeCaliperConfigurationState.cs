namespace ImageViewer.Models
{
    internal sealed record DualEdgeCaliperConfigurationState(
        PointD CaliperCenter,
        double CaliperAngleDegrees,
        bool HasExplicitCaliperRegion,
        int CaliperCount,
        int CaliperSearchRange,
        int CaliperSamplingHalfWidth,
        double CaliperEdgeSigma,
        double CaliperRegionLength,
        double CaliperMinimumGradient,
        int MinimumValidCalipers,
        double CaliperOutlierThreshold,
        CaliperEdgePolarity CaliperEdgePolarity,
        HalconEdgeExtractionMode CaliperEdgeExtractionMode,
        HalconLineFitMode CaliperLineFitMode,
        int CaliperFitClippingEndPoints)
    {
        public static DualEdgeCaliperConfigurationState Capture(CaliperMeasureRoi roi)
        {
            return new DualEdgeCaliperConfigurationState(
                roi.CaliperCenter,
                roi.CaliperAngleDegrees,
                roi.HasExplicitCaliperRegion,
                roi.CaliperCount,
                roi.CaliperSearchRange,
                roi.CaliperSamplingHalfWidth,
                roi.CaliperEdgeSigma,
                roi.CaliperRegionLength,
                roi.CaliperMinimumGradient,
                roi.MinimumValidCalipers,
                roi.CaliperOutlierThreshold,
                roi.CaliperEdgePolarity,
                roi.CaliperEdgeExtractionMode,
                roi.CaliperLineFitMode,
                roi.CaliperFitClippingEndPoints);
        }

        public void ApplyTo(CaliperMeasureRoi roi)
        {
            roi.CaliperCenter = CaliperCenter;
            roi.CaliperAngleDegrees = CaliperAngleDegrees;
            roi.HasExplicitCaliperRegion = HasExplicitCaliperRegion;
            roi.CaliperCount = CaliperCount;
            roi.CaliperSearchRange = CaliperSearchRange;
            roi.CaliperSamplingHalfWidth = CaliperSamplingHalfWidth;
            roi.CaliperEdgeSigma = CaliperEdgeSigma;
            roi.CaliperRegionLength = CaliperRegionLength;
            roi.CaliperMinimumGradient = CaliperMinimumGradient;
            roi.MinimumValidCalipers = MinimumValidCalipers;
            roi.CaliperOutlierThreshold = CaliperOutlierThreshold;
            roi.CaliperEdgePolarity = CaliperEdgePolarity;
            roi.CaliperEdgeExtractionMode = CaliperEdgeExtractionMode;
            roi.CaliperLineFitMode = CaliperLineFitMode;
            roi.CaliperFitClippingEndPoints = CaliperFitClippingEndPoints;
        }
    }
}
