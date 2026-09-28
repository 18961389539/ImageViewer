using System;
using System.IO;
using System.Windows;
using ImageViewer.Models;
using JLVisionLib;

namespace ImageViewer.Services
{
    /// <summary>
    /// JLVision native fitting adapter.
    /// Chinese: 把 ImageViewer 的点序列转换为 JLVision XLD 轮廓，使用原生
    /// fit_line_contour_xld / fit_circle_contour_xld，并把 JLVision 的 row/column
    /// 坐标转换回 ImageViewer 的 x/y 坐标。
    /// English: Bridges ImageViewer point sequences to JLVision XLD fitting operators.
    /// </summary>
    internal static class JLVisionFitAdapter
    {
        /// <summary>
        /// Runs <c>fit_line_contour_xld</c> through JLVision.
        /// </summary>
        /// <param name="fitMode">Regression maps to <c>regression</c>; Huber, Tukey, Drop and Gauss map to the corresponding native robust algorithms.</param>
        /// <param name="clippingEndPoints">Number of points removed from each contour end before fitting. It does not change the returned fitted segment.</param>
        public static bool TryFitLine(
            Point[] points,
            JLVisionLineFitMode fitMode,
            int clippingEndPoints,
            out LineSegmentOverlay segment)
        {
            segment = default;
            if (points.Length < 2)
            {
                return false;
            }

            EnsureNativeLibrary();

            try
            {
                double[] rows = new double[points.Length];
                double[] columns = new double[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    rows[i] = points[i].Y;
                    columns[i] = points[i].X;
                }

                using var rowTuple = new JlTuple(rows);
                using var columnTuple = new JlTuple(columns);
                using var contour = new JlXLDCont(rowTuple, columnTuple);
                contour.FitLineContourXld(
                    GetLineAlgorithm(fitMode),
                    maxNumPoints: -1,
                    clippingEndPoints: Math.Max(0, clippingEndPoints),
                    iterations: GetIterations(fitMode),
                    clippingFactor: GetClippingFactor(fitMode),
                    out double rowBegin,
                    out double columnBegin,
                    out double rowEnd,
                    out double columnEnd,
                    out _,
                    out _,
                    out _);

                if (!IsFinite(rowBegin) || !IsFinite(columnBegin) || !IsFinite(rowEnd) || !IsFinite(columnEnd))
                {
                    return false;
                }

                segment = new LineSegmentOverlay(
                    new PointD(columnBegin, rowBegin),
                    new PointD(columnEnd, rowEnd));
                return IsFinite(segment.Start) && IsFinite(segment.End) &&
                       DistanceSquared(segment.Start, segment.End) > 1e-12;
            }
            catch (Exception exception) when (IsNativeFailure(exception))
            {
                return false;
            }
        }

        /// <summary>
        /// Runs <c>fit_circle_contour_xld</c> through JLVision's geometric fit.
        /// </summary>
        /// <param name="points">Ordered XLD contour samples in ImageViewer x/y coordinates.</param>
        /// <param name="center">Fitted center in ImageViewer x/y coordinates.</param>
        /// <param name="radius">Fitted radius in pixels.</param>
        public static bool TryFitCircle(
            Point[] points,
            out Point center,
            out double radius)
        {
            center = default;
            radius = 0;
            if (points.Length < 3)
            {
                return false;
            }

            EnsureNativeLibrary();

            try
            {
                double[] rows = new double[points.Length];
                double[] columns = new double[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    rows[i] = points[i].Y;
                    columns[i] = points[i].X;
                }

                using var rowTuple = new JlTuple(rows);
                using var columnTuple = new JlTuple(columns);
                using var contour = new JlXLDCont(rowTuple, columnTuple);
                contour.FitCircleContourXld(
                    algorithm: "geometric",
                    maxNumPoints: -1,
                    maxClosureDist: 0.0,
                    clippingEndPoints: 0,
                    iterations: 3,
                    clippingFactor: 2.0,
                    out double row,
                    out double column,
                    out double fittedRadius,
                    out _,
                    out _,
                    out _);

                if (!IsFinite(row) || !IsFinite(column) || !IsFinite(fittedRadius) || fittedRadius <= 0)
                {
                    return false;
                }

                center = new Point(column, row);
                radius = fittedRadius;
                return true;
            }
            catch (Exception exception) when (IsNativeFailure(exception))
            {
                if (IsRuntimeLoadFailure(exception))
                {
                    throw new InvalidOperationException(
                        JLVisionRuntimeDiagnostics.BuildLoadFailureMessage(exception),
                        exception);
                }
                return false;
            }
        }

        private static string GetLineAlgorithm(JLVisionLineFitMode fitMode) => fitMode switch
        {
            JLVisionLineFitMode.Huber => "huber",
            JLVisionLineFitMode.Tukey => "tukey",
            JLVisionLineFitMode.Drop => "drop",
            JLVisionLineFitMode.Gauss => "gauss",
            _ => "regression"
        };

        private static int GetIterations(JLVisionLineFitMode fitMode) =>
            fitMode == JLVisionLineFitMode.Regression ? 0 : 3;

        private static double GetClippingFactor(JLVisionLineFitMode fitMode) => fitMode switch
        {
            JLVisionLineFitMode.Huber => 1.0,
            JLVisionLineFitMode.Tukey => 2.0,
            _ => 2.0
        };

        private static void EnsureNativeLibrary()
        {
            if (!File.Exists(JLVisionRuntimeDiagnostics.RuntimePath))
            {
                throw new DllNotFoundException(JLVisionRuntimeDiagnostics.BuildMissingRuntimeMessage());
            }
        }

        private static bool IsNativeFailure(Exception exception) =>
            exception is DllNotFoundException or BadImageFormatException or
            EntryPointNotFoundException or JlException or JlOperatorException or
            TypeInitializationException;

        private static bool IsRuntimeLoadFailure(Exception exception) =>
            exception is DllNotFoundException or BadImageFormatException or
            EntryPointNotFoundException or TypeInitializationException;

        private static bool IsFinite(PointD point) => IsFinite(point.X) && IsFinite(point.Y);

        private static bool IsFinite(double value) => double.IsFinite(value);

        private static double DistanceSquared(PointD first, PointD second)
        {
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return dx * dx + dy * dy;
        }

    }
}
