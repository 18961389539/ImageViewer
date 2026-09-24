using System.Collections.Generic;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Abstractions
{
    /// <summary>
    /// 保存会话 / 项目包时需要落盘的一整份状态。
    /// Chinese: 把原先在多个方法签名与调用点里逐参数透传的散装状态收敛成一个不可变载荷。
    /// English: A single immutable payload carrying everything a session or project package needs to persist,
    /// replacing the previously duplicated long parameter lists.
    /// </summary>
    public sealed record ImageViewerPersistenceSnapshot(
        string? ImagePath,
        IReadOnlyList<RoiBase> Rois,
        double PixelSize,
        string? PhysicalUnit,
        double Scale,
        double TranslateX,
        double TranslateY,
        CameraCalibration? Calibration)
    {
        /// <summary>
        /// 加载时未能识别的 ROI 载荷，保存时原样回写。
        /// Chinese: 缺插件导致无法还原的标注不会被"保存即抹掉"，重新获得插件后仍可正常打开。
        /// English: ROI payloads that could not be resolved on load, written back verbatim so a save never erases them.
        /// </summary>
        public IReadOnlyList<RoiPersistenceData> UnresolvedRois { get; init; } = [];
    }
}
