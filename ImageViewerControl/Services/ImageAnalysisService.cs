using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Services
{
    internal static class ImageAnalysisService
    {
        internal const double MaxCaliperScore = 255.0;

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

        public static int[] CreateHistogram(BitmapSource bitmap, int binCount)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binCount);

            bitmap = NormalizeBitmap(bitmap);

            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            int[] histogram = new int[binCount];
            for (int index = 0; index < pixels.Length; index += bytesPerPixel)
            {
                byte intensity = GetPixelIntensity(pixels, index, bytesPerPixel, bitmap.Format);
                int binIndex = intensity * binCount / 256;
                if (binIndex >= binCount)
                {
                    binIndex = binCount - 1;
                }

                histogram[binIndex]++;
            }

            return histogram;
        }

        public static byte[] CreateProfile(BitmapSource bitmap, Point start, Point end)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            bitmap = NormalizeBitmap(bitmap);

            var points = GetLinePoints(start, end);
            if (points.Count == 0)
            {
                return Array.Empty<byte>();
            }

            int minX = (int)points.Min(p => p.X);
            int maxX = (int)points.Max(p => p.X);
            int minY = (int)points.Min(p => p.Y);
            int maxY = (int)points.Max(p => p.Y);

            if (maxX < 0 || maxY < 0 || minX >= bitmap.PixelWidth || minY >= bitmap.PixelHeight)
            {
                return Array.Empty<byte>();
            }

            int roiX = Math.Max(0, minX);
            int roiY = Math.Max(0, minY);
            int roiW = Math.Min(bitmap.PixelWidth, maxX + 1) - roiX;
            int roiH = Math.Min(bitmap.PixelHeight, maxY + 1) - roiY;
            if (roiW <= 0 || roiH <= 0)
            {
                return Array.Empty<byte>();
            }

            int bytesPerPixel = (bitmap.Format.BitsPerPixel + 7) / 8;
            int stride = roiW * bytesPerPixel;
            byte[] pixels = new byte[roiH * stride];
            bitmap.CopyPixels(new Int32Rect(roiX, roiY, roiW, roiH), pixels, stride, 0);

            byte[] profileData = new byte[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                int pixelX = (int)points[i].X;
                int pixelY = (int)points[i].Y;
                if (pixelX < roiX || pixelX >= roiX + roiW || pixelY < roiY || pixelY >= roiY + roiH)
                {
                    continue;
                }

                int localX = pixelX - roiX;
                int localY = pixelY - roiY;
                int index = localY * stride + localX * bytesPerPixel;
                profileData[i] = GetPixelIntensity(pixels, index, bytesPerPixel, bitmap.Format);
            }

            return profileData;
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

            Vector measurementDirection = line.GetCaliperMeasurementDirection();
            double estimatedDistance = GeometryUtils.Distance(line.P1, line.P2);

            bitmap = NormalizeBitmap(bitmap);
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            Vector caliperDirection = new(-measurementDirection.Y, measurementDirection.X);
            Point measurementCenter = line.CaliperCenter;
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
                if (!TryFindStrongestGradientPair(pixels, bitmap.PixelWidth, bitmap.PixelHeight, stride, bytesPerPixel, bitmap.Format, caliperCenter, measurementDirection, caliperDirection, halfSearchRange, line.CaliperSamplingHalfWidth, line.CaliperMinimumGradient, line.CaliperEdgePolarity, line.MinimumEdgeGap, line.NominalEdgeGap, line.NominalEdgeGapTolerance, out CaliperEdgeSample edge1Sample, out CaliperEdgeSample edge2Sample))
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
            if (!TryIntersectLines(measurementCenter, measurementDirection, fittedEdge1.Start, fittedEdge1.End - fittedEdge1.Start, out Point detectedP1) ||
                !TryIntersectLines(measurementCenter, measurementDirection, fittedEdge2.Start, fittedEdge2.End - fittedEdge2.Start, out Point detectedP2))
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
            double edge1AngleDegrees = NormalizeLineAngleDegrees(fittedEdge1.End - fittedEdge1.Start);
            double edge2AngleDegrees = NormalizeLineAngleDegrees(fittedEdge2.End - fittedEdge2.Start);
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

            Vector lineDirection = line.P2 - line.P1;
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
            Vector fittedDirection = fittedLine.End - fittedLine.Start;
            if (fittedDirection.LengthSquared < 1e-6)
            {
                return false;
            }

            Point detectedP1 = ProjectPointOntoLine(line.P1, fittedLine.Start, fittedDirection);
            Point detectedP2 = ProjectPointOntoLine(line.P2, fittedLine.Start, fittedDirection);
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
                line.P1,
                line.P2,
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
                Point sampleCenter = caliper.Center + radialDirection * caliper.Radius;
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

            List<CaliperEdgeSample> filteredSamples = FilterCircularInlierSamples(edgeSamples, caliper.Center, caliper.Radius, caliper.CaliperOutlierThreshold, minimumValidCalipers);
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
                caliper.Center,
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
                Point sampleCenter = caliper.Center + radialDirection * caliper.Radius;
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

            List<CaliperEdgeSample> filteredSamples = FilterCircularInlierSamples(edgeSamples, caliper.Center, caliper.Radius, caliper.CaliperOutlierThreshold, minimumValidCalipers);
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
                caliper.Center,
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

        public static bool TryCalculateStatistics(BitmapSource bitmap, RoiBase roi, out RoiStatistics statistics)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(roi);

            bitmap = NormalizeBitmap(bitmap);

            statistics = new RoiStatistics();
            Rect bounds = GetRoiBounds(roi);
            if (bounds.IsEmpty)
            {
                return false;
            }

            int minX = Math.Max(0, (int)Math.Floor(bounds.X));
            int minY = Math.Max(0, (int)Math.Floor(bounds.Y));
            int maxX = Math.Min(bitmap.PixelWidth - 1, (int)Math.Ceiling(bounds.Right));
            int maxY = Math.Min(bitmap.PixelHeight - 1, (int)Math.Ceiling(bounds.Bottom));
            if (maxX < minX || maxY < minY)
            {
                return false;
            }

            int roiW = maxX - minX + 1;
            int roiH = maxY - minY + 1;
            int bytesPerPixel = (bitmap.Format.BitsPerPixel + 7) / 8;
            int stride = roiW * bytesPerPixel;
            byte[] pixels = new byte[roiH * stride];
            bitmap.CopyPixels(new Int32Rect(minX, minY, roiW, roiH), pixels, stride, 0);

            int count = 0;
            long sum = 0;
            double sumSquares = 0;
            byte min = byte.MaxValue;
            byte max = byte.MinValue;

            for (int localY = 0; localY < roiH; localY++)
            {
                for (int localX = 0; localX < roiW; localX++)
                {
                    Point samplePoint = new(minX + localX + 0.5, minY + localY + 0.5);
                    if (!Contains(roi, samplePoint))
                    {
                        continue;
                    }

                    int index = localY * stride + localX * bytesPerPixel;
                    byte value = GetPixelIntensity(pixels, index, bytesPerPixel, bitmap.Format);
                    count++;
                    sum += value;
                    sumSquares += value * value;
                    if (value < min)
                    {
                        min = value;
                    }

                    if (value > max)
                    {
                        max = value;
                    }
                }
            }

            if (count == 0)
            {
                return false;
            }

            double mean = (double)sum / count;
            double variance = Math.Max(0, sumSquares / count - mean * mean);
            statistics = new RoiStatistics
            {
                PixelCount = count,
                Mean = mean,
                Min = min,
                Max = max,
                StandardDeviation = Math.Sqrt(variance)
            };
            return true;
        }

        private static List<Point> GetLinePoints(Point start, Point end)
        {
            int x0 = (int)start.X;
            int y0 = (int)start.Y;
            int x1 = (int)end.X;
            int y1 = (int)end.Y;

            var points = new List<Point>();
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            int cx = x0;
            int cy = y0;
            while (true)
            {
                points.Add(new Point(cx, cy));
                if (cx == x1 && cy == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    cx += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    cy += sy;
                }
            }

            return points;
        }

        private static Rect GetRoiBounds(RoiBase roi)
        {
            return roi switch
            {
                RotatedRect rect => GeometryUtils.GetBoundingBox(new[]
                {
                    GeometryUtils.RotatePoint(new Point(rect.Center.X - rect.Width / 2, rect.Center.Y - rect.Height / 2), rect.Center, rect.Angle),
                    GeometryUtils.RotatePoint(new Point(rect.Center.X + rect.Width / 2, rect.Center.Y - rect.Height / 2), rect.Center, rect.Angle),
                    GeometryUtils.RotatePoint(new Point(rect.Center.X + rect.Width / 2, rect.Center.Y + rect.Height / 2), rect.Center, rect.Angle),
                    GeometryUtils.RotatePoint(new Point(rect.Center.X - rect.Width / 2, rect.Center.Y + rect.Height / 2), rect.Center, rect.Angle)
                }),
                EllipseRoi ellipse => GeometryUtils.GetBoundingBox(new[]
                {
                    GeometryUtils.RotatePoint(new Point(ellipse.Center.X - ellipse.RadiusX, ellipse.Center.Y - ellipse.RadiusY), ellipse.Center, ellipse.Angle),
                    GeometryUtils.RotatePoint(new Point(ellipse.Center.X + ellipse.RadiusX, ellipse.Center.Y - ellipse.RadiusY), ellipse.Center, ellipse.Angle),
                    GeometryUtils.RotatePoint(new Point(ellipse.Center.X + ellipse.RadiusX, ellipse.Center.Y + ellipse.RadiusY), ellipse.Center, ellipse.Angle),
                    GeometryUtils.RotatePoint(new Point(ellipse.Center.X - ellipse.RadiusX, ellipse.Center.Y + ellipse.RadiusY), ellipse.Center, ellipse.Angle)
                }),
                CircleRoi circle => new Rect(circle.Center.X - circle.Radius, circle.Center.Y - circle.Radius, circle.Radius * 2, circle.Radius * 2),
                RingRoi ring => new Rect(ring.Center.X - ring.OuterRadius, ring.Center.Y - ring.OuterRadius, ring.OuterRadius * 2, ring.OuterRadius * 2),
                PolygonRoi poly => GeometryUtils.GetBoundingBox(poly.Points),
                _ => Rect.Empty
            };
        }

        /// <summary>
        /// 亚像素定位调度：锐利阶跃用灰度矩法，模糊/渐变边缘用抛物线插值（互补）。
        /// Chinese: 灰度矩对理想阶跃更稳，但对含中间灰度的渐变边缘存在偏置；按窗口内最大单步跳变自动选择。
        /// English: Dispatches subpixel localization: gray-moment for sharp steps, parabolic for soft/gradient edges.
        /// </summary>
        private static double SelectSubpixelPosition(int bestIndex, double[] profile, double[] score)
        {
            const double sharpStepThreshold = 200.0;
            double maxStep = 0;
            int lo = Math.Max(0, bestIndex - 1);
            int hi = Math.Min(profile.Length - 1, bestIndex + 1);
            for (int i = lo + 1; i <= hi; i++)
            {
                maxStep = Math.Max(maxStep, Math.Abs(profile[i] - profile[i - 1]));
            }

            return maxStep >= sharpStepThreshold
                ? RefineGrayMomentOffset(bestIndex, profile)
                : RefinePeakOffset(bestIndex, score);
        }

        /// <summary>
        /// 单边缘选择完毕后的亚像素定位（锐利/渐变自适应）。
        /// </summary>
        private static bool TryFindStrongestCircularGradient(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector measurementDirection, Vector averagingDirection, int searchRange, int averagingHalfWidth, double minimumGradient, CaliperEdgePolarity polarity, int edgeSelection, out CaliperEdgeSample edgeSample)
        {
            edgeSample = default;
            int sampleCount = searchRange * 2 + 1;
            double[] profile = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int axisOffset = i - searchRange;
                Point sampleCenter = center + measurementDirection * axisOffset;
                profile[i] = SampleAveragedIntensity(pixels, pixelWidth, pixelHeight, stride, bytesPerPixel, format, sampleCenter, averagingDirection, averagingHalfWidth);
            }

            double[] score = BuildGradientScoreProfile(profile, polarity);

            if (!TrySelectPeak(score, edgeSelection, out int bestIndex, out double strongestGradient))
            {
                return false;
            }

            if (strongestGradient < minimumGradient)
            {
                return false;
            }

            // 亚像素定位：锐利阶跃用灰度矩法，模糊/渐变边缘回退抛物线插值。
            double subpixelPosition = SelectSubpixelPosition(bestIndex, profile, score);
            Point detectedPosition = center + measurementDirection * (subpixelPosition - searchRange);
            edgeSample = new CaliperEdgeSample(detectedPosition, strongestGradient);
            return true;
        }

        /// <summary>
        /// 在得分序列中按"边缘序号"选择山峰：1=最强，2=次强，依此类推。
        /// Chinese: 收集全部局部极大峰并按得分降序排列，取第 edgeSelection 个；越界时回退到最强峰。
        /// English: Selects the N-th strongest gradient peak from the score profile.
        /// </summary>
        private static bool TrySelectPeak(double[] score, int edgeSelection, out int bestIndex, out double strongestGradient)
        {
            bestIndex = -1;
            strongestGradient = 0;
            if (edgeSelection <= 1)
            {
                for (int i = 0; i < score.Length; i++)
                {
                    if (score[i] > strongestGradient)
                    {
                        strongestGradient = score[i];
                        bestIndex = i;
                    }
                }

                return bestIndex >= 0;
            }

            // 收集所有严格局部峰。
            List<int> peaks = new(score.Length);
            for (int i = 1; i < score.Length - 1; i++)
            {
                if (score[i] > score[i - 1] && score[i] >= score[i + 1])
                {
                    peaks.Add(i);
                }
            }

            if (peaks.Count == 0)
            {
                return false;
            }

            peaks.Sort((left, right) => score[right].CompareTo(score[left]));
            int index = Math.Min(edgeSelection - 1, peaks.Count - 1);
            if (index < 0)
            {
                return false;
            }

            bestIndex = peaks[index];
            strongestGradient = score[bestIndex];
            return true;
        }

        /// <summary>
        /// 构建基于强度的梯度分数序列（对齐到索引 0..n-1，与采样点一一对应）。
        /// Chinese: 中心差分梯度 + 极性方向得分；序列端点梯度置零以支持亚像素抛物线插值。
        /// English: Builds a gradient score profile aligned to the sample indices 0..n-1.
        /// </summary>
        private static double[] BuildGradientScoreProfile(double[] profile, CaliperEdgePolarity polarity)
        {
            var score = new double[profile.Length];
            for (int i = 1; i < profile.Length - 1; i++)
            {
                double gradient = profile[i + 1] - profile[i - 1];
                score[i] = polarity switch
                {
                    CaliperEdgePolarity.DarkToLight => Math.Max(gradient, 0),
                    CaliperEdgePolarity.LightToDark => Math.Max(-gradient, 0),
                    _ => Math.Abs(gradient)
                };
            }

            return score;
        }

        /// <summary>
        /// 对得分序列在指定索引处做抛物线插值，返回亚像素峰值位置。
        /// Chinese: 三点抛物线拟合，结果以双精度索引表示（例如 bestIndex=5 时返回 4.7 表示峰值偏向左侧）。
        /// English: Parabolic interpolation of the score profile to locate the peak at subpixel precision.
        /// </summary>
        internal static double RefinePeakOffset(int bestIndex, double[] score)
        {
            if (bestIndex <= 0 || bestIndex >= score.Length - 1)
            {
                return bestIndex;
            }

            double left = score[bestIndex - 1];
            double center = score[bestIndex];
            double right = score[bestIndex + 1];
            double denominator = left - 2 * center + right;
            if (Math.Abs(denominator) < 1e-9)
            {
                return bestIndex;
            }

            double offset = 0.5 * (left - right) / denominator;
            return bestIndex + Math.Clamp(offset, -0.5, 0.5);
        }

        private static bool Contains(RoiBase roi, Point point)
        {
            switch (roi)
            {
                case RotatedRect rect:
                    var rectMatrix = new Matrix();
                    rectMatrix.RotateAt(-rect.Angle, rect.Center.X, rect.Center.Y);
                    Point rectPoint = rectMatrix.Transform(point);
                    double rectHalfW = rect.Width / 2;
                    double rectHalfH = rect.Height / 2;
                    return rectPoint.X >= rect.Center.X - rectHalfW && rectPoint.X <= rect.Center.X + rectHalfW &&
                           rectPoint.Y >= rect.Center.Y - rectHalfH && rectPoint.Y <= rect.Center.Y + rectHalfH;

                case EllipseRoi ellipse:
                    var ellipseMatrix = new Matrix();
                    ellipseMatrix.RotateAt(-ellipse.Angle, ellipse.Center.X, ellipse.Center.Y);
                    Point ellipsePoint = ellipseMatrix.Transform(point);
                    double dx = ellipsePoint.X - ellipse.Center.X;
                    double dy = ellipsePoint.Y - ellipse.Center.Y;
                    return ellipse.RadiusX > 0 && ellipse.RadiusY > 0 &&
                           (dx * dx) / (ellipse.RadiusX * ellipse.RadiusX) + (dy * dy) / (ellipse.RadiusY * ellipse.RadiusY) <= 1;

                case CircleRoi circle:
                    return GeometryUtils.Distance(circle.Center, point) <= circle.Radius;

                case RingRoi ring:
                    double distance = GeometryUtils.Distance(ring.Center, point);
                    return distance >= ring.InnerRadius && distance <= ring.OuterRadius;

                case PolygonRoi poly:
                    return GeometryUtils.IsPointInPolygon(point, poly.Points);

                default:
                    return false;
            }
        }

        private static byte GetPixelIntensity(byte[] pixels, int index, int bytesPerPixel, PixelFormat format)
        {
            if (format == PixelFormats.Gray8)
            {
                return pixels[index];
            }

            if (bytesPerPixel >= 3)
            {
                byte b = pixels[index];
                byte g = pixels[index + 1];
                byte r = pixels[index + 2];
                return (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            }

            return 0;
        }

        /// <summary>
        /// 由卡尺边缘样本的梯度得分派生拟合权重（0.15–1.0）。
        /// Chinese: 弱边缘点保留最小权重而非完全丢弃，避免低对比区域被忽略。
        /// English: Derives fit weights from edge gradient scores, clamped to [0.15, 1.0].
        /// </summary>
        private static double[] BuildScoreWeights(IReadOnlyList<CaliperEdgeSample> samples)
        {
            var weights = new double[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                weights[i] = 0.15 + 0.85 * Math.Clamp(samples[i].Score / MaxCaliperScore, 0, 1);
            }

            return weights;
        }

        /// <summary>
        /// 灰度矩法亚像素边缘定位（矩保持原理，Tabatabai 1984）。
        /// Chinese: 用窗口内强度样本的前三阶矩拟合理想阶跃边缘模型，闭合求解边缘位置；
        /// 对非对称边缘、低对比与带噪斜坡比抛物线插值更稳。
        /// English: Gray-moment subpixel edge localization via moment-preserving step-edge model.
        /// </summary>
        internal static double RefineGrayMomentOffset(int bestIndex, double[] profile)
        {
            const int windowRadius = 2;
            if (bestIndex < 0 || bestIndex >= profile.Length)
            {
                return bestIndex;
            }

            int lo = Math.Max(0, bestIndex - windowRadius);
            int hi = Math.Min(profile.Length - 1, bestIndex + windowRadius);
            int count = hi - lo + 1;
            if (count < 3)
            {
                return bestIndex;
            }

            double m1 = 0;
            double m2 = 0;
            double m3 = 0;
            for (int i = lo; i <= hi; i++)
            {
                double value = profile[i];
                m1 += value;
                m2 += value * value;
                m3 += value * value * value;
            }

            m1 /= count;
            m2 /= count;
            m3 /= count;

            double variance = m2 - m1 * m1;
            if (variance < 1e-9)
            {
                return bestIndex;
            }

            double standardDeviation = Math.Sqrt(variance);
            double thirdCentralMoment = m3 - 3 * m1 * m2 + 2 * m1 * m1 * m1;
            if (Math.Abs(thirdCentralMoment) < 1e-9)
            {
                return bestIndex;
            }

            // 低侧占比 p（理想阶跃两水平模型的矩闭合解）；第三中心矩符号决定阶跃方向。
            // 高→低（暗在右）时低侧位于窗口右端，位置从右侧回溯。
            double p = thirdCentralMoment > 0
                ? 0.5 * (1 + standardDeviation / Math.Sqrt(thirdCentralMoment))
                : 0.5 * (1 - standardDeviation / Math.Sqrt(-thirdCentralMoment));
            p = Math.Clamp(p, 0, 1);

            return thirdCentralMoment > 0
                ? lo + p * (count - 1)
                : hi - p * (count - 1);
        }

        private static bool TryFindStrongestGradientPair(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector measurementDirection, Vector averagingDirection, int searchRange, int averagingHalfWidth, double minimumGradient, CaliperEdgePolarity polarity, double minimumEdgeGapPx, double nominalEdgeGapPx, double nominalEdgeGapTolerancePx, out CaliperEdgeSample edge1Sample, out CaliperEdgeSample edge2Sample)
        {
            edge1Sample = default;
            edge2Sample = default;
            int sampleCount = searchRange * 2 + 1;
            double[] profile = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int axisOffset = i - searchRange;
                Point sampleCenter = center + measurementDirection * axisOffset;
                profile[i] = SampleAveragedIntensity(pixels, pixelWidth, pixelHeight, stride, bytesPerPixel, format, sampleCenter, averagingDirection, averagingHalfWidth);
            }

            double[] score = BuildGradientScoreProfile(profile, polarity);
            int middleIndex = searchRange;
            double strongestGradient1 = 0;
            double strongestGradient2 = 0;
            int bestIndex1 = -1;
            int bestIndex2 = -1;
            for (int i = 1; i < score.Length - 1; i++)
            {
                if (i < middleIndex)
                {
                    double leadingScore = polarity switch
                    {
                        CaliperEdgePolarity.DarkToLight => Math.Max(profile[i + 1] - profile[i - 1], 0),
                        CaliperEdgePolarity.LightToDark => Math.Max(-(profile[i + 1] - profile[i - 1]), 0),
                        _ => Math.Abs(profile[i + 1] - profile[i - 1])
                    };
                    if (leadingScore > strongestGradient1)
                    {
                        strongestGradient1 = leadingScore;
                        bestIndex1 = i;
                    }
                }
                else if (i > middleIndex)
                {
                    double trailingScore = polarity switch
                    {
                        CaliperEdgePolarity.DarkToLight => Math.Max(-(profile[i + 1] - profile[i - 1]), 0),
                        CaliperEdgePolarity.LightToDark => Math.Max(profile[i + 1] - profile[i - 1], 0),
                        _ => Math.Abs(profile[i + 1] - profile[i - 1])
                    };
                    if (trailingScore > strongestGradient2)
                    {
                        strongestGradient2 = trailingScore;
                        bestIndex2 = i;
                    }
                }
            }

            if (bestIndex1 < 0 || bestIndex2 < 0 || strongestGradient1 < minimumGradient || strongestGradient2 < minimumGradient)
            {
                return false;
            }

            // 全局边缘对仲裁：在满足“跨中点、最低梯度、最小间距”的全部候选组合中，
            // 以“两侧得分之和 − 标称宽度先验惩罚”综合评分取最优，避免局部最强对错配。
            if (minimumEdgeGapPx > 0 || nominalEdgeGapPx > 0)
            {
                int bestI1 = -1;
                int bestI2 = -1;
                double bestPairScore = -1;
                for (int i1 = 0; i1 < middleIndex; i1++)
                {
                    if (score[i1] < minimumGradient)
                    {
                        continue;
                    }

                    for (int i2 = middleIndex + 1; i2 < score.Length; i2++)
                    {
                        if (score[i2] < minimumGradient)
                        {
                            continue;
                        }

                        double gap = i2 - i1;
                        if (gap < minimumEdgeGapPx)
                        {
                            continue;
                        }

                        double pairScore = score[i1] + score[i2];
                        if (nominalEdgeGapPx > 0)
                        {
                            double deviation = Math.Abs(gap - nominalEdgeGapPx) - Math.Max(0, nominalEdgeGapTolerancePx);
                            if (deviation > 0)
                            {
                                pairScore -= deviation;
                            }
                        }

                        if (pairScore > bestPairScore)
                        {
                            bestPairScore = pairScore;
                            bestI1 = i1;
                            bestI2 = i2;
                        }
                    }
                }

                if (bestI1 >= 0)
                {
                    bestIndex1 = bestI1;
                    bestIndex2 = bestI2;
                    strongestGradient1 = score[bestIndex1];
                    strongestGradient2 = score[bestIndex2];
                }
            }

            double subpixelPosition1 = SelectSubpixelPosition(bestIndex1, profile, score);
            double subpixelPosition2 = SelectSubpixelPosition(bestIndex2, profile, score);
            edge1Sample = new CaliperEdgeSample(center + measurementDirection * (subpixelPosition1 - searchRange), strongestGradient1);
            edge2Sample = new CaliperEdgeSample(center + measurementDirection * (subpixelPosition2 - searchRange), strongestGradient2);
            return true;
        }

        private static List<CaliperEdgeSample> FilterInlierSamples(List<CaliperEdgeSample> samples, Vector preferredDirection, double fallbackHalfLength, double configuredThreshold, int minimumRequired)
        {
            if (samples.Count <= minimumRequired)
            {
                return samples;
            }

            Point[] points = [..samples.Select(sample => sample.Point)];

            LineSegmentOverlay provisionalFit = FitLine(points, preferredDirection, fallbackHalfLength);
            Vector fitDirection = provisionalFit.End - provisionalFit.Start;
            if (fitDirection.LengthSquared < 1e-6)
            {
                return samples;
            }

            List<(CaliperEdgeSample Sample, double Distance)> distances = new(samples.Count);
            foreach (CaliperEdgeSample sample in samples)
            {
                distances.Add((sample, DistanceToLine(sample.Point, provisionalFit.Start, fitDirection)));
            }

            double threshold = configuredThreshold > 0
                ? configuredThreshold
                : Math.Max(1.0, distances.Select(item => item.Distance).OrderBy(value => value).Skip(distances.Count / 2).FirstOrDefault() * 2.5);

            List<CaliperEdgeSample> filtered = distances
                .Where(item => item.Distance <= threshold)
                .Select(item => item.Sample)
                .ToList();

            return filtered.Count >= minimumRequired ? filtered : samples;
        }

        private static List<CaliperEdgeSample> FilterCircularInlierSamples(List<CaliperEdgeSample> samples, Point fallbackCenter, double fallbackRadius, double configuredThreshold, int minimumRequired)
        {
            if (samples.Count <= minimumRequired)
            {
                return samples;
            }

            Point[] points = [..samples.Select(sample => sample.Point)];
            if (!TryFitCircle(points, out Point center, out double radius) || radius <= 0)
            {
                center = fallbackCenter;
                radius = fallbackRadius;
            }

            List<(CaliperEdgeSample Sample, double Distance)> distances = new(samples.Count);
            foreach (CaliperEdgeSample sample in samples)
            {
                distances.Add((sample, Math.Abs(GeometryUtils.Distance(sample.Point, center) - radius)));
            }

            double threshold = configuredThreshold > 0
                ? configuredThreshold
                : Math.Max(1.0, distances.Select(item => item.Distance).OrderBy(value => value).Skip(distances.Count / 2).FirstOrDefault() * 2.5);

            List<CaliperEdgeSample> filtered = distances
                .Where(item => item.Distance <= threshold)
                .Select(item => item.Sample)
                .ToList();

            return filtered.Count >= minimumRequired ? filtered : samples;
        }

        private static (double Rms, double Max) ComputeResidualMetrics(Point[] points, LineSegmentOverlay fittedLine)
        {
            if (points.Length == 0)
            {
                return (0, 0);
            }

            Vector direction = fittedLine.End - fittedLine.Start;
            double sumSquares = 0;
            double maxResidual = 0;
            foreach (Point point in points)
            {
                double distance = DistanceToLine(point, fittedLine.Start, direction);
                sumSquares += distance * distance;
                maxResidual = Math.Max(maxResidual, distance);
            }

            return (Math.Sqrt(sumSquares / points.Length), maxResidual);
        }

        private static (double Rms, double Max) ComputeCircularResidualMetrics(Point[] points, Point center, double radius)
        {
            if (points.Length == 0)
            {
                return (0, 0);
            }

            double sumSquares = 0;
            double maxResidual = 0;
            foreach (Point point in points)
            {
                double distance = Math.Abs(GeometryUtils.Distance(point, center) - radius);
                sumSquares += distance * distance;
                maxResidual = Math.Max(maxResidual, distance);
            }

            return (Math.Sqrt(sumSquares / points.Length), maxResidual);
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

        private static bool TryFitCircle(Point[] points, out Point center, out double radius, double[]? weights = null)
        {
            center = default;
            radius = 0;
            if (points.Length < 3)
            {
                return false;
            }

            // RANSAC 预处理：压制强离群（遮挡/飞溅），输出一致性子集后再进入代数+几何拟合。
            const double ransacInlierThreshold = 1.5;
            (Point[] ransacPoints, double[]? ransacWeights) = SelectRansacCircleInliers(points, weights, ransacInlierThreshold);
            points = ransacPoints;
            weights = ransacWeights;
            if (points.Length < 3)
            {
                return false;
            }

            double sumX = 0;
            double sumY = 0;
            double sumXX = 0;
            double sumYY = 0;
            double sumXY = 0;
            double sumXr2 = 0;
            double sumYr2 = 0;
            double sumR2 = 0;

            foreach (Point point in points)
            {
                double x = point.X;
                double y = point.Y;
                double r2 = x * x + y * y;
                sumX += x;
                sumY += y;
                sumXX += x * x;
                sumYY += y * y;
                sumXY += x * y;
                sumXr2 += x * r2;
                sumYr2 += y * r2;
                sumR2 += r2;
            }

            double[,] matrix =
            {
                { sumXX, sumXY, sumX },
                { sumXY, sumYY, sumY },
                { sumX, sumY, points.Length }
            };
            double[] rhs =
            {
                -sumXr2,
                -sumYr2,
                -sumR2
            };

            if (!TrySolveLinearSystem3x3(matrix, rhs, out double[] solution))
            {
                return false;
            }

            double d = solution[0];
            double e = solution[1];
            double f = solution[2];
            center = new Point(-d / 2, -e / 2);
            double radiusSquared = center.X * center.X + center.Y * center.Y - f;
            if (radiusSquared <= 0)
            {
                return false;
            }

            radius = Math.Sqrt(radiusSquared);

            // 几何精化：以代数拟合为初值，用加权 Gauss-Newton 迭代最小化点到圆周的欧氏距离平方和，
            // 消除代数法（最小化代数距离）在小圆/带噪点时的系统性偏置；weights 提供边缘分数与稳健权重。
            RefineCircleGeometric(points, ref center, ref radius, weights);
            return true;
        }

        /// <summary>
        /// 用加权 Gauss-Newton 迭代精化圆拟合（几何距离），并以外层 Tukey 稳健迭代压制残余离群点。
        /// Chinese: 每轮先按当前残差的 MAD 尺度计算 Tukey 权，再做一次加权 GN 更新 (Cx, Cy, R)；共 2 轮。
        /// English: Refines a circle fit by weighted Gauss-Newton iterations with Tukey robust reweighting.
        /// </summary>
        internal static void RefineCircleGeometric(Point[] points, ref Point center, ref double radius, double[]? weights = null)
        {
            const double k = 4.685;
            double[]? workingWeights = weights;

            for (int iteration = 0; iteration < 2; iteration++)
            {
                double[] residuals = new double[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    residuals[i] = Math.Abs(GeometryUtils.Distance(points[i], center) - radius);
                }

                double median = MedianOf(residuals);
                if (median < 1e-9)
                {
                    // 残差已接近零（完美圆），执行最后一次不加稳健权重的 GN 然后退出。
                    ApplyWeightedCircleGaussNewtonStep(points, ref center, ref radius, weights);
                    return;
                }

                double scale = 1.4826 * median;
                double[] robust = new double[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    double u = residuals[i] / (k * scale);
                    if (Math.Abs(u) >= 1)
                    {
                        robust[i] = 0;
                        continue;
                    }

                    double baseWeight = weights?[i] ?? 1.0;
                    double tukey = (1 - u * u) * (1 - u * u);
                    robust[i] = baseWeight * tukey;
                }

                workingWeights = robust;

                if (!ApplyWeightedCircleGaussNewtonStep(points, ref center, ref radius, workingWeights))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// 单步加权 Gauss-Newton 圆拟合更新（JᵀWJ·δ = -JᵀWr）。
        /// Chinese: 奇异或退化时返回 false 保持原值。
        /// English: One weighted Gauss-Newton step for circle fitting; returns false when singular.
        /// </summary>
        private static bool ApplyWeightedCircleGaussNewtonStep(Point[] points, ref Point center, ref double radius, double[]? weights)
        {
            double cx = center.X;
            double cy = center.Y;
            double r = radius;

            double j00 = 0, j01 = 0, j02 = 0;
            double j11 = 0, j12 = 0, j22 = 0;
            double b0 = 0, b1 = 0, b2 = 0;
            bool degenerate = false;

            for (int i = 0; i < points.Length; i++)
            {
                double weight = weights?[i] ?? 1.0;
                if (weight <= 1e-9)
                {
                    continue;
                }

                double dx = points[i].X - cx;
                double dy = points[i].Y - cy;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-9)
                {
                    degenerate = true;
                    break;
                }

                double residual = d - r;
                double jx = -dx / d;
                double jy = -dy / d;

                j00 += weight * jx * jx;
                j01 += weight * jx * jy;
                j02 += weight * jx * -1;
                j11 += weight * jy * jy;
                j12 += weight * jy * -1;
                j22 += weight * 1;
                b0 += weight * jx * -residual;
                b1 += weight * jy * -residual;
                b2 += weight * -1 * -residual;
            }

            if (degenerate)
            {
                return false;
            }

            double[]? step = TrySolveLinearSystem3x3Symmetric(j00, j01, j02, j11, j12, j22, b0, b1, b2);
            if (step == null)
            {
                return false;
            }

            center = new Point(cx + step[0], cy + step[1]);
            radius = Math.Max(1e-6, r + step[2]);
            return true;
        }

        /// <summary>
        /// 求解对称 3x3 正规方程 (JᵀJ)δ = -Jᵀr，返回解或 null（奇异）。
        /// Chinese: 用 Cramer 法则求解；行列式接近 0 时返回 null 表示不可解。
        /// English: Solves a symmetric 3x3 linear system by Cramer's rule; returns null when singular.
        /// </summary>
        private static double[]? TrySolveLinearSystem3x3Symmetric(double a00, double a01, double a02, double a11, double a12, double a22, double b0, double b1, double b2)
        {
            double determinant =
                a00 * (a11 * a22 - a12 * a12) -
                a01 * (a01 * a22 - a12 * a02) +
                a02 * (a01 * a12 - a11 * a02);
            if (Math.Abs(determinant) < 1e-12)
            {
                return null;
            }

            double d00 = b0 * (a11 * a22 - a12 * a12) - a01 * (b1 * a22 - a12 * b2) + a02 * (b1 * a12 - a11 * b2);
            double d01 = a00 * (b1 * a22 - a12 * b2) - b0 * (a01 * a22 - a12 * a02) + a02 * (a01 * b2 - b1 * a02);
            double d02 = a00 * (a11 * b2 - b1 * a12) - a01 * (a01 * b2 - b1 * a02) + b0 * (a01 * a12 - a11 * a02);

            return [d00 / determinant, d01 / determinant, d02 / determinant];
        }

        private static bool TrySolveLinearSystem3x3(double[,] matrix, double[] rhs, out double[] solution)
        {
            solution = new double[3];
            double determinant = Determinant3x3(matrix);
            if (Math.Abs(determinant) < 1e-8)
            {
                return false;
            }

            for (int column = 0; column < 3; column++)
            {
                double[,] working = (double[,])matrix.Clone();
                for (int row = 0; row < 3; row++)
                {
                    working[row, column] = rhs[row];
                }

                solution[column] = Determinant3x3(working) / determinant;
            }

            return true;
        }

        private static double Determinant3x3(double[,] matrix)
        {
            return matrix[0, 0] * (matrix[1, 1] * matrix[2, 2] - matrix[1, 2] * matrix[2, 1])
                 - matrix[0, 1] * (matrix[1, 0] * matrix[2, 2] - matrix[1, 2] * matrix[2, 0])
                 + matrix[0, 2] * (matrix[1, 0] * matrix[2, 1] - matrix[1, 1] * matrix[2, 0]);
        }

        private static double DistanceToLine(Point point, Point linePoint, Vector lineDirection)
        {
            if (lineDirection.LengthSquared < 1e-6)
            {
                return GeometryUtils.Distance(point, linePoint);
            }

            Vector delta = point - linePoint;
            double cross = Math.Abs(delta.X * lineDirection.Y - delta.Y * lineDirection.X);
            return cross / lineDirection.Length;
        }

        private static Point ProjectPointOntoLine(Point point, Point linePoint, Vector lineDirection)
        {
            if (lineDirection.LengthSquared < 1e-6)
            {
                return linePoint;
            }

            double t = ((point.X - linePoint.X) * lineDirection.X + (point.Y - linePoint.Y) * lineDirection.Y) / lineDirection.LengthSquared;
            return linePoint + lineDirection * t;
        }

        private static LineSegmentOverlay FitLine(Point[] points, Vector preferredDirection, double fallbackHalfLength, double[]? weights = null)
        {
            Point centroid = GeometryUtils.GetCentroid(points);
            if (points.Length == 1)
            {
                Vector direction = preferredDirection;
                if (direction.LengthSquared < 1e-6)
                {
                    direction = new Vector(1, 0);
                }

                direction.Normalize();
                return new LineSegmentOverlay(centroid - direction * fallbackHalfLength, centroid + direction * fallbackHalfLength);
            }

            // RANSAC 预处理：压制强离群（遮挡/飞溅），输出一致性子集后再进入加权稳健拟合。
            const double ransacInlierThreshold = 1.5;
            (Point[] ransacPoints, double[]? ransacWeights) = SelectRansacLineInliers(points, weights, ransacInlierThreshold);
            points = ransacPoints;
            weights = ransacWeights;

            // 初始方向：加权协方差主轴（无权重时等价于等权）。
            Vector directionVector = ComputeWeightedPrincipalDirection(points, centroid, weights);
            if (directionVector.LengthSquared < 1e-6)
            {
                directionVector = preferredDirection;
            }

            if (directionVector.LengthSquared < 1e-6)
            {
                directionVector = new Vector(1, 0);
            }

            directionVector.Normalize();

            // Tukey 稳健迭代：用正交残差的 MAD 尺度重估权重，弱化残余离群点对各点方向的影响。
            double[]? workingWeights = weights;
            for (int iteration = 0; iteration < 2; iteration++)
            {
                double[] residuals = new double[points.Length];
                double median;
                for (int i = 0; i < points.Length; i++)
                {
                    residuals[i] = DistanceToLine(points[i], centroid, directionVector);
                }

                median = MedianOf(residuals);
                if (median < 1e-9)
                {
                    break;
                }

                double scale = 1.4826 * median;
                workingWeights = ComputeTukeyWeights(residuals, weights, scale);
                Point weightedCentroid = ComputeWeightedCentroid(points, workingWeights);
                Vector updated = ComputeWeightedPrincipalDirection(points, weightedCentroid, workingWeights);
                updated.Normalize();
                if (updated.LengthSquared < 1e-6)
                {
                    break;
                }

                directionVector = updated;
                centroid = weightedCentroid;
            }

            // 端点：取权重有效（内点）集合的投影 min/max，避免被残余离群点拉长。
            double minProjection = double.PositiveInfinity;
            double maxProjection = double.NegativeInfinity;
            double effectiveWeightSum = 0;
            for (int i = 0; i < points.Length; i++)
            {
                double weight = workingWeights?[i] ?? 1.0;
                if (weight <= 1e-6)
                {
                    continue;
                }

                effectiveWeightSum += weight;
                double projection = (points[i].X - centroid.X) * directionVector.X + (points[i].Y - centroid.Y) * directionVector.Y;
                minProjection = Math.Min(minProjection, projection);
                maxProjection = Math.Max(maxProjection, projection);
            }

            if (effectiveWeightSum < 1e-6)
            {
                minProjection = -fallbackHalfLength;
                maxProjection = fallbackHalfLength;
            }

            if (maxProjection - minProjection < 1)
            {
                minProjection = -fallbackHalfLength;
                maxProjection = fallbackHalfLength;
            }

            return new LineSegmentOverlay(centroid + directionVector * minProjection, centroid + directionVector * maxProjection);
        }

        /// <summary>
        /// 加权质心。
        /// Chinese: weights 为 null 时退化为等权质心。
        /// English: Weighted centroid; falls back to the arithmetic centroid when weights are null.
        /// </summary>
        private static Point ComputeWeightedCentroid(Point[] points, double[]? weights)
        {
            if (weights == null)
            {
                return GeometryUtils.GetCentroid(points);
            }

            double sumX = 0;
            double sumY = 0;
            double weightSum = 0;
            for (int i = 0; i < points.Length; i++)
            {
                double weight = Math.Max(0, weights[i]);
                sumX += points[i].X * weight;
                sumY += points[i].Y * weight;
                weightSum += weight;
            }

            return weightSum < 1e-9 ? GeometryUtils.GetCentroid(points) : new Point(sumX / weightSum, sumY / weightSum);
        }

        /// <summary>
        /// 加权协方差主轴方向（PCA）。
        /// Chinese: 对每个点按权重贡献协方差，返回主轴单位向量；权重为 null 时等权。
        /// English: Computes the principal axis of the weighted covariance matrix.
        /// </summary>
        private static Vector ComputeWeightedPrincipalDirection(Point[] points, Point centroid, double[]? weights)
        {
            double xx = 0;
            double xy = 0;
            double yy = 0;
            for (int i = 0; i < points.Length; i++)
            {
                double weight = weights?[i] ?? 1.0;
                if (weight <= 1e-6)
                {
                    continue;
                }

                double dx = points[i].X - centroid.X;
                double dy = points[i].Y - centroid.Y;
                xx += weight * dx * dx;
                xy += weight * dx * dy;
                yy += weight * dy * dy;
            }

            double angle = 0.5 * Math.Atan2(2 * xy, xx - yy);
            return new Vector(Math.Cos(angle), Math.Sin(angle));
        }

        /// <summary>
        /// 计算稳健权重（Tukey biweight）：u = 残差/(k·尺度)，|u|≥1 时权重为 0。
        /// Chinese: k=4.685，尺度来自残差 MAD×1.4826；最终权重为 Tukey 权与基权（边缘分数权）的乘积。
        /// English: Tukey biweight robust residuals combined with the base edge-score weights.
        /// </summary>
        private static double[] ComputeTukeyWeights(double[] residuals, double[]? baseWeights, double scale)
        {
            const double k = 4.685;
            var weights = new double[residuals.Length];
            for (int i = 0; i < residuals.Length; i++)
            {
                double u = residuals[i] / (k * scale);
                if (Math.Abs(u) >= 1)
                {
                    weights[i] = 0;
                    continue;
                }

                double baseWeight = baseWeights?[i] ?? 1.0;
                double tukey = (1 - u * u) * (1 - u * u);
                weights[i] = baseWeight * tukey;
            }

            return weights;
        }

        private static double MedianOf(double[] values)
        {
            double[] sorted = [.. values];
            Array.Sort(sorted);
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        }

        /// <summary>
        /// 直线 RANSAC 预处理：从含强离群的点集中挑选一致性子集（两点定线，距离阈值 inlier 判定）。
        /// Chinese: 点数 ≤40 时枚举全部两点组合，否则随机采样 80 次；无显著改进时返回原集合。
        /// English: RANSAC pre-filtering for line fitting that tolerates up to ~50% outliers.
        /// </summary>
        internal static (Point[] Points, double[]? Weights) SelectRansacLineInliers(Point[] points, double[]? weights, double threshold)
        {
            int n = points.Length;
            if (n < 4)
            {
                return (points, weights);
            }

            int iterations = n <= 40 ? n * (n - 1) / 2 : 80;
            bool[]? bestMask = null;
            int bestCount = 0;
            var random = new Random(12345);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                int i;
                int j;
                if (n <= 40)
                {
                    int flat = iteration;
                    i = 0;
                    while (flat >= n - 1 - i)
                    {
                        flat -= n - 1 - i;
                        i++;
                    }

                    j = i + 1 + flat;
                }
                else
                {
                    i = random.Next(n);
                    do
                    {
                        j = random.Next(n);
                    }
                    while (j == i);
                }

                Vector direction = points[j] - points[i];
                if (direction.LengthSquared < 1e-9)
                {
                    continue;
                }

                int count = 0;
                for (int k = 0; k < n; k++)
                {
                    if (DistanceToLine(points[k], points[i], direction) <= threshold)
                    {
                        count++;
                    }
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    var mask = new bool[n];
                    for (int k = 0; k < n; k++)
                    {
                        mask[k] = DistanceToLine(points[k], points[i], direction) <= threshold;
                    }

                    bestMask = mask;
                }
            }

            if (bestMask == null || bestCount < 3)
            {
                return (points, weights);
            }

            Point[] inliers = points.Where((_, index) => bestMask![index]).ToArray();
            double[]? inlierWeights = weights?.Where((_, index) => bestMask![index]).ToArray();
            return (inliers, inlierWeights);
        }

        /// <summary>
        /// 圆 RANSAC 预处理：三点定圆，距离阈值 inlier 判定。
        /// Chinese: 点数 ≤40 时枚举全部三点组合，否则随机采样 80 次。
        /// English: RANSAC pre-filtering for circle fitting that tolerates up to ~50% outliers.
        /// </summary>
        internal static (Point[] Points, double[]? Weights) SelectRansacCircleInliers(Point[] points, double[]? weights, double threshold)
        {
            int n = points.Length;
            if (n < 6)
            {
                return (points, weights);
            }

            int iterations = n <= 40 ? n * (n - 1) * (n - 2) / 6 : 80;
            bool[]? bestMask = null;
            int bestCount = 0;
            var random = new Random(24680);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                int i;
                int j;
                int k;
                if (n <= 40)
                {
                    int flat = iteration;
                    i = 0;
                    while (flat >= (n - 1 - i) * (n - 2 - i) / 2)
                    {
                        flat -= (n - 1 - i) * (n - 2 - i) / 2;
                        i++;
                    }

                    j = i + 1;
                    while (flat >= n - 1 - j)
                    {
                        flat -= n - 1 - j;
                        j++;
                    }

                    k = j + 1 + flat;
                }
                else
                {
                    i = random.Next(n);
                    do { j = random.Next(n); } while (j == i);
                    do { k = random.Next(n); } while (k == i || k == j);
                }

                if (!TryFitCircle([points[i], points[j], points[k]], out Point circleCenter, out double circleRadius))
                {
                    continue;
                }

                int count = 0;
                for (int m = 0; m < n; m++)
                {
                    if (Math.Abs(GeometryUtils.Distance(points[m], circleCenter) - circleRadius) <= threshold)
                    {
                        count++;
                    }
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    var mask = new bool[n];
                    for (int m = 0; m < n; m++)
                    {
                        mask[m] = Math.Abs(GeometryUtils.Distance(points[m], circleCenter) - circleRadius) <= threshold;
                    }

                    bestMask = mask;
                }
            }

            if (bestMask == null || bestCount < 6)
            {
                return (points, weights);
            }

            Point[] inliers = points.Where((_, index) => bestMask![index]).ToArray();
            double[]? inlierWeights = weights?.Where((_, index) => bestMask![index]).ToArray();
            return (inliers, inlierWeights);
        }

        private static bool TryIntersectLines(Point p1, Vector d1, Point p2, Vector d2, out Point intersection)
        {
            intersection = default;
            double determinant = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(determinant) < 1e-6)
            {
                return false;
            }

            Vector delta = p2 - p1;
            double t = (delta.X * d2.Y - delta.Y * d2.X) / determinant;
            intersection = p1 + d1 * t;
            return true;
        }

        private static double SampleAveragedIntensity(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector normal, int averagingHalfWidth)
        {
            double sum = 0;
            int count = 0;
            for (int offset = -averagingHalfWidth; offset <= averagingHalfWidth; offset++)
            {
                Point samplePoint = center + normal * offset;
                int x = (int)Math.Round(samplePoint.X);
                int y = (int)Math.Round(samplePoint.Y);
                if (x < 0 || x >= pixelWidth || y < 0 || y >= pixelHeight)
                {
                    continue;
                }

                int index = y * stride + x * bytesPerPixel;
                sum += GetPixelIntensity(pixels, index, bytesPerPixel, format);
                count++;
            }

            return count == 0 ? 0 : sum / count;
        }

        private static BitmapSource NormalizeBitmap(BitmapSource bitmap)
        {
            if (bitmap.Format == PixelFormats.Gray8 ||
                bitmap.Format == PixelFormats.Bgr24 ||
                bitmap.Format == PixelFormats.Bgr32 ||
                bitmap.Format == PixelFormats.Bgra32)
            {
                return bitmap;
            }

            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = bitmap;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();
            return converted;
        }
    }
}
