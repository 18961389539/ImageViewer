using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageViewer.Models;
using ImageViewer.Rendering;
using ImageViewer.Utils;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private void UpdatePixelGrid()
        {
            if (!ShowPixelGrid || Scale < PixelGridScaleThreshold || !ImageViewerImageSourceUtilities.TryGetSourceImageSize(ImageSource, out Size imageSize))
            {
                if (_pixelGridPath != null)
                {
                    _pixelGridPath.Visibility = Visibility.Collapsed;
                }

                return;
            }

            _pixelGridPath ??= new Path
            {
                Stroke = Brushes.Gray,
                IsHitTestVisible = false
            };

            if (!pixelGridCanvas.Children.Contains(_pixelGridPath))
            {
                pixelGridCanvas.Children.Clear();
                pixelGridCanvas.Children.Add(_pixelGridPath);
            }

            double width = imageSize.Width;
            double height = imageSize.Height;
            bool needsRebuild = !ReferenceEquals(_cachedPixelGridImageSource, ImageSource)
                || _cachedPixelGridWidth != width
                || _cachedPixelGridHeight != height
                || _pixelGridPath.Data == null;

            if (needsRebuild)
            {
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    for (int x = 0; x <= (int)Math.Ceiling(width); x++)
                    {
                        ctx.BeginFigure(new Point(x, 0), false, false);
                        ctx.LineTo(new Point(x, height), true, false);
                    }

                    for (int y = 0; y <= (int)Math.Ceiling(height); y++)
                    {
                        ctx.BeginFigure(new Point(0, y), false, false);
                        ctx.LineTo(new Point(width, y), true, false);
                    }
                }

                geometry.Freeze();
                _pixelGridPath.Data = geometry;
                _cachedPixelGridImageSource = ImageSource;
                _cachedPixelGridWidth = width;
                _cachedPixelGridHeight = height;
            }

            _pixelGridPath.StrokeThickness = 1 / Scale;
            _pixelGridPath.Visibility = Visibility.Visible;
        }

        private void DrawRois(bool immediate = false, bool forceAnalysis = false)
        {
            RequestViewportOverlayRefresh(immediate);
            RequestAnalysisRefresh(forceAnalysis, immediate: immediate && forceAnalysis);
        }

        private void RefreshViewportOverlay()
        {
            var vm = ViewModel;
            ScreenOverlayCanvas.Children.Clear();
            DrawCommittedRois(vm);
            DrawSelectedRoi(vm);
            DrawActiveRois();
        }

        private void DrawSelectedRoiLayer()
        {
            DrawRois(immediate: true, forceAnalysis: true);
        }

        private void DrawCommittedRois(ImageViewerViewModel vm)
        {
            committedOverlayCanvas.Children.Clear();
            var context = CreateRoiRenderContext(committedOverlayCanvas);
            RoiRenderer.RenderCommitted(vm.AllRois, context, vm.SelectedRoi);
        }

        private void DrawSelectedRoi(ImageViewerViewModel vm)
        {
            selectionOverlayCanvas.Children.Clear();
            var context = CreateRoiRenderContext(selectionOverlayCanvas);
            RoiRenderer.RenderSelected(vm.SelectedRoi, context);
        }

        private void DrawActiveRois()
        {
            activeOverlayCanvas.Children.Clear();
            var context = CreateRoiRenderContext(activeOverlayCanvas);

            if (ActiveDrawRoi is RoiBase activeRoi)
            {
                RoiRenderer.RenderActive([activeRoi], context);
            }

            // 插件化绘制会话自行绘制"画到一半"的预览（橡皮筋、闭合虚线、实时数值文本）。
            _activeDrawSession?.DrawOverlay(DrawHost, context);
        }
    }
}
