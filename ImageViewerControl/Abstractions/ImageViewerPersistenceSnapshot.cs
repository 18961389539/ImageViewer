using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Abstractions
{
    /// <summary>
    /// 保存会话 / 项目包时需要落盘的一整份状态。
    /// Chinese: 引用不可变（positional record，属性只读），并且 <see cref="RoiDocument"/> 是调用方在 UI 线程上
    /// 一次性构建出来的**脱离 UI 的纯数据图**——它不再引用 <c>ViewerState.AllRois</c> 这个活集合，也不再引用任何可变的
    /// <c>RoiBase</c>。因此拿到快照后，序列化可以安全地放到后台线程：UI 上继续拖拽/增删 ROI 不会让落盘内容出现半新半旧的
    /// 几何，也不会出现"枚举过程中集合被修改"。
    /// English: Reference-immutable, and <see cref="RoiDocument"/> is a detached plain-data graph built on the UI thread.
    /// It shares nothing with the live ROI collection, so callers may serialize it off the UI thread without racing the editor.
    /// Chinese: 注意 <see cref="RoiDocument"/> 本身是序列化契约类型（属性可写、条目是 List）：这里说的"不可变"是指引用不再
    /// 指向活对象，不是语言层面的 immutable。构建完成后不要再改写它。
    /// English: The document type is a mutable serialization contract; "immutable" here means the reference no longer points at
    /// live objects. Treat the built document as frozen.
    /// </summary>
    public sealed record ImageViewerPersistenceSnapshot(
        string? ImagePath,
        RoiDocument RoiDocument,
        double Scale,
        double TranslateX,
        double TranslateY,
        CameraCalibration? Calibration)
    {
    }
}
