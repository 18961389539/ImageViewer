using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using ImageViewer.Rendering;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    /// <summary>
    /// CPU / GPU 伪彩色一致性守卫。
    /// Chinese: 两条路径必须共享同一套定义——强度权重、alpha 策略、调色板取值。
    /// 早先 CPU 依赖 WPF 的 Gray8 转换（权重不可控）、alpha 固定 255、调色板各自实现（hot/jet 曲线不同），
    /// 因此在没有 GPU 的机器上颜色会不一样。这些用例把"共享定义"钉住。
    /// English: Guards that the CPU fallback and the shader share one definition — intensity weights, alpha policy, palette
    /// values — so the same document renders identically on machines with and without a usable shader.
    /// </summary>
    public class PseudoColorConsistencyTests
    {
        [Fact]
        public void ApplyPseudoColor_PreservesInputAlpha()
        {
            // Bgra32 的半透明像素（BGRA 字节序）：alpha 必须保留（与 Shader 的 float4(color, inputColor.a) 一致）。
            byte[] translucent = ReadOutputPixel(CreateBitmap(1, 1, [0, 0, 255, 128]));
            Assert.Equal(128, translucent[3]);

            byte[] opaque = ReadOutputPixel(CreateBitmap(1, 1, [0, 0, 255, 255]));
            Assert.Equal(255, opaque[3]);
        }

        [Fact]
        public void ApplyPseudoColor_WithoutAlphaChannel_TreatsInputAsOpaque()
        {
            byte[] color = ReadOutputPixel(CreateBitmap(1, 1, PixelFormats.Bgr24, [255, 0, 0]));
            Assert.Equal(255, color[3]);

            byte[] gray = ReadOutputPixel(CreateBitmap(1, 1, PixelFormats.Gray8, [200]));
            Assert.Equal(255, gray[3]);
        }

        [Fact]
        public void ApplyPseudoColor_UsesTheSameLuminanceWeightsAsTheShader()
        {
            // 纯红：强度 = round(0.299 * 255) = 76 —— 与 Shader 的 dot(rgb, 0.299/0.587/0.114) 同一组权重。
            byte[] lut = ImageViewerDisplaySourceService.GetPaletteLut(PseudoColorPalette.Hot);
            byte[] actual = ReadOutputPixel(CreateBitmap(1, 1, [0, 0, 255, 255]));
            int lutOffset = 76 * 4;

            Assert.Equal(lut[lutOffset], actual[0]);
            Assert.Equal(lut[lutOffset + 1], actual[1]);
            Assert.Equal(lut[lutOffset + 2], actual[2]);
        }

        [Fact]
        public void ApplyPseudoColor_MapsGray16WithTheSameRangeAsTheShader()
        {
            byte[] lut = ImageViewerDisplaySourceService.GetPaletteLut(PseudoColorPalette.Hot);

            // 65535 → 255，257 → 1（v/257），0 → 0；与 Shader 的 intensity * 255（intensity = v/65535）一致。
            Assert.Equal(lut[255 * 4], ReadOutputPixel(CreateGray16(65535))[0]);
            Assert.Equal(lut[1 * 4], ReadOutputPixel(CreateGray16(257))[0]);
            Assert.Equal(lut[0], ReadOutputPixel(CreateGray16(0))[0]);
        }

        [Fact]
        public void ApplyPseudoColor_NonePalette_ReturnsTheSourceUnchanged()
        {
            System.Windows.Media.Imaging.BitmapSource source = CreateBitmap(1, 1, [10, 20, 30, 255]);

            Assert.Same(source, ImageViewerDisplaySourceService.ApplyPseudoColor(source, PseudoColorPalette.None));
        }

        [Fact]
        public void Shader_SamplesTheSharedLutInsteadOfCarryingPaletteFormulas()
        {
            string source = ReadEmbeddedShaderSource();

            // 着色器不再自带调色板公式——hot/jet/viridis 只在 C# 的 LUT 里定义一次。
            Assert.DoesNotContain("hotPalette", source, StringComparison.Ordinal);
            Assert.DoesNotContain("jetPalette", source, StringComparison.Ordinal);
            Assert.DoesNotContain("viridisPalette", source, StringComparison.Ordinal);
            Assert.Contains("sampler2D PaletteLut : register(s1)", source, StringComparison.Ordinal);
            Assert.Contains("tex2D(PaletteLut", source, StringComparison.Ordinal);

            // 权重必须与 ImageViewerPixelAccess.ReadIntensity 一致（上面那条纯红用例把 CPU 侧钉住了）。
            Assert.Contains("0.299, 0.587, 0.114", source, StringComparison.Ordinal);

            // 保留输入 alpha。
            Assert.Contains("inputColor.a", source, StringComparison.Ordinal);
        }

        [Fact]
        public void PaletteLutBitmap_HasOneEntryPerIntensityLevel()
        {
            System.Windows.Media.Imaging.BitmapSource lut = ImageViewerDisplaySourceService.CreatePaletteLutBitmap(PseudoColorPalette.Viridis);

            Assert.Equal(256, lut.PixelWidth);
            Assert.Equal(1, lut.PixelHeight);
            Assert.True(lut.IsFrozen);
        }

        [Fact]
        public void CreatePseudoColorEffect_WithLutSampler_CompilesAndBuildsForEveryPalette()
        {
            // d3dcompiler_47.dll 在少数环境缺失：那属于"没有 GPU 路径"，跳过而不是误报失败。
            try
            {
                _ = PseudoColorShaderCompiler.GetPixelShader();
            }
            catch (Exception exception) when (exception is DllNotFoundException or FileNotFoundException or EntryPointNotFoundException)
            {
                return;
            }

            foreach (PseudoColorPalette palette in new[]
            {
                PseudoColorPalette.Hot,
                PseudoColorPalette.Jet,
                PseudoColorPalette.Viridis
            })
            {
                System.Windows.Media.Effects.Effect? effect = ImageViewerDisplaySourceService.CreatePseudoColorEffect(palette);

                var shaderEffect = Assert.IsType<PseudoColorShaderEffect>(effect);
                Assert.NotNull(shaderEffect.PaletteLut);
                Assert.Equal((double)palette, shaderEffect.PaletteIndex);
            }
        }

        private static byte[] ReadOutputPixel(System.Windows.Media.Imaging.BitmapSource source)
        {
            System.Windows.Media.Imaging.BitmapSource result = ImageViewerDisplaySourceService.ApplyPseudoColor(source, PseudoColorPalette.Hot);
            byte[] pixels = new byte[result.PixelWidth * result.PixelHeight * 4];
            result.CopyPixels(pixels, result.PixelWidth * 4, 0);
            return pixels;
        }

        private static System.Windows.Media.Imaging.BitmapSource CreateBitmap(int width, int height, byte[] pixels)
        {
            return CreateBitmap(width, height, System.Windows.Media.PixelFormats.Bgra32, pixels);
        }

        private static System.Windows.Media.Imaging.BitmapSource CreateBitmap(int width, int height, System.Windows.Media.PixelFormat format, byte[] pixels)
        {
            System.Windows.Media.Imaging.BitmapSource bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                width,
                height,
                96,
                96,
                format,
                null,
                pixels,
                width * ((format.BitsPerPixel + 7) / 8));
            bitmap.Freeze();
            return bitmap;
        }

        private static System.Windows.Media.Imaging.BitmapSource CreateGray16(ushort value)
        {
            return CreateBitmap(1, 1, System.Windows.Media.PixelFormats.Gray16, [(byte)value, (byte)(value >> 8)]);
        }

        /// <summary>
        /// 读取着色器源码：WPF 的 &lt;Resource&gt; 项在 &lt;程序集&gt;.g.resources 容器里，键是小写化的相对路径。
        /// </summary>
        private static string ReadEmbeddedShaderSource()
        {
            System.Reflection.Assembly assembly = typeof(ImageViewerDisplaySourceService).Assembly;
            string? container = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(container);

            using Stream containerStream = assembly.GetManifestResourceStream(container)!;
            using var resourceReader = new System.Resources.ResourceReader(containerStream);
            foreach (System.Collections.DictionaryEntry entry in resourceReader)
            {
                if (entry.Key is string key &&
                    key.EndsWith("shaders/pseudocoloreffect.ps.hlsl", StringComparison.OrdinalIgnoreCase) &&
                    entry.Value is Stream shaderStream)
                {
                    using (shaderStream)
                    using (var reader = new StreamReader(shaderStream))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }

            throw new InvalidOperationException("Pseudo-color shader source was not found in the g.resources container.");
        }
    }
}
