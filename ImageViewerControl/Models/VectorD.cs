using System;

namespace ImageViewer.Models
{
    /// <summary>
    /// 与 UI 框架无关的二维向量。
    /// Chinese: 模型层的方向/位移值类型，替代 System.Windows.Vector，使模型层不引用 WPF。
    /// English: UI-framework-agnostic 2D vector used by the model layer, replacing System.Windows.Vector.
    /// </summary>
    public readonly record struct VectorD(double X, double Y)
    {
        public double LengthSquared => X * X + Y * Y;

        public double Length => Math.Sqrt(LengthSquared);

        /// <summary>
        /// 返回单位向量。
        /// Chinese: 零向量（长度趋近 0）原样返回，避免除零产生 NaN。
        /// English: Returns the unit vector, or the original value for a zero-length vector.
        /// </summary>
        public VectorD Normalized()
        {
            double length = Length;
            return length <= 1e-12 ? this : new VectorD(X / length, Y / length);
        }

        public override string ToString() => $"({X}, {Y})";
    }
}