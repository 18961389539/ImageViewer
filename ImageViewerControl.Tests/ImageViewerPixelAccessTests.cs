using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class ImageViewerPixelAccessTests
{
    [Fact]
    public void Gray16IntensityRead_PreservesFullSample()
    {
        byte[] pixels = { 0x34, 0x12, 0xCD, 0xAB };

        ushort first = ImageViewerPixelAccess.ReadIntensity(pixels, 0, 2, PixelFormats.Gray16);
        ushort second = ImageViewerPixelAccess.ReadIntensity(pixels, 2, 2, PixelFormats.Gray16);

        Assert.Equal((ushort)0x1234, first);
        Assert.Equal((ushort)0xABCD, second);
        Assert.Equal((byte)0x12, ImageViewerPixelAccess.ReadIntensity8(pixels, 0, 2, PixelFormats.Gray16));
    }

    [Fact]
    public void Gray16Profile_PreservesFullSamples()
    {
        BitmapSource bitmap = CreateGray16Bitmap(3, 1, [0x0123, 0x8001, 0xFEDC]);

        ushort[] profile = ImageAnalysisService.CreateProfile16(bitmap, new Point(0, 0), new Point(2, 0));

        Assert.Equal(new ushort[] { 0x0123, 0x8001, 0xFEDC }, profile);
    }

    [Fact]
    public void Gray16VolumeSlice_PreservesGray16FormatAndValues()
    {
        BitmapSource first = CreateGray16Bitmap(2, 2, [0x0102, 0x1234, 0xABCD, 0xFEDC]);
        BitmapSource second = CreateGray16Bitmap(2, 2, [0x1111, 0x2222, 0x3333, 0x4444]);
        var volume = new VolumeData([first, second]);

        BitmapSource slice = VolumeSliceService.GetSlice(volume, VolumeSliceOrientation.Coronal, 0);

        Assert.Equal(PixelFormats.Gray16, slice.Format);
        byte[] output = new byte[slice.PixelHeight * slice.PixelWidth * 2];
        slice.CopyPixels(output, slice.PixelWidth * 2, 0);
        ushort[] values = Enumerable.Range(0, 4)
            .Select(index => (ushort)(output[index * 2] | (output[index * 2 + 1] << 8)))
            .ToArray();
        Assert.Equal(new ushort[] { 0x0102, 0x1234, 0x1111, 0x2222 }, values);
    }

    [Fact]
    public void Gray16VolumeSlice_SagittalOrientationPreservesSamplesAndUsesCache()
    {
        BitmapSource first = CreateGray16Bitmap(2, 2, [0x0102, 0x1234, 0xABCD, 0xFEDC]);
        BitmapSource second = CreateGray16Bitmap(2, 2, [0x1111, 0x2222, 0x3333, 0x4444]);
        var volume = new VolumeData([first, second]);

        BitmapSource firstRead = VolumeSliceService.GetSlice(volume, VolumeSliceOrientation.Sagittal, 1);
        BitmapSource secondRead = VolumeSliceService.GetSlice(volume, VolumeSliceOrientation.Sagittal, 1);

        Assert.Same(firstRead, secondRead);
        Assert.Equal(PixelFormats.Gray16, firstRead.Format);
        Assert.Equal(new ushort[] { 0x1234, 0x2222, 0xFEDC, 0x4444 }, ReadGray16(firstRead));
    }

    [Fact]
    public void MixedBitDepthVolume_PromotesEightBitSamplesToGray16Range()
    {
        BitmapSource gray16 = CreateGray16Bitmap(1, 1, [0x1234]);
        BitmapSource gray8 = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Gray8, null, new byte[] { 128 }, 1);
        var volume = new VolumeData([gray16, gray8]);

        BitmapSource coronal = VolumeSliceService.GetSlice(volume, VolumeSliceOrientation.Coronal, 0);

        Assert.Equal(PixelFormats.Gray16, coronal.Format);
        Assert.Equal(new ushort[] { 0x1234, 0x8080 }, ReadGray16(coronal));
    }

    [Fact]
    public void ColorVolumeMpr_UsesStableLuminanceForEveryOrientation()
    {
        BitmapSource first = CreateBgr24Bitmap(2, 1, [(byte)0, 0, 100, (byte)0, 100, 0]);
        BitmapSource second = CreateBgr24Bitmap(2, 1, [(byte)0, 0, 200, (byte)0, 200, 0]);
        var volume = new VolumeData([first, second]);

        BitmapSource coronal = VolumeSliceService.GetSlice(volume, VolumeSliceOrientation.Coronal, 0);
        BitmapSource sagittal = VolumeSliceService.GetSlice(volume, VolumeSliceOrientation.Sagittal, 1);

        Assert.Equal(PixelFormats.Gray8, coronal.Format);
        Assert.Equal(PixelFormats.Gray8, sagittal.Format);
        Assert.Equal(new byte[] { 30, 59, 60, 117 }, ReadGray8(coronal));
        Assert.Equal(new byte[] { 59, 117 }, ReadGray8(sagittal));
    }

    [Fact]
    public void Gray16Statistics_PreservesRangeWithoutIntOverflow()
    {
        BitmapSource bitmap = CreateGray16Bitmap(2, 2, [0, 65535, 32768, 16384]);
        var roi = new RotatedRect
        {
            Center = new PointD(1, 1),
            Width = 2,
            Height = 2
        };

        Assert.True(ImageAnalysisService.TryCalculateStatistics(bitmap, roi, out RoiStatistics statistics));
        Assert.Equal((ushort)0, statistics.Min);
        Assert.Equal(ushort.MaxValue, statistics.Max);
        Assert.Equal((0d + 65535 + 32768 + 16384) / 4, statistics.Mean, precision: 6);
        Assert.True(statistics.StandardDeviation > 20_000);
    }

    [Fact]
    public void PixelSize_RejectsNonFiniteAndNonPositiveValues()
    {
        WpfTestRunner.Run(() =>
        {
            using var viewer = new global::ImageViewer.Controls.ImageViewer();

            AssertInvalidPixelSize(() => viewer.PixelSize = 0);
            AssertInvalidPixelSize(() => viewer.PixelSize = -1);
            AssertInvalidPixelSize(() => viewer.PixelSize = double.NaN);
            AssertInvalidPixelSize(() => viewer.PixelSize = double.PositiveInfinity);

            viewer.PixelSize = 0.25;
            Assert.Equal(0.25, viewer.PixelSize);
        });
    }

    private static void AssertInvalidPixelSize(Action assignment)
    {
        Assert.Throws<ArgumentException>(assignment);
    }

    private static BitmapSource CreateGray16Bitmap(int width, int height, ushort[] values)
    {
        byte[] bytes = new byte[values.Length * 2];
        for (int index = 0; index < values.Length; index++)
        {
            bytes[index * 2] = (byte)values[index];
            bytes[index * 2 + 1] = (byte)(values[index] >> 8);
        }

        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray16, null, bytes, width * 2);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateBgr24Bitmap(int width, int height, byte[] values)
    {
        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, values, width * 3);
        bitmap.Freeze();
        return bitmap;
    }

    private static ushort[] ReadGray16(BitmapSource bitmap)
    {
        byte[] bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 2];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 2, 0);
        return Enumerable.Range(0, bitmap.PixelWidth * bitmap.PixelHeight)
            .Select(index => (ushort)(bytes[index * 2] | (bytes[index * 2 + 1] << 8)))
            .ToArray();
    }

    private static byte[] ReadGray8(BitmapSource bitmap)
    {
        byte[] bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth, 0);
        return bytes;
    }
}
