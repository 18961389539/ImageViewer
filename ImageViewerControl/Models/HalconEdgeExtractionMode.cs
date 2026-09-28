namespace ImageViewer.Models
{
    /// <summary>
    /// HALCON edges_sub_pix 风格的一维边缘响应模式。
    /// Chinese: 这些模式用于卡尺剖面的边缘响应计算；默认的 GaussianDerivative 保持现有结果。
    /// English: HALCON edges_sub_pix-inspired response filters for caliper profiles. GaussianDerivative
    /// is the compatibility default and preserves the existing response.
    /// </summary>
    public enum HalconEdgeExtractionMode
    {
        /// <summary>现有高斯导数响应，作为兼容默认值。</summary>
        GaussianDerivative,

        /// <summary>Deriche 风格的递归平滑 + 一阶导数。</summary>
        Deriche,

        /// <summary>Lanser1 风格的三点一阶导数。</summary>
        Lanser1,

        /// <summary>Lanser2 风格的五点一阶导数。</summary>
        Lanser2,

        /// <summary>Shen-Castan（mshen）风格的递归平滑响应。</summary>
        Mshen,

        /// <summary>Canny 风格的非极大值抑制与双阈值滞回。</summary>
        Canny,

        /// <summary>快速 Sobel 风格的三点响应。</summary>
        SobelFast,

        /// <summary>带更宽支持域的 Sobel 风格响应。</summary>
        Sobel
    }
}
