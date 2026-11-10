using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageViewer.Core.Analysis;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    internal static class BlobAnalysisService
    {
        /// <summary>
        /// 使用托管算法完成斑点分割、连通域分析和几何特征提取。
        /// </summary>
        public static List<BlobFeature> DetectBlobs(
            BitmapSource bitmap,
            Rect searchRoi,
            bool useOtsu,
            int threshold,
            bool detectDark = false,
            int minArea = 10,
            CancellationToken cancellationToken = default)
        {
            var results = new List<BlobFeature>();
            if (bitmap == null || searchRoi.Width <= 0 || searchRoi.Height <= 0)
            {
                return results;
            }

            cancellationToken.ThrowIfCancellationRequested();

            int x = Math.Max(0, (int)searchRoi.X);
            int y = Math.Max(0, (int)searchRoi.Y);
            int width = Math.Min(bitmap.PixelWidth - x, (int)searchRoi.Width);
            int height = Math.Min(bitmap.PixelHeight - y, (int)searchRoi.Height);
            if (width <= 0 || height <= 0)
            {
                return results;
            }

            BitmapSource normalized = ImageViewerPixelAccess.NormalizeForIntensity(bitmap);
            var crop = new CroppedBitmap(normalized, new Int32Rect(x, y, width, height));
            if (crop.CanFreeze)
            {
                crop.Freeze();
            }

            IntensityImage image = VisionAlgorithmEntryPoint.CreateIntensity(crop);
            cancellationToken.ThrowIfCancellationRequested();

            int areaThreshold = Math.Max(1, minArea);
            IReadOnlyList<BlobFeature> regions = VisionAlgorithmEntryPoint.ExtractBlobs(
                image, useOtsu, threshold, detectDark, areaThreshold);

            for (int i = 0; i < regions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BlobFeature region = regions[i];
                results.Add(region with
                {
                    // Rendering shows "#<Label>", so keep the dense 1-based numbering the
                    // control has always produced rather than the raw component label.
                    Label = i + 1,
                    Centroid = new PointD(region.Centroid.X + x, region.Centroid.Y + y),
                    BoundingBox = new RectD(
                        region.BoundingBox.X + x,
                        region.BoundingBox.Y + y,
                        region.BoundingBox.Width,
                        region.BoundingBox.Height)
                });
            }

            return results;
        }
    }
}
