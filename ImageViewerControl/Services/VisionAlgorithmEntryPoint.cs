using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Core.Analysis;
using ImageViewer.Models;

namespace ImageViewer.Services;

/// <summary>
/// WPF edge adapter for the managed analysis algorithms. It converts <see cref="BitmapSource"/>
/// pixels and WPF <see cref="Point"/>/<see cref="Vector"/> values into the framework-neutral
/// Core contracts and maps the results back for the rendering and ROI layer.
/// </summary>
internal static class VisionAlgorithmEntryPoint
{
    public static IntensityImage CreateIntensity(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        BitmapSource source = ImageViewerPixelAccess.NormalizeForIntensity(bitmap);
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        PixelFormat format = source.Format;
        int stride = checked(((width * format.BitsPerPixel) + 31) / 32 * 4);
        byte[] buffer = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        source.CopyPixels(buffer, stride, 0);

        var pixels = GC.AllocateUninitializedArray<ushort>(checked(width * height));
        if (format == PixelFormats.Gray8)
        {
            ConvertGray8(buffer, pixels, width, height, stride);
        }
        else if (format == PixelFormats.Gray16)
        {
            ConvertGray16(buffer, pixels, width, height, stride);
        }
        else if (format == PixelFormats.Bgr24)
        {
            ConvertBgr24(buffer, pixels, width, height, stride);
        }
        else
        {
            ConvertBgra32(buffer, pixels, width, height, stride, format);
        }

        return new IntensityImage(width, height, ImageViewerPixelAccess.GetIntensityMaximum(format), pixels);
    }

    private static void ConvertGray8(byte[] buffer, ushort[] pixels, int width, int height, int stride)
    {
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * stride;
            int outputRow = y * width;
            for (int x = 0; x < width; x++)
            {
                pixels[outputRow + x] = buffer[sourceRow + x];
            }
        }
    }

    private static void ConvertGray16(byte[] buffer, ushort[] pixels, int width, int height, int stride)
    {
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * stride;
            int outputRow = y * width;
            for (int x = 0; x < width; x++)
            {
                int offset = sourceRow + (x * 2);
                pixels[outputRow + x] = (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
            }
        }
    }

    private static void ConvertBgr24(byte[] buffer, ushort[] pixels, int width, int height, int stride)
    {
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * stride;
            int outputRow = y * width;
            for (int x = 0; x < width; x++)
            {
                int offset = sourceRow + (x * 3);
                pixels[outputRow + x] = Luminance(buffer[offset], buffer[offset + 1], buffer[offset + 2]);
            }
        }
    }

    private static void ConvertBgra32(byte[] buffer, ushort[] pixels, int width, int height, int stride, PixelFormat format)
    {
        if (format != PixelFormats.Bgr32 && format != PixelFormats.Bgra32)
        {
            int bytesPerPixel = Math.Max(1, (format.BitsPerPixel + 7) / 8);
            for (int y = 0; y < height; y++)
            {
                int sourceRow = y * stride;
                int outputRow = y * width;
                for (int x = 0; x < width; x++)
                {
                    pixels[outputRow + x] = ImageViewerPixelAccess.ReadIntensity(
                        buffer, sourceRow + (x * bytesPerPixel), bytesPerPixel, format);
                }
            }

            return;
        }

        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * stride;
            int outputRow = y * width;
            for (int x = 0; x < width; x++)
            {
                int offset = sourceRow + (x * 4);
                pixels[outputRow + x] = Luminance(buffer[offset], buffer[offset + 1], buffer[offset + 2]);
            }
        }
    }

    private static ushort Luminance(byte b, byte g, byte r)
    {
        return (ushort)Math.Clamp((int)Math.Round(0.299 * r + 0.587 * g + 0.114 * b), 0, byte.MaxValue);
    }

    public static bool TryFitLine(
        Point[] points,
        LineFitMode fitMode,
        int clippingEndPoints,
        out LineSegmentOverlay segment)
    {
        ArgumentNullException.ThrowIfNull(points);
        segment = default;
        if (!RobustFitAlgorithm.TryFitLine(ToPointD(points), fitMode, clippingEndPoints, out DetectedLineSegment fitted))
        {
            return false;
        }

        segment = new LineSegmentOverlay(fitted.Start, fitted.End);
        return true;
    }

    public static bool TryFitCircle(
        Point[] points,
        LineFitMode fitMode,
        out Point center,
        out double radius)
    {
        ArgumentNullException.ThrowIfNull(points);
        center = default;
        radius = 0;
        if (!RobustFitAlgorithm.TryFitCircle(ToPointD(points), fitMode, out PointD detectedCenter, out double detectedRadius))
        {
            return false;
        }

        center = new Point(detectedCenter.X, detectedCenter.Y);
        radius = detectedRadius;
        return true;
    }

    public static bool TryMeasureSingle(
        IntensityImage image,
        Point center,
        Vector measurementDirection,
        int searchRange,
        int averagingHalfWidth,
        double sigma,
        double minimumGradient,
        CaliperEdgePolarity polarity,
        int edgeSelection,
        out CaliperEdge sample) =>
        CaliperAlgorithm.TryMeasureSingle(
            image,
            new PointD(center.X, center.Y),
            new VectorD(measurementDirection.X, measurementDirection.Y),
            searchRange,
            averagingHalfWidth,
            sigma,
            minimumGradient,
            polarity,
            edgeSelection,
            out sample);

    public static bool TryMeasurePair(
        IntensityImage image,
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
        out CaliperEdgePair pair) =>
        CaliperAlgorithm.TryMeasurePair(
            image,
            new PointD(center.X, center.Y),
            new VectorD(measurementDirection.X, measurementDirection.Y),
            searchRange,
            averagingHalfWidth,
            sigma,
            minimumGradient,
            polarity,
            minimumEdgeGap,
            nominalEdgeGap,
            nominalEdgeGapTolerance,
            out pair);

    /// <summary>
    /// Measures one radial edge sample using a rectangular caliper oriented along the
    /// radial direction at the nominal radius.
    /// </summary>
    public static bool TryMeasureCircular(
        IntensityImage image,
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
        out CaliperEdge sample) =>
        CaliperAlgorithm.TryMeasureCircular(
            image,
            new PointD(center.X, center.Y),
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

    public static IReadOnlyList<BlobFeature> ExtractBlobs(
        IntensityImage image,
        bool useOtsu,
        int threshold,
        bool detectDark,
        int minArea) =>
        BlobAlgorithm.ExtractRegions(image, useOtsu, threshold, detectDark, minArea);

    private static PointD[] ToPointD(Point[] points)
    {
        var converted = new PointD[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            converted[i] = new PointD(points[i].X, points[i].Y);
        }

        return converted;
    }
}
