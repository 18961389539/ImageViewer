using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    /// <summary>
    /// MPR 切片重建服务。
    /// Chinese: 同一 (方向, 索引) 的重建是 **single-flight** 的——拖动时同一个索引被反复请求也只会构建一次完整平面，
    /// 而调用方各自按自己的取消令牌等待（谁不等了谁就走，不影响共享任务与其它调用方）。
    /// English: Rebuilding a given (orientation, index) plane is single-flight: repeated requests for the same plane during a
    /// drag produce exactly one reconstruction, while each caller waits with its own cancellation token without affecting
    /// the shared work or other callers.
    /// </summary>
    public sealed class VolumeSliceService
    {
        private static readonly ConditionalWeakTable<VolumeData, CachedIntensityVolume> IntensityCaches = new();

        public static BitmapSource GetSlice(VolumeData volume, VolumeSliceOrientation orientation, int sliceIndex, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(volume);
            return orientation switch
            {
                VolumeSliceOrientation.Axial => volume.GetAxialSlice(sliceIndex),
                VolumeSliceOrientation.Coronal => GetOrCreateSlice(volume, orientation, sliceIndex, cancellationToken),
                VolumeSliceOrientation.Sagittal => GetOrCreateSlice(volume, orientation, sliceIndex, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unsupported slice orientation.")
            };
        }

        /// <summary>
        /// 测试专用故障注入。
        /// Chinese: 返回非 null 异常时构建会立即抛出——否则"失败条目被移除并重建"这条不变式无法被测试
        /// （判据：重试时 <see cref="GetSliceBuildCount"/> 递增）。测试必须在 finally 里复位。
        /// English: Test-only hook. When it returns a non-null exception the build throws immediately; tests must reset it.
        /// </summary>
        internal static Func<VolumeData, (VolumeSliceOrientation Orientation, int Index), Exception?>? SliceBuildFaultForTest;

        /// <summary>
        /// 该卷实际执行过的重建次数。
        /// Chinese: 供测试观测"同一 (方向, 索引) 只构建一次"——没有这个观测点，single-flight 就只能靠代码审查。
        /// English: Exposes how many reconstructions actually ran, so single-flight can be asserted instead of reviewed.
        /// </summary>
        internal static int GetSliceBuildCount(VolumeData volume)
        {
            ArgumentNullException.ThrowIfNull(volume);
            return IntensityCaches.TryGetValue(volume, out CachedIntensityVolume? cache) ? cache.SliceBuildCount : 0;
        }

        /// <summary>已入账的切片字节数（观测"全部调用方取消也必须受预算约束"）。</summary>
        internal static long GetSliceCacheBytes(VolumeData volume)
        {
            ArgumentNullException.ThrowIfNull(volume);
            return IntensityCaches.TryGetValue(volume, out CachedIntensityVolume? cache) ? cache.SliceCacheBytes : 0;
        }

        /// <summary>条目数（观测"失败条目被移除"）。</summary>
        internal static int GetSliceEntryCount(VolumeData volume)
        {
            ArgumentNullException.ThrowIfNull(volume);
            return IntensityCaches.TryGetValue(volume, out CachedIntensityVolume? cache) ? cache.EntryCount : 0;
        }

        private static BitmapSource GetOrCreateSlice(VolumeData volume, VolumeSliceOrientation orientation, int sliceIndex, CancellationToken cancellationToken)
        {
            // 先做边界校验（保持原有异常语义），避免用一个非法索引去污染 single-flight 字典。
            int sliceCount = orientation == VolumeSliceOrientation.Coronal ? volume.Height : volume.Width;
            if ((uint)sliceIndex >= (uint)sliceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(sliceIndex));
            }

            CachedIntensityVolume cache = IntensityCaches.GetValue(volume, static source => new CachedIntensityVolume(source));
            return cache.GetSlice(volume, orientation, sliceIndex, cancellationToken);
        }

        private sealed class CachedIntensityVolume
        {
            private const long MaxSliceCacheBytes = 64L * 1024 * 1024;

            private readonly object _sliceCacheLock = new();
            private readonly ConcurrentDictionary<(VolumeSliceOrientation Orientation, int Index), SliceEntry> _slices = new();
            private readonly LinkedList<(VolumeSliceOrientation Orientation, int Index)> _sliceCacheOrder = new();
            private long _sliceCacheBytes;
            private int _sliceBuildCount;

            public CachedIntensityVolume(VolumeData volume)
            {
                // Keep only the format decision here. Normalized bitmaps and intensity
                // buffers are created one axial slice at a time during MPR reconstruction.
                // Retaining one full buffer per axial slice made the first MPR request scale
                // with the entire volume instead of with the requested output plane.
                IsGray16 = volume.Slices.Any(slice => slice.Format == PixelFormats.Gray16);
            }

            public bool IsGray16 { get; }

            public int SliceBuildCount => Volatile.Read(ref _sliceBuildCount);

            public int EntryCount => _slices.Count;

            /// <summary>已入账的切片字节数。</summary>
            public long SliceCacheBytes => Volatile.Read(ref _sliceCacheBytes);

            public BitmapSource GetSlice(VolumeData volume, VolumeSliceOrientation orientation, int sliceIndex, CancellationToken cancellationToken)
            {
                return GetSliceAsync(volume, orientation, sliceIndex, cancellationToken).GetAwaiter().GetResult();
            }

            public BitmapSource BuildCoronalSlice(VolumeData volume, int sliceIndex, CancellationToken cancellationToken)
            {
                int width = volume.Width;
                int height = volume.Depth;
                int outputBytesPerPixel = IsGray16 ? 2 : 1;
                byte[] output = new byte[checked(width * height * outputBytesPerPixel)];
                byte[] sourcePixels = new byte[checked(width * 4)];

                for (int depthIndex = 0; depthIndex < height; depthIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    BitmapSource source = ImageViewerPixelAccess.NormalizeForIntensity(volume.GetAxialSlice(depthIndex));
                    int bytesPerPixel = ImageViewerPixelAccess.GetBytesPerPixel(source);
                    int sourceStride = checked(width * bytesPerPixel);
                    source.CopyPixels(new Int32Rect(0, sliceIndex, width, 1), sourcePixels, sourceStride, 0);
                    CopyRowToOutput(sourcePixels, bytesPerPixel, source.Format, output, depthIndex * width * outputBytesPerPixel, width, IsGray16);
                }

                return CreateOutputBitmap(width, height, output, IsGray16);
            }

            public BitmapSource BuildSagittalSlice(VolumeData volume, int sliceIndex, CancellationToken cancellationToken)
            {
                int width = volume.Depth;
                int height = volume.Height;
                int outputBytesPerPixel = IsGray16 ? 2 : 1;
                byte[] output = new byte[checked(width * height * outputBytesPerPixel)];
                byte[] sourcePixels = new byte[checked(height * 4)];

                for (int depthIndex = 0; depthIndex < width; depthIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    BitmapSource source = ImageViewerPixelAccess.NormalizeForIntensity(volume.GetAxialSlice(depthIndex));
                    int bytesPerPixel = ImageViewerPixelAccess.GetBytesPerPixel(source);
                    source.CopyPixels(new Int32Rect(sliceIndex, 0, 1, height), sourcePixels, bytesPerPixel, 0);
                    CopyColumnToOutput(sourcePixels, bytesPerPixel, source.Format, output, depthIndex, width, height, IsGray16);
                }

                return CreateOutputBitmap(width, height, output, IsGray16);
            }

            private async Task<BitmapSource> GetSliceAsync(
                VolumeData volume,
                VolumeSliceOrientation orientation,
                int sliceIndex,
                CancellationToken cancellationToken)
            {
                (VolumeSliceOrientation Orientation, int Index) key = (orientation, sliceIndex);
                SliceEntry entry = _slices.GetOrAdd(
                    key,
                    static (sliceKey, state) => new SliceEntry(new Lazy<Task<BitmapSource>>(() => state.cache.BuildSliceAsync(state.volume, sliceKey))),
                    (cache: this, volume));

                // 条目的完成路径不依赖任何调用方：成功即入账（哪怕所有调用方都放弃了，结果也必须受预算约束）、
                // 失败即移除（不能把一次瞬时失败永久缓存）。等待者只负责等待与触碰 LRU 新近度。
                AttachEntryLifecycle(key, entry);

                BitmapSource? slice = null;
                try
                {
                    // 每个调用方按自己的令牌等待：放弃的调用方不会取消共享任务，其它调用方照样拿到结果。
                    slice = await entry.Slice.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (slice is not null)
                    {
                        TouchSlice(entry);
                    }
                }

                return slice;
            }

            /// <summary>
            /// 挂接条目的生命周期续接（每条目恰好一次）。
            /// Chinese: 必须与调用方解耦——"成功入账/失败移除"若绑在某个等待者身上，快速拖动时全部等待者
            /// 都可能先行取消：成功结果绕过 64MB 预算、失败任务永久缓存。续接在共享任务的完成线程上同步执行。
            /// English: Entry lifecycle must not depend on waiters: on success the slice is registered (so the budget always
            /// applies), on failure the entry is removed (so a transient failure is never cached). The continuation runs once.
            /// </summary>
            private void AttachEntryLifecycle((VolumeSliceOrientation Orientation, int Index) key, SliceEntry entry)
            {
                if (Volatile.Read(ref entry.LifecycleAttached) != 0)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref entry.LifecycleAttached, 1, 0) != 0)
                {
                    return;
                }

                _ = entry.Slice.Value.ContinueWith(
                    completed =>
                    {
                        if (completed.IsCanceled || completed.IsFaulted)
                        {
                            RemoveEntry(key, entry);
                        }
                        else
                        {
                            RegisterCompletedSlice(key, entry, completed.Result);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            private void TouchSlice(SliceEntry entry)
            {
                lock (_sliceCacheLock)
                {
                    if (entry.Node is { } node)
                    {
                        _sliceCacheOrder.Remove(node);
                        _sliceCacheOrder.AddLast(node);
                    }
                }
            }

            /// <summary>
            /// 执行一次平面重建。
            /// Chinese: 刻意不带任何调用方的令牌——shared task 用某个调用方的 token，一次被放弃的拖动就会毒化
            /// 共享结果，后面所有调用方都会看到"已取消"。因此构建会跑完并被缓存，之后再请求同一片直接命中。
            /// English: Deliberately detached from caller tokens. Binding the shared task to one caller's token would let a
            /// single abandoned drag poison the shared result for every later caller; instead the build completes and is
            /// cached, so returning to that plane is an instant hit.
            /// </summary>
            private Task<BitmapSource> BuildSliceAsync(VolumeData volume, (VolumeSliceOrientation Orientation, int Index) key)
            {
                return Task.Run(() =>
                {
                    Interlocked.Increment(ref _sliceBuildCount);

                    Exception? fault = SliceBuildFaultForTest?.Invoke(volume, key);
                    if (fault is not null)
                    {
                        throw fault;
                    }

                    BitmapSource slice = key.Orientation == VolumeSliceOrientation.Coronal
                        ? BuildCoronalSlice(volume, key.Index, CancellationToken.None)
                        : BuildSagittalSlice(volume, key.Index, CancellationToken.None);
                    return slice;
                });
            }

            private void RegisterCompletedSlice((VolumeSliceOrientation Orientation, int Index) key, SliceEntry entry, BitmapSource slice)
            {
                long bytes = EstimateBytes(slice);
                lock (_sliceCacheLock)
                {
                    if (entry.Node is { } existingNode)
                    {
                        _sliceCacheOrder.Remove(existingNode);
                        _sliceCacheOrder.AddLast(existingNode);
                        return;
                    }

                    if (bytes > MaxSliceCacheBytes)
                    {
                        // 单张就超出预算：与旧行为一致——不入账，也不留在字典里（调用方仍然拿到这一片）。
                        _slices.TryRemove(new KeyValuePair<(VolumeSliceOrientation Orientation, int Index), SliceEntry>(key, entry));
                        return;
                    }

                    entry.Bytes = bytes;
                    entry.Node = _sliceCacheOrder.AddLast(key);
                    _sliceCacheBytes += bytes;

                    while (_sliceCacheBytes > MaxSliceCacheBytes && _sliceCacheOrder.First is { } oldest)
                    {
                        _sliceCacheOrder.RemoveFirst();
                        if (_slices.TryRemove(oldest.Value, out SliceEntry? evicted))
                        {
                            _sliceCacheBytes -= evicted.Bytes;
                            evicted.Node = null;
                        }
                    }
                }
            }

            private void RemoveEntry((VolumeSliceOrientation Orientation, int Index) key, SliceEntry entry)
            {
                _slices.TryRemove(new KeyValuePair<(VolumeSliceOrientation Orientation, int Index), SliceEntry>(key, entry));

                lock (_sliceCacheLock)
                {
                    if (entry.Node is { } node)
                    {
                        _sliceCacheOrder.Remove(node);
                        _sliceCacheBytes -= entry.Bytes;
                        entry.Node = null;
                    }
                }
            }

            private long EstimateBytes(BitmapSource slice) =>
                checked((long)slice.PixelWidth * slice.PixelHeight * (IsGray16 ? 2 : 1));

            private sealed class SliceEntry
            {
                public SliceEntry(Lazy<Task<BitmapSource>> slice)
                {
                    Slice = slice;
                }

                /// <summary>共享的重建任务：完成后的结果同时充当切片缓存。</summary>
                public Lazy<Task<BitmapSource>> Slice { get; }

                /// <summary>LRU 节点（仅在成功完成并入账后存在），省掉按 key 线性查找链表。</summary>
                public LinkedListNode<(VolumeSliceOrientation Orientation, int Index)>? Node { get; set; }

                public long Bytes { get; set; }

                /// <summary>生命周期续接只允许挂一次（外层类用 Interlocked 操作此标记）。</summary>
                public int LifecycleAttached;
            }

            private static void CopyRowToOutput(
                byte[] sourcePixels,
                int sourceBytesPerPixel,
                PixelFormat sourceFormat,
                byte[] output,
                int outputOffset,
                int width,
                bool gray16)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort value = ImageViewerPixelAccess.ReadIntensity(sourcePixels, x * sourceBytesPerPixel, sourceBytesPerPixel, sourceFormat);
                    WriteOutputValue(output, outputOffset + x * (gray16 ? 2 : 1), value, gray16, sourceFormat);
                }
            }

            private static void CopyColumnToOutput(
                byte[] sourcePixels,
                int sourceBytesPerPixel,
                PixelFormat sourceFormat,
                byte[] output,
                int depthIndex,
                int outputWidth,
                int height,
                bool gray16)
            {
                for (int y = 0; y < height; y++)
                {
                    ushort value = ImageViewerPixelAccess.ReadIntensity(sourcePixels, y * sourceBytesPerPixel, sourceBytesPerPixel, sourceFormat);
                    WriteOutputValue(output, (y * outputWidth + depthIndex) * (gray16 ? 2 : 1), value, gray16, sourceFormat);
                }
            }

            private static void WriteOutputValue(byte[] output, int offset, ushort value, bool gray16, PixelFormat sourceFormat)
            {
                if (!gray16)
                {
                    output[offset] = (byte)Math.Min(value, byte.MaxValue);
                    return;
                }

                // A mixed-format volume uses Gray16 for the reconstructed output. Promote
                // 8-bit and color samples to the same full range as native Gray16 samples.
                ushort normalized = sourceFormat == PixelFormats.Gray16 ? value : (ushort)(value * 257);
                output[offset] = (byte)normalized;
                output[offset + 1] = (byte)(normalized >> 8);
            }

            private static BitmapSource CreateOutputBitmap(int width, int height, byte[] pixels, bool gray16)
            {
                BitmapSource result = BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    gray16 ? PixelFormats.Gray16 : PixelFormats.Gray8,
                    null,
                    pixels,
                    width * (gray16 ? 2 : 1));
                result.Freeze();
                return result;
            }
        }
    }
}
