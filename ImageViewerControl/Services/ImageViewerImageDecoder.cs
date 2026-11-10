using System;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using ImageViewer.Controls;

namespace ImageViewer.Services
{
    /// <summary>
    /// Chinese: 统一的文件图像解码入口：先读头部校验像素预算，再解码并应用 EXIF 方向。
    /// English: Single file-decoding entry point: validate the pixel budget from the header, decode, then apply EXIF orientation.
    /// </summary>
    internal static class ImageViewerImageDecoder
    {
        internal const long DefaultMaxPixelCount = 100_000_000;

        internal static BitmapSource LoadFromFile(
            string filePath,
            CancellationToken cancellationToken,
            IProgress<double>? progress = null,
            long maxPixelCount = DefaultMaxPixelCount)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            cancellationToken.ThrowIfCancellationRequested();

            ImageHeader header = ReadHeader(filePath, cancellationToken);
            EnsureWithinLimits(header.PixelWidth, header.PixelHeight, maxPixelCount);

            BitmapSource decoded = DecodePixels(filePath, cancellationToken, progress);
            BitmapSource oriented = ExifOrientationTransform.Apply(decoded, header.Orientation);
            ImageViewerImageSourceRegistry.Register(oriented, filePath);
            return oriented;
        }

        internal static void EnsureWithinLimits(int pixelWidth, int pixelHeight, long maxPixelCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPixelCount);

            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                throw new InvalidDataException("Image dimensions must be positive.");
            }

            long pixelCount = (long)pixelWidth * pixelHeight;
            if (pixelCount > maxPixelCount)
            {
                throw new ImageViewerImageTooLargeException(pixelWidth, pixelHeight, maxPixelCount);
            }
        }

        private static ImageHeader ReadHeader(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream stream = File.OpenRead(filePath);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);

            if (decoder.Frames.Count == 0)
            {
                throw new InvalidDataException("Image contains no frames.");
            }

            BitmapFrame frame = decoder.Frames[0];
            return new ImageHeader(
                frame.PixelWidth,
                frame.PixelHeight,
                ExifOrientationTransform.ReadOrientation(frame.Metadata));
        }

        private static BitmapSource DecodePixels(
            string filePath,
            CancellationToken cancellationToken,
            IProgress<double>? progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var fileStream = File.OpenRead(filePath);
            using var progressStream = new DecodeProgressStream(fileStream, cancellationToken, progress);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = progressStream;
            bitmap.EndInit();
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(80);
            bitmap.Freeze();
            return bitmap;
        }

        private readonly record struct ImageHeader(int PixelWidth, int PixelHeight, int Orientation);

        private sealed class DecodeProgressStream : Stream
        {
            private readonly Stream _inner;
            private readonly CancellationToken _cancellationToken;
            private readonly IProgress<double>? _progress;

            public DecodeProgressStream(Stream inner, CancellationToken cancellationToken, IProgress<double>? progress)
            {
                _inner = inner;
                _cancellationToken = cancellationToken;
                _progress = progress;
            }

            public override bool CanRead => _inner.CanRead;

            public override bool CanSeek => _inner.CanSeek;

            public override bool CanWrite => false;

            public override long Length => _inner.Length;

            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public override void Flush() => _inner.Flush();

            public override int Read(byte[] buffer, int offset, int count)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                int read = _inner.Read(buffer, offset, count);
                ReportProgress();
                _cancellationToken.ThrowIfCancellationRequested();
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }

            private void ReportProgress()
            {
                if (_progress is null || _inner.Length <= 0)
                {
                    return;
                }

                double fraction = Math.Clamp((double)_inner.Position / _inner.Length, 0, 1);
                _progress.Report(10 + fraction * 70);
            }
        }
    }
}
