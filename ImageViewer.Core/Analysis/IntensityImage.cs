using System;

namespace ImageViewer.Core.Analysis;

/// <summary>
/// Single-channel intensity plane consumed by the managed measurement algorithms.
/// Chinese: 灰度测量图像。Gray16 保留完整 16 位动态范围；彩色输入按亮度折叠为 8 位。
/// English: Gray16 keeps its full 16-bit range; color input collapses to 8-bit luminance.
/// </summary>
/// <remarks>
/// The plane is framework-neutral. WPF adapters decode a <c>BitmapSource</c> into a
/// <c>ushort[]</c> buffer and hand ownership of that buffer to this type.
/// </remarks>
public sealed class IntensityImage
{
    private readonly ushort[] _pixels;

    /// <summary>
    /// Creates an intensity plane. The pixel buffer is adopted, not copied, so the caller
    /// must not mutate it afterwards.
    /// </summary>
    /// <param name="width">Pixel column count.</param>
    /// <param name="height">Pixel row count.</param>
    /// <param name="maxValue">255 for 8-bit sources, 65535 for Gray16.</param>
    /// <param name="pixels">Row-major intensity samples.</param>
    public IntensityImage(int width, int height, int maxValue, ushort[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        }

        if (maxValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue), maxValue, "MaxValue must be positive.");
        }

        if (pixels.Length != checked(width * height))
        {
            throw new ArgumentException("Pixel buffer length must equal width * height.", nameof(pixels));
        }

        _pixels = pixels;
        Width = width;
        Height = height;
        MaxValue = maxValue;
    }

    /// <summary>Pixel column count.</summary>
    public int Width { get; }

    /// <summary>Pixel row count.</summary>
    public int Height { get; }

    /// <summary>255 for 8-bit sources, 65535 for Gray16.</summary>
    public int MaxValue { get; }

    /// <summary>Reads one sample by integer pixel coordinates.</summary>
    public ushort this[int x, int y] => _pixels[(y * Width) + x];

    /// <summary>
    /// Nearest-neighbor sample. Out-of-image positions read as 0; callers that need the
    /// border-truncation semantics check bounds before sampling.
    /// </summary>
    public double SampleNearest(double x, double y)
    {
        int ix = (int)Math.Round(x, MidpointRounding.AwayFromZero);
        int iy = (int)Math.Round(y, MidpointRounding.AwayFromZero);
        if ((uint)ix >= (uint)Width || (uint)iy >= (uint)Height)
        {
            return 0;
        }

        return _pixels[(iy * Width) + ix];
    }
}
