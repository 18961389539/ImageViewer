using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageViewer.Services
{
    internal static class ImageViewerPixelAccess
    {
        public static BitmapSource NormalizeForIntensity(BitmapSource bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            if (bitmap.Format == PixelFormats.Gray8 ||
                bitmap.Format == PixelFormats.Gray16 ||
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

        public static int GetBytesPerPixel(BitmapSource bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            return Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
        }

        /// <summary>
        /// Reads a pixel intensity without reducing a 16-bit grayscale sample to 8 bits.
        /// Color formats still return an 8-bit luminance value because their source channels
        /// are 8-bit. Callers that need a display-byte representation should use
        /// <see cref="ReadIntensity8"/> explicitly.
        /// </summary>
        public static ushort ReadIntensity(byte[] pixels, int index, int bytesPerPixel, PixelFormat format)
        {
            ArgumentNullException.ThrowIfNull(pixels);

            if (format == PixelFormats.Gray8)
            {
                return pixels[index];
            }

            if (format == PixelFormats.Gray16)
            {
                return (ushort)(pixels[index] | (pixels[index + 1] << 8));
            }

            if (bytesPerPixel >= 3)
            {
                byte b = pixels[index];
                byte g = pixels[index + 1];
                byte r = pixels[index + 2];
                return (ushort)Math.Clamp((int)Math.Round(0.299 * r + 0.587 * g + 0.114 * b), 0, byte.MaxValue);
            }

            return pixels[index];
        }

        /// <summary>
        /// Reads an intensity mapped to the legacy 8-bit display range. This conversion is
        /// intentionally explicit so measurement and segmentation code can retain Gray16 data.
        /// </summary>
        public static byte ReadIntensity8(byte[] pixels, int index, int bytesPerPixel, PixelFormat format)
        {
            ushort value = ReadIntensity(pixels, index, bytesPerPixel, format);
            return format == PixelFormats.Gray16
                ? (byte)(value >> 8)
                : (byte)Math.Min(value, byte.MaxValue);
        }

        public static int GetIntensityMaximum(PixelFormat format)
        {
            return format == PixelFormats.Gray16 ? ushort.MaxValue : byte.MaxValue;
        }
    }
}
