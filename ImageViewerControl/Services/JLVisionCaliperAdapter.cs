using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ImageViewer.Models;
using ImageViewer.Utils;
using JLVisionLib;

namespace ImageViewer.Services;

internal readonly record struct JLVisionEdgeSample(Point Point, double Score);

internal readonly record struct JLVisionEdgePair(
    JLVisionEdgeSample Edge1,
    JLVisionEdgeSample Edge2,
    double Gap);

/// <summary>
/// Runs JLVision one-dimensional rectangular calipers and maps their row/column
/// results into ImageViewer's x/y coordinate system.
/// </summary>
internal static class JLVisionCaliperAdapter
{
    private const string Interpolation = "nearest_neighbor";

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
        out JLVisionEdgeSample sample)
    {
        sample = default;
        if (!TryNormalizeDirection(measurementDirection, out Vector direction) || searchRange <= 0)
        {
            return false;
        }

        try
        {
            using var measure = CreateMeasure(
                center,
                direction,
                searchRange,
                averagingHalfWidth,
                imageWidth,
                imageHeight);
            measure.MeasurePos(
                image,
                Math.Max(0.5, sigma),
                Math.Max(0, minimumGradient),
                ToTransition(polarity),
                "all",
                out JlTuple rowEdge,
                out JlTuple columnEdge,
                out JlTuple amplitude,
                out JlTuple distance);
            using (rowEdge)
            using (columnEdge)
            using (amplitude)
            using (distance)
            {
                double[] rows = rowEdge.ToDArr();
                double[] columns = columnEdge.ToDArr();
                double[] amplitudes = amplitude.ToDArr();
                int count = Math.Min(rows.Length, Math.Min(columns.Length, amplitudes.Length));
                if (count == 0)
                {
                    return false;
                }

                var candidates = new List<JLVisionEdgeSample>(count);
                for (int i = 0; i < count; i++)
                {
                    if (!IsFinite(rows[i]) || !IsFinite(columns[i]) || !IsFinite(amplitudes[i]))
                    {
                        continue;
                    }

                    candidates.Add(new JLVisionEdgeSample(
                        new Point(columns[i], rows[i]),
                        Math.Abs(amplitudes[i])));
                }

                if (candidates.Count == 0)
                {
                    return false;
                }

                candidates.Sort(static (left, right) => right.Score.CompareTo(left.Score));
                int selectedIndex = Math.Clamp(edgeSelection, 1, candidates.Count) - 1;
                sample = candidates[selectedIndex];
                return true;
            }
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
        out JLVisionEdgePair pair)
    {
        pair = default;
        if (!TryNormalizeDirection(measurementDirection, out Vector direction) || searchRange <= 0)
        {
            return false;
        }

        try
        {
            using var measure = CreateMeasure(
                center,
                direction,
                searchRange,
                averagingHalfWidth,
                imageWidth,
                imageHeight);
            measure.MeasurePairs(
                image,
                Math.Max(0.5, sigma),
                Math.Max(0, minimumGradient),
                ToTransition(polarity),
                "all",
                out JlTuple rowEdgeFirst,
                out JlTuple columnEdgeFirst,
                out JlTuple amplitudeFirst,
                out JlTuple rowEdgeSecond,
                out JlTuple columnEdgeSecond,
                out JlTuple amplitudeSecond,
                out JlTuple intraDistance,
                out JlTuple interDistance);
            using (rowEdgeFirst)
            using (columnEdgeFirst)
            using (amplitudeFirst)
            using (rowEdgeSecond)
            using (columnEdgeSecond)
            using (amplitudeSecond)
            using (intraDistance)
            using (interDistance)
            {
                double[] firstRows = rowEdgeFirst.ToDArr();
                double[] firstColumns = columnEdgeFirst.ToDArr();
                double[] firstAmplitudes = amplitudeFirst.ToDArr();
                double[] secondRows = rowEdgeSecond.ToDArr();
                double[] secondColumns = columnEdgeSecond.ToDArr();
                double[] secondAmplitudes = amplitudeSecond.ToDArr();
                double[] gaps = intraDistance.ToDArr();
                int count = new[]
                {
                    firstRows.Length,
                    firstColumns.Length,
                    firstAmplitudes.Length,
                    secondRows.Length,
                    secondColumns.Length,
                    secondAmplitudes.Length
                }.Min();
                if (count == 0)
                {
                    return false;
                }

                var candidates = new List<PairCandidate>(count);
                for (int i = 0; i < count; i++)
                {
                    if (!IsFinite(firstRows[i]) || !IsFinite(firstColumns[i]) ||
                        !IsFinite(firstAmplitudes[i]) || !IsFinite(secondRows[i]) ||
                        !IsFinite(secondColumns[i]) || !IsFinite(secondAmplitudes[i]))
                    {
                        continue;
                    }

                    Point first = new(firstColumns[i], firstRows[i]);
                    Point second = new(secondColumns[i], secondRows[i]);
                    double firstProjection = Dot(first - center, direction);
                    double secondProjection = Dot(second - center, direction);
                    if (firstProjection > secondProjection)
                    {
                        (first, second) = (second, first);
                        (firstProjection, secondProjection) = (secondProjection, firstProjection);
                    }

                    double gap = IsFiniteAt(gaps, i)
                        ? Math.Abs(gaps[i])
                        : GeometryUtils.Distance(first, second);
                    if (gap < Math.Max(0, minimumEdgeGap))
                    {
                        continue;
                    }

                    double firstScore = Math.Abs(firstAmplitudes[i]);
                    double secondScore = Math.Abs(secondAmplitudes[i]);
                    double score = 0.7 * (firstScore + secondScore) + 0.3 * Math.Min(firstScore, secondScore);
                    if (nominalEdgeGap > 0)
                    {
                        double deviation = Math.Abs(gap - nominalEdgeGap) - Math.Max(0, nominalEdgeGapTolerance);
                        if (deviation > 0)
                        {
                            score -= deviation * 1.25;
                        }
                    }

                    candidates.Add(new PairCandidate(
                        new JLVisionEdgeSample(first, firstScore),
                        new JLVisionEdgeSample(second, secondScore),
                        gap,
                        firstProjection <= 0 && secondProjection >= 0,
                        Math.Abs((firstProjection + secondProjection) / 2),
                        score));
                }

                if (candidates.Count == 0)
                {
                    return false;
                }

                List<PairCandidate> centeredCandidates = candidates
                    .Where(candidate => candidate.StraddlesCenter)
                    .ToList();
                if (centeredCandidates.Count == 0)
                {
                    return false;
                }

                PairCandidate selected = centeredCandidates
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.CenterOffset)
                    .First();
                pair = new JLVisionEdgePair(selected.Edge1, selected.Edge2, selected.Gap);
                return true;
            }
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

    /// <summary>
    /// Measures one radial sample with a native JLVision arc measure. The annulus
    /// half-width is the radial search range; the local angular extent represents
    /// the configured tangential averaging width.
    /// </summary>
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
        out JLVisionEdgeSample sample)
    {
        sample = default;
        if (!IsFinite(nominalRadius) || nominalRadius <= 0 || !IsFinite(angleRadians) ||
            !IsFinite(angleExtent) || angleExtent <= 0 || searchRange <= 0)
        {
            return false;
        }

        try
        {
            using var measure = new JlMeasure(
                center.Y,
                center.X,
                nominalRadius,
                angleRadians - Math.Clamp(angleExtent, 1e-3, 2 * Math.PI) / 2,
                Math.Clamp(angleExtent, 1e-3, 2 * Math.PI),
                Math.Max(0.5, searchRange),
                imageWidth,
                imageHeight,
                Interpolation);
            measure.MeasurePos(
                image,
                Math.Max(0.5, sigma),
                Math.Max(0, minimumGradient),
                ToTransition(polarity),
                "all",
                out JlTuple rowEdge,
                out JlTuple columnEdge,
                out JlTuple amplitude,
                out JlTuple distance);
            using (rowEdge)
            using (columnEdge)
            using (amplitude)
            using (distance)
            {
                double[] rows = rowEdge.ToDArr();
                double[] columns = columnEdge.ToDArr();
                double[] amplitudes = amplitude.ToDArr();
                int count = Math.Min(rows.Length, Math.Min(columns.Length, amplitudes.Length));
                if (count == 0)
                {
                    return false;
                }

                var candidates = new List<JLVisionEdgeSample>(count);
                for (int i = 0; i < count; i++)
                {
                    if (!IsFinite(rows[i]) || !IsFinite(columns[i]) || !IsFinite(amplitudes[i]))
                    {
                        continue;
                    }

                    candidates.Add(new JLVisionEdgeSample(
                        new Point(columns[i], rows[i]),
                        Math.Abs(amplitudes[i])));
                }

                if (candidates.Count == 0)
                {
                    return false;
                }

                candidates.Sort(static (left, right) => right.Score.CompareTo(left.Score));
                int selectedIndex = Math.Clamp(edgeSelection, 1, candidates.Count) - 1;
                sample = candidates[selectedIndex];
                return true;
            }
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

    private static JlMeasure CreateMeasure(
        Point center,
        Vector measurementDirection,
        int searchRange,
        int averagingHalfWidth,
        int imageWidth,
        int imageHeight)
    {
        double phi = Math.Atan2(measurementDirection.Y, measurementDirection.X);
        return new JlMeasure(
            center.Y,
            center.X,
            phi,
            searchRange,
            Math.Max(0.5, averagingHalfWidth),
            imageWidth,
            imageHeight,
            Interpolation);
    }

    private static string ToTransition(CaliperEdgePolarity polarity) => polarity switch
    {
        CaliperEdgePolarity.DarkToLight => "positive",
        CaliperEdgePolarity.LightToDark => "negative",
        _ => "all"
    };

    private static bool TryNormalizeDirection(Vector direction, out Vector normalized)
    {
        normalized = direction;
        if (normalized.LengthSquared < 1e-9)
        {
            return false;
        }

        normalized.Normalize();
        return true;
    }

    private static double Dot(Vector value, Vector direction) =>
        value.X * direction.X + value.Y * direction.Y;

    private static bool IsFiniteAt(double[] values, int index) =>
        index < values.Length && IsFinite(values[index]);

    private static bool IsFinite(double value) => double.IsFinite(value);

    private static bool IsNativeFailure(Exception exception) =>
        exception is DllNotFoundException or BadImageFormatException or
        EntryPointNotFoundException or JlException or JlOperatorException or
        TypeInitializationException;

    private static bool IsRuntimeLoadFailure(Exception exception) =>
        exception is DllNotFoundException or BadImageFormatException or
        EntryPointNotFoundException or TypeInitializationException;

    private readonly record struct PairCandidate(
        JLVisionEdgeSample Edge1,
        JLVisionEdgeSample Edge2,
        double Gap,
        bool StraddlesCenter,
        double CenterOffset,
        double Score);
}
