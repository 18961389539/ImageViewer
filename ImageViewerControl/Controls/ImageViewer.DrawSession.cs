using System;
using System.Windows;
using System.Windows.Input;
using ImageViewer.Drawing;
using ImageViewer.Models;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private IRoiDrawSession? _activeDrawSession;
        private IRoiDrawHost? _drawHost;

        private IRoiDrawHost DrawHost => _drawHost ??= new RoiDrawHost(this);

        /// <summary>
        /// 当前绘制会话中正在创建的 ROI；无会话或无内容时为 null。
        /// Chinese: 供活动图层渲染与实时剖面解析使用。
        /// English: The ROI currently being created by the active draw session, or null.
        /// </summary>
        private RoiBase? ActiveDrawRoi => _activeDrawSession?.ActiveRoi;

        private bool IsToolInteractionActive => _activeDrawSession != null;

        /// <summary>
        /// 进入由插件提供的绘制模式。
        /// Chinese: 这是新增可绘制 ROI 类型的公开入口，替代为每种工具硬编码一个 StartXxxMode()。
        /// English: Enters a plugin-supplied draw mode. This is the public extension point that replaces
        /// hard-coding one StartXxxMode() per tool.
        /// </summary>
        public void StartDraw(IRoiDrawController controller)
        {
            ArgumentNullException.ThrowIfNull(controller);

            ExitCurrentMode();

            _activeDrawSession = controller.CreateSession();
            rootGrid.Cursor = controller.Cursor;
            rootGrid.MouseDown += OnToolMouseDown;
            rootGrid.MouseMove += OnToolMouseMove;
            rootGrid.MouseUp += OnToolMouseUp;
        }

        /// <summary>
        /// 退出当前绘制模式，并给会话一次提交或丢弃的机会。
        /// Chinese: 语义由会话自决——多边形在此提交，折线在此丢弃。
        /// English: Leaves the current draw mode, giving the session a chance to commit or discard.
        /// Whether to commit is up to the session (a polygon commits, a polyline discards).
        /// </summary>
        public void ExitCurrentMode()
        {
            // 先取出并置空会话，避免会话在 OnModeExiting 中提交/结束时重入本方法。
            IRoiDrawSession? drawSession = _activeDrawSession;
            _activeDrawSession = null;
            drawSession?.OnModeExiting(DrawHost);

            ReleaseRootGridMouseIfCaptured();
            LeaveInteractionMode();
            DrawRois();
        }

        /// <summary>
        /// 清除绘制会话并释放捕获，但不重绘。
        /// Chinese: 由 StartDraw 与 IRoiDrawHost.EndDraw 调用；调用前会话已被置空以避免重入。
        /// English: Clears the session and releases capture without redrawing.
        /// </summary>
        private void EndActiveDraw()
        {
            _activeDrawSession = null;
            ReleaseRootGridMouseIfCaptured();
            LeaveInteractionMode();
        }

        private void LeaveInteractionMode()
        {
            rootGrid.Cursor = Cursors.Arrow;
            rootGrid.MouseDown -= OnToolMouseDown;
            rootGrid.MouseMove -= OnToolMouseMove;
            rootGrid.MouseUp -= OnToolMouseUp;
        }

        private void OnToolMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_activeDrawSession is IRoiDrawSession drawSession)
            {
                DrawPointerEvent drawArgs = CreateDrawPointerArgs(e);
                drawSession.OnPointerDown(DrawHost, drawArgs);
                e.Handled = drawArgs.Handled;
            }
        }

        private void OnToolMouseMove(object sender, MouseEventArgs e)
        {
            if (_activeDrawSession is IRoiDrawSession drawSession)
            {
                DrawPointerEvent drawArgs = CreateDrawPointerArgs(e);
                drawSession.OnPointerMove(DrawHost, drawArgs);
                e.Handled = drawArgs.Handled;
            }
        }

        private void OnToolMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_activeDrawSession is IRoiDrawSession drawSession)
            {
                DrawPointerEvent drawArgs = CreateDrawPointerArgs(e);
                drawSession.OnPointerUp(DrawHost, drawArgs);
                e.Handled = drawArgs.Handled;
            }
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            _interactionController.HandleLostMouseCapture();

            _activeDrawSession?.OnCaptureLost(DrawHost);
        }

        private DrawPointerEvent CreateDrawPointerArgs(MouseEventArgs e)
        {
            MouseButtonEventArgs? buttonArgs = e as MouseButtonEventArgs;

            return new DrawPointerEvent(
                e.GetPosition(imageContainer),
                e.LeftButton == MouseButtonState.Pressed,
                e.RightButton == MouseButtonState.Pressed,
                buttonArgs?.ClickCount ?? 0);
        }

        /// <summary>
        /// 解析"正在绘制中的直线测量 ROI"，供实时剖面使用。
        /// Chinese: 绘制会话在进行中时其 ActiveRoi 即为目标；其余情况回退到选中项。
        /// English: While a draw session is active its ActiveRoi is the target; otherwise the resolver
        /// falls back to the selected ROI.
        /// </summary>
        private LineMeasureRoi? ResolveInProgressProfileLine()
        {
            return ActiveDrawRoi as LineMeasureRoi;
        }
    }
}
