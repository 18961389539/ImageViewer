using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ImageViewer.Controls;

namespace ImageViewer.Services
{
    /// <summary>
    /// Chinese: 统一的文件图像解码入口：先读头部校验像素预算，再解码并应用 EXIF 方向。
    /// English: Single file-decoding entry point: validate the pixel budget from the header, decode, then apply EXIF orientation.
    /// </summary>
    public static class ImageViewerImageDecoder
    {
        internal const long DefaultMaxPixelCount = 100_000_000;

        /// <summary>
        /// Decodes a file off the UI thread and preserves its pixel format. The returned bitmap is frozen.
        /// Progress is reported on a 0–100 scale. Disable EXIF orientation for volume slices whose
        /// spatial metadata describes the stored pixel grid.
        /// </summary>
        public static Task<BitmapSource> LoadFromFileAsync(
            string filePath,
            IProgress<double>? progress = null,
            bool applyExifOrientation = true,
            CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                BitmapSource bitmap = LoadFromFile(filePath, cancellationToken, progress, applyExifOrientation: applyExifOrientation);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(100);
                return bitmap;
            }, cancellationToken);

        internal static BitmapSource LoadFromFile(
            string filePath,
            CancellationToken cancellationToken,
            IProgress<double>? progress = null,
            long maxPixelCount = DefaultMaxPixelCount,
            bool applyExifOrientation = true)
            => LoadFromFileCore(filePath, progress, maxPixelCount, applyExifOrientation, false, null, null, cancellationToken);

        internal static BitmapSource LoadVerifiedVolumeSlice(
            string filePath, string sha256, ImageHeader verifiedHeader, CancellationToken cancellationToken)
            => LoadFromFileCore(filePath, null, DefaultMaxPixelCount,
                applyExifOrientation: false, trackIdentity: true, expectedSha256: sha256, verifiedHeader, cancellationToken);

        internal static Task<BitmapSource> LoadDocumentImageAsync(
            string filePath, IProgress<double>? progress, string? expectedSha256, CancellationToken cancellationToken)
            => Task.Run(() =>
            {
                BitmapSource bitmap = LoadFromFileCore(filePath, progress, DefaultMaxPixelCount,
                    applyExifOrientation: true, trackIdentity: true, expectedSha256, null, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(100);
                return bitmap;
            }, cancellationToken);

        private static BitmapSource LoadFromFileCore(
            string filePath, IProgress<double>? progress,
            long maxPixelCount, bool applyExifOrientation, bool trackIdentity, string? expectedSha256,
            ImageHeader? verifiedHeader, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            cancellationToken.ThrowIfCancellationRequested();

            FileStream? cacheLease = ImageViewerProjectPackageCache.TryLeaseAsset(filePath, cancellationToken);
            try
            {
                using FileStream stream = File.OpenRead(filePath);
                string? sha256 = null;
                if (trackIdentity)
                {
                    sha256 = ReadSha256(stream, cancellationToken);
                    ImageViewerSessionImageIdentity.Verify(expectedSha256, sha256, filePath);
                    stream.Position = 0;
                }
                // Preflight read this header from the file whose bytes we have just verified.
                ImageHeader header = verifiedHeader ?? ReadHeader(stream, cancellationToken);
                EnsureWithinLimits(header.PixelWidth, header.PixelHeight, maxPixelCount);

                stream.Position = 0;
                BitmapSource decoded = DecodePixels(stream, cancellationToken, progress);
                BitmapSource oriented = applyExifOrientation ? ExifOrientationTransform.Apply(decoded, header.Orientation) : decoded;
                // A verified volume slice uses the stored grid. With an EXIF transform, a session
                // must save those pixels as an asset because opening the file would orient it.
                if (applyExifOrientation || !trackIdentity || header.Orientation == ExifOrientationTransform.NormalOrientation)
                {
                    ImageViewerImageSourceRegistry.Register(oriented, filePath, cacheLease, sha256);
                    cacheLease = null;
                }
                return oriented;
            }
            finally
            {
                cacheLease?.Dispose();
            }
        }

        internal static (ImageHeader Header, string? Sha256) InspectFile(
            string filePath, bool readIdentity, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream stream = File.OpenRead(filePath);
            ImageHeader header = ReadHeader(stream, cancellationToken);
            EnsureWithinLimits(header.PixelWidth, header.PixelHeight, DefaultMaxPixelCount);
            stream.Position = 0;
            return (header, readIdentity ? ReadSha256(stream, cancellationToken) : null);
        }

        private static string ReadSha256(Stream stream, CancellationToken cancellationToken)
        {
            using var hashStream = new DecodeProgressStream(stream, cancellationToken, null);
            return Convert.ToHexString(SHA256.HashData(hashStream));
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

        private static ImageHeader ReadHeader(Stream stream, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                frame.Format.BitsPerPixel,
                ExifOrientationTransform.ReadOrientation(frame.Metadata));
        }

        private static BitmapSource DecodePixels(
            Stream stream,
            CancellationToken cancellationToken,
            IProgress<double>? progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var progressStream = new DecodeProgressStream(stream, cancellationToken, progress);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = progressStream;
            bitmap.EndInit();
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(80);
            bitmap.Freeze();
            return bitmap;
        }

        internal readonly record struct ImageHeader(int PixelWidth, int PixelHeight, int BitsPerPixel, int Orientation);

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
