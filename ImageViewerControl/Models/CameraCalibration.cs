using System;

namespace ImageViewer.Models
{
    /// <summary>
    /// 相机径向畸变标定参数（归一化多项式模型）。
    /// Chinese: 畸变模型为 r_distorted = r_ideal · (1 + K1·r² + K2·r⁴)，其中 r 为归一化半径
    /// （像点到主点像素距离 / NormalizationRadius）。局部放大倍率为 1 + K1·r² + K2·r⁴，
    /// UndistortScaleFactor 返回其倒数，用于将畸变像素长度换算为理想（去畸变）像素长度。
    /// English: Radial distortion parameters using a normalized polynomial model.
    /// </summary>
    public sealed class CameraCalibration
    {
        private const double MinScaleDenominator = 0.2;

        /// <summary>
        /// 主点 X（像素）。Chinese: 默认取图像中心。English: Principal PointD X in pixels.
        /// </summary>
        public double PrincipalX { get; set; }

        /// <summary>
        /// 主点 Y（像素）。Chinese: 默认取图像中心。English: Principal PointD Y in pixels.
        /// </summary>
        public double PrincipalY { get; set; }

        /// <summary>
        /// 一阶径向畸变系数（基于归一化半径）。
        /// English: First-order radial distortion coefficient (normalized radius).
        /// </summary>
        public double K1 { get; set; }

        /// <summary>
        /// 二阶径向畸变系数（基于归一化半径）。
        /// English: Second-order radial distortion coefficient (normalized radius).
        /// </summary>
        public double K2 { get; set; }

        /// <summary>
        /// 归一化参考半径（像素）。Chinese: 小于等于 0 时视为未配置，不做畸变校正。
        /// English: Normalization reference radius in pixels; <= 0 disables correction.
        /// </summary>
        public double NormalizationRadius { get; set; } = 1.0;

        /// <summary>
        /// 是否配置了畸变系数。
        /// English: Whether any distortion coefficient is configured.
        /// </summary>
        public bool IsEnabled => Math.Abs(K1) > 1e-12 || Math.Abs(K2) > 1e-12;

        /// <summary>
        /// 归一化半径平方 r²。
        /// English: Squared normalized radius at an image PointD.
        /// </summary>
        public double NormalizedRadiusSquared(PointD imagePoint)
        {
            if (NormalizationRadius <= 0)
            {
                return 0;
            }

            double dx = imagePoint.X - PrincipalX;
            double dy = imagePoint.Y - PrincipalY;
            return (dx * dx + dy * dy) / (NormalizationRadius * NormalizationRadius);
        }

        /// <summary>
        /// 去畸变缩放因子：将局部畸变像素长度换算为理想像素长度的乘数。
        /// Chinese: 取畸变放大倍率的倒数，并对极端参数做截断保护。
        /// English: Factor converting local distorted pixels to ideal pixels.
        /// </summary>
        public double UndistortScaleFactor(PointD imagePoint)
        {
            if (!IsEnabled)
            {
                return 1.0;
            }

            double r2 = NormalizedRadiusSquared(imagePoint);
            double denominator = 1.0 + K1 * r2 + K2 * r2 * r2;
            if (denominator < MinScaleDenominator)
            {
                denominator = MinScaleDenominator;
            }

            return 1.0 / denominator;
        }

        /// <summary>
        /// 对像素长度应用畸变校正（以线段中点为校正参考点）。
        /// Chinese: 返回校正后的等效像素长度，后端换算物理长度时再乘像素尺寸 PixelSize。
        /// English: Corrects a pixel-length measured at the given midpoint.
        /// </summary>
        public double CorrectPixelLength(double pixelLength, PointD midPoint)
        {
            return pixelLength * UndistortScaleFactor(midPoint);
        }

        /// <summary>
        /// 按图像尺寸自动设置主点（图像中心）与归一化半径（短边一半）并创建标定参数。
        /// English: Creates calibration with principal PointD and normalization radius inferred from image size.
        /// </summary>
        public static CameraCalibration CreateForImage(double k1, double k2, double imageWidth, double imageHeight)
        {
            return new CameraCalibration
            {
                K1 = k1,
                K2 = k2,
                PrincipalX = imageWidth > 0 ? imageWidth / 2.0 : 0,
                PrincipalY = imageHeight > 0 ? imageHeight / 2.0 : 0,
                NormalizationRadius = imageWidth > 0 && imageHeight > 0 ? Math.Min(imageWidth, imageHeight) / 2.0 : 1.0
            };
        }
    }
}