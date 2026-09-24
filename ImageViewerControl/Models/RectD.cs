namespace ImageViewer.Models
{
    /// <summary>
    /// 与 UI 框架无关的矩形（左上角坐标 + 宽高）。
    /// Chinese: 模型层的矩形值类型，替代 System.Windows.Rect；与 WPF 的互转集中在
    /// ImageViewer.Utils.RoiGeometryWpfExtensions。
    /// English: UI-framework-agnostic rectangle used by the model layer, replacing System.Windows.Rect.
    /// Conversions to and from WPF live in ImageViewer.Utils.RoiGeometryWpfExtensions.
    /// </summary>
    public readonly record struct RectD(double X, double Y, double Width, double Height)
    {
        public override string ToString() => $"({X}, {Y}, {Width}, {Height})";
    }
}