using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
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
        private static readonly ConditionalWeakTable<VolumeData, CachedIntensityVolume> IntensityCaches = new();

        public static BitmapSource GetSlice(VolumeData volume, VolumeSliceOrientation orientation, int sliceIndex, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(volume);
            return orientation switch
            {
                VolumeSliceOrientation.Axial => volume.GetAxialSlice(sliceIndex),
                VolumeSliceOrientation.Coronal => BuildCoronalSlice(volume, sliceIndex, cancellationToken),
                VolumeSliceOrientation.Sagittal => BuildSagittalSlice(volume, sliceIndex, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unsupported slice orientation.")
            };
        }

        private static BitmapSource BuildCoronalSlice(VolumeData volume, int sliceIndex, CancellationToken cancellationToken)
        {
            if ((uint)sliceIndex >= (uint)volume.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(sliceIndex));
            }

            CachedIntensityVolume cache = IntensityCaches.GetValue(volume, static source => new CachedIntensityVolume(source));
            if (cache.TryGetSlice(VolumeSliceOrientation.Coronal, sliceIndex, out BitmapSource? cachedSlice))
            {
                return cachedSlice!;
            }

            cache.GetAllPlanes(cancellationToken);
            BitmapSource result = BuildResampledSlice(volume.Width, volume.Depth, (x, y) => cache.Read(y, x, sliceIndex), cache.IsGray16, cancellationToken);
            cache.StoreSlice(VolumeSliceOrientation.Coronal, sliceIndex, result);
            return result;
        }

        private static BitmapSource BuildSagittalSlice(VolumeData volume, int sliceIndex, CancellationToken cancellationToken)
        {
            if ((uint)sliceIndex >= (uint)volume.Width)
            {
                throw new ArgumentOutOfRangeException(nameof(sliceIndex));
            }

            CachedIntensityVolume cache = IntensityCaches.GetValue(volume, static source => new CachedIntensityVolume(source));
            if (cache.TryGetSlice(VolumeSliceOrientation.Sagittal, sliceIndex, out BitmapSource? cachedSlice))
            {
                return cachedSlice!;
            }

            cache.GetAllPlanes(cancellationToken);
            BitmapSource result = BuildResampledSlice(volume.Depth, volume.Height, (x, y) => cache.Read(x, sliceIndex, y), cache.IsGray16, cancellationToken);
            cache.StoreSlice(VolumeSliceOrientation.Sagittal, sliceIndex, result);
            return result;
        }

        private static BitmapSource BuildResampledSlice(int width, int height, Func<int, int, ushort> readPixel, bool gray16, CancellationToken cancellationToken)
        {
            if (!gray16)
            {
                byte[] pixels = new byte[width * height];
                for (int y = 0; y < height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = 0; x < width; x++)
                    {
                        pixels[y * width + x] = (byte)Math.Min(readPixel(x, y), byte.MaxValue);
                    }
                }

                BitmapSource gray8Result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
                gray8Result.Freeze();
                return gray8Result;
            }

            byte[] gray16Pixels = new byte[width * height * 2];
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    ushort value = readPixel(x, y);
                    int index = (y * width + x) * 2;
                    gray16Pixels[index] = (byte)value;
                    gray16Pixels[index + 1] = (byte)(value >> 8);
                }
            }

            BitmapSource result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray16, null, gray16Pixels, width * 2);
            result.Freeze();
            return result;
        }

        private sealed class CachedIntensityVolume
        {
            private readonly BitmapSource[] _slices;
            private readonly IntensityPlane[] _planes;
            private readonly object[] _planeLocks;
            private readonly object _sliceCacheLock = new();
            private readonly Dictionary<(VolumeSliceOrientation Orientation, int Index), BitmapSource> _sliceCache = new();
            private readonly LinkedList<(VolumeSliceOrientation Orientation, int Index)> _sliceCacheOrder = new();
            private long _sliceCacheBytes;
            private const long MaxSliceCacheBytes = 64L * 1024 * 1024;

            public CachedIntensityVolume(VolumeData volume)
            {
                _slices = volume.Slices.Select(ImageViewerPixelAccess.NormalizeForIntensity).ToArray();
                _planes = new IntensityPlane[_slices.Length];
                _planeLocks = Enumerable.Range(0, _slices.Length).Select(_ => new object()).ToArray();
                IsGray16 = _slices.Any(slice => slice.Format == PixelFormats.Gray16);
            }

            public bool IsGray16 { get; }

            public bool TryGetSlice(VolumeSliceOrientation orientation, int index, out BitmapSource? slice)
            {
                lock (_sliceCacheLock)
                {
                    if (!_sliceCache.TryGetValue((orientation, index), out slice))
                    {
                        return false;
                    }

                    LinkedListNode<(VolumeSliceOrientation Orientation, int Index)>? node = _sliceCacheOrder.Find((orientation, index));
                    if (node is not null)
                    {
                        _sliceCacheOrder.Remove(node);
                        _sliceCacheOrder.AddLast(node);
                    }

                    return true;
                }
            }

            public void StoreSlice(VolumeSliceOrientation orientation, int index, BitmapSource slice)
            {
                ArgumentNullException.ThrowIfNull(slice);
                long bytes = checked((long)slice.PixelWidth * slice.PixelHeight * (IsGray16 ? 2 : 1));
                if (bytes > MaxSliceCacheBytes)
                {
                    return;
                }

                lock (_sliceCacheLock)
                {
                    var key = (orientation, index);
                    if (_sliceCache.Remove(key, out BitmapSource? previous))
                    {
                        _sliceCacheBytes -= EstimateBytes(previous);
                        LinkedListNode<(VolumeSliceOrientation Orientation, int Index)>? previousNode = _sliceCacheOrder.Find(key);
                        if (previousNode is not null)
                        {
                            _sliceCacheOrder.Remove(previousNode);
                        }
                    }

                    while (_sliceCacheBytes + bytes > MaxSliceCacheBytes && _sliceCacheOrder.First is { } oldest)
                    {
                        _sliceCacheOrder.RemoveFirst();
                        if (_sliceCache.Remove(oldest.Value, out BitmapSource? evicted))
                        {
                            _sliceCacheBytes -= EstimateBytes(evicted);
                        }
                    }

                    _sliceCache[key] = slice;
                    _sliceCacheOrder.AddLast(key);
                    _sliceCacheBytes += bytes;
                }
            }

            private long EstimateBytes(BitmapSource slice) =>
                checked((long)slice.PixelWidth * slice.PixelHeight * (IsGray16 ? 2 : 1));

            public IntensityPlane[] GetAllPlanes(CancellationToken cancellationToken)
            {
                for (int index = 0; index < _planes.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    GetPlane(index, cancellationToken);
                }

                return _planes;
            }

            public ushort Read(int planeIndex, int x, int y)
            {
                ushort value = _planes[planeIndex].Read(x, y);
                // A mixed-format volume uses Gray16 for the reconstructed output. Promote
                // 8-bit planes to the same full range so orientation changes cannot alter contrast.
                return IsGray16 && !_planes[planeIndex].IsGray16
                    ? (ushort)(value * 257)
                    : value;
            }

            private IntensityPlane GetPlane(int index, CancellationToken cancellationToken)
            {
                IntensityPlane? cached = _planes[index];
                if (cached != null)
                {
                    return cached;
                }

                lock (_planeLocks[index])
                {
                    cached = _planes[index];
                    if (cached != null)
                    {
                        return cached;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    cached = IntensityPlane.Create(_slices[index]);
                    _planes[index] = cached;
                    return cached;
                }
            }
        }

        private sealed class IntensityPlane
        {
            private readonly byte[] _pixels;
            private readonly int _stride;
            private readonly int _bytesPerPixel;
            private readonly PixelFormat _format;

            private IntensityPlane(byte[] pixels, int stride, int bytesPerPixel, PixelFormat format)
            {
                _pixels = pixels;
                _stride = stride;
                _bytesPerPixel = bytesPerPixel;
                _format = format;
            }

            public bool IsGray16 => _format == PixelFormats.Gray16;

            public static IntensityPlane Create(BitmapSource source)
            {
                BitmapSource bitmap = ImageViewerPixelAccess.NormalizeForIntensity(source);
                int bytesPerPixel = ImageViewerPixelAccess.GetBytesPerPixel(bitmap);
                int stride = bitmap.PixelWidth * bytesPerPixel;
                byte[] pixels = new byte[bitmap.PixelHeight * stride];
                bitmap.CopyPixels(pixels, stride, 0);
                return new IntensityPlane(pixels, stride, bytesPerPixel, bitmap.Format);
            }

            public ushort Read(int x, int y)
            {
                int index = y * _stride + x * _bytesPerPixel;
                return ImageViewerPixelAccess.ReadIntensity(_pixels, index, _bytesPerPixel, _format);
            }
        }
    }
}
