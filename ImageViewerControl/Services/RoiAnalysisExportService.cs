using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Services
{
    public static class RoiAnalysisExportService
    {
        private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

        public static string BuildSummary(IEnumerable<RoiBase> rois, BitmapSource? bitmap, double pixelSize, string? physicalUnit, CameraCalibration? calibration = null)
        {
            ArgumentNullException.ThrowIfNull(rois);

            var roiList = rois.ToList();
            var lines = new List<string>
            {
                $"Total ROIs: {roiList.Count}"
            };

            foreach (var group in roiList.GroupBy(roi => roi.RoiTypeName).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                lines.Add($"- {group.Key}: {group.Count()}");
            }

            double totalLineLength = roiList.OfType<LineMeasureRoi>().Sum(line => GeometryUtils.Distance(line.P1.ToWpfPoint(), line.P2.ToWpfPoint()) * RoiCalibrationHelper.GetLengthCorrection(line, calibration) * pixelSize);
            double totalPolylineLength = roiList.OfType<PolylineRoi>().Sum(poly => GeometryUtils.PolylineLength(poly.Points.ToWpfPointArray()) * RoiCalibrationHelper.GetLengthCorrection(poly, calibration)) * pixelSize;
            double totalPolygonArea = roiList.OfType<PolygonRoi>().Where(poly => poly.IsClosed && poly.Points.Count >= 3).Sum(poly => GeometryUtils.PolygonArea(poly.Points.ToWpfPointArray()) * RoiCalibrationHelper.GetAreaCorrection(poly, calibration)) * pixelSize * pixelSize;
            double totalRectArea = roiList.OfType<RotatedRect>().Sum(rect => rect.Width * rect.Height * RoiCalibrationHelper.GetAreaCorrection(rect, calibration)) * pixelSize * pixelSize;
            double totalEllipseArea = roiList.OfType<EllipseRoi>().Sum(ellipse => Math.PI * ellipse.RadiusX * ellipse.RadiusY * RoiCalibrationHelper.GetAreaCorrection(ellipse, calibration)) * pixelSize * pixelSize;
            double totalCircleArea = roiList.OfType<CircleRoi>().Sum(circle => Math.PI * circle.Radius * circle.Radius * RoiCalibrationHelper.GetAreaCorrection(circle, calibration)) * pixelSize * pixelSize;
            totalCircleArea += roiList.OfType<ThreePointCircleMeasureRoi>().Where(circle => circle.IsValid).Sum(circle => Math.PI * circle.Radius * circle.Radius * RoiCalibrationHelper.GetAreaCorrection(circle, calibration)) * pixelSize * pixelSize;
            double totalCenterDistance = roiList.OfType<CenterDistanceMeasureRoi>().Sum(distance => distance.CenterDistance * RoiCalibrationHelper.GetLengthCorrection(distance, calibration)) * pixelSize;
            string unit = string.IsNullOrWhiteSpace(physicalUnit) ? "px" : physicalUnit;

            lines.Add(string.Empty);
            lines.Add($"Line Length Total: {totalLineLength:F2} {unit}");
            lines.Add($"Center Distance Total: {totalCenterDistance:F2} {unit}");
            lines.Add($"Polyline Length Total: {totalPolylineLength:F2} {unit}");
            lines.Add($"Area Total: {totalPolygonArea + totalRectArea + totalEllipseArea + totalCircleArea:F2} {unit}²");
            if (roiList.OfType<AngleMeasureRoi>().Any())
            {
                lines.Add($"Average Angle: {roiList.OfType<AngleMeasureRoi>().Average(angle => GeometryUtils.SmallestAngle(angle.P1.ToWpfPoint(), angle.Vertex.ToWpfPoint(), angle.P2.ToWpfPoint())):F2}°");
            }

            if (bitmap != null)
            {
                int statisticsCount = roiList.Count(roi => ImageAnalysisService.TryCalculateStatistics(bitmap, roi, out _));
                lines.Add($"ROIs With Statistics: {statisticsCount}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public static void SaveCsv(string filePath, IEnumerable<RoiBase> rois, BitmapSource? bitmap, double pixelSize, string? physicalUnit, CameraCalibration? calibration = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(rois);
            File.WriteAllText(filePath, BuildCsv(rois, bitmap, pixelSize, physicalUnit, calibration), Encoding.UTF8);
        }

        public static Task SaveCsvAsync(string filePath, IEnumerable<RoiBase> rois, BitmapSource? bitmap, double pixelSize, string? physicalUnit, CameraCalibration? calibration = null, CancellationToken cancellationToken = default)
        {
            return SaveCsvAsync(filePath, rois, bitmap, pixelSize, physicalUnit, calibration, exportContext: null, cancellationToken: cancellationToken);
        }

        public static Task SaveCsvAsync(string filePath, IEnumerable<RoiBase> rois, BitmapSource? bitmap, double pixelSize, string? physicalUnit, CameraCalibration? calibration, RoiAnalysisExportContext? exportContext, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(rois);
            return SaveCsvWithMetadataAsync(filePath, rois.ToList(), bitmap, pixelSize, physicalUnit, calibration, exportContext, cancellationToken);
        }

        private static async Task SaveCsvWithMetadataAsync(
            string filePath,
            IReadOnlyList<RoiBase> rois,
            BitmapSource? bitmap,
            double pixelSize,
            string? physicalUnit,
            CameraCalibration? calibration,
            RoiAnalysisExportContext? exportContext,
            CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(filePath, BuildCsv(rois, bitmap, pixelSize, physicalUnit, calibration), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            if (exportContext == null)
            {
                return;
            }

            string metadataPath = Path.ChangeExtension(filePath, ".metadata.json");
            string csvHash = await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
            string metadata = await BuildMetadataJsonAsync(rois, bitmap, pixelSize, physicalUnit, calibration, exportContext, csvHash, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(metadataPath, metadata, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
        {
            await using FileStream stream = File.OpenRead(filePath);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static async Task<string> BuildMetadataJsonAsync(
            IReadOnlyList<RoiBase> rois,
            BitmapSource? bitmap,
            double pixelSize,
            string? physicalUnit,
            CameraCalibration? calibration,
            RoiAnalysisExportContext exportContext,
            string csvHash,
            CancellationToken cancellationToken)
        {
            string? sourceHash = null;
            long? sourceLength = null;
            DateTime? sourceLastWriteTimeUtc = null;
            if (!string.IsNullOrWhiteSpace(exportContext.SourcePath) && File.Exists(exportContext.SourcePath))
            {
                FileInfo sourceInfo = new(exportContext.SourcePath);
                sourceLength = sourceInfo.Length;
                sourceLastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
                await using FileStream stream = File.OpenRead(exportContext.SourcePath);
                byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                sourceHash = Convert.ToHexString(hash).ToLowerInvariant();
            }

            object? roiDocument = null;
            if (exportContext.PluginRegistry != null)
            {
                string roiJson = RoiPersistenceService.Serialize(rois, pixelSize, physicalUnit, exportContext.PluginRegistry);
                using JsonDocument document = JsonDocument.Parse(roiJson);
                roiDocument = document.RootElement.Clone();
            }

            var metadata = new Dictionary<string, object?>
            {
                ["exportFormatVersion"] = 2,
                ["exportedAtUtc"] = DateTimeOffset.UtcNow,
                ["applicationVersion"] = typeof(RoiAnalysisExportService).Assembly.GetName().Version?.ToString() ?? "unknown",
                ["analysisAlgorithm"] = FittingAlgorithmMetadata.Version,
                ["fitting"] = new Dictionary<string, object?>
                {
                    ["ellipseAlgorithm"] = FittingAlgorithmMetadata.Ellipse,
                    ["caliperEdgeAlgorithm"] = FittingAlgorithmMetadata.CaliperEdge,
                    ["ellipseLoss"] = RobustFitLoss.Tukey.ToString(),
                    ["ellipseMaxIterations"] = EllipseFitOptions.Default.MaxIterations,
                    ["ellipseMaxRansacSamples"] = EllipseFitOptions.Default.MaxRansacSamples,
                    ["ellipseRandomSeed"] = EllipseFitOptions.Default.RandomSeed,
                    ["fittedEllipses"] = rois.OfType<FittedEllipseRoi>().Select(ellipse => new Dictionary<string, object?>
                    {
                        ["label"] = ellipse.Label,
                        ["sourcePointCount"] = ellipse.SourcePointCount,
                        ["inlierCount"] = ellipse.FitInlierCount,
                        ["outlierCount"] = ellipse.FitOutlierCount,
                        ["residualRms"] = ellipse.FitResidualRms,
                        ["residualMedian"] = ellipse.FitResidualMedian,
                        ["residualMax"] = ellipse.FitResidualMax,
                        ["noiseScale"] = ellipse.FitNoiseScale,
                        ["aspectRatio"] = ellipse.FitAspectRatio,
                        ["algorithm"] = ellipse.FitAlgorithm
                    }).ToList()
                },
                ["result"] = new Dictionary<string, object?>
                {
                    ["csvSha256"] = csvHash
                },
                ["source"] = new Dictionary<string, object?>
                {
                    ["path"] = exportContext.SourcePath,
                    ["sha256"] = sourceHash,
                    ["lengthBytes"] = sourceLength,
                    ["lastWriteTimeUtc"] = sourceLastWriteTimeUtc,
                    ["pixelWidth"] = bitmap?.PixelWidth,
                    ["pixelHeight"] = bitmap?.PixelHeight
                },
                ["calibration"] = new Dictionary<string, object?>
                {
                    ["pixelSize"] = pixelSize,
                    ["physicalUnit"] = string.IsNullOrWhiteSpace(physicalUnit) ? "px" : physicalUnit,
                    ["camera"] = calibration
                },
                ["roiCount"] = rois.Count,
                ["roiTypes"] = rois
                    .GroupBy(roi => roi.RoiTypeName)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                ["roiDocument"] = roiDocument,
                ["renderSettings"] = exportContext.RenderSettings
            };

            return JsonSerializer.Serialize(metadata, MetadataJsonOptions);
        }

        public static string BuildCsv(IEnumerable<RoiBase> rois, BitmapSource? bitmap, double pixelSize, string? physicalUnit, CameraCalibration? calibration = null)
        {
            ArgumentNullException.ThrowIfNull(rois);

            string unit = string.IsNullOrWhiteSpace(physicalUnit) ? "px" : physicalUnit;
            var builder = new StringBuilder();
            builder.AppendLine("Type,Label,Metric1,Metric2,Metric3,Mean,Min,Max,StdDev,PixelCount");

            foreach (var roi in rois)
            {
                var metrics = GetMetrics(roi, pixelSize, unit, calibration);
                string mean = string.Empty;
                string min = string.Empty;
                string max = string.Empty;
                string stddev = string.Empty;
                string pixelCount = string.Empty;

                if (bitmap != null && ImageAnalysisService.TryCalculateStatistics(bitmap, roi, out RoiStatistics statistics))
                {
                    mean = statistics.Mean.ToString("F2", CultureInfo.InvariantCulture);
                    min = statistics.Min.ToString(CultureInfo.InvariantCulture);
                    max = statistics.Max.ToString(CultureInfo.InvariantCulture);
                    stddev = statistics.StandardDeviation.ToString("F2", CultureInfo.InvariantCulture);
                    pixelCount = statistics.PixelCount.ToString(CultureInfo.InvariantCulture);
                }

                builder.AppendLine(string.Join(",",
                    Escape(roi.RoiTypeName),
                    Escape(roi.Label),
                    Escape(metrics.Metric1),
                    Escape(metrics.Metric2),
                    Escape(metrics.Metric3),
                    mean,
                    min,
                    max,
                    stddev,
                    pixelCount));
            }

            return builder.ToString();
        }

        private static (string Metric1, string Metric2, string Metric3) GetMetrics(RoiBase roi, double pixelSize, string unit, CameraCalibration? calibration)
        {
            double correction = RoiCalibrationHelper.GetLengthCorrection(roi, calibration);
            double areaCorrection = correction * correction;
            return roi switch
            {
                RotatedRect rect => ($"Width={rect.Width * correction * pixelSize:F2} {unit}", $"Height={rect.Height * correction * pixelSize:F2} {unit}", $"Angle={rect.Angle:F1}°"),
                FittedEllipseRoi fittedEllipse => ($"RadiusX={fittedEllipse.RadiusX * correction * pixelSize:F2} {unit}", $"RadiusY={fittedEllipse.RadiusY * correction * pixelSize:F2} {unit}", $"Angle={fittedEllipse.Angle:F1}°;RMS={fittedEllipse.FitResidualRms * correction * pixelSize:F3};Inliers={fittedEllipse.FitInlierCount}"),
                EllipseRoi ellipse => ($"RadiusX={ellipse.RadiusX * correction * pixelSize:F2} {unit}", $"RadiusY={ellipse.RadiusY * correction * pixelSize:F2} {unit}", $"Angle={ellipse.Angle:F1}°"),
                CircleRoi circle => ($"Radius={circle.Radius * correction * pixelSize:F2} {unit}", string.Empty, string.Empty),
                PolygonRoi polygon when polygon.IsClosed && polygon.Points.Count >= 3 => ($"Area={GeometryUtils.PolygonArea(polygon.Points.ToWpfPointArray()) * areaCorrection * pixelSize * pixelSize:F2} {unit}²", $"Perimeter={GeometryUtils.PolygonPerimeter(polygon.Points.ToWpfPointArray()) * correction * pixelSize:F2} {unit}", $"Vertices={polygon.Points.Count};Closed=true"),
                PolygonRoi polygon => ($"Perimeter={GeometryUtils.PolylineLength(polygon.Points.ToWpfPointArray()) * correction * pixelSize:F2} {unit}", $"Vertices={polygon.Points.Count}", "Closed=false"),
                PolylineRoi polyline => ($"Length={GeometryUtils.PolylineLength(polyline.Points.ToWpfPointArray()) * correction * pixelSize:F2} {unit}", $"Segments={Math.Max(0, polyline.Points.Count - 1)}", $"Freehand={polyline.IsFreehand}"),
                PointAnnotationRoi point => ($"X={point.Position.X:F1}", $"Y={point.Position.Y:F1}", string.Empty),
                PointCoordinateMeasureRoi pointCoordinate => ($"X={pointCoordinate.Position.X:F1}", $"Y={pointCoordinate.Position.Y:F1}", pointCoordinate.IsEdgeSnapped ? $"EdgeConfidence={pointCoordinate.EdgeConfidence * 100:F1}%" : string.Empty),
                TextAnnotationRoi text => ($"X={text.Position.X:F1}", $"Y={text.Position.Y:F1}", string.Empty),
                LineMeasureRoi line => ($"Length={GeometryUtils.Distance(line.P1.ToWpfPoint(), line.P2.ToWpfPoint()) * correction * pixelSize:F2} {unit}", $"dX={(line.P2.X - line.P1.X) * correction * pixelSize:F2} {unit}", $"dY={(line.P2.Y - line.P1.Y) * correction * pixelSize:F2} {unit}"),
                AngleMeasureRoi angle => ($"Angle={GeometryUtils.SmallestAngle(angle.P1.ToWpfPoint(), angle.Vertex.ToWpfPoint(), angle.P2.ToWpfPoint()):F1}°", $"Leg1={GeometryUtils.Distance(angle.P1.ToWpfPoint(), angle.Vertex.ToWpfPoint()) * correction * pixelSize:F2} {unit}", $"Leg2={GeometryUtils.Distance(angle.Vertex.ToWpfPoint(), angle.P2.ToWpfPoint()) * correction * pixelSize:F2} {unit}"),
                CenterDistanceMeasureRoi centerDistance => ($"CenterDistance={centerDistance.CenterDistance * correction * pixelSize:F2} {unit}", $"C1=({centerDistance.Center1.X:F1},{centerDistance.Center1.Y:F1})", $"C2=({centerDistance.Center2.X:F1},{centerDistance.Center2.Y:F1})"),
                ThreePointCircleMeasureRoi threePointCircle when threePointCircle.IsValid => ($"Radius={threePointCircle.Radius * correction * pixelSize:F2} {unit}", $"Center=({threePointCircle.Center.X:F1},{threePointCircle.Center.Y:F1})", "Points=3"),
                _ => (string.Empty, string.Empty, string.Empty)
            };
        }

        private static string Escape(string? value)
        {
            return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
        }
    }
}
