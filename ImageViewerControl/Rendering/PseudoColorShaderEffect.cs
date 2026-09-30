using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ImageViewer.Services;

namespace ImageViewer.Rendering
{
    internal sealed class PseudoColorShaderEffect : ShaderEffect
    {
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(PseudoColorShaderEffect), 0);

        /// <summary>
        /// 调色板 LUT（256×1 位图，s1）。
        /// Chinese: 与 CPU 路径共用同一张表——着色器不再自带 hot/jet/viridis 公式，两条路径不可能再漂移。
        /// English: The very same 256-entry table the CPU path uses; the shader carries no palette formulas of its own.
        /// </summary>
        public static readonly DependencyProperty PaletteLutProperty = RegisterPixelShaderSamplerProperty(nameof(PaletteLut), typeof(PseudoColorShaderEffect), 1);

        public static readonly DependencyProperty PaletteIndexProperty = DependencyProperty.Register(
            nameof(PaletteIndex),
            typeof(double),
            typeof(PseudoColorShaderEffect),
            new UIPropertyMetadata(0d, PixelShaderConstantCallback(0)));

        public PseudoColorShaderEffect(PseudoColorPalette palette)
        {
            PixelShader = PseudoColorShaderCompiler.GetPixelShader();
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(PaletteLutProperty);
            UpdateShaderValue(PaletteIndexProperty);
            PaletteLut = CreatePaletteLutBrush(palette);
            PaletteIndex = (double)palette;
        }

        public Brush Input
        {
            get => (Brush)GetValue(InputProperty);
            set => SetValue(InputProperty, value);
        }

        public Brush PaletteLut
        {
            get => (Brush)GetValue(PaletteLutProperty);
            set => SetValue(PaletteLutProperty, value);
        }

        public double PaletteIndex
        {
            get => (double)GetValue(PaletteIndexProperty);
            set => SetValue(PaletteIndexProperty, value);
        }

        /// <summary>
        /// 把共用的调色板表包成采样输入。
        /// Chinese: 用 ImageBrush 直接映射整张 256×1 位图（不设 Stretch 偏移），避免任何额外的坐标变换影响取样位置。
        /// English: Wraps the shared table in an ImageBrush with a straight 1:1 mapping so sampling lands on the intended texel.
        /// </summary>
        private static Brush CreatePaletteLutBrush(PseudoColorPalette palette)
        {
            var brush = new ImageBrush(ImageViewerDisplaySourceService.CreatePaletteLutBitmap(palette))
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 256, 1),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 256, 1)
            };

            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }
    }
}
