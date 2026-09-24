using System;
using System.Windows;
using ImageViewer.Models;
using ImageViewer.Rendering;

namespace ImageViewer.Drawing
{
    /// <summary>
    /// 拖拽式绘制会话：按下起点 → 移动改几何 → 抬起校验并提交。
    /// Chinese: 覆盖矩形、椭圆、圆、圆环、卡尺与直线测量等"按住拖出尺寸"的工具。
    /// English: Drag-to-size draw session: press an origin, drag to resize, release to validate and
    /// commit. Covers rectangle/ellipse/circle/ring/caliper/line-measure style tools.
    /// </summary>
    /// <remarks>
    /// 多段拖拽（<paramref name="finalStage"/> &gt; 0）用于圆环：第一段抬起不提交，只推进阶段。
    /// Chinese: 阶段推进时按既有行为释放鼠标捕获，因此后续阶段的移动不再改变几何。
    /// English: Multi-stage drag is used by the ring: releasing a non-final stage advances the stage
    /// instead of committing. Stage advancement releases mouse capture, matching the existing behavior,
    /// so later stages no longer follow the pointer.
    /// </remarks>
    public sealed class DragDrawSession<T> : IRoiDrawSession
        where T : RoiBase
    {
        private readonly Func<IRoiDrawHost, Point, T> _createInitial;
        private readonly Action<IRoiDrawHost, T, Point, Point> _applyDrag;
        private readonly Func<IRoiDrawHost, T, bool>? _shouldCommit;
        private readonly Action<IRoiDrawHost, T>? _beforeCommit;
        private readonly int _finalStage;
        private readonly Func<IRoiDrawHost, T, bool>? _shouldAdvanceStage;
        private readonly Action<IRoiDrawHost, T, int>? _onStageAdvanced;
        private readonly bool _cancelOnRightButton;

        private T? _roi;
        private Point _origin;
        private int _stage;
        private bool _captureReleased;

        public DragDrawSession(
            Func<IRoiDrawHost, Point, T> createInitial,
            Action<IRoiDrawHost, T, Point, Point> applyDrag,
            Func<IRoiDrawHost, T, bool>? shouldCommit = null,
            Action<IRoiDrawHost, T>? beforeCommit = null,
            int finalStage = 0,
            Func<IRoiDrawHost, T, bool>? shouldAdvanceStage = null,
            Action<IRoiDrawHost, T, int>? onStageAdvanced = null,
            bool cancelOnRightButton = false)
        {
            _createInitial = createInitial ?? throw new ArgumentNullException(nameof(createInitial));
            _applyDrag = applyDrag ?? throw new ArgumentNullException(nameof(applyDrag));
            _shouldCommit = shouldCommit;
            _beforeCommit = beforeCommit;
            _finalStage = finalStage;
            _shouldAdvanceStage = shouldAdvanceStage;
            _onStageAdvanced = onStageAdvanced;
            _cancelOnRightButton = cancelOnRightButton;
        }

        public RoiBase? ActiveRoi => _roi;

        public void OnPointerDown(IRoiDrawHost host, DrawPointerEvent e)
        {
            if (e.Handled)
            {
                return;
            }

            if (_cancelOnRightButton && e.IsRightButtonPressed)
            {
                _roi = null;
                host.EndDraw();
                return;
            }

            if (!e.IsLeftButtonPressed || _roi != null)
            {
                return;
            }

            if (!host.TryCaptureMouse())
            {
                return;
            }

            _origin = host.SnapPoint(e.RawPosition);
            _roi = _createInitial(host, _origin);
            _stage = 0;
            _captureReleased = false;
        }

        public void OnPointerMove(IRoiDrawHost host, DrawPointerEvent e)
        {
            if (_roi is not T roi || _captureReleased)
            {
                return;
            }

            Point current = host.SnapPoint(e.RawPosition);
            _applyDrag(host, roi, _origin, current);
            host.InvalidateOverlay();
        }

        public void OnPointerUp(IRoiDrawHost host, DrawPointerEvent e)
        {
            if (_roi is not T roi)
            {
                // 没有进行中的 ROI 时也离开当前模式（例如按下未成功创建图形后的抬起）。
                host.EndDraw();
                return;
            }

            if (_stage < _finalStage)
            {
                AdvanceStage(host, roi);
                return;
            }

            if (_shouldCommit?.Invoke(host, roi) ?? true)
            {
                _beforeCommit?.Invoke(host, roi);
                host.Commit(roi);
            }

            _roi = null;
            host.EndDraw();
        }

        public void OnCaptureLost(IRoiDrawHost host)
        {
            if (_roi == null)
            {
                return;
            }

            _roi = null;
            host.EndDraw();
        }

        public void OnModeExiting(IRoiDrawHost host)
        {
            _roi = null;
            _captureReleased = false;
        }

        public void DrawOverlay(IRoiDrawHost host, RoiRenderContext context)
        {
            // 进行中的 ROI 由宿主经 IRoiRenderer 渲染，拖拽类无需额外预览。
        }

        private void AdvanceStage(IRoiDrawHost host, T roi)
        {
            if (!(_shouldAdvanceStage?.Invoke(host, roi) ?? true))
            {
                _roi = null;
                host.EndDraw();
                return;
            }

            _stage++;
            _onStageAdvanced?.Invoke(host, roi, _stage);

            // 阶段推进即释放捕获，与既有行为一致。
            _captureReleased = true;
            host.ReleaseMouseCapture();
            host.InvalidateOverlay();
        }
    }
}
