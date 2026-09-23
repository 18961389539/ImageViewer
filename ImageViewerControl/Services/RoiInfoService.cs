using System;
using System.Collections.Generic;
using System.Linq;
using ImageViewer.Models;
using ImageViewer.Localization;
using ImageViewer.Plugins;
using ImageViewer.Utils;
using System.Windows.Media.Imaging;

namespace ImageViewer.Services
{
    public static class RoiInfoService
    {
        public static string BuildInfo(RoiBase roi, BitmapSource? bitmap, double pixelSize, string? physicalUnit, RoiPluginRegistry? pluginRegistry = null, bool includeStatistics = true, CameraCalibration? calibration = null)
        {
            ArgumentNullException.ThrowIfNull(roi);
            var roiPlugins = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
            List<string> lines = new();
            if (!string.IsNullOrWhiteSpace(roi.Label))
            {
                lines.Add(roi.Label);
            }

            var pluginLines = roiPlugins.FindByRoi(roi)?.BuildInfoLines(roi, bitmap, pixelSize, physicalUnit);
            if (pluginLines is { Count: > 0 })
            {
                lines.AddRange(pluginLines);
            }
            else
            {
                AppendBuiltInInfoLines(lines, roi, pixelSize, physicalUnit, calibration);
            }

            if (includeStatistics && bitmap != null && ImageAnalysisService.TryCalculateStatistics(bitmap, roi, out RoiStatistics statistics))
            {
                AppendStatisticsLines(lines, statistics);
            }

            AppendCalibrationLine(lines, pixelSize, physicalUnit, calibration);

            return string.Join(Environment.NewLine, lines);
        }

        public static bool TryBuildStatisticsText(BitmapSource? bitmap, RoiBase roi, out string text)
        {
            text = string.Empty;
            if (bitmap == null || !ImageAnalysisService.TryCalculateStatistics(bitmap, roi, out RoiStatistics statistics))
            {
                return false;
            }

            List<string> lines = new();
            AppendStatisticsLines(lines, statistics);
            text = string.Join(Environment.NewLine, lines);
            return true;
        }

