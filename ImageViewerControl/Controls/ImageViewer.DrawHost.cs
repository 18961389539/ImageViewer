using System;
using System.Windows;
using System.Windows.Input;
using ImageViewer.Drawing;
using ImageViewer.Models;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        /// <summary>
        /// <see cref="IRoiDrawHost"/> 的控件侧实现。
        /// Chinese: 把绘制会话需要的能力转发给 ImageViewer，复用既有的命中测试、吸附、捕获与提交逻辑。
        /// English: Control-side implementation of <see cref="IRoiDrawHost"/>. Forwards the capabilities a
        /// draw session needs, reusing the existing hit-test, snapping, mouse-capture and commit logic.
        /// </summary>
        private sealed class RoiDrawHost : IRoiDrawHost
        {
            private readonly ImageViewer _owner;

            public RoiDrawHost(ImageViewer owner)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public ImageViewerViewModel ViewModel => _owner.ViewModel;

            public double Scale => _owner.Scale;

            public double MinimumDrawableSize => ImageViewer.MinimumDrawableSize;

            public double MinimumLineLength => ImageViewer.MinimumLineLength;

            public double HitTestTolerance => ImageViewer.HitTestTolerance;

            public Point SnapPoint(Point rawImagePoint) => _owner.SnapPoint(rawImagePoint);

            public RoiBase? HitTest(Point point) => _owner.HitTest(point);

            public bool TryCaptureMouse() => _owner.TryCaptureRootGridMouse();

            public void ReleaseMouseCapture() => _owner.ReleaseRootGridMouseIfCaptured();

            public void SetCursor(Cursor cursor) => _owner.rootGrid.Cursor = cursor;

            public void InvalidateOverlay() => _owner.DrawRois();

            public string? RequestTextInput(string message, string defaultValue)
                => _owner._dialogWorkflowService.ShowTextInput(message, defaultValue);

            public bool TryApplyAnalysis(RoiBase roi) => _owner.TryRefreshCaliperDetection(roi);

            public void Commit(RoiBase roi) => _owner.CommitRoi(roi);

            public void EndDraw()
            {
                _owner.ReleaseRootGridMouseIfCaptured();
                _owner.LeaveInteractionMode();
                _owner.DrawRois();
            }
        }
    }
}
