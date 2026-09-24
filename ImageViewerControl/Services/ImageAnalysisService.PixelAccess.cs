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
    /// 像素与位图访问
    /// Chinese: 灰度读取、位图格式归一化、平均采样，以及直方图与剖面提取。
    /// English: Intensity reads, bitmap format normalization, averaged sampling, histogram and profile extraction.
    /// </summary>
    internal static partial class ImageAnalysisService
    {

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
