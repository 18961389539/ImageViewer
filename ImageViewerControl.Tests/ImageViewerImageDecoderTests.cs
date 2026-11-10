using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Controls;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    public sealed class ImageViewerImageDecoderTests
    {
        private const uint Red = 0xFFFF0000;
        private const uint Green = 0xFF00FF00;
        private const uint Blue = 0xFF0000FF;
        private const uint White = 0xFFFFFFFF;
        private const uint Black = 0xFF000000;
        private const uint Yellow = 0xFF00FFFF;

        public static IEnumerable<object[]> OrientationCases()
        {
            yield return new object[] { 1, new[] { Red, Green, Blue, White, Black, Yellow }, 2, 3 };
            yield return new object[] { 2, new[] { Green, Red, White, Blue, Yellow, Black }, 2, 3 };
            yield return new object[] { 3, new[] { Yellow, Black, White, Blue, Green, Red }, 2, 3 };
            yield return new object[] { 4, new[] { Black, Yellow, Blue, White, Red, Green }, 2, 3 };
            yield return new object[] { 5, new[] { Red, Blue, Black, Green, White, Yellow }, 3, 2 };
            yield return new object[] { 6, new[] { Black, Blue, Red, Yellow, White, Green }, 3, 2 };
            yield return new object[] { 7, new[] { Yellow, White, Green, Black, Blue, Red }, 3, 2 };
            yield return new object[] { 8, new[] { Green, White, Yellow, Red, Blue, Black }, 3, 2 };
        }

        [Theory]
        [MemberData(nameof(OrientationCases))]
        public void Apply_MapsPixelsForEveryExifOrientation(int orientation, uint[] expectedPixels, int expectedWidth, int expectedHeight)
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource source = CreateSource();
                BitmapSource result = ExifOrientationTransform.Apply(source, orientation);

                Assert.Equal(expectedWidth, result.PixelWidth);
                Assert.Equal(expectedHeight, result.PixelHeight);
                Assert.Equal(expectedPixels, ReadPixels(result));
            });
        }

        [Fact]
        public void Apply_LeavesUnknownOrientationUnchanged()
        {
            WpfTestRunner.Run(() =>
            {
                BitmapSource source = CreateSource();
                BitmapSource result = ExifOrientationTransform.Apply(source, 0);

                Assert.Same(source, result);
            });
        }

        [Fact]
        public void EnsureWithinLimits_AllowsBoundary()
        {
            ImageViewerImageDecoder.EnsureWithinLimits(100, 100, 10_000);
        }

        [Fact]
        public void EnsureWithinLimits_ThrowsWhenBudgetExceeded()
        {
            ImageViewerImageTooLargeException exception = Assert.Throws<ImageViewerImageTooLargeException>(
                () => ImageViewerImageDecoder.EnsureWithinLimits(101, 100, 10_000));

            Assert.Equal(101, exception.PixelWidth);
            Assert.Equal(100, exception.PixelHeight);
            Assert.Equal(10_100, exception.PixelCount);
            Assert.Equal(10_000, exception.MaxPixelCount);
        }

        [Fact]
        public void LoadFromFile_AppliesExifOrientationAndRegistersSourcePath()
        {
            WpfTestRunner.Run(() =>
            {
                string filePath = Path.Combine(Path.GetTempPath(), $"imageviewer-exif-{Guid.NewGuid():N}.jpg");
                try
                {
                    WriteJpegWithOrientation(filePath, 6);

                    BitmapSource loaded = ImageViewerImageDecoder.LoadFromFile(filePath, CancellationToken.None);

                    Assert.Equal(3, loaded.PixelWidth);
                    Assert.Equal(2, loaded.PixelHeight);
                    Assert.True(ImageViewerImageSourceRegistry.TryGetPath(loaded, out string registeredPath));
                    Assert.Equal(filePath, registeredPath);
                }
                finally
                {
                    File.Delete(filePath);
                }
            });
        }

        private static BitmapSource CreateSource()
        {
            uint[] pixels = { Red, Green, Blue, White, Black, Yellow };
            BitmapSource source = BitmapSource.Create(2, 3, 96, 96, PixelFormats.Bgra32, null, pixels, 2 * 4);
            source.Freeze();
            return source;
        }

        private static uint[] ReadPixels(BitmapSource bitmap)
        {
            var pixels = new uint[bitmap.PixelWidth * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * sizeof(uint), 0);
            return pixels;
        }

        private static void WriteJpegWithOrientation(string filePath, ushort orientation)
        {
            var metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);

            var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
            encoder.Frames.Add(BitmapFrame.Create(CreateSource(), null, metadata, null));
            using FileStream stream = File.Create(filePath);
            encoder.Save(stream);
        }
    }
}
