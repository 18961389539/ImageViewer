using System;
using System.Collections.Generic;
using ImageViewer.Models;

namespace ImageViewer.Core.Analysis;

/// <summary>
/// Managed threshold segmentation and connected-component analysis.
/// Chinese: Otsu 取最大类间方差的最小灰度阈值；亮目标取 g&gt;T，暗目标取 g≤T。
/// 手动阈值上下界均含等号；Gray16 阈值按 ×257 缩放。连通域为 8 邻接，按首像素行优先排序。
/// English: Otsu picks the smallest gray maximizing between-class variance; light targets
/// select g&gt;T and dark targets g≤T. Manual thresholds include both bounds. Components are
/// 8-connected and ordered row-major by their first pixel.
/// </summary>
public static class BlobAlgorithm
{
    /// <summary>Produces the foreground mask for the requested threshold mode.</summary>
    public static bool[] Segment(IntensityImage image, bool useOtsu, int threshold, bool detectDark)
    {
        ArgumentNullException.ThrowIfNull(image);
        int width = image.Width;
        int height = image.Height;
        int maximum = image.MaxValue;
        var mask = new bool[checked(width * height)];

        double effectiveThreshold;
        bool lightExclusive;
        if (useOtsu)
        {
            effectiveThreshold = OtsuThreshold(image);
            lightExclusive = true;
        }
        else
        {
            effectiveThreshold = Math.Clamp(threshold, 0, byte.MaxValue);
            if (maximum == ushort.MaxValue)
            {
                effectiveThreshold *= 257.0;
            }

            lightExclusive = false;
        }

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                double value = image[x, y];
                bool foreground = detectDark
                    ? value <= effectiveThreshold
                    : lightExclusive ? value > effectiveThreshold : value >= effectiveThreshold;
                mask[row + x] = foreground;
            }
        }

        return mask;
    }

    /// <summary>
    /// Labels 8-connected components of the mask, keeps those with area ≥ <paramref name="minArea"/>,
    /// and returns them ordered row-major by first pixel.
    /// </summary>
    public static IReadOnlyList<BlobFeature> Connect(bool[] mask, int width, int height, int minArea)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var labels = new int[mask.Length];
        var regions = new List<BlobFeature>();
        int nextLabel = 0;
        int areaThreshold = Math.Max(1, minArea);
        var stack = new Stack<int>();

        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || labels[start] != 0)
            {
                continue;
            }

            nextLabel++;
            long area = 0;
            double sumRow = 0, sumColumn = 0;
            int row1 = int.MaxValue, column1 = int.MaxValue, row2 = int.MinValue, column2 = int.MinValue;
            stack.Push(start);
            labels[start] = nextLabel;
            while (stack.Count > 0)
            {
                int index = stack.Pop();
                int row = index / width;
                int column = index % width;
                area++;
                sumRow += row;
                sumColumn += column;
                row1 = Math.Min(row1, row);
                column1 = Math.Min(column1, column);
                row2 = Math.Max(row2, row);
                column2 = Math.Max(column2, column);

                for (int dy = -1; dy <= 1; dy++)
                {
                    int neighborRow = row + dy;
                    if (neighborRow < 0 || neighborRow >= height)
                    {
                        continue;
                    }

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        int neighborColumn = column + dx;
                        if (neighborColumn < 0 || neighborColumn >= width)
                        {
                            continue;
                        }

                        int neighbor = (neighborRow * width) + neighborColumn;
                        if (mask[neighbor] && labels[neighbor] == 0)
                        {
                            labels[neighbor] = nextLabel;
                            stack.Push(neighbor);
                        }
                    }
                }
            }

            if (area >= areaThreshold)
            {
                regions.Add(new BlobFeature(
                    nextLabel,
                    (int)area,
                    new PointD(sumColumn / area, sumRow / area),
                    new RectD(column1, row1, column2 - column1 + 1, row2 - row1 + 1)));
            }
        }

        return regions;
    }

    /// <summary>Segments the image and returns the connected components above the area floor.</summary>
    public static IReadOnlyList<BlobFeature> ExtractRegions(
        IntensityImage image,
        bool useOtsu,
        int threshold,
        bool detectDark,
        int minArea)
    {
        bool[] mask = Segment(image, useOtsu, threshold, detectDark);
        return Connect(mask, image.Width, image.Height, minArea);
    }

    private static double OtsuThreshold(IntensityImage image)
    {
        int maximum = image.MaxValue;
        // Pixel count is bounded by the intensity buffer length (an int), so an int histogram
        // cannot overflow and avoids the extra 512 KB a long histogram would allocate per call.
        var histogram = new int[maximum + 1];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                histogram[image[x, y]]++;
            }
        }

        long total = (long)image.Width * image.Height;
        double sum = 0;
        for (int i = 0; i <= maximum; i++)
        {
            sum += (double)i * histogram[i];
        }

        double sumB = 0;
        long weightB = 0;
        double bestVariance = -1;
        int bestThreshold = 0;
        for (int t = 0; t <= maximum; t++)
        {
            weightB += histogram[t];
            if (weightB == 0)
            {
                continue;
            }

            long weightF = total - weightB;
            if (weightF == 0)
            {
                break;
            }

            sumB += (double)t * histogram[t];
            double meanB = sumB / weightB;
            double meanF = (sum - sumB) / weightF;
            double variance = (double)weightB * weightF * (meanB - meanF) * (meanB - meanF);
            if (variance > bestVariance)
            {
                bestVariance = variance;
                bestThreshold = t;
            }
        }

        return bestThreshold;
    }
}