        private static void AppendBuiltInInfoLines(List<string> lines, RoiBase roi, double pixelSize, string? physicalUnit, CameraCalibration? calibration)
        {
            double correction = RoiCalibrationHelper.GetLengthCorrection(roi, calibration);
            double areaCorrection = correction * correction;

            switch (roi)
            {
                case BlobAnalysisRoi blob:
                    AppendBlobAnalysisInfoLines(lines, blob, pixelSize, physicalUnit, correction, areaCorrection);
                    break;
                case CaliperMeasureRoi caliper:
                    AppendDualEdgeCaliperInfoLines(lines, caliper, pixelSize, physicalUnit, correction);
                    break;
                case LineCaliperMeasureRoi lineCaliper:
                    AppendSingleEdgeCaliperInfoLines(
                        lines,
                        UiText.Get("InfoTitleLineCaliper"),
                        $"Distance:{FormatLength(GeometryUtils.Distance(lineCaliper.P1, lineCaliper.P2) * correction, pixelSize, physicalUnit)}",
                        lineCaliper,
                        pixelSize,
                        physicalUnit,
                        correction,
                        $"Angle:{lineCaliper.AngleDegrees:F2}°");
                    break;
                case ArcCaliperMeasureRoi arcCaliper:
                    AppendSingleEdgeCaliperInfoLines(
                        lines,
                        UiText.Get("InfoTitleArcCaliper"),
                        UiText.FormatInvariant("InfoLineArcCaliperGeometry", FormatLength(arcCaliper.Radius * correction, pixelSize, physicalUnit), arcCaliper.SweepAngle),
                        arcCaliper,
                        pixelSize,
                        physicalUnit,
                        correction,
                        UiText.FormatInvariant("InfoLineArcCaliperDetail", arcCaliper.Center.X, arcCaliper.Center.Y, arcCaliper.StartAngle));
                    break;
                case CircularCaliperMeasureRoi circularCaliper:
                    AppendSingleEdgeCaliperInfoLines(
                        lines,
                        UiText.Get("InfoTitleCircularCaliper"),
                        $"Radius:{FormatLength(circularCaliper.Radius * correction, pixelSize, physicalUnit)}",
                        circularCaliper,
                        pixelSize,
                        physicalUnit,
                        correction,
                        UiText.FormatInvariant("InfoLineCircularCaliperDetail", circularCaliper.Center.X, circularCaliper.Center.Y));
                    break;
                case RotatedRect rect:
                    lines.Add(UiText.FormatInvariant("InfoLineRectDimensions", FormatLength(rect.Width * correction, pixelSize, physicalUnit), FormatLength(rect.Height * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLineArea", FormatArea(rect.Width * rect.Height * areaCorrection, pixelSize, physicalUnit)));
                    break;
                case FittedEllipseRoi fittedEllipse:
                    lines.Add(UiText.FormatInvariant("InfoLineFittedEllipse", FormatLength(fittedEllipse.RadiusX * correction, pixelSize, physicalUnit), FormatLength(fittedEllipse.RadiusY * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLineAnglePoints", fittedEllipse.Angle, fittedEllipse.SourcePointCount));
                    break;
                case EllipseRoi ellipse:
                    lines.Add(UiText.FormatInvariant("InfoLineEllipse", FormatLength(ellipse.RadiusX * correction, pixelSize, physicalUnit), FormatLength(ellipse.RadiusY * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLineArea", FormatArea(Math.PI * ellipse.RadiusX * ellipse.RadiusY * areaCorrection, pixelSize, physicalUnit)));
                    break;
                case CircleRoi circle:
                    lines.Add(UiText.FormatInvariant("InfoLineCircle", FormatLength(circle.Radius * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLineArea", FormatArea(Math.PI * circle.Radius * circle.Radius * areaCorrection, pixelSize, physicalUnit)));
                    break;
                case RingRoi ring:
                    lines.Add(UiText.FormatInvariant("InfoLineRing", FormatLength(ring.InnerRadius * correction, pixelSize, physicalUnit), FormatLength(ring.OuterRadius * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLineArea", FormatArea(ring.Area * areaCorrection, pixelSize, physicalUnit)));
                    break;
                case PolygonRoi polygon:
                    lines.Add(UiText.FormatInvariant("InfoLinePolygon", FormatLength(GeometryUtils.PolygonPerimeter(polygon.Points) * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLineArea", FormatArea(GeometryUtils.PolygonArea(polygon.Points) * areaCorrection, pixelSize, physicalUnit)));
                    break;
                case PolylineRoi polyline when polyline.Points.Count > 1:
                    double length = 0;
                    for (int i = 1; i < polyline.Points.Count; i++)
                    {
                        length += GeometryUtils.Distance(polyline.Points[i - 1], polyline.Points[i]);
                    }
                    lines.Add(UiText.FormatInvariant("InfoLinePolyline", FormatLength(length * correction, pixelSize, physicalUnit)));
                    break;
                case ArrowAnnotationRoi arrow:
                    lines.Add(UiText.FormatInvariant("InfoLineArrow", FormatLength(GeometryUtils.Distance(arrow.P1, arrow.P2) * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLinePointPair", arrow.P1.X, arrow.P1.Y, arrow.P2.X, arrow.P2.Y));
                    break;
                case LineMeasureRoi line:
                    lines.Add(UiText.FormatInvariant("InfoLineLineMeasure", FormatLength(GeometryUtils.Distance(line.P1, line.P2) * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLinePointPair", line.P1.X, line.P1.Y, line.P2.X, line.P2.Y));
                    break;
                case AngleMeasureRoi angle:
                    lines.Add(UiText.FormatInvariant("InfoLineAngleMeasure", GeometryUtils.SmallestAngle(angle.P1, angle.Vertex, angle.P2)));
                    break;
                case ArcMeasureRoi arc:
                    if (arc.IsValid)
                    {
                        lines.Add(UiText.FormatInvariant("InfoLineArcMeasureRadius", FormatLength(arc.Radius * correction, pixelSize, physicalUnit)));
                        lines.Add(UiText.FormatInvariant("InfoLineArcMeasureDetail", FormatLength(arc.ArcLength * correction, pixelSize, physicalUnit), arc.CentralAngle));
                        lines.Add(UiText.FormatInvariant("InfoLineCenterPoint", arc.Center.X, arc.Center.Y));
                    }
                    else
                    {
                        lines.Add(UiText.Get("InfoLineInvalidArc"));
                    }
                    break;
                case PointToLineDistanceRoi pointToLine:
                    lines.Add(UiText.FormatInvariant("InfoLinePointToLine", FormatLength(pointToLine.Distance * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLinePointFoot", pointToLine.Point.X, pointToLine.Point.Y, pointToLine.FootPoint.X, pointToLine.FootPoint.Y));
                    break;
                case PointToCircleDistanceRoi pointToCircle:
                    lines.Add(UiText.FormatInvariant("InfoLinePointToCircle", FormatLength(pointToCircle.DistanceToCircle * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLinePointCircleDetail", pointToCircle.Center.X, pointToCircle.Center.Y, FormatLength(pointToCircle.Radius * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLineNearestPoint", pointToCircle.NearestPointOnCircle.X, pointToCircle.NearestPointOnCircle.Y));
                    break;
                case ParallelismMeasureRoi parallelism:
                    lines.Add(UiText.FormatInvariant("InfoLineParallelism", parallelism.AngleDifference));
                    lines.Add(UiText.FormatInvariant("InfoLineParallelismDetail", FormatLength(parallelism.AverageDistance * correction, pixelSize, physicalUnit), parallelism.ParallelismError));
                    break;
                case PerpendicularityMeasureRoi perpendicularity:
                    lines.Add(UiText.FormatInvariant("InfoLinePerpendicularity", perpendicularity.AngleBetweenLines));
                    lines.Add(UiText.FormatInvariant("InfoLinePerpendicularityDetail", perpendicularity.PerpendicularityError));
                    break;
                case ConcentricityMeasureRoi concentricity:
                    lines.Add(UiText.FormatInvariant("InfoLineConcentricity", FormatLength(concentricity.CenterDistance * correction, pixelSize, physicalUnit)));
                    lines.Add(UiText.FormatInvariant("InfoLineConcentricityDetail", FormatLength(concentricity.Radius1 * correction, pixelSize, physicalUnit), FormatLength(concentricity.Radius2 * correction, pixelSize, physicalUnit)));
                    break;
                case PointAnnotationRoi point:
                    lines.Add(UiText.FormatInvariant("InfoLinePointAnnotation", point.Position.X, point.Position.Y));
                    break;
                case TextAnnotationRoi text:
                    lines.Add(UiText.FormatInvariant("InfoLineTextAnnotation", text.Position.X, text.Position.Y));
                    break;
            }

            AppendToleranceJudgementLine(lines, roi, pixelSize, calibration);
        }

        /// <summary>
        /// 若 ROI 配置了公差，则追加合格/超差判定行（使用主测量值，物理单位）。
        /// Chinese: 主测量值仅对距离/半径类测量 ROI 有效；无主测量或未配置公差时不做判定。
        /// English: Appends a pass/fail judgement line when a tolerance is configured for the ROI.
        /// </summary>
        private static void AppendToleranceJudgementLine(List<string> lines, RoiBase roi, double pixelSize, CameraCalibration? calibration)
        {
            if (roi.Tolerance is not { IsEnabled: true } tolerance)
            {
                return;
            }

            double? measured = GetPrimaryMeasurementValue(roi, pixelSize, calibration);
            if (measured == null)
            {
                return;
            }

            string key = tolerance.IsWithinTolerance(measured.Value)
                ? "InfoTolerancePass"
                : "InfoToleranceFail";
            lines.Add(UiText.FormatInvariant(key, measured.Value, tolerance.Nominal ?? 0));
        }

        /// <summary>
        /// 主测量值（物理单位）：距离类取 P1-P2 距离，圆/弧类取半径。
        /// Chinese: 与信息面板中展示的"距离/半径"保持一致，便于按同一数值设定公差。
        /// English: Primary measurement value in physical units for tolerance judgement.
        /// </summary>
        private static double? GetPrimaryMeasurementValue(RoiBase roi, double pixelSize, CameraCalibration? calibration)
        {
            double correction = RoiCalibrationHelper.GetLengthCorrection(roi, calibration);
            return roi switch
            {
                LineMeasureRoi line => GeometryUtils.Distance(line.P1, line.P2) * correction * pixelSize,
                CircularCaliperMeasureRoi circular => circular.Radius * correction * pixelSize,
                ArcMeasureRoi arc => arc.Radius * correction * pixelSize,
                _ => null
            };
        }

        private static void AppendBlobAnalysisInfoLines(List<string> lines, BlobAnalysisRoi blob, double pixelSize, string? physicalUnit, double correction, double areaCorrection)
        {
            lines.Add(UiText.FormatInvariant("InfoLineBlobAnalysis", FormatLength(blob.Width * correction, pixelSize, physicalUnit), FormatLength(blob.Height * correction, pixelSize, physicalUnit), blob.Angle));
            lines.Add(blob.UseOtsu
                ? UiText.FormatInvariant("InfoLineBlobThresholdOtsu", blob.DetectDark ? UiText.Get("InfoPolarityDark") : UiText.Get("InfoPolarityLight"), blob.MinArea)
                : UiText.FormatInvariant("InfoLineBlobThresholdManual", blob.ManualThreshold, blob.DetectDark ? UiText.Get("InfoPolarityDark") : UiText.Get("InfoPolarityLight"), blob.MinArea));

            if (blob.DetectedBlobs.Count == 0)
            {
                lines.Add(UiText.FormatInvariant("InfoLineBlobCount", 0));
                return;
            }

            lines.Add(UiText.FormatInvariant("InfoLineBlobSummary", blob.DetectedBlobs.Count, FormatArea(blob.DetectedBlobs.Sum(item => item.Area) * areaCorrection, pixelSize, physicalUnit)));

            foreach (BlobFeature detectedBlob in blob.DetectedBlobs.OrderByDescending(item => item.Area).Take(5))
            {
                lines.Add(UiText.FormatInvariant("InfoLineBlobItem", detectedBlob.Label, FormatArea(detectedBlob.Area * areaCorrection, pixelSize, physicalUnit), FormatLength(detectedBlob.BoundingBox.Width * correction, pixelSize, physicalUnit), FormatLength(detectedBlob.BoundingBox.Height * correction, pixelSize, physicalUnit), detectedBlob.Centroid.X, detectedBlob.Centroid.Y));
                lines.Add(UiText.FormatInvariant("InfoLineBlobBox", detectedBlob.BoundingBox.X, detectedBlob.BoundingBox.Y, FormatLength(detectedBlob.BoundingBox.Width * correction, pixelSize, physicalUnit), FormatLength(detectedBlob.BoundingBox.Height * correction, pixelSize, physicalUnit)));
            }

            int remainingBlobCount = blob.DetectedBlobs.Count - 5;
            if (remainingBlobCount > 0)
            {
                lines.Add(UiText.FormatInvariant("InfoLineBlobRemaining", remainingBlobCount));
            }
        }

        private static string GetDisplayUnit(string? physicalUnit)
        {
            return string.IsNullOrWhiteSpace(physicalUnit) ? UiText.Get("InfoUnitPixels") : physicalUnit;
        }

        private static void AppendDualEdgeCaliperInfoLines(List<string> lines, CaliperMeasureRoi caliper, double pixelSize, string? physicalUnit, double correction)
        {
            lines.Add(UiText.FormatInvariant("InfoLineCaliperMeasure", FormatLength(GeometryUtils.Distance(caliper.P1, caliper.P2) * correction, pixelSize, physicalUnit)));
            lines.Add(UiText.FormatInvariant("InfoLineRegionSearch", FormatLength(caliper.GetResolvedCaliperRegionLength() * correction, pixelSize, physicalUnit), FormatLength(caliper.CaliperSearchRange * 2 * correction, pixelSize, physicalUnit)));
            AppendCaliperParameterLine(lines, caliper);
            if (!caliper.HasDetectedEdges)
            {
                return;
            }

            AppendDetectionQualityLines(lines, caliper);
            lines.Add(UiText.FormatInvariant("InfoLineCaliperAngles", caliper.Edge1AngleDegrees, caliper.Edge2AngleDegrees, caliper.ParallelismErrorDegrees));
        }

        private static void AppendSingleEdgeCaliperInfoLines(List<string> lines, string title, string geometryLine, ICaliperInfoSource caliper, double pixelSize, string? physicalUnit, double correction, string? detectionDetailLine = null)
        {
            lines.Add(UiText.FormatInvariant("InfoLineSingleCaliper", title, geometryLine));
            lines.Add(UiText.FormatInvariant("InfoLineSingleSearch", FormatLength(caliper.CaliperSearchRange * 2 * correction, pixelSize, physicalUnit)));
            AppendCaliperParameterLine(lines, caliper);
            if (!caliper.HasDetection)
            {
                return;
            }

            AppendDetectionQualityLines(lines, caliper);
            if (!string.IsNullOrWhiteSpace(detectionDetailLine))
            {
                lines.Add(detectionDetailLine);
            }
        }

        private static void AppendCaliperParameterLine(List<string> lines, ICaliperInfoSource caliper)
        {
            lines.Add(UiText.FormatInvariant("InfoLineCaliperParameters", caliper.CaliperCount, caliper.CaliperSamplingHalfWidth * 2 + 1, caliper.CaliperEdgePolarity));
        }

        private static void AppendDetectionQualityLines(List<string> lines, ICaliperInfoSource caliper)
        {
            lines.Add(UiText.FormatInvariant("InfoLineFit", caliper.ValidCaliperCount, ImageAnalysisService.NormalizeCaliperScore(caliper.AverageScore)));
            lines.Add(UiText.FormatInvariant("InfoLineResidual", caliper.ResidualRms, caliper.ResidualMax));
            lines.Add(UiText.FormatInvariant("InfoLineConfidence", caliper.Confidence));
        }

        private static void AppendStatisticsLines(List<string> lines, RoiStatistics statistics)
        {
            lines.Add(UiText.FormatInvariant("InfoLineStatisticsMean", statistics.Mean, statistics.Min, statistics.Max));
            lines.Add(UiText.FormatInvariant("InfoLineStatisticsStdDev", statistics.StandardDeviation, statistics.PixelCount));
        }

        private static void AppendCalibrationLine(List<string> lines, double pixelSize, string? physicalUnit, CameraCalibration? calibration)
        {
            if (!string.IsNullOrWhiteSpace(physicalUnit))
            {
                lines.Add(UiText.FormatInvariant("InfoLineCalibration", pixelSize, GetDisplayUnit(physicalUnit)));
            }

            if (calibration is { IsEnabled: true })
            {
                lines.Add(UiText.FormatInvariant("InfoLineCalibrationDistortion", calibration.K1, calibration.K2));
            }
        }

        private static string FormatLength(double pixelLength, double pixelSize, string? physicalUnit)
        {
            return $"{pixelLength * pixelSize:F2} {GetDisplayUnit(physicalUnit)}";
        }

        private static string FormatArea(double pixelArea, double pixelSize, string? physicalUnit)
        {
            return $"{pixelArea * pixelSize * pixelSize:F2} {GetDisplayUnit(physicalUnit)}²";
        }
    }
}
