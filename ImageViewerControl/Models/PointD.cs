using System;

namespace ImageViewer.Models
{
    /// <summary>
    /// 与 UI 框架无关的二维坐标点。
    /// Chinese: 模型层的坐标值类型，替代 System.Windows.Point，使模型层不引用 WPF；
    /// 与 WPF Point 的互转集中在 ImageViewer.Utils.RoiGeometryWpfExtensions。
    /// English: UI-framework-agnostic 2D point used by the model layer, replacing System.Windows.Point.
    /// Conversions to and from WPF live in ImageViewer.Utils.RoiGeometryWpfExtensions.
    /// </summary>
    public readonly record struct PointD(double X, double Y)
    {
        /// <summary>
        /// 到另一点的欧氏距离。
        /// Chinese: 供模型内部的尺寸/间距估算使用，避免模型层依赖 WPF 侧的几何工具。
        /// English: Euclidean distance to another point, kept inside the model layer.
        /// </summary>
        public double DistanceTo(PointD other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>两点相减得到位移向量。</summary>
        public static VectorD operator -(PointD left, PointD right)
        {
            return new VectorD(left.X - right.X, left.Y - right.Y);
        }

        /// <summary>点按向量平移。</summary>
        public static PointD operator +(PointD point, VectorD vector)
        {
            return new PointD(point.X + vector.X, point.Y + vector.Y);
        }

        public override string ToString() => $"({X}, {Y})";
    }
}