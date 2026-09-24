using System;
using ImageViewer.Common;
using ImageViewer.Localization;

namespace ImageViewer.Models
{
    /// <summary>
    /// ROI 基类
    /// Chinese: 表示所有 ROI（感兴趣区域）对象的基类，包含共享属性如标签、边框颜色、粗细、可见性等。
    /// English: Base class for all ROI (Region of Interest) objects. Provides common properties such as
    /// Label, StrokeColor, StrokeThickness, visibility and locking.
    /// </summary>
    public abstract class RoiBase : BaseViewModel
    {
        private string _label = string.Empty;
        private RoiColor _strokeColor = RoiColors.Cyan;
        private double _strokeThickness = 2.0;
        private bool _isSelected;
        private bool _isVisible = true;
        private bool _isLocked;
        private MeasurementTolerance? _tolerance;

        public string Label
        {
            get => _label;
            set
            {
                if (SetProperty(ref _label, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public virtual string RoiTypeName => GetType().Name;

        public string DisplayTypeName => RoiDisplayNameLocalizer.GetDisplayName(this);

        public string DisplayName => string.IsNullOrWhiteSpace(Label) ? DisplayTypeName : $"{DisplayTypeName}: {Label}";

        /// <summary>
        /// 复制共享的视觉状态（颜色、粗细、可见性、锁定）。
        /// Chinese: 不复制选中态——选中是瞬时 UI 状态，不属于可快照的视觉状态。
        /// English: Copies the shared visual state. Selection is deliberately excluded: it is transient
        /// UI state, not part of a snapshot.
        /// </summary>
        public void CopyVisualStateFrom(RoiBase source)
        {
            ArgumentNullException.ThrowIfNull(source);

            StrokeColor = source.StrokeColor;
            StrokeThickness = source.StrokeThickness;
            IsVisible = source.IsVisible;
            IsLocked = source.IsLocked;
        }

        protected void ApplyCommonState(RoiBase source)
        {
            ArgumentNullException.ThrowIfNull(source);

            Label = source.Label;
            Tolerance = source.Tolerance?.Clone();
            CopyVisualStateFrom(source);
        }

        public RoiColor StrokeColor
        {
            get => _strokeColor;
            set => SetProperty(ref _strokeColor, value);
        }

        public double StrokeThickness
        {
            get => _strokeThickness;
            set => SetProperty(ref _strokeThickness, value);
        }

        /// <summary>
        /// 该 ROI 当前是否被选中。
        /// Chinese: 由 ViewModel 维护；仅用于外部读取，绘制选中态由渲染层独立处理。
        /// English: Whether this ROI is currently selected. Maintained by the ViewModel; rendering of the
        /// selection is handled independently by the render layer.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public bool IsLocked
        {
            get => _isLocked;
            set => SetProperty(ref _isLocked, value);
        }

        /// <summary>
        /// 测量公差判定参数（标称值 + 上下公差）；非测量 ROI 不使用。
        /// Chinese: 仅在信息面板展示合格/超差判定，不影响绘制与检测。
        /// English: Optional pass-fail tolerance applied to the primary measurement of this ROI.
        /// </summary>
        public MeasurementTolerance? Tolerance
        {
            get => _tolerance;
            set => SetProperty(ref _tolerance, value);
        }

        public abstract RoiBase Clone();
        public abstract void ApplyFrom(RoiBase source);
        /// <summary>
        /// 克隆当前 ROI 的方法（深拷贝或值拷贝依具体实现而定）。
        /// Chinese: 返回一个表示当前对象状态的副本，供撤销/重做与状态保存使用。
        /// English: Creates and returns a copy of the ROI instance for undo/redo or state snapshots.
        /// </summary>
        /// <returns>返回 RoiBase 的副本 / A copy of the RoiBase instance.</returns>
    }
}
