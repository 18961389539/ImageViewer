using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    public enum VolumeSliceOrientation
    {
        Axial,
        Coronal,
        Sagittal
    }

    public sealed class VolumeSliceService
    {
        public static BitmapSource GetSlice(VolumeData volume, VolumeSliceOrientation orientation, int sliceIndex)
        {
            ArgumentNullException.ThrowIfNull(volume);
            return orientation switch
            {
                VolumeSliceOrientation.Axial => volume.GetAxialSlice(sliceIndex),
                VolumeSliceOrientation.Coronal => BuildCoronalSlice(volume, sliceIndex),
                VolumeSliceOrientation.Sagittal => BuildSagittalSlice(volume, sliceIndex),
                _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unsupported slice orientation.")
            };
        }

        private static BitmapSource BuildCoronalSlice(VolumeData volume, int sliceIndex)
        {
            if ((uint)sliceIndex >= (uint)volume.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(sliceIndex));
            }

            return BuildResampledSlice(volume, volume.Width, volume.Depth, (x, y) =>
            {
                BitmapSource slice = volume.GetAxialSlice(y);
                return slice.Format == PixelFormats.Gray8
                    ? ReadGray8(slice, x, sliceIndex)
                    : ReadIntensity(slice, x, sliceIndex);
            });
        }

        private static BitmapSource BuildSagittalSlice(VolumeData volume, int sliceIndex)
        {
            if ((uint)sliceIndex >= (uint)volume.Width)
            {
                throw new ArgumentOutOfRangeException(nameof(sliceIndex));
            }

            return BuildResampledSlice(volume, volume.Depth, volume.Height, (x, y) => ReadIntensity(volume.GetAxialSlice(x), sliceIndex, y));
        }

        private static BitmapSource BuildResampledSlice(VolumeData volume, int width, int height, Func<int, int, byte> readPixel)
        {
            byte[] pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = readPixel(x, y);
                }
            }

            BitmapSource result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
            result.Freeze();
            return result;
        }

        private static byte ReadGray8(BitmapSource bitmap, int x, int y)
        {
            byte[] pixel = new byte[1];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 1, 0);
            return pixel[0];
        }

        private static byte ReadIntensity(BitmapSource bitmap, int x, int y)
        {
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            byte[] pixel = new byte[bytesPerPixel];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, bytesPerPixel, 0);
            return bytesPerPixel >= 3
                ? (byte)(pixel[0] * 0.114 + pixel[1] * 0.587 + pixel[2] * 0.299)
                : pixel[0];
        }
    }
}
