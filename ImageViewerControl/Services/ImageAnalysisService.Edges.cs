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
        /// </summary>
        private static bool TryFindStrongestCircularGradient(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector measurementDirection, Vector averagingDirection, int searchRange, int averagingHalfWidth, double edgeSigma, double minimumGradient, CaliperEdgePolarity polarity, int edgeSelection, out CaliperEdgeSample edgeSample)
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

            double[] score = BuildGradientScoreProfile(profile, polarity, edgeSigma);

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
        private static double[] BuildGradientScoreProfile(double[] profile, CaliperEdgePolarity polarity, double edgeSigma = 1.0)
        {
            var score = new double[profile.Length];
            double sigma = Math.Clamp(edgeSigma, 0.5, 5.0);
            int radius = Math.Max(1, (int)Math.Ceiling(3 * sigma));
            radius = Math.Min(radius, Math.Max(1, (profile.Length - 1) / 2));
            double[] derivativeKernel = new double[radius * 2 + 1];
            double normalization = 0;
            for (int offset = -radius; offset <= radius; offset++)
            {
                double value = offset * Math.Exp(-(offset * offset) / (2 * sigma * sigma));
                derivativeKernel[offset + radius] = value;
                normalization += Math.Abs(value);
            }

            if (normalization < 1e-9)
            {
                normalization = 1;
            }

            for (int i = radius; i < profile.Length - radius; i++)
            {
                double gradient = 0;
                for (int offset = -radius; offset <= radius; offset++)
                {
                    gradient += profile[i + offset] * derivativeKernel[offset + radius];
                }

                gradient = gradient * 2 / normalization;
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

        private static bool TryFindStrongestGradientPair(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector measurementDirection, Vector averagingDirection, int searchRange, int averagingHalfWidth, double edgeSigma, double minimumGradient, CaliperEdgePolarity polarity, double minimumEdgeGapPx, double nominalEdgeGapPx, double nominalEdgeGapTolerancePx, out CaliperEdgeSample edge1Sample, out CaliperEdgeSample edge2Sample)
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

            double[] score = BuildGradientScoreProfile(profile, polarity, edgeSigma);
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
    }
}
