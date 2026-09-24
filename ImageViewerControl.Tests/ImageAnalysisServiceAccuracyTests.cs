using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    /// <summary>
    /// 测量算法精度测试：亚像素插值、圆拟合几何精化、低质量判失败。
    /// Chinese: 验证 ImageAnalysisService 的三项精度改进行为。
    /// English: Accuracy tests for the measurement pipeline (subpixel interpolation, geometric circle refinement, low-confidence rejection).
    /// </summary>
    public class ImageAnalysisServiceAccuracyTests
    {
        [Fact]
        public void RefinePeakOffset_SymmetricPeak_StaysAtCenter()
        {
            double[] score = [0, 1, 2, 1, 0];

            double refined = ImageAnalysisService.RefinePeakOffset(2, score);

            Assert.Equal(2.0, refined, precision: 6);
        }

        [Fact]
        public void RefinePeakOffset_AsymmetricPeak_ShiftsTowardSteeperSide()
        {
            // 抛物线峰值偏右：三点采样 (10, 60, 30) 的顶点位于 +0.125 处 → 峰值索引 6.125。
            double[] score = [0, 0, 0, 0, 0, 10, 60, 30, 0, 0];

            double refined = ImageAnalysisService.RefinePeakOffset(6, score);

            Assert.Equal(6.125, refined, precision: 4);
            Assert.True(refined > 6.0, "峰值应偏向较陡的一侧（索引增大）。");
        }

        [Fact]
        public void RefinePeakOffset_FlatPeak_ReturnsBestIndex()
        {
            double[] score = [0, 5, 5, 5, 0];

            double refined = ImageAnalysisService.RefinePeakOffset(2, score);

            Assert.Equal(2.0, refined, precision: 6);
        }

        [Fact]
        public void RefinePeakOffset_EdgeIndex_ReturnsSameIndex()
        {
            double[] score = [9, 3, 1, 0, 0];

            // bestIndex=0 位于端部，无法插值，应原样返回。
            double refined = ImageAnalysisService.RefinePeakOffset(0, score);

            Assert.Equal(0.0, refined, precision: 6);
        }

        [Fact]
        public void RefineCircleGeometric_ConvergesTowardTrueCircle()
        {
            const double trueCenterX = 10.0;
            const double trueCenterY = 12.0;
            const double trueRadius = 20.0;
            Point[] points = CreateCirclePoints(new Point(trueCenterX, trueCenterY), trueRadius, count: 36);

            Point center = new(trueCenterX + 0.6, trueCenterY - 0.4);
            double radius = trueRadius + 0.5;

            ImageAnalysisService.RefineCircleGeometric(points, ref center, ref radius);

            Assert.True(Math.Abs(center.X - trueCenterX) < 0.15, $"Center-X error too large: {Math.Abs(center.X - trueCenterX)}");
            Assert.True(Math.Abs(center.Y - trueCenterY) < 0.15, $"Center-Y error too large: {Math.Abs(center.Y - trueCenterY)}");
            Assert.True(Math.Abs(radius - trueRadius) < 0.15, $"Radius error too large: {Math.Abs(radius - trueRadius)}");
        }

        [Fact]
        public void SelectRansacLineInliers_WithHalfOutliers_KeepsConsistentSubset()
        {
            // 10 个共线内点（y=0）混入 10 个大偏移离群点，RANSAC 应保留内点一致性子集。
            var points = new List<Point>();
            for (int i = 0; i < 10; i++)
            {
                points.Add(new Point(i * 2.0, 0));
            }

            var random = new Random(7);
            for (int i = 0; i < 10; i++)
            {
                points.Add(new Point(random.Next(0, 60), 15 + random.Next(0, 20)));
            }

            Point[] all = [.. points];
            (Point[] inliers, _) = ImageAnalysisService.SelectRansacLineInliers(all, null, threshold: 1.0);

            Assert.True(inliers.Length >= 9, $"Expected >=9 inliers, got {inliers.Length}.");
            Assert.All(inliers, point => Assert.True(Math.Abs(point.Y) < 1.0, $"Non-inlier point kept: {point}"));
        }

        [Fact]
        public void SelectRansacCircleInliers_WithHalfOutliers_KeepsConsistentSubset()
        {
            var points = new List<Point>(CreateCirclePoints(new Point(20, 20), 10, count: 18));
            var random = new Random(11);
            for (int i = 0; i < 18; i++)
            {
                points.Add(new Point(random.Next(0, 80), random.Next(0, 80)));
            }

            (Point[] inliers, _) = ImageAnalysisService.SelectRansacCircleInliers([.. points], null, threshold: 1.0);

            Assert.True(inliers.Length >= 15, $"Expected >=15 inliers, got {inliers.Length}.");
        }

        [Fact]
        public void RefineGrayMomentOffset_SymmetricStep_ReturnsCenter()
        {
            // 窗口内容沿中心对称 → 第三中心矩为零，退化返回峰索引（窗口中心）。
            double[] profile = [0, 0, 100, 100];

            double offset = ImageAnalysisService.RefineGrayMomentOffset(2, profile);

            Assert.Equal(2.0, offset, precision: 4);
        }

        [Fact]
        public void RefineGrayMomentOffset_OffCenterStep_LocatesSubpixelEdge()
        {
            // 长剖面中的阶跃（低 4 / 高 4，真实边缘位于 3.5），窗口取峰索引 3 的邻域。
            double[] profile = [0, 0, 0, 0, 100, 100, 100, 100];

            double offset = ImageAnalysisService.RefineGrayMomentOffset(3, profile);

            Assert.Equal(3.5, offset, precision: 3);
        }

        [Fact]
        public void RefineGrayMomentOffset_FallingStep_MirrorsRisingStep()
        {
            // 与上升阶跃镜像的下降阶跃，同一几何边缘（3.5），暗侧位于窗口右端。
            double[] profile = [100, 100, 100, 100, 0, 0, 0, 0];

            double offset = ImageAnalysisService.RefineGrayMomentOffset(3, profile);

            Assert.Equal(3.5, offset, precision: 3);
        }

        [Theory]
        [InlineData(0.16)]
        [InlineData(1.0)]
        [InlineData(2.55)]
        public void RefineGrayMomentOffset_SameGeometry_IndependentOfContrast(double contrastScale)
        {
            // 同一几何边缘（真实边缘位于 3.5）在不同灰度幅值下必须给出同一亚像素位置：
            // 闭式解需使用无量纲偏度；用 σ/√|μ3| 逐项作比例时量纲为灰度^-0.5，结果会随对比度漂移。
            double high = 100 * contrastScale;
            double[] profile = [0, 0, 0, 0, high, high, high, high];

            double offset = ImageAnalysisService.RefineGrayMomentOffset(3, profile);

            Assert.Equal(3.5, offset, precision: 3);
        }

        [Fact]
        public void RefineGrayMomentOffset_LinearRamp_ReturnsMidpoint()
        {
            double[] profile = [0, 25, 50, 75, 100];

            double offset = ImageAnalysisService.RefineGrayMomentOffset(2, profile);

            Assert.Equal(2.0, offset, precision: 4);
        }

        private static Point[] CreateCirclePoints(Point center, double radius, int count)
        {
            var points = new Point[count];
            for (int i = 0; i < count; i++)
            {
                double angle = i * Math.PI * 2 / count;
                points[i] = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
            }

            return points;
        }
    }

    /// <summary>
    /// 测量管线集成测试：在合成图像上验证检测精度与低质量判失败。
    /// Chinese: 使用确定性合成位图验证圆/拟合直线检测的亚像素精度与均匀图像拒绝。
    /// English: Integration tests over synthetic bitmaps for the caliper detection pipeline.
    /// </summary>
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Accuracy")]
    public class ImageAnalysisServiceDetectionAccuracyTests
    {
        [Fact]
        public void TryDetectCircularCaliperEdges_SyntheticDisk_RadiusWithinSubpixelTolerance()
        {
            const int size = 40;
            const double centerX = 20.0;
            const double centerY = 20.0;
            const double trueRadius = 8.4;
            Color[] pixels = BuildDiskPixels(size, new Point(centerX, centerY), trueRadius);
            BitmapSource bitmap = CreateBgraBitmap(size, pixels);

            var caliper = new CircularCaliperMeasureRoi
            {
                Center = new PointD(centerX, centerY),
                Radius = 8.0,
                CaliperCount = 24,
                CaliperSearchRange = 5
            };

            bool success = ImageAnalysisService.TryDetectCircularCaliperEdges(bitmap, caliper, out CircularCaliperDetectionResult result);

            Assert.True(success, "合成圆盘应能成功检出。");
            Assert.InRange(result.DetectedRadius, trueRadius - 0.4, trueRadius + 0.4);
        }

        [Fact]
        public void TryDetectLineMeasureEdges_SyntheticVerticalBand_WidthWithinSubpixelTolerance()
        {
            const int size = 40;
            // 垂直亮带：x ∈ [13, 17)，其中右边界像素 16 为中间灰度，模拟半像素偏移边缘。
            Color[] pixels = BuildBandPixels(size, leftStart: 13, bandWidthPixels: 4, boundaryGreyPixel: 16, greyLevel: 127);
            BitmapSource bitmap = CreateBgraBitmap(size, pixels);

            // 测量线水平横跨亮带：搜索沿 x 方向穿过左右两个边缘。
            var line = new CaliperMeasureRoi
            {
                P1 = new PointD(5, 20),
                P2 = new PointD(25, 20),
                CaliperCount = 12,
                CaliperSamplingHalfWidth = 1,
                CaliperMinimumGradient = 10,
                MinimumValidCalipers = 4
            };
            line.EnsureCaliperRegion();

            bool success = ImageAnalysisService.TryDetectLineMeasureEdges(bitmap, line, out LineMeasureGradientDetectionResult result);
            if (!success)
            {
                throw new InvalidOperationException(
                    $"Line detection failed: SearchRange={line.CaliperSearchRange}, RegionLength={line.GetResolvedCaliperRegionLength()}, " +
                    $"MeasurementDirection={line.GetCaliperMeasurementDirection()}, CaliperCenter={line.CaliperCenter}");
            }
            // 理想宽度 = 3.5（左边缘 13，右边缘 16.5 的半像素偏移处），亚像素定位应接近该值，
            // 整像素结果（3 或 4）不满足该区间。
            double detectedWidth = Math.Abs(result.DetectedP1.X - result.DetectedP2.X);
            Assert.InRange(detectedWidth, 3.15, 3.85);
        }

        [Fact]
        public void TryDetectCircularCaliperEdges_UniformImage_ReturnsFalse()
        {
            const int size = 20;
            Color[] pixels = Enumerable.Repeat(new Color { R = 128, G = 128, B = 128, A = 255 }, size * size).ToArray();
            BitmapSource bitmap = CreateBgraBitmap(size, pixels);

            var caliper = new CircularCaliperMeasureRoi
            {
                Center = new PointD(10, 10),
                Radius = 4.0,
                CaliperCount = 12,
                CaliperSearchRange = 3
            };

            bool success = ImageAnalysisService.TryDetectCircularCaliperEdges(bitmap, caliper, out _);

            Assert.False(success, "均匀图像没有任何边缘，检测应失败。");
        }

        private static Color[] BuildDiskPixels(int size, Point center, double radius)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double dx = x + 0.5 - center.X;
                    double dy = y + 0.5 - center.Y;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    pixels[y * size + x] = distance <= radius
                        ? new Color { R = 255, G = 255, B = 255, A = 255 }
                        : new Color { R = 0, G = 0, B = 0, A = 255 };
                }
            }

            return pixels;
        }

        private static Color[] BuildBandPixels(int size, int leftStart, int bandWidthPixels, int boundaryGreyPixel, byte greyLevel)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte level = 0;
                    if (x >= leftStart && x < leftStart + bandWidthPixels)
                    {
                        level = x == boundaryGreyPixel ? greyLevel : (byte)255;
                    }

                    pixels[y * size + x] = new Color { R = level, G = level, B = level, A = 255 };
                }
            }

            return pixels;
        }

        private static BitmapSource CreateBgraBitmap(int size, Color[] pixels)
        {
            byte[] bgra = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color color = pixels[i];
                bgra[i * 4 + 0] = color.B;
                bgra[i * 4 + 1] = color.G;
                bgra[i * 4 + 2] = color.R;
                bgra[i * 4 + 3] = color.A;
            }

            return BitmapSource.Create(
                size,
                size,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                bgra,
                size * 4);
        }
    }
}