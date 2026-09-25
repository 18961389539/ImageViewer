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
    /// <summary>
    /// 几何拟合与鲁棒估计
    /// Chinese: 直线/圆最小二乘拟合、加权与 RANSAC 内点筛选、残差度量与小规模线性求解。
    /// English: Line/circle least squares, weighting, RANSAC inlier selection, residual metrics and small linear solvers.
    /// </summary>
    internal static partial class ImageAnalysisService
    {

        private static List<CaliperEdgeSample> FilterInlierSamples(List<CaliperEdgeSample> samples, Vector preferredDirection, double fallbackHalfLength, double configuredThreshold, int minimumRequired)
        {
            if (samples.Count <= minimumRequired)
            {
                return samples;
            }

            Point[] points = [..samples.Select(sample => sample.Point)];

            LineSegmentOverlay provisionalFit = FitLine(points, preferredDirection, fallbackHalfLength);
            Vector fitDirection = (provisionalFit.End - provisionalFit.Start).ToWpfVector();
            if (fitDirection.LengthSquared < 1e-6)
            {
                return samples;
            }

            List<(CaliperEdgeSample Sample, double Distance)> distances = new(samples.Count);
            foreach (CaliperEdgeSample sample in samples)
            {
                distances.Add((sample, DistanceToLine(sample.Point, provisionalFit.Start.ToWpfPoint(), fitDirection)));
            }

            double[] distanceValues = distances.Select(item => item.Distance).ToArray();
            double distanceMedian = MedianOf(distanceValues);
            double distanceScale = 1.4826 * MedianOf(distanceValues.Select(value => Math.Abs(value - distanceMedian)).ToArray());
            double threshold = configuredThreshold > 0
                ? configuredThreshold
                : Math.Clamp(Math.Max(0.75, distanceMedian + 3.0 * distanceScale), 0.75, 8.0);

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

            double[] distanceValues = distances.Select(item => item.Distance).ToArray();
            double distanceMedian = MedianOf(distanceValues);
            double distanceScale = 1.4826 * MedianOf(distanceValues.Select(value => Math.Abs(value - distanceMedian)).ToArray());
            double threshold = configuredThreshold > 0
                ? configuredThreshold
                : Math.Clamp(Math.Max(0.75, distanceMedian + 3.0 * distanceScale), 0.75, 8.0);

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

            Vector direction = (fittedLine.End - fittedLine.Start).ToWpfVector();
            double sumSquares = 0;
            double maxResidual = 0;
            foreach (Point point in points)
            {
                double distance = DistanceToLine(point, fittedLine.Start.ToWpfPoint(), direction);
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

        private static bool TryFitCircle(Point[] points, out Point center, out double radius, double[]? weights = null)
        {
            center = default;
            radius = 0;
            if (points.Length < 3)
            {
                return false;
            }

            // RANSAC 预处理：压制强离群（遮挡/飞溅），输出一致性子集后再进入代数+几何拟合。
            double ransacInlierThreshold = ComputeAdaptiveCircleThreshold(points);
            (Point[] ransacPoints, double[]? ransacWeights) = SelectRansacCircleInliers(points, weights, ransacInlierThreshold);
            points = ransacPoints;
            weights = ransacWeights;
            if (points.Length < 3)
            {
                return false;
            }

            Point origin = new(
                MedianOf(points.Select(point => point.X).ToArray()),
                MedianOf(points.Select(point => point.Y).ToArray()));
            double coordinateScale = points.Max(point => GeometryUtils.Distance(point, origin));
            coordinateScale = Math.Max(1.0, coordinateScale);
            double sumX = 0;
            double sumY = 0;
            double sumXX = 0;
            double sumYY = 0;
            double sumXY = 0;
            double sumXr2 = 0;
            double sumYr2 = 0;
            double sumR2 = 0;
            double weightSum = 0;

            for (int index = 0; index < points.Length; index++)
            {
                double weight = weights?[index] ?? 1.0;
                if (weight <= 1e-9)
                {
                    continue;
                }

                double x = (points[index].X - origin.X) / coordinateScale;
                double y = (points[index].Y - origin.Y) / coordinateScale;
                double r2 = x * x + y * y;
                weightSum += weight;
                sumX += weight * x;
                sumY += weight * y;
                sumXX += weight * x * x;
                sumYY += weight * y * y;
                sumXY += weight * x * y;
                sumXr2 += weight * x * r2;
                sumYr2 += weight * y * r2;
                sumR2 += weight * r2;
            }

            if (weightSum < 3)
            {
                return false;
            }

            double[,] matrix =
            {
                { sumXX, sumXY, sumX },
                { sumXY, sumYY, sumY },
                { sumX, sumY, weightSum }
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
            Point normalizedCenter = new(-d / 2, -e / 2);
            double radiusSquared = normalizedCenter.X * normalizedCenter.X + normalizedCenter.Y * normalizedCenter.Y - f;
            if (radiusSquared <= 0)
            {
                return false;
            }

            center = new Point(origin.X + normalizedCenter.X * coordinateScale, origin.Y + normalizedCenter.Y * coordinateScale);
            radius = Math.Sqrt(radiusSquared) * coordinateScale;

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

            for (int iteration = 0; iteration < 4; iteration++)
            {
                double[] residuals = new double[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    residuals[i] = Math.Abs(GeometryUtils.Distance(points[i], center) - radius);
                }

                double median = MedianOf(residuals);
                double scale = 1.4826 * MedianOf(residuals.Select(value => Math.Abs(value - median)).ToArray());
                if (scale < 1e-9)
                {
                    // 残差已接近零（完美圆），执行最后一次不加稳健权重的 GN 然后退出。
                    ApplyWeightedCircleGaussNewtonStep(points, ref center, ref radius, weights);
                    return;
                }

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
                return new LineSegmentOverlay((centroid - direction * fallbackHalfLength).ToPointD(), (centroid + direction * fallbackHalfLength).ToPointD());
            }

            // RANSAC 预处理：压制强离群（遮挡/飞溅），输出一致性子集后再进入加权稳健拟合。
            double ransacInlierThreshold = ComputeAdaptiveLineThreshold(points);
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

            return new LineSegmentOverlay((centroid + directionVector * minProjection).ToPointD(), (centroid + directionVector * maxProjection).ToPointD());
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
            if (values.Length == 0)
            {
                return 0;
            }

            double[] sorted = [.. values];
            Array.Sort(sorted);
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        }

        private static double ComputeAdaptiveLineThreshold(Point[] points)
        {
            if (points.Length < 4)
            {
                return 1.5;
            }

            Point center = new(
                MedianOf(points.Select(point => point.X).ToArray()),
                MedianOf(points.Select(point => point.Y).ToArray()));
            Vector direction = ComputeWeightedPrincipalDirection(points, center, weights: null);
            if (direction.LengthSquared < 1e-9)
            {
                return 1.5;
            }

            double[] residuals = points
                .Select(point => DistanceToLine(point, center, direction))
                .ToArray();
            double median = MedianOf(residuals);
            double scale = 1.4826 * MedianOf(residuals.Select(value => Math.Abs(value - median)).ToArray());
            if (scale < 1e-6)
            {
                scale = Math.Sqrt(residuals.Select(value => value * value).Average());
            }

            return Math.Clamp(Math.Max(0.75, scale * 3.0), 0.75, 6.0);
        }

        private static double ComputeAdaptiveCircleThreshold(Point[] points)
        {
            if (points.Length < 4)
            {
                return 1.5;
            }

            Point center = new(
                MedianOf(points.Select(point => point.X).ToArray()),
                MedianOf(points.Select(point => point.Y).ToArray()));
            double[] radii = points
                .Select(point => GeometryUtils.Distance(point, center))
                .ToArray();
            double medianRadius = MedianOf(radii);
            double[] residuals = radii
                .Select(value => Math.Abs(value - medianRadius))
                .ToArray();
            double medianResidual = MedianOf(residuals);
            double scale = 1.4826 * MedianOf(residuals.Select(value => Math.Abs(value - medianResidual)).ToArray());
            if (scale < 1e-6)
            {
                scale = Math.Sqrt(residuals.Select(value => value * value).Average());
            }

            return Math.Clamp(Math.Max(0.75, scale * 3.0), 0.75, 6.0);
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
    }
}
