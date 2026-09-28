namespace ImageViewer.Models
{
    /// <summary>
    /// HALCON fit_line_contour_xld 风格的直线拟合模式。
    /// Chinese: 这些模式控制边缘点到直线的稳健权重；默认 Tukey 保持现有行为。
    /// English: HALCON fit_line_contour_xld-inspired robust weighting modes for line fits.
    /// Tukey is the compatibility default and preserves the current behavior.
    /// </summary>
    public enum HalconLineFitMode
    {
        /// <summary>加权回归，不做残差重加权。</summary>
        Regression,

        /// <summary>Huber 权重，保留较远点的有限影响。</summary>
        Huber,

        /// <summary>Tukey 双权重，较远点逐渐降为零。</summary>
        Tukey,

        /// <summary>硬剔除超出 MAD 阈值的点。</summary>
        Drop,

        /// <summary>高斯残差权重，连续衰减离群点影响。</summary>
        Gauss
    }
}
