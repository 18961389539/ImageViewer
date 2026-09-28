using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    internal static class BlobAnalysisService
    {
        /// <summary>
        /// 使用 JLVision 完成斑点分割、连通域分析和几何特征提取。
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

            using JLVisionImageHandle imageHandle = JLVisionAlgorithmEntryPoint.CreateIntensity(crop);
            cancellationToken.ThrowIfCancellationRequested();

            using var segmented = JLVisionAlgorithmEntryPoint.SegmentBlob(imageHandle, useOtsu, threshold, detectDark);
            cancellationToken.ThrowIfCancellationRequested();

            using var connected = JLVisionAlgorithmEntryPoint.ConnectRegion(segmented);
            cancellationToken.ThrowIfCancellationRequested();

            int areaThreshold = Math.Max(1, minArea);
            using var filtered = JLVisionAlgorithmEntryPoint.SelectBlobArea(connected, areaThreshold);

            int count = filtered.CountObj();
            for (int index = 1; index <= count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var blob = filtered.SelectObj(index);

                int area = JLVisionAlgorithmEntryPoint.GetAreaCenter(blob, out double row, out double column);
                if (area < areaThreshold)
                {
                    continue;
                }

                JLVisionAlgorithmEntryPoint.GetBoundingRectangle(
                    blob,
                    out int row1,
                    out int column1,
                    out int row2,
                    out int column2);

                results.Add(new BlobFeature(
                    index,
                    area,
                    new PointD(column + x, row + y),
                    new RectD(
                        column1 + x,
                        row1 + y,
                        column2 - column1 + 1,
                        row2 - row1 + 1)));
            }

            return results;
        }
    }
}
