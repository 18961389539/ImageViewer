using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ImageViewer.Rendering;

namespace ImageViewer.Services
{
    internal sealed class ImageViewerDisplaySourceService
    {
        public static ImageSource? BuildDisplaySource(ImageSource? source, PseudoColorPalette palette)
        {
            if (source is not BitmapSource bitmap || palette == PseudoColorPalette.None)
            {
                return source;
            }

            return ApplyPseudoColor(bitmap, palette);
        }

        public static void ApplyGpuCaching(Canvas imageContainer, bool enableGpuRendering)
        {
            ArgumentNullException.ThrowIfNull(imageContainer);
            imageContainer.CacheMode = enableGpuRendering ? new BitmapCache() : null;
        }

        public static BitmapSource? GetAnalysisBitmap(ImageSource? source)
        {
            if (source is not BitmapSource bitmap)
            {
                return null;
            }

            if (bitmap is RenderTargetBitmap)
            {
                var detachedBitmap = new WriteableBitmap(bitmap);
                if (detachedBitmap.CanFreeze)
                {
                    detachedBitmap.Freeze();
                }

                return detachedBitmap;
            }

            if (bitmap.IsFrozen)
            {
                return bitmap;
            }

            BitmapSource clone = bitmap.Clone();
            if (clone.CanFreeze)
            {
                clone.Freeze();
            }

            return clone;
        }

        public static Effect? CreatePseudoColorEffect(PseudoColorPalette palette)
        {
            if (palette == PseudoColorPalette.None)
            {
                return null;
            }

            try
            {
                return new PseudoColorShaderEffect(palette);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// CPU 伪彩色。
        /// Chinese: 强度与 alpha 都按"和 Shader 同一套定义"计算：
        /// ① 强度用 <see cref="ImageViewerPixelAccess.ReadIntensity"/>，与 Shader 的 `dot(rgb, 0.299/0.587/0.114)` 同权重——
        ///    早先依赖 WPF 的 Gray8 格式转换，权重由 WPF 内部决定、不可控，且彩色图会与 Shader 结果不同；
        /// ② 保留输入 alpha（Bgra32/Pbgra32），其余格式按不透明处理——与 Shader 的 `float4(color, inputColor.a)` 对齐；
        /// ③ 调色板只有一份定义（本文件的 LUT），Shader 改为采样同一张 LUT 纹理。
        /// English: Intensity uses the same weights as the shader and input alpha is preserved, so the CPU fallback and the
        /// shader agree by construction; the palette exists only once, in the LUT below.
        /// </summary>
        internal static BitmapSource ApplyPseudoColor(BitmapSource source, PseudoColorPalette palette)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (palette == PseudoColorPalette.None)
            {
                return source;
            }

            BitmapSource readableSource = NormalizeForColorRead(source);
            int width = readableSource.PixelWidth;
            int height = readableSource.PixelHeight;
            int bytesPerPixel = ImageViewerPixelAccess.GetBytesPerPixel(readableSource);
            int stride = checked(width * bytesPerPixel);
            byte[] sourcePixels = new byte[checked(stride * height)];
            readableSource.CopyPixels(sourcePixels, stride, 0);

            bool hasAlpha = readableSource.Format == PixelFormats.Bgra32 || readableSource.Format == PixelFormats.Pbgra32;
            byte[] colorPixels = new byte[width * height * 4];
            byte[] paletteLut = GetPaletteLut(palette);
            for (int i = 0; i < width * height; i++)
            {
                int sourceIndex = i * bytesPerPixel;
                int colorIndex = i * 4;
                int lutIndex = ReadPaletteIndex(sourcePixels, sourceIndex, bytesPerPixel, readableSource.Format) * 4;
                colorPixels[colorIndex] = paletteLut[lutIndex];
                colorPixels[colorIndex + 1] = paletteLut[lutIndex + 1];
                colorPixels[colorIndex + 2] = paletteLut[lutIndex + 2];
                colorPixels[colorIndex + 3] = hasAlpha ? sourcePixels[sourceIndex + 3] : byte.MaxValue;
            }

            var result = BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, colorPixels, width * 4);
            result.Freeze();
            return result;
        }

        /// <summary>
        /// 归一化到"可以按像素直接读强度"的格式。
        /// Chinese: 直通与 Shader 能看到相同数值的格式；其它格式转成 Bgra32（而不是 Gray8，那会丢掉 alpha）。
        /// English: Passes through formats whose samples match what the shader sees; other formats are converted to Bgra32
        /// rather than Gray8, which would drop alpha.
        /// </summary>
        private static BitmapSource NormalizeForColorRead(BitmapSource source)
        {
            return source.Format == PixelFormats.Gray8 ||
                   source.Format == PixelFormats.Gray16 ||
                   source.Format == PixelFormats.Bgr24 ||
                   source.Format == PixelFormats.Bgr32 ||
                   source.Format == PixelFormats.Bgra32 ||
                   source.Format == PixelFormats.Pbgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        }

