using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JLVisionLib;

namespace ImageViewer.Services;

/// <summary>
/// Owns a JLVision image handle created from a WPF bitmap.
/// The wrapper also owns any pinned channel buffers used by a three-plane image.
/// </summary>
internal sealed class JLVisionImageHandle : IDisposable
{
    private GCHandle _redHandle;
    private GCHandle _greenHandle;
    private GCHandle _blueHandle;
    private bool _disposed;

    internal JLVisionImageHandle(
        JlImage image,
        int width,
        int height,
        PixelFormat sourceFormat,
        bool isColor,
        GCHandle redHandle = default,
        GCHandle greenHandle = default,
        GCHandle blueHandle = default)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        Width = width;
        Height = height;
        SourceFormat = sourceFormat;
        IsColor = isColor;
        _redHandle = redHandle;
        _greenHandle = greenHandle;
        _blueHandle = blueHandle;
    }

    public JlImage Image { get; }

    public int Width { get; }

    public int Height { get; }

    public PixelFormat SourceFormat { get; }

    public bool IsColor { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Image.Dispose();
        FreeHandle(ref _redHandle);
        FreeHandle(ref _greenHandle);
        FreeHandle(ref _blueHandle);
        GC.SuppressFinalize(this);
    }

    private static void FreeHandle(ref GCHandle handle)
    {
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }
}

/// <summary>
/// Converts WPF bitmaps to JLVision images without exposing native handle lifetime to callers.
/// </summary>
internal static class JLVisionImageBridge
{
    /// <summary>
    /// Creates the single-channel image used by measurement and segmentation operators.
    /// Color input is converted to the same luminance definition used by ImageViewer's
    /// existing intensity accessors. Gray16 is preserved as a 16-bit uint2 image.
    /// </summary>
    public static JLVisionImageHandle CreateIntensity(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        EnsureNativeLibrary();

        BitmapSource source = ImageViewerPixelAccess.NormalizeForIntensity(bitmap);
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        PixelFormat format = source.Format;
        int bytesPerPixel = GetBytesPerPixel(format);
        int sourceStride = GetStride(width, format.BitsPerPixel);
        byte[] sourcePixels = new byte[checked(sourceStride * height)];
        source.CopyPixels(sourcePixels, sourceStride, 0);

        if (format == PixelFormats.Gray8 || format == PixelFormats.Gray16)
        {
            using var pinned = Pin(sourcePixels);
            var image = new JlImage();
            try
            {
                image.GenImage1Rect(
                    pinned.AddrOfPinnedObject(),
                    width,
                    height,
                    sourceStride,
                    format == PixelFormats.Gray16 ? 16 : 8,
                    format == PixelFormats.Gray16 ? 16 : 8,
                    "true",
                    IntPtr.Zero);
                return new JLVisionImageHandle(image, width, height, format, isColor: false);
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        byte[] intensityPixels = new byte[checked(width * height)];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * sourceStride;
            int outputRow = y * width;
            for (int x = 0; x < width; x++)
            {
                int index = sourceRow + x * bytesPerPixel;
                intensityPixels[outputRow + x] = ImageViewerPixelAccess.ReadIntensity8(
                    sourcePixels,
                    index,
                    bytesPerPixel,
                    format);
            }
        }

        using var intensityPinned = Pin(intensityPixels);
        var intensityImage = new JlImage();
        try
        {
            intensityImage.GenImage1Rect(
                intensityPinned.AddrOfPinnedObject(),
                width,
                height,
                width,
                8,
                8,
                "true",
                IntPtr.Zero);
            return new JLVisionImageHandle(intensityImage, width, height, format, isColor: false);
        }
        catch
        {
            intensityImage.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a three-channel byte image from Bgr24, Bgr32 or Bgra32 input.
    /// JLVision expects separate planes; WPF's interleaved B/G/R(A) pixels are split here.
    /// </summary>
    public static JLVisionImageHandle CreateColor(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        EnsureNativeLibrary();

        BitmapSource source = ImageViewerPixelAccess.NormalizeForIntensity(bitmap);
        PixelFormat format = source.Format;
        if (format != PixelFormats.Bgr24 &&
            format != PixelFormats.Bgr32 &&
            format != PixelFormats.Bgra32)
        {
            throw new NotSupportedException(
                $"JLVision color bridging requires Bgr24, Bgr32 or Bgra32 input; received {format}. " +
                "Use CreateIntensity for grayscale analysis.");
        }

        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int bytesPerPixel = GetBytesPerPixel(format);
        int sourceStride = GetStride(width, format.BitsPerPixel);
        byte[] sourcePixels = new byte[checked(sourceStride * height)];
        source.CopyPixels(sourcePixels, sourceStride, 0);

        byte[] red = new byte[checked(width * height)];
        byte[] green = new byte[checked(width * height)];
        byte[] blue = new byte[checked(width * height)];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * sourceStride;
            int outputRow = y * width;
            for (int x = 0; x < width; x++)
            {
                int sourceIndex = sourceRow + x * bytesPerPixel;
                int outputIndex = outputRow + x;
                blue[outputIndex] = sourcePixels[sourceIndex];
                green[outputIndex] = sourcePixels[sourceIndex + 1];
                red[outputIndex] = sourcePixels[sourceIndex + 2];
            }
        }

        GCHandle redHandle = default;
        GCHandle greenHandle = default;
        GCHandle blueHandle = default;
        var image = new JlImage();
        try
        {
            redHandle = GCHandle.Alloc(red, GCHandleType.Pinned);
            greenHandle = GCHandle.Alloc(green, GCHandleType.Pinned);
            blueHandle = GCHandle.Alloc(blue, GCHandleType.Pinned);
            image.GenImage3(
                "byte",
                width,
                height,
                redHandle.AddrOfPinnedObject(),
                greenHandle.AddrOfPinnedObject(),
                blueHandle.AddrOfPinnedObject());

            return new JLVisionImageHandle(
                image,
                width,
                height,
                format,
                isColor: true,
                redHandle,
                greenHandle,
                blueHandle);
        }
        catch
        {
            image.Dispose();
            FreeHandle(ref redHandle);
            FreeHandle(ref greenHandle);
            FreeHandle(ref blueHandle);
            throw;
        }
    }

    private static void EnsureNativeLibrary()
    {
        if (!File.Exists(JLVisionRuntimeDiagnostics.RuntimePath))
        {
            throw new DllNotFoundException(JLVisionRuntimeDiagnostics.BuildMissingRuntimeMessage());
        }
    }

    private static int GetBytesPerPixel(PixelFormat format) =>
        Math.Max(1, (format.BitsPerPixel + 7) / 8);

    private static int GetStride(int width, int bitsPerPixel) =>
        checked(((width * bitsPerPixel + 31) / 32) * 4);

    private static void FreeHandle(ref GCHandle handle)
    {
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }

    private static PinnedBuffer Pin(byte[] buffer) => new(buffer);

    private readonly struct PinnedBuffer : IDisposable
    {
        private readonly GCHandle _handle;

        public PinnedBuffer(byte[] buffer)
        {
            _handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        }

        public IntPtr AddrOfPinnedObject() => _handle.AddrOfPinnedObject();

        public void Dispose()
        {
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
        }
    }
}
