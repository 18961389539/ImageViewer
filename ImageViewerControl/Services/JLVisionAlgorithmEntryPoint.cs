using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using JLVisionLib;

namespace ImageViewer.Services;

/// <summary>
/// Single entry point for all image algorithms backed by JLVision.
/// Higher-level analysis services use this facade instead of selecting a
/// bridge, fitting adapter, or native region operator themselves.
/// </summary>
internal static class JLVisionAlgorithmEntryPoint
{
    public static JLVisionImageHandle CreateIntensity(BitmapSource bitmap) =>
        JLVisionImageBridge.CreateIntensity(bitmap);

    public static bool TryFitLine(
        Point[] points,
        JLVisionLineFitMode fitMode,
        int clippingEndPoints,
        out LineSegmentOverlay segment) =>
        JLVisionFitAdapter.TryFitLine(points, fitMode, clippingEndPoints, out segment);

    public static bool TryFitCircle(
        Point[] points,
        out Point center,
        out double radius) =>
        JLVisionFitAdapter.TryFitCircle(points, out center, out radius);

    public static bool TryMeasureSingle(
        JlImage image,
        int imageWidth,
        int imageHeight,
        Point center,
        Vector measurementDirection,
        int searchRange,
        int averagingHalfWidth,
        double sigma,
        double minimumGradient,
        CaliperEdgePolarity polarity,
        int edgeSelection,
        out JLVisionEdgeSample sample) =>
        JLVisionCaliperAdapter.TryMeasureSingle(
            image,
            imageWidth,
            imageHeight,
            center,
            measurementDirection,
            searchRange,
            averagingHalfWidth,
            sigma,
            minimumGradient,
            polarity,
            edgeSelection,
            out sample);

    public static bool TryMeasurePair(
        JlImage image,
        int imageWidth,
        int imageHeight,
        Point center,
        Vector measurementDirection,
        int searchRange,
        int averagingHalfWidth,
        double sigma,
        double minimumGradient,
        CaliperEdgePolarity polarity,
        double minimumEdgeGap,
        double nominalEdgeGap,
        double nominalEdgeGapTolerance,
        out JLVisionEdgePair pair) =>
        JLVisionCaliperAdapter.TryMeasurePair(
            image,
            imageWidth,
            imageHeight,
            center,
            measurementDirection,
            searchRange,
            averagingHalfWidth,
            sigma,
            minimumGradient,
            polarity,
            minimumEdgeGap,
            nominalEdgeGap,
            nominalEdgeGapTolerance,
            out pair);

    public static bool TryMeasureCircular(
        JlImage image,
        int imageWidth,
        int imageHeight,
        Point center,
        double nominalRadius,
        double angleRadians,
        double angleExtent,
        int searchRange,
        int averagingHalfWidth,
        double sigma,
        double minimumGradient,
        CaliperEdgePolarity polarity,
        int edgeSelection,
        out JLVisionEdgeSample sample) =>
        JLVisionCaliperAdapter.TryMeasureCircular(
            image,
            imageWidth,
            imageHeight,
            center,
            nominalRadius,
            angleRadians,
            angleExtent,
            searchRange,
            averagingHalfWidth,
            sigma,
            minimumGradient,
            polarity,
            edgeSelection,
            out sample);

    public static JlRegion SegmentBlob(
        JLVisionImageHandle image,
        bool useOtsu,
        int threshold,
        bool detectDark)
    {
        if (useOtsu)
        {
            return image.Image.BinaryThreshold(
                "max_separability",
                detectDark ? "dark" : "light",
                out int _);
        }

        double maximum = image.SourceFormat == PixelFormats.Gray16
            ? ushort.MaxValue
            : byte.MaxValue;
        double effectiveThreshold = Math.Clamp(threshold, 0, byte.MaxValue);
        if (maximum == ushort.MaxValue)
        {
            effectiveThreshold *= 257.0;
        }

        return detectDark
            ? image.Image.Threshold(0.0, effectiveThreshold)
            : image.Image.Threshold(effectiveThreshold, maximum);
    }

    public static JlRegion ConnectRegion(JlRegion region) => region.Connection();

    public static JlRegion SelectBlobArea(JlRegion connected, int minArea) =>
        connected.SelectShape("area", "and", Math.Max(1, minArea), int.MaxValue);

    public static int GetAreaCenter(JlRegion blob, out double row, out double column) =>
        blob.AreaCenter(out row, out column);

    public static void GetBoundingRectangle(
        JlRegion blob,
        out int row1,
        out int column1,
        out int row2,
        out int column2) =>
        blob.SmallestRectangle1(out row1, out column1, out row2, out column2);
}