        /// <summary>
        /// 求调色板索引（0..255）。
        /// Chinese: 16 位强度按 v/257 落到 0..255——与 Shader 的 `intensity * 255`（intensity = v/65535）在数学上同一个映射。
        /// English: 16-bit intensities map as v/257, which is the same mapping as the shader's `intensity * 255`.
        /// </summary>
        private static int ReadPaletteIndex(byte[] pixels, int index, int bytesPerPixel, PixelFormat format)
        {
            ushort intensity = ImageViewerPixelAccess.ReadIntensity(pixels, index, bytesPerPixel, format);
            if (format == PixelFormats.Gray16)
            {
                return Math.Clamp((int)Math.Round(intensity / 257d), 0, 255);
            }

            return Math.Min(intensity, byte.MaxValue);
        }

        /// <summary>
        /// 把调色板 LUT 包成 256×1 位图，供 Shader 采样。
        /// Chinese: 这是 CPU 与 GPU 共用的唯一定义——Shader 不再自带 hot/jet/viridis 公式。
        /// English: The LUT as a 256×1 bitmap so the shader can sample the very same table instead of carrying its own ramps.
        /// </summary>
        internal static BitmapSource CreatePaletteLutBitmap(PseudoColorPalette palette)
        {
            byte[] lut = GetPaletteLut(palette);
            var bitmap = BitmapSource.Create(256, 1, 96, 96, PixelFormats.Bgra32, null, lut, 256 * 4);
            bitmap.Freeze();
            return bitmap;
        }

        private static readonly byte[] HotPaletteLut = CreatePaletteLut(PseudoColorPalette.Hot);
        private static readonly byte[] JetPaletteLut = CreatePaletteLut(PseudoColorPalette.Jet);
        private static readonly byte[] ViridisPaletteLut = CreatePaletteLut(PseudoColorPalette.Viridis);
        private static readonly byte[] DefaultPaletteLut = CreatePaletteLut(PseudoColorPalette.None);

        /// <summary>取调色板 LUT（BGRA，256 项）。测试与 Shader 采样路径都读这一份。</summary>
        internal static byte[] GetPaletteLut(PseudoColorPalette palette)
        {
            return palette switch
            {
                PseudoColorPalette.Hot => HotPaletteLut,
                PseudoColorPalette.Jet => JetPaletteLut,
                PseudoColorPalette.Viridis => ViridisPaletteLut,
                _ => DefaultPaletteLut,
            };
        }

        private static byte[] CreatePaletteLut(PseudoColorPalette palette)
        {
            var lut = new byte[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                Color color = GetPaletteColor(i / 255d, palette);
                int offset = i * 4;
                lut[offset] = color.B;
                lut[offset + 1] = color.G;
                lut[offset + 2] = color.R;
                lut[offset + 3] = 255;
            }

            return lut;
        }

        private static Color GetPaletteColor(double value, PseudoColorPalette palette)
        {
            value = Math.Clamp(value, 0d, 1d);
            return palette switch
            {
                PseudoColorPalette.Hot => Interpolate(value,
                    (0.0, Colors.Black),
                    (0.33, Colors.DarkRed),
                    (0.66, Colors.Orange),
                    (1.0, Colors.Yellow)),
                PseudoColorPalette.Jet => Interpolate(value,
                    (0.0, Color.FromRgb(0, 0, 128)),
                    (0.35, Colors.Cyan),
                    (0.66, Colors.Yellow),
                    (1.0, Color.FromRgb(128, 0, 0))),
                PseudoColorPalette.Viridis => Interpolate(value,
                    (0.0, Color.FromRgb(68, 1, 84)),
                    (0.33, Color.FromRgb(59, 82, 139)),
                    (0.66, Color.FromRgb(33, 145, 140)),
                    (1.0, Color.FromRgb(253, 231, 37))),
                _ => Colors.Transparent
            };
        }

        private static Color Interpolate(double value, params (double Stop, Color Color)[] stops)
        {
            if (stops.Length == 0)
            {
                return Colors.Transparent;
            }

            if (value <= stops[0].Stop)
            {
                return stops[0].Color;
            }

            for (int i = 1; i < stops.Length; i++)
            {
                if (value <= stops[i].Stop)
                {
                    double range = stops[i].Stop - stops[i - 1].Stop;
                    double t = range <= 0 ? 0 : (value - stops[i - 1].Stop) / range;
                    return Color.FromRgb(
                        (byte)Math.Round(stops[i - 1].Color.R + (stops[i].Color.R - stops[i - 1].Color.R) * t),
                        (byte)Math.Round(stops[i - 1].Color.G + (stops[i].Color.G - stops[i - 1].Color.G) * t),
                        (byte)Math.Round(stops[i - 1].Color.B + (stops[i].Color.B - stops[i - 1].Color.B) * t));
                }
            }

            return stops[^1].Color;
        }
    }
}
