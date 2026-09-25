using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ImageViewer.Models;
using ImageViewer.Rendering;

namespace ImageViewer.Drawing
{
    /// <summary>
    /// 自动边缘吸附点的交互会话：移动时显示候选位置，点击时提交同一候选结果。
    /// </summary>
    public sealed class EdgeSnapDrawSession : IRoiDrawSession
    {
        private readonly Func<Point, double, double, RoiBase?> _createAt;
        private Point _previewPoint;
        private double _previewScore;
        private double _previewConfidence;
        private bool _hasPreview;

        public EdgeSnapDrawSession(Func<Point, double, double, RoiBase?> createAt)
        {
            _createAt = createAt ?? throw new ArgumentNullException(nameof(createAt));
        }

        public RoiBase? ActiveRoi => null;

        public void OnPointerDown(IRoiDrawHost host, DrawPointerEvent e)
        {
            if (e.Handled || !e.IsLeftButtonPressed)
            {
                return;
            }

            if (!TryUpdateCandidate(host, e.RawPosition))
            {
                return;
            }

            RoiBase? roi = _createAt(_previewPoint, _previewScore, _previewConfidence);
            if (roi == null)
            {
                return;
            }

            host.Commit(roi);
            host.EndDraw();
            e.Handled = true;
        }

        public void OnPointerMove(IRoiDrawHost host, DrawPointerEvent e)
        {
            if (e.Handled)
            {
                return;
            }

            TryUpdateCandidate(host, e.RawPosition);
        }

        public void OnPointerUp(IRoiDrawHost host, DrawPointerEvent e)
        {
        }

        public void OnCaptureLost(IRoiDrawHost host)
        {
            _hasPreview = false;
        }

        public void OnModeExiting(IRoiDrawHost host)
        {
            _hasPreview = false;
        }

        public void DrawOverlay(IRoiDrawHost host, RoiRenderContext context)
        {
            if (!_hasPreview)
            {
                return;
            }

            Brush brush = _previewConfidence >= 0.35 ? Brushes.LimeGreen : Brushes.Gold;
            double arm = 7 / context.Scale;
            context.DrawLineSegment(
                new Point(_previewPoint.X - arm, _previewPoint.Y),
                new Point(_previewPoint.X + arm, _previewPoint.Y),
                brush,
                1.5 / context.Scale);
            context.DrawLineSegment(
                new Point(_previewPoint.X, _previewPoint.Y - arm),
                new Point(_previewPoint.X, _previewPoint.Y + arm),
                brush,
                1.5 / context.Scale);
            context.DrawDot(_previewPoint, 3 / context.Scale, brush);
            context.DrawInfoText(
                $"Edge {_previewConfidence * 100:F0}%",
                new Point(_previewPoint.X + 8 / context.Scale, _previewPoint.Y - 8 / context.Scale),
                brush,
                true);
        }

        private bool TryUpdateCandidate(IRoiDrawHost host, Point rawPosition)
        {
            bool found = host.TrySnapPointToEdge(rawPosition, out Point snapped, out double score, out double confidence);
            if (found)
            {
                _previewPoint = snapped;
                _previewScore = score;
                _previewConfidence = confidence;
                _hasPreview = true;
                host.SetCursor(Cursors.Hand);
            }
            else
            {
                _hasPreview = false;
                host.SetCursor(Cursors.Cross);
            }

            host.InvalidateOverlay();
            return found;
        }
    }
}
