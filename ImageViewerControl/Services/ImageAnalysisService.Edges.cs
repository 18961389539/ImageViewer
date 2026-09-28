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
    /// 边缘采样与亚像素定位
    /// Chinese: 梯度得分剖面、峰值选择、抛物线插值与灰度矩亚像素定位。
    /// English: Gradient score profile, peak selection, parabolic interpolation and gray-moment sub-pixel refinement.
    /// </summary>
    internal static partial class ImageAnalysisService
    {

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
        /// Chinese: sampleStep 为剖面采样步长（像素），粗定位阶段用它减少采样点数；1 表示逐像素。
        /// English: Locates an edge along one search profile with subpixel refinement. <c>sampleStep</c> is
        /// the profile stepping in pixels: a coarse scan passes a value above 1 to cut sample count.
        /// </summary>
        private static bool TryFindStrongestCircularGradient(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector measurementDirection, Vector averagingDirection, int searchRange, int averagingHalfWidth, double edgeSigma, double minimumGradient, CaliperEdgePolarity polarity, int edgeSelection, out CaliperEdgeSample edgeSample, HalconEdgeExtractionMode extractionMode = HalconEdgeExtractionMode.GaussianDerivative, int sampleStep = 1)
        {
            edgeSample = default;
            int sampleCount = searchRange * 2 + 1;
            double[] profile = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int axisOffset = i - searchRange;
                Point sampleCenter = center + measurementDirection * (axisOffset * sampleStep);
                profile[i] = SampleAveragedIntensity(pixels, pixelWidth, pixelHeight, stride, bytesPerPixel, format, sampleCenter, averagingDirection, averagingHalfWidth);
            }

            double[] score = BuildGradientScoreProfile(profile, polarity, edgeSigma, extractionMode, minimumGradient);

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
            Point detectedPosition = center + measurementDirection * ((subpixelPosition - searchRange) * sampleStep);
            edgeSample = new CaliperEdgeSample(detectedPosition, strongestGradient);
            return true;
        }

        /// <summary>
        /// 沿射线选择离起点最近的可靠边缘。
        /// Chinese: 自动圆粗定位优先使用点击点向外遇到的第一条边缘，避免被更远但更强的外轮廓抢走。
        /// English: Selects the nearest reliable edge along a ray so automatic-circle coarse localization
        /// is not hijacked by a farther, stronger outer contour.
        /// </summary>
        private static bool TryFindNearestCircularGradient(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector measurementDirection, Vector averagingDirection, int searchRange, int averagingHalfWidth, double edgeSigma, double minimumGradient, CaliperEdgePolarity polarity, out CaliperEdgeSample edgeSample, HalconEdgeExtractionMode extractionMode = HalconEdgeExtractionMode.GaussianDerivative, int sampleStep = 1)
        {
            edgeSample = default;
            int sampleCount = searchRange * 2 + 1;
            double[] profile = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int axisOffset = i - searchRange;
                Point sampleCenter = center + measurementDirection * (axisOffset * sampleStep);
                profile[i] = SampleAveragedIntensity(pixels, pixelWidth, pixelHeight, stride, bytesPerPixel, format, sampleCenter, averagingDirection, averagingHalfWidth);
            }

            double[] score = BuildGradientScoreProfile(profile, polarity, edgeSigma, extractionMode, minimumGradient);
            for (int index = 1; index < score.Length - 1; index++)
            {
                if (score[index] < minimumGradient || score[index] < score[index - 1] || score[index] < score[index + 1])
                {
                    continue;
                }

                double subpixelPosition = SelectSubpixelPosition(index, profile, score);
                Point detectedPosition = center + measurementDirection * ((subpixelPosition - searchRange) * sampleStep);
                edgeSample = new CaliperEdgeSample(detectedPosition, score[index]);
                return true;
            }

            return false;
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
        private static double[] BuildGradientScoreProfile(double[] profile, CaliperEdgePolarity polarity, double edgeSigma = 1.0, HalconEdgeExtractionMode extractionMode = HalconEdgeExtractionMode.GaussianDerivative, double minimumGradient = 0)
        {
            double[] response = BuildEdgeResponse(profile, edgeSigma, extractionMode);
            var score = new double[profile.Length];
            for (int i = 0; i < response.Length; i++)
            {
                score[i] = polarity switch
                {
                    CaliperEdgePolarity.DarkToLight => Math.Max(response[i], 0),
                    CaliperEdgePolarity.LightToDark => Math.Max(-response[i], 0),
                    _ => Math.Abs(response[i])
                };
            }

            if (extractionMode == HalconEdgeExtractionMode.Canny)
            {
                ApplyCannyNonMaximumSuppression(score);
                ApplyCannyHysteresis(score, Math.Max(0, minimumGradient));
            }

            return score;
        }

        private static double[] BuildEdgeResponse(double[] profile, double edgeSigma, HalconEdgeExtractionMode extractionMode)
        {
            double sigma = Math.Clamp(edgeSigma, 0.5, 5.0);
            return extractionMode switch
            {
                HalconEdgeExtractionMode.Deriche => BuildRecursiveDerivativeResponse(profile, sigma, symmetric: false),
                HalconEdgeExtractionMode.Mshen => BuildRecursiveDerivativeResponse(profile, sigma, symmetric: true),
                HalconEdgeExtractionMode.Lanser1 or HalconEdgeExtractionMode.SobelFast =>
                    ApplyDerivativeKernel(profile, [-1, 1]),
                HalconEdgeExtractionMode.Lanser2 or HalconEdgeExtractionMode.Sobel =>
                    ApplyDerivativeKernel(profile, [-1, -2, 0, 2, 1]),
                HalconEdgeExtractionMode.Canny or HalconEdgeExtractionMode.GaussianDerivative =>
                    BuildGaussianDerivativeResponse(profile, sigma),
                _ => BuildGaussianDerivativeResponse(profile, sigma)
            };
        }

        private static double[] BuildGaussianDerivativeResponse(double[] profile, double sigma)
        {
            int radius = Math.Max(1, (int)Math.Ceiling(3 * sigma));
            radius = Math.Min(radius, Math.Max(1, (profile.Length - 1) / 2));
            double[] kernel = new double[radius * 2 + 1];
            for (int offset = -radius; offset <= radius; offset++)
            {
                kernel[offset + radius] = offset * Math.Exp(-(offset * offset) / (2 * sigma * sigma));
            }

            return ApplyDerivativeKernel(profile, kernel);
        }

        private static double[] ApplyDerivativeKernel(double[] profile, double[] kernel)
        {
            var response = new double[profile.Length];
            int radius = kernel.Length / 2;
            double normalization = kernel.Sum(Math.Abs);
            if (normalization < 1e-9)
            {
                return response;
            }

            for (int i = radius; i < profile.Length - radius; i++)
            {
                double value = 0;
                for (int k = 0; k < kernel.Length; k++)
                {
                    value += profile[i + k - radius] * kernel[k];
                }

                response[i] = value * 2 / normalization;
            }

            return response;
        }

        private static double[] BuildRecursiveDerivativeResponse(double[] profile, double sigma, bool symmetric)
        {
            var response = new double[profile.Length];
            if (profile.Length < 3)
            {
                return response;
            }

            double alpha = Math.Clamp(1.0 / (sigma + (symmetric ? 0.5 : 0.1)), 0.08, 0.95);
            var forward = new double[profile.Length];
            var backward = new double[profile.Length];
            forward[0] = profile[0];
            for (int i = 1; i < profile.Length; i++)
            {
                forward[i] = alpha * profile[i] + (1 - alpha) * forward[i - 1];
            }

            backward[^1] = profile[^1];
            for (int i = profile.Length - 2; i >= 0; i--)
            {
                backward[i] = alpha * profile[i] + (1 - alpha) * backward[i + 1];
            }

            double[] smooth = new double[profile.Length];
            for (int i = 0; i < profile.Length; i++)
            {
                smooth[i] = symmetric
                    ? 0.5 * (forward[i] + backward[i])
                    : forward[i];
            }

            for (int i = 1; i < profile.Length - 1; i++)
            {
                response[i] = smooth[i + 1] - smooth[i - 1];
            }

            return response;
        }

        private static void ApplyCannyNonMaximumSuppression(double[] score)
        {
            double[] original = [.. score];
            for (int i = 1; i < score.Length - 1; i++)
            {
                score[i] = original[i] >= original[i - 1] && original[i] >= original[i + 1]
                    ? original[i]
                    : 0;
            }

            if (score.Length > 0)
            {
                score[0] = 0;
                score[^1] = 0;
            }
        }

        private static void ApplyCannyHysteresis(double[] score, double highThreshold)
        {
            if (highThreshold <= 0)
            {
                return;
            }

            double lowThreshold = highThreshold * 0.5;
            bool[] strong = new bool[score.Length];
            for (int i = 0; i < score.Length; i++)
            {
                strong[i] = score[i] >= highThreshold;
            }

            for (int i = 0; i < score.Length; i++)
            {
                if (!strong[i])
                {
                    continue;
                }

                for (int j = i - 1; j >= 0 && score[j] >= lowThreshold; j--)
                {
                    score[j] = Math.Max(score[j], highThreshold);
                }

                for (int j = i + 1; j < score.Length && score[j] >= lowThreshold; j++)
                {
                    score[j] = Math.Max(score[j], highThreshold);
                }
            }

            for (int i = 0; i < score.Length; i++)
            {
                if (score[i] < highThreshold)
                {
                    score[i] = 0;
                }
            }
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

        /// <summary>
        /// 由卡尺边缘样本的梯度得分派生拟合权重（0.15–1.0）。
        /// Chinese: 弱边缘点保留最小权重而非完全丢弃，避免低对比区域被忽略。
        /// English: Derives fit weights from edge gradient scores, clamped to [0.15, 1.0].
        /// </summary>
        private static double[] BuildScoreWeights(IReadOnlyList<CaliperEdgeSample> samples, ImageAnalysisQualityProfile quality)
        {
            var weights = new double[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                weights[i] = 0.15 + 0.85 * Math.Clamp(samples[i].Score / quality.MaxCaliperScore, 0, 1);
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

            // 低侧（暗侧）占比 q 的矩闭合解。闭式解必须使用无量纲偏度 s = μ3 / σ³：
            //   两水平模型下恒有 s = (2q - 1) / √(q(1-q))，与灰度幅值无关，故位置不随对比度漂移；
            //   q = 0.5 * (1 + s / √(4 + s²))。
            double s = thirdCentralMoment / (standardDeviation * variance);
            double lowFraction = Math.Clamp(0.5 * (1 + s / Math.Sqrt(4 + s * s)), 0, 1);

            // 暗侧在窗口左端时，边缘距左端为 q；暗侧在右端时取镜像。
            // 样点代表像素覆盖区域（质心在样点处），窗口支撑区间为 [lo - 0.5, hi + 0.5]、宽度 count，
            // 与抛物线分支共用同一套"样点索引"坐标（窗口居中时两者都对齐 bestIndex）。
            bool lowSideAtLeft = profile[lo] <= profile[hi];
            double edgeFromLeft = lowSideAtLeft ? lowFraction : 1 - lowFraction;
            return lo - 0.5 + edgeFromLeft * count;
        }

        private static bool TryFindStrongestGradientPair(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector measurementDirection, Vector averagingDirection, int searchRange, int averagingHalfWidth, double edgeSigma, double minimumGradient, CaliperEdgePolarity polarity, double minimumEdgeGapPx, double nominalEdgeGapPx, double nominalEdgeGapTolerancePx, out CaliperEdgeSample edge1Sample, out CaliperEdgeSample edge2Sample, HalconEdgeExtractionMode extractionMode = HalconEdgeExtractionMode.GaussianDerivative)
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

            double[] score = BuildGradientScoreProfile(profile, polarity, edgeSigma, extractionMode, minimumGradient);
            int middleIndex = searchRange;
            List<int> leadingCandidates = FindGradientPeaks(score, 1, middleIndex - 1, minimumGradient);
            List<int> trailingCandidates = FindGradientPeaks(score, middleIndex + 1, score.Length - 2, minimumGradient);
            if (leadingCandidates.Count == 0 || trailingCandidates.Count == 0)
            {
                return false;
            }

            // 全局边缘对仲裁：始终在两侧局部峰中选择一对，而不是分别取两侧的单个最强梯度。
            // 这样即使存在更强的纹理伪边，也会用两侧得分平衡和标称宽度先验共同决定目标边缘。
            int bestIndex1 = -1;
            int bestIndex2 = -1;
            double bestPairScore = double.NegativeInfinity;
            foreach (int candidate1 in leadingCandidates)
            {
                foreach (int candidate2 in trailingCandidates)
                {
                    double gap = candidate2 - candidate1;
                    if (gap < Math.Max(0, minimumEdgeGapPx))
                    {
                        continue;
                    }

                    double weakerEdgeScore = Math.Min(score[candidate1], score[candidate2]);
                    double pairScore = 0.7 * (score[candidate1] + score[candidate2]) + 0.3 * weakerEdgeScore;
                    if (nominalEdgeGapPx > 0)
                    {
                        double deviation = Math.Abs(gap - nominalEdgeGapPx) - Math.Max(0, nominalEdgeGapTolerancePx);
                        if (deviation > 0)
                        {
                            pairScore -= deviation * 1.25;
                        }
                    }

                    if (pairScore > bestPairScore)
                    {
                        bestPairScore = pairScore;
                        bestIndex1 = candidate1;
                        bestIndex2 = candidate2;
                    }
                }
            }

            if (bestIndex1 < 0 || bestIndex2 < 0)
            {
                return false;
            }

            double strongestGradient1 = score[bestIndex1];
            double strongestGradient2 = score[bestIndex2];
            double subpixelPosition1 = SelectSubpixelPosition(bestIndex1, profile, score);
            double subpixelPosition2 = SelectSubpixelPosition(bestIndex2, profile, score);
            edge1Sample = new CaliperEdgeSample(center + measurementDirection * (subpixelPosition1 - searchRange), strongestGradient1);
            edge2Sample = new CaliperEdgeSample(center + measurementDirection * (subpixelPosition2 - searchRange), strongestGradient2);
            return true;
        }

        private static List<int> FindGradientPeaks(double[] score, int startIndex, int endIndex, double minimumGradient)
        {
            var peaks = new List<int>();
            int start = Math.Max(1, startIndex);
            int end = Math.Min(score.Length - 2, endIndex);
            for (int i = start; i <= end; i++)
            {
                if (score[i] >= minimumGradient && score[i] > score[i - 1] && score[i] >= score[i + 1])
                {
                    peaks.Add(i);
                }
            }

            if (peaks.Count > 0)
            {
                return peaks;
            }

            int strongestIndex = -1;
            double strongestScore = minimumGradient;
            for (int i = start; i <= end; i++)
            {
                if (score[i] > strongestScore)
                {
                    strongestScore = score[i];
                    strongestIndex = i;
                }
            }

            if (strongestIndex >= 0)
            {
                peaks.Add(strongestIndex);
            }

            return peaks;
        }
    }
}
