using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Services
{
    internal static partial class ImageAnalysisService
    {
        internal const double MaxCaliperScore = 255.0;

        private sealed class EdgeSnapPixelBuffer
        {
            public EdgeSnapPixelBuffer(BitmapSource bitmap)
            {
                PixelWidth = bitmap.PixelWidth;
                PixelHeight = bitmap.PixelHeight;
                Format = bitmap.Format;
                BytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
                Stride = PixelWidth * BytesPerPixel;
                Pixels = new byte[PixelHeight * Stride];
                bitmap.CopyPixels(Pixels, Stride, 0);
            }

            public int PixelWidth { get; }
            public int PixelHeight { get; }
            public PixelFormat Format { get; }
            public int BytesPerPixel { get; }
            public int Stride { get; }
            public byte[] Pixels { get; }
        }

        private static readonly ConditionalWeakTable<BitmapSource, EdgeSnapPixelBuffer> EdgeSnapBuffers = new();

        /// <summary>
        /// 检测结果置信度低于该值时判定检测失败，避免把几近无意义的结果作为有效测量返回。
        /// Chinese: 对应"分数极低/残差过大/有效卡尺数过少"的综合结果拦截。
        /// English: Minimum confidence required for a detection to be considered valid.
        /// </summary>
        internal const double MinimumDetectionConfidence = 0.05;

        private readonly record struct CaliperEdgeSample(Point Point, double Score);

        internal static double NormalizeCaliperScore(double score)
        {
            return Math.Clamp(score / MaxCaliperScore * 100.0, 0, 100);
        }

        public static bool TryDetectLineMeasureEdges(BitmapSource bitmap, CaliperMeasureRoi line, out LineMeasureGradientDetectionResult result)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(line);
            line.EnsureCaliperRegion();
            if (line.CaliperSearchRange <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(line), line.CaliperSearchRange, "CaliperSearchRange must be positive.");
            }

            if (line.CaliperSamplingHalfWidth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(line), line.CaliperSamplingHalfWidth, "CaliperSamplingHalfWidth must be non-negative.");
            }
            result = default;

            Vector measurementDirection = line.GetCaliperMeasurementDirection().ToWpfVector();
            double estimatedDistance = GeometryUtils.Distance(line.P1.ToWpfPoint(), line.P2.ToWpfPoint());

            bitmap = NormalizeBitmap(bitmap);
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            Vector caliperDirection = new(-measurementDirection.Y, measurementDirection.X);
            Point measurementCenter = line.CaliperCenter.ToWpfPoint();
            int halfSearchRange = line.CaliperSearchRange;
            double regionHalfLength = line.GetResolvedCaliperRegionLength() / 2;
            int caliperCount = Math.Clamp(line.CaliperCount, 3, 31);
            int minimumValidCalipers = Math.Min(Math.Max(2, line.MinimumValidCalipers), caliperCount);

            List<Point> invalidCaliperCenters = new(caliperCount);
            List<CaliperEdgeSample> edge1Samples = new(caliperCount);
            List<CaliperEdgeSample> edge2Samples = new(caliperCount);

            for (int i = 0; i < caliperCount; i++)
            {
                double lerp = caliperCount == 1 ? 0.5 : (double)i / (caliperCount - 1);
                double tangentOffset = -regionHalfLength + regionHalfLength * 2 * lerp;
                Point caliperCenter = measurementCenter + caliperDirection * tangentOffset;
                if (!TryFindStrongestGradientPair(pixels, bitmap.PixelWidth, bitmap.PixelHeight, stride, bytesPerPixel, bitmap.Format, caliperCenter, measurementDirection, caliperDirection, halfSearchRange, line.CaliperSamplingHalfWidth, line.CaliperEdgeSigma, line.CaliperMinimumGradient, line.CaliperEdgePolarity, line.MinimumEdgeGap, line.NominalEdgeGap, line.NominalEdgeGapTolerance, out CaliperEdgeSample edge1Sample, out CaliperEdgeSample edge2Sample))
                {
                    invalidCaliperCenters.Add(caliperCenter);
                    continue;
                }

                edge1Samples.Add(edge1Sample);
                edge2Samples.Add(edge2Sample);
            }

            if (edge1Samples.Count < minimumValidCalipers || edge2Samples.Count < minimumValidCalipers)
            {
                return false;
            }

            List<CaliperEdgeSample> filteredEdge1Samples = FilterInlierSamples(edge1Samples, caliperDirection, regionHalfLength, line.CaliperOutlierThreshold, minimumValidCalipers);
            List<CaliperEdgeSample> filteredEdge2Samples = FilterInlierSamples(edge2Samples, caliperDirection, regionHalfLength, line.CaliperOutlierThreshold, minimumValidCalipers);
            if (filteredEdge1Samples.Count < minimumValidCalipers || filteredEdge2Samples.Count < minimumValidCalipers)
            {
                return false;
            }

            Point[] filteredEdge1Points = [..filteredEdge1Samples.Select(sample => sample.Point)];
            Point[] filteredEdge2Points = [..filteredEdge2Samples.Select(sample => sample.Point)];
            Point[] rejectedEdge1Points = [..edge1Samples.Where(sample => !filteredEdge1Samples.Contains(sample)).Select(sample => sample.Point)];
            Point[] rejectedEdge2Points = [..edge2Samples.Where(sample => !filteredEdge2Samples.Contains(sample)).Select(sample => sample.Point)];
            LineSegmentOverlay fittedEdge1 = FitLine(filteredEdge1Points, caliperDirection, regionHalfLength, BuildScoreWeights(filteredEdge1Samples));
            LineSegmentOverlay fittedEdge2 = FitLine(filteredEdge2Points, caliperDirection, regionHalfLength, BuildScoreWeights(filteredEdge2Samples));
            if (!TryIntersectLines(measurementCenter, measurementDirection, fittedEdge1.Start.ToWpfPoint(), (fittedEdge1.End - fittedEdge1.Start).ToWpfVector(), out Point detectedP1) ||
                !TryIntersectLines(measurementCenter, measurementDirection, fittedEdge2.Start.ToWpfPoint(), (fittedEdge2.End - fittedEdge2.Start).ToWpfVector(), out Point detectedP2))
            {
                return false;
            }

            if (GeometryUtils.Distance(detectedP1, detectedP2) <= 0.5)
            {
                return false;
            }

            double edge1AverageScore = filteredEdge1Samples.Average(sample => sample.Score);
            double edge2AverageScore = filteredEdge2Samples.Average(sample => sample.Score);
            (double edge1ResidualRms, double edge1ResidualMax) = ComputeResidualMetrics(filteredEdge1Points, fittedEdge1);
            (double edge2ResidualRms, double edge2ResidualMax) = ComputeResidualMetrics(filteredEdge2Points, fittedEdge2);
            double edge1AngleDegrees = NormalizeLineAngleDegrees((fittedEdge1.End - fittedEdge1.Start).ToWpfVector());
            double edge2AngleDegrees = NormalizeLineAngleDegrees((fittedEdge2.End - fittedEdge2.Start).ToWpfVector());
            double parallelismErrorDegrees = Math.Abs(edge1AngleDegrees - edge2AngleDegrees);
            parallelismErrorDegrees = parallelismErrorDegrees > 90 ? 180 - parallelismErrorDegrees : parallelismErrorDegrees;
            double confidence = ComputeConfidence(edge1AverageScore, edge2AverageScore, edge1ResidualRms, edge2ResidualRms, parallelismErrorDegrees, Math.Min(filteredEdge1Points.Length, filteredEdge2Points.Length), caliperCount);
            if (confidence < MinimumDetectionConfidence)
            {
                return false;
            }

            result = new LineMeasureGradientDetectionResult(
                detectedP1,
                detectedP2,
                [..invalidCaliperCenters],
                [..filteredEdge1Points],
                [..filteredEdge2Points],
                [..rejectedEdge1Points],
                [..rejectedEdge2Points],
                [..filteredEdge1Samples.Select(sample => sample.Score)],
                [..filteredEdge2Samples.Select(sample => sample.Score)],
                new DetectedLineSegment(fittedEdge1.Start, fittedEdge1.End),
                new DetectedLineSegment(fittedEdge2.Start, fittedEdge2.End),
                edge1AverageScore,
                edge2AverageScore,
                edge1ResidualRms,
                edge2ResidualRms,
                edge1ResidualMax,
                edge2ResidualMax,
                Math.Min(filteredEdge1Points.Length, filteredEdge2Points.Length),
                edge1AngleDegrees,
                edge2AngleDegrees,
                parallelismErrorDegrees,
                confidence);

            return true;
        }

        public static bool TryDetectLineCaliperEdges(BitmapSource bitmap, LineCaliperMeasureRoi line, out LineCaliperDetectionResult result)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(line);
            result = default;

            Vector lineDirection = (line.P2 - line.P1).ToWpfVector();
            double lineLength = lineDirection.Length;
            if (lineLength <= 0.5 || line.CaliperSearchRange <= 0)
            {
                return false;
            }

            lineDirection.Normalize();
            Vector measurementDirection = new(-lineDirection.Y, lineDirection.X);

            bitmap = NormalizeBitmap(bitmap);
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            int caliperCount = Math.Clamp(line.CaliperCount, 6, 180);
            int minimumValidCalipers = Math.Min(Math.Max(3, line.MinimumValidCalipers), caliperCount);
            List<Point> invalidCaliperCenters = new(caliperCount);
            List<CaliperEdgeSample> edgeSamples = new(caliperCount);

            for (int i = 0; i < caliperCount; i++)
            {
                double lerp = caliperCount == 1 ? 0.5 : (double)i / (caliperCount - 1);
                Point sampleCenter = new(
                    line.P1.X + (line.P2.X - line.P1.X) * lerp,
                    line.P1.Y + (line.P2.Y - line.P1.Y) * lerp);
                if (!TryFindStrongestCircularGradient(
                        pixels,
                        bitmap.PixelWidth,
                        bitmap.PixelHeight,
                        stride,
                        bytesPerPixel,
                        bitmap.Format,
                        sampleCenter,
                        measurementDirection,
                        lineDirection,
                        line.CaliperSearchRange,
                        line.CaliperSamplingHalfWidth,
                        line.CaliperEdgeSigma,
                        line.CaliperMinimumGradient,
                        line.CaliperEdgePolarity,
                        line.EdgeSelection,
                        out CaliperEdgeSample edgeSample))
                {
                    invalidCaliperCenters.Add(sampleCenter);
                    continue;
                }

                edgeSamples.Add(edgeSample);
            }

            if (edgeSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            List<CaliperEdgeSample> filteredSamples = FilterInlierSamples(edgeSamples, lineDirection, lineLength / 2, line.CaliperOutlierThreshold, minimumValidCalipers);
            if (filteredSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            Point[] filteredPoints = [..filteredSamples.Select(sample => sample.Point)];
            Point[] rejectedPoints = [..edgeSamples.Where(sample => !filteredSamples.Contains(sample)).Select(sample => sample.Point)];
            LineSegmentOverlay fittedLine = FitLine(filteredPoints, lineDirection, lineLength / 2, BuildScoreWeights(filteredSamples));
            Vector fittedDirection = (fittedLine.End - fittedLine.Start).ToWpfVector();
            if (fittedDirection.LengthSquared < 1e-6)
            {
                return false;
            }

            Point detectedP1 = ProjectPointOntoLine(line.P1.ToWpfPoint(), fittedLine.Start.ToWpfPoint(), fittedDirection);
            Point detectedP2 = ProjectPointOntoLine(line.P2.ToWpfPoint(), fittedLine.Start.ToWpfPoint(), fittedDirection);
            if (GeometryUtils.Distance(detectedP1, detectedP2) <= 0.5)
            {
                return false;
            }

            double averageScore = filteredSamples.Average(sample => sample.Score);
            (double residualRms, double residualMax) = ComputeResidualMetrics(filteredPoints, fittedLine);
            double angleDegrees = NormalizeLineAngleDegrees(fittedDirection);
            double confidence = ComputeCircularConfidence(averageScore, residualRms, filteredPoints.Length, caliperCount);
            if (confidence < MinimumDetectionConfidence)
            {
                return false;
            }

            result = new LineCaliperDetectionResult(
                line.P1.ToWpfPoint(),
                line.P2.ToWpfPoint(),
                detectedP1,
                detectedP2,
                [..invalidCaliperCenters],
                filteredPoints,
                [..rejectedPoints],
                [..filteredSamples.Select(sample => sample.Score)],
                new DetectedLineSegment(fittedLine.Start, fittedLine.End),
                averageScore,
                residualRms,
                residualMax,
                filteredPoints.Length,
                angleDegrees,
                confidence);

            return true;
        }

        public static bool TryDetectCircularCaliperEdges(BitmapSource bitmap, CircularCaliperMeasureRoi caliper, out CircularCaliperDetectionResult result)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(caliper);
            result = default;

            if (caliper is ArcCaliperMeasureRoi arcCaliper)
            {
                return TryDetectArcCaliperEdges(bitmap, arcCaliper, out result);
            }

            if (caliper.Radius <= 0 || caliper.CaliperSearchRange <= 0)
            {
                return false;
            }

            bitmap = NormalizeBitmap(bitmap);
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            int caliperCount = Math.Clamp(caliper.CaliperCount, 6, 180);
            int minimumValidCalipers = Math.Min(Math.Max(3, caliper.MinimumValidCalipers), caliperCount);
            List<Point> invalidSamplePoints = new(caliperCount);
            List<CaliperEdgeSample> edgeSamples = new(caliperCount);

            for (int i = 0; i < caliperCount; i++)
            {
                double angleRadians = i * Math.PI * 2 / caliperCount;
                Vector radialDirection = new(Math.Cos(angleRadians), Math.Sin(angleRadians));
                Vector tangentDirection = new(-radialDirection.Y, radialDirection.X);
                Point sampleCenter = caliper.Center.ToWpfPoint() + radialDirection * caliper.Radius;
                if (!TryFindStrongestCircularGradient(
                        pixels,
                        bitmap.PixelWidth,
                        bitmap.PixelHeight,
                        stride,
                        bytesPerPixel,
                        bitmap.Format,
                        sampleCenter,
                        radialDirection,
                        tangentDirection,
                        caliper.CaliperSearchRange,
                        caliper.CaliperSamplingHalfWidth,
                        caliper.CaliperEdgeSigma,
                        caliper.CaliperMinimumGradient,
                        caliper.CaliperEdgePolarity,
                        caliper.EdgeSelection,
                        out CaliperEdgeSample edgeSample))
                {
                    invalidSamplePoints.Add(sampleCenter);
                    continue;
                }

                edgeSamples.Add(edgeSample);
            }

            if (edgeSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            List<CaliperEdgeSample> filteredSamples = FilterCircularInlierSamples(edgeSamples, caliper.Center.ToWpfPoint(), caliper.Radius, caliper.CaliperOutlierThreshold, minimumValidCalipers);
            if (filteredSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            Point[] filteredPoints = [..filteredSamples.Select(sample => sample.Point)];
            Point[] rejectedPoints = [..edgeSamples.Where(sample => !filteredSamples.Contains(sample)).Select(sample => sample.Point)];

            if (!TryFitCircle(filteredPoints, out Point detectedCenter, out double detectedRadius, BuildScoreWeights(filteredSamples)) || detectedRadius <= 0)
            {
                return false;
            }

            (double residualRms, double residualMax) = ComputeCircularResidualMetrics(filteredPoints, detectedCenter, detectedRadius);
            double averageScore = filteredSamples.Average(sample => sample.Score);
            double confidence = ComputeCircularConfidence(averageScore, residualRms, filteredPoints.Length, caliperCount);
            if (confidence < MinimumDetectionConfidence)
            {
                return false;
            }

            result = new CircularCaliperDetectionResult(
                caliper.Center.ToWpfPoint(),
                caliper.Radius,
                detectedCenter,
                detectedRadius,
                [..invalidSamplePoints],
                filteredPoints,
                [..rejectedPoints],
                [..filteredSamples.Select(sample => sample.Score)],
                averageScore,
                residualRms,
                residualMax,
                filteredPoints.Length,
                confidence);

            return true;
        }

        /// <summary>
        /// 从一个近似落点自动搜索整幅图像范围内的圆边缘。
        /// Chinese: 自动圆工具只需要一个落点；以落点为参考中心建立宽搜索卡尺，再复用圆形卡尺的鲁棒拟合与质量评估。
        /// English: Finds a circle from a single approximate click by using a wide radial search and
        /// the same robust fitting and quality checks as the circular caliper.
        /// </summary>
        internal static bool TryDetectAutomaticCircle(BitmapSource bitmap, Point seed, out CircularCaliperMeasureRoi roi)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            roi = null!;

            if (bitmap.PixelWidth < 8 || bitmap.PixelHeight < 8 ||
                double.IsNaN(seed.X) || double.IsNaN(seed.Y) ||
                double.IsInfinity(seed.X) || double.IsInfinity(seed.Y))
            {
                return false;
            }

            double maxDistance = 0;
            foreach (Point corner in new[]
            {
                new Point(0, 0),
                new Point(bitmap.PixelWidth - 1, 0),
                new Point(0, bitmap.PixelHeight - 1),
                new Point(bitmap.PixelWidth - 1, bitmap.PixelHeight - 1)
            })
            {
                maxDistance = Math.Max(maxDistance, GeometryUtils.Distance(seed, corner));
            }

            // A bounded search keeps a click responsive on very large images while still covering
            // ordinary inspection targets. The actual image boundary remains the final limiter.
            double searchExtent = Math.Clamp(maxDistance, 18, 4096);
            int searchRange = Math.Max(9, (int)Math.Ceiling(searchExtent / 2));
            var candidate = new CircularCaliperMeasureRoi
            {
                Center = seed.ToPointD(),
                Radius = searchRange,
                CaliperCount = 96,
                CaliperSearchRange = searchRange,
                CaliperSamplingHalfWidth = 1,
                CaliperEdgeSigma = 1.0,
                MinimumValidCalipers = 24,
                CaliperMinimumGradient = 8,
                CaliperOutlierThreshold = 0,
                CaliperEdgePolarity = CaliperEdgePolarity.Any,
                EdgeSelection = 1
            };

            if (!TryDetectCircularCaliperEdges(bitmap, candidate, out CircularCaliperDetectionResult detection))
            {
                return false;
            }

            if (detection.ValidCaliperCount < candidate.MinimumValidCalipers ||
                detection.DetectedRadius < 2 ||
                detection.DetectedRadius > searchExtent * 1.15 ||
                detection.ResidualRms > Math.Max(3.0, detection.DetectedRadius * 0.08))
            {
                return false;
            }

            // Keep the committed ROI compact and editable after the wide search has succeeded.
            // The broad search is an implementation detail of the click; subsequent refreshes use
            // a local caliper around the fitted circle instead of drawing a full-image overlay.
            candidate.CaliperSearchRange = Math.Clamp((int)Math.Ceiling(detection.DetectedRadius * 0.15), 8, 64);
            CircularCaliperDetectionResult displayDetection = detection with
            {
                ReferenceCenter = detection.DetectedCenter,
                ReferenceRadius = detection.DetectedRadius
            };
            RoiDetectionResultMapper.Apply(candidate, displayDetection);
            roi = candidate;
            return true;
        }

        /// <summary>
        /// 在点击点周围沿多方向搜索局部梯度峰，并用既有的亚像素边缘定位器返回最佳点。
        /// </summary>
        internal static bool TrySnapPointToEdge(BitmapSource bitmap, Point seed, out Point snapped, out double score, out double confidence)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            snapped = default;
            score = 0;
            confidence = 0;

            if (bitmap.PixelWidth < 5 || bitmap.PixelHeight < 5 ||
                double.IsNaN(seed.X) || double.IsNaN(seed.Y) ||
                double.IsInfinity(seed.X) || double.IsInfinity(seed.Y))
            {
                return false;
            }

            // Immutable sources can safely reuse their byte buffer while the pointer moves.
            // Mutable sources (for example WriteableBitmap) must be copied per request so a
            // live image update cannot leave the snapper working on stale pixels.
            EdgeSnapPixelBuffer buffer = bitmap.IsFrozen
                ? EdgeSnapBuffers.GetValue(bitmap, static source => new EdgeSnapPixelBuffer(NormalizeBitmap(source)))
                : new EdgeSnapPixelBuffer(NormalizeBitmap(bitmap));

            const int searchRange = 12;
            const int directionCount = 16;
            const double minimumGradient = 10;
            var candidates = new List<CaliperEdgeSample>(directionCount);
            for (int i = 0; i < directionCount; i++)
            {
                double angle = i * Math.PI / directionCount;
                Vector direction = new(Math.Cos(angle), Math.Sin(angle));
                Vector tangent = new(-direction.Y, direction.X);
                if (TryFindStrongestCircularGradient(
                        buffer.Pixels,
                        buffer.PixelWidth,
                        buffer.PixelHeight,
                        buffer.Stride,
                        buffer.BytesPerPixel,
                        buffer.Format,
                        seed,
                        direction,
                        tangent,
                        searchRange,
                        averagingHalfWidth: 1,
                        edgeSigma: 1.0,
                        minimumGradient,
                        CaliperEdgePolarity.Any,
                        edgeSelection: 1,
                        out CaliperEdgeSample candidate))
                {
                    candidates.Add(candidate);
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            CaliperEdgeSample best = candidates
                .OrderByDescending(candidate => candidate.Score * (0.65 + 0.35 * Math.Clamp(1 - GeometryUtils.Distance(seed, candidate.Point) / searchRange, 0, 1)))
                .First();
            double distance = GeometryUtils.Distance(seed, best.Point);
            if (best.Score < minimumGradient || distance > searchRange + 1)
            {
                return false;
            }

            snapped = best.Point;
            score = best.Score;
            int consensusCount = candidates.Count(candidate => GeometryUtils.Distance(candidate.Point, best.Point) <= 3.0);
            double consensus = Math.Clamp((double)consensusCount / Math.Max(2, directionCount * 0.35), 0, 1);
            confidence = Math.Clamp(
                0.7 * best.Score / MaxCaliperScore +
                0.3 * (1 - Math.Min(distance / searchRange, 1)),
                0,
                1);
            confidence *= 0.65 + 0.35 * consensus;
            return confidence >= MinimumDetectionConfidence;
        }

        private static bool TryDetectArcCaliperEdges(BitmapSource bitmap, ArcCaliperMeasureRoi caliper, out CircularCaliperDetectionResult result)
        {
            result = default;
            if (caliper.Radius <= 0 || caliper.CaliperSearchRange <= 0 || Math.Abs(caliper.SweepAngle) < 1)
            {
                return false;
            }

            bitmap = NormalizeBitmap(bitmap);
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            int caliperCount = Math.Clamp(caliper.CaliperCount, 4, 180);
            int minimumValidCalipers = Math.Min(Math.Max(3, caliper.MinimumValidCalipers), caliperCount);
            List<Point> invalidSamplePoints = new(caliperCount);
            List<CaliperEdgeSample> edgeSamples = new(caliperCount);

            for (int i = 0; i < caliperCount; i++)
            {
                double angleDegrees = caliper.StartAngle + (caliperCount == 1 ? 0 : caliper.SweepAngle * i / (caliperCount - 1));
                double angleRadians = angleDegrees * Math.PI / 180.0;
                Vector radialDirection = new(Math.Cos(angleRadians), Math.Sin(angleRadians));
                Vector tangentDirection = new(-radialDirection.Y, radialDirection.X);
                Point sampleCenter = caliper.Center.ToWpfPoint() + radialDirection * caliper.Radius;
                if (!TryFindStrongestCircularGradient(
                        pixels,
                        bitmap.PixelWidth,
                        bitmap.PixelHeight,
                        stride,
                        bytesPerPixel,
                        bitmap.Format,
                        sampleCenter,
                        radialDirection,
                        tangentDirection,
                        caliper.CaliperSearchRange,
                        caliper.CaliperSamplingHalfWidth,
                        caliper.CaliperEdgeSigma,
                        caliper.CaliperMinimumGradient,
                        caliper.CaliperEdgePolarity,
                        caliper.EdgeSelection,
                        out CaliperEdgeSample edgeSample))
                {
                    invalidSamplePoints.Add(sampleCenter);
                    continue;
                }

                edgeSamples.Add(edgeSample);
            }

            if (edgeSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            List<CaliperEdgeSample> filteredSamples = FilterCircularInlierSamples(edgeSamples, caliper.Center.ToWpfPoint(), caliper.Radius, caliper.CaliperOutlierThreshold, minimumValidCalipers);
            if (filteredSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            Point[] filteredPoints = [..filteredSamples.Select(sample => sample.Point)];
            Point[] rejectedPoints = [..edgeSamples.Where(sample => !filteredSamples.Contains(sample)).Select(sample => sample.Point)];
            if (!TryFitCircle(filteredPoints, out Point detectedCenter, out double detectedRadius, BuildScoreWeights(filteredSamples)) || detectedRadius <= 0)
            {
                return false;
            }

            (double residualRms, double residualMax) = ComputeCircularResidualMetrics(filteredPoints, detectedCenter, detectedRadius);
            double averageScore = filteredSamples.Average(sample => sample.Score);
            double confidence = ComputeCircularConfidence(averageScore, residualRms, filteredPoints.Length, caliperCount);
            if (confidence < MinimumDetectionConfidence)
            {
                return false;
            }

            result = new CircularCaliperDetectionResult(
                caliper.Center.ToWpfPoint(),
                caliper.Radius,
                detectedCenter,
                detectedRadius,
                [..invalidSamplePoints],
                filteredPoints,
                [..rejectedPoints],
                [..filteredSamples.Select(sample => sample.Score)],
                averageScore,
                residualRms,
                residualMax,
                filteredPoints.Length,
                confidence);

            return true;
        }

        private static double NormalizeLineAngleDegrees(Vector direction)
        {
            if (direction.LengthSquared < 1e-6)
            {
                return 0;
            }

            double angle = Math.Atan2(direction.Y, direction.X) * 180 / Math.PI;
            if (angle < 0)
            {
                angle += 180;
            }

            return angle >= 180 ? angle - 180 : angle;
        }

        private static double ComputeConfidence(double edge1AverageScore, double edge2AverageScore, double edge1ResidualRms, double edge2ResidualRms, double parallelismErrorDegrees, int validCaliperCount, int totalCaliperCount)
        {
            double scoreComponent = Math.Clamp(((edge1AverageScore + edge2AverageScore) / 2) / 64.0, 0, 1);
            double residualComponent = 1.0 / (1.0 + Math.Max(edge1ResidualRms, edge2ResidualRms));
            double parallelComponent = 1.0 / (1.0 + parallelismErrorDegrees / 5.0);
            double validRatio = totalCaliperCount <= 0 ? 0 : (double)validCaliperCount / totalCaliperCount;
            return Math.Clamp(scoreComponent * residualComponent * parallelComponent * validRatio, 0, 1);
        }

        private static double ComputeCircularConfidence(double averageScore, double residualRms, int validCaliperCount, int totalCaliperCount)
        {
            double scoreComponent = Math.Clamp(averageScore / 64.0, 0, 1);
            double residualComponent = 1.0 / (1.0 + residualRms);
            double validRatio = totalCaliperCount <= 0 ? 0 : (double)validCaliperCount / totalCaliperCount;
            return Math.Clamp(scoreComponent * residualComponent * validRatio, 0, 1);
        }
    }
}
