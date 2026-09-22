using System;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    public static class VolumeInteractionService
    {
        public static VolumeCropBounds NormalizeCrop(VolumeData volume, VolumeCropBounds? bounds)
        {
            ArgumentNullException.ThrowIfNull(volume);
            return (bounds ?? VolumeCropBounds.Full(volume)).Clamp(volume);
        }

        public static VolumeVoxelLocation ReadVoxel(VolumeData volume, int x, int y, int z)
        {
            ArgumentNullException.ThrowIfNull(volume);
            if ((uint)x >= (uint)volume.Width || (uint)y >= (uint)volume.Height || (uint)z >= (uint)volume.Depth)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "Voxel coordinates must be inside the volume.");
            }

            BitmapSource slice = volume.GetAxialSlice(z);
            byte[] pixel = new byte[Math.Max(1, (slice.Format.BitsPerPixel + 7) / 8)];
            slice.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, pixel.Length, 0);
            byte intensity = pixel.Length >= 3
                ? (byte)(pixel[0] * 0.114 + pixel[1] * 0.587 + pixel[2] * 0.299)
                : pixel[0];
            return new VolumeVoxelLocation(
                x,
                y,
                z,
                x * volume.SpacingX,
                y * volume.SpacingY,
                z * volume.SpacingZ,
                intensity);
        }

        public static bool TryReadVoxelAtWorldPoint(VolumeData volume, Point3D point, out VolumeVoxelLocation? voxel)
        {
            ArgumentNullException.ThrowIfNull(volume);
            double width = volume.Width * volume.SpacingX;
            double height = volume.Height * volume.SpacingY;
            double depth = volume.Depth * volume.SpacingZ;
            int x = (int)Math.Floor((point.X + width / 2) / volume.SpacingX);
            int y = (int)Math.Floor((point.Y + height / 2) / volume.SpacingY);
            int z = (int)Math.Floor((point.Z + depth / 2) / volume.SpacingZ);
            if ((uint)x >= (uint)volume.Width || (uint)y >= (uint)volume.Height || (uint)z >= (uint)volume.Depth)
            {
                voxel = null;
                return false;
            }

            voxel = ReadVoxel(volume, x, y, z);
            return true;
        }
    }
}
