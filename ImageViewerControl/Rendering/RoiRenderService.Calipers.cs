using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Rendering
{
    public sealed partial class RoiRenderService
    {
        private sealed class CircularCaliperMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is CircularCaliperMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var caliper = (CircularCaliperMeasureRoi)roi;
                if (!caliper.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, caliper.StrokeColor);
                Brush barBrush = Brushes.Gold;
                Brush invalidBrush = Brushes.Gray;
                Brush rejectedBrush = Brushes.IndianRed;
                bool showDetails = isSelected || ReferenceEquals(strokeOverride, Brushes.Orange);

                IEnumerable<LineSegmentOverlay> previewSegments = caliper.RegionSegments.Length > 0
                    ? caliper.RegionSegments
                    : CaliperOverlayGeometryHelper.BuildCircularCaliperRegionSegments(caliper);
                IEnumerable<LineSegmentOverlay> previewBars = caliper.CaliperBars.Length > 0
                    ? caliper.CaliperBars
                    : CaliperOverlayGeometryHelper.BuildCircularCaliperBars(caliper);

                if (showDetails)
                {
                    foreach (LineSegmentOverlay segment in previewSegments)
                    {
                        context.DrawLineSegment(segment.Start.ToWpfPoint(), segment.End.ToWpfPoint(), barBrush, 1.1 / context.Scale, new DoubleCollection { 3, 2 }, opacity: 0.32);
                    }

                    foreach (LineSegmentOverlay segment in previewBars)
                    {
                        context.DrawLineSegment(segment.Start.ToWpfPoint(), segment.End.ToWpfPoint(), barBrush, 0.8 / context.Scale, opacity: 0.32);
                    }

                    SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.InvalidCaliperMarkers, invalidBrush, opacity: 0.45);
                    SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.RejectedEdgeMarkers, rejectedBrush, opacity: 0.55);
                    SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.EdgeMarkers, Brushes.Cyan, opacity: 0.6);
                    SingleEdgeCaliperRenderHelper.DrawScoreOverlays(context, caliper.ScoreOverlays);
                    SingleEdgeCaliperRenderHelper.DrawLegend(context, new Point(caliper.Center.X + caliper.Radius / 2, caliper.Center.Y - caliper.Radius / 2), invalidBrush, rejectedBrush, context.ShowCaliperScores);
                }

                context.DrawEllipseOutline(caliper.Center.ToWpfPoint(), caliper.Radius, caliper.Radius, 0, brush, (isSelected ? 3 : caliper.StrokeThickness) / context.Scale);

                if (isSelected)
                {
                    StandardRoiLayoutHelper.DrawCircleHandles(context, caliper.Center.ToWpfPoint(), caliper.Radius);
                }

                if (showDetails)
                {
                    string info = SingleEdgeCaliperRenderHelper.BuildSummaryText(caliper, $"半径：{context.FormatLength(caliper.Radius)}", "圆形边缘测量", context.ShowCaliperScores);
                    context.DrawInfoText(info, StandardRoiLayoutHelper.GetTopInfoAnchor(caliper.Center.ToWpfPoint(), caliper.Radius, context), Brushes.White, true);
                }
            }
        }

        private sealed class ArcCaliperMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is ArcCaliperMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var caliper = (ArcCaliperMeasureRoi)roi;
                if (!caliper.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, caliper.StrokeColor);
                Brush barBrush = Brushes.Gold;
                Brush invalidBrush = Brushes.Gray;
                Brush rejectedBrush = Brushes.IndianRed;
                bool showDetails = isSelected || ReferenceEquals(strokeOverride, Brushes.Orange);

                IEnumerable<LineSegmentOverlay> previewSegments = caliper.RegionSegments.Length > 0
                    ? caliper.RegionSegments
                    : CaliperOverlayGeometryHelper.BuildArcCaliperRegionSegments(caliper);
                IEnumerable<LineSegmentOverlay> previewBars = caliper.CaliperBars.Length > 0
                    ? caliper.CaliperBars
                    : CaliperOverlayGeometryHelper.BuildArcCaliperBars(caliper);

                if (showDetails)
                {
                    foreach (LineSegmentOverlay segment in previewSegments)
                    {
                        context.DrawLineSegment(segment.Start.ToWpfPoint(), segment.End.ToWpfPoint(), barBrush, 1.1 / context.Scale, new DoubleCollection { 3, 2 }, opacity: 0.32);
                    }

                    foreach (LineSegmentOverlay segment in previewBars)
                    {
                        context.DrawLineSegment(segment.Start.ToWpfPoint(), segment.End.ToWpfPoint(), barBrush, 0.8 / context.Scale, opacity: 0.32);
                    }

                    SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.InvalidCaliperMarkers, invalidBrush, opacity: 0.45);
                    SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.RejectedEdgeMarkers, rejectedBrush, opacity: 0.55);
                    SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.EdgeMarkers, Brushes.Cyan, opacity: 0.6);
                    SingleEdgeCaliperRenderHelper.DrawScoreOverlays(context, caliper.ScoreOverlays);
                }

                context.DrawArc(caliper.Center.ToWpfPoint(), caliper.Radius, caliper.StartAngle, caliper.SweepAngle, brush, (isSelected ? 3 : caliper.StrokeThickness) / context.Scale);
                DrawArcEndpoints(context, caliper, brush, (isSelected ? 3 : caliper.StrokeThickness) / context.Scale);

                if (isSelected)
                {
                    StandardRoiLayoutHelper.DrawCircleHandles(context, caliper.Center.ToWpfPoint(), caliper.Radius);
                }

                if (showDetails)
                {
                    string info = SingleEdgeCaliperRenderHelper.BuildSummaryText(caliper, $"半径：{context.FormatLength(caliper.Radius)} 弧角：{caliper.SweepAngle:F0}°", "弧形边缘测量", context.ShowCaliperScores);
                    context.DrawInfoText(info, StandardRoiLayoutHelper.GetTopInfoAnchor(caliper.Center.ToWpfPoint(), caliper.Radius, context), Brushes.White, true);
                }
            }

            private static void DrawArcEndpoints(RoiRenderContext context, ArcCaliperMeasureRoi caliper, Brush brush, double thickness)
            {
                double startRadians = caliper.StartAngle * Math.PI / 180.0;
                double endRadians = (caliper.StartAngle + caliper.SweepAngle) * Math.PI / 180.0;
                Point start = new(caliper.Center.X + Math.Cos(startRadians) * caliper.Radius, caliper.Center.Y + Math.Sin(startRadians) * caliper.Radius);
                Point end = new(caliper.Center.X + Math.Cos(endRadians) * caliper.Radius, caliper.Center.Y + Math.Sin(endRadians) * caliper.Radius);
                context.DrawLineSegment(caliper.Center.ToWpfPoint(), start, brush, thickness * 0.6);
                context.DrawLineSegment(caliper.Center.ToWpfPoint(), end, brush, thickness * 0.6);
            }
        }

        private sealed class LineCaliperMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is LineCaliperMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var line = (LineCaliperMeasureRoi)roi;
                if (!line.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, line.StrokeColor);
                Brush edgeBrush = Brushes.Cyan;
                Brush barBrush = Brushes.Gold;
                Brush invalidBrush = Brushes.Gray;
                Brush rejectedBrush = Brushes.IndianRed;
                bool showDetails = isSelected || ReferenceEquals(strokeOverride, Brushes.Orange);

                LineSegmentOverlay[] previewSegments = line.RegionSegments.Length > 0
                    ? line.RegionSegments
                    : CaliperOverlayGeometryHelper.BuildLineCaliperRegionSegments(line);
                LineSegmentOverlay[] previewBars = line.CaliperBars.Length > 0
                    ? line.CaliperBars
                    : CaliperOverlayGeometryHelper.BuildLineCaliperBars(line);

                if (showDetails)
                {
                    foreach (LineSegmentOverlay segment in previewSegments)
                    {
                        context.DrawLineSegment(segment.Start.ToWpfPoint(), segment.End.ToWpfPoint(), barBrush, 1.2 / context.Scale, new DoubleCollection { 3, 2 }, opacity: 0.32);
                    }

                    foreach (LineSegmentOverlay segment in previewBars)
                    {
                        context.DrawLineSegment(segment.Start.ToWpfPoint(), segment.End.ToWpfPoint(), barBrush, 0.8 / context.Scale, opacity: 0.32);
                    }

                    SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, line.InvalidCaliperMarkers, invalidBrush, opacity: 0.45);
                    SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, line.RejectedEdgeMarkers, rejectedBrush, opacity: 0.55);
                    SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, line.EdgeMarkers, edgeBrush, opacity: 0.6);
                    SingleEdgeCaliperRenderHelper.DrawScoreOverlays(context, line.ScoreOverlays);
                    SingleEdgeCaliperRenderHelper.DrawLegend(context, new Point((line.P1.X + line.P2.X) / 2 + 10 / context.Scale, (line.P1.Y + line.P2.Y) / 2 - 28 / context.Scale), invalidBrush, rejectedBrush, context.ShowCaliperScores);
                }

                context.DrawLineSegment(
                    line.P1.ToWpfPoint(),
                    line.P2.ToWpfPoint(),
                    brush,
                    (isSelected ? 3 : line.StrokeThickness) / context.Scale,
                    snapToDevicePixels: false);

                if (line.HasDetectedLine && (line.DetectedP1 != default || line.DetectedP2 != default))
                {
                    context.DrawLineSegment(
                        line.DetectedP1.ToWpfPoint(),
                        line.DetectedP2.ToWpfPoint(),
                        Brushes.LimeGreen,
                        2.4 / context.Scale,
                        snapToDevicePixels: false);
                }

                double handleSize = context.HandleSize / context.Scale;
                context.DrawHandle(line.P1.ToWpfPoint(), isSelected ? ResizeHandle.P1 : ResizeHandle.None, handleSize, false, brush);
                context.DrawHandle(line.P2.ToWpfPoint(), isSelected ? ResizeHandle.P2 : ResizeHandle.None, handleSize, false, brush);

                if (showDetails)
                {
                    PointD measurementP1 = line.HasDetectedLine && (line.DetectedP1 != default || line.DetectedP2 != default)
                        ? line.DetectedP1
                        : line.P1;
                    PointD measurementP2 = line.HasDetectedLine && (line.DetectedP1 != default || line.DetectedP2 != default)
                        ? line.DetectedP2
                        : line.P2;
                    string info = SingleEdgeCaliperRenderHelper.BuildSummaryText(line, $"边缘距离：{context.FormatLength(GeometryUtils.Distance(measurementP1.ToWpfPoint(), measurementP2.ToWpfPoint()))}", "单边缘测量", context.ShowCaliperScores);
                    context.DrawInfoText(info, new Point((line.P1.X + line.P2.X) / 2, (line.P1.Y + line.P2.Y) / 2), Brushes.White, true);
                }
            }
        }

        private sealed class CaliperMeasureRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is CaliperMeasureRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var caliper = (CaliperMeasureRoi)roi;
                if (!caliper.IsVisible) return;

                caliper.EnsureCaliperRegion();
                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, caliper.StrokeColor);
                Brush edgeBrush = Brushes.LimeGreen;
                Brush barBrush = Brushes.Gold;
                Brush invalidBrush = Brushes.Gray;
                Brush rejectedBrush = Brushes.IndianRed;
                bool showDetails = isSelected || ReferenceEquals(strokeOverride, Brushes.Orange);

                LineSegmentOverlay[] previewSegments = caliper.RegionSegments.Length > 0
                    ? caliper.RegionSegments
                    : CaliperOverlayGeometryHelper.BuildDualEdgeCaliperRegionSegments(caliper);
                LineSegmentOverlay[] previewBars = caliper.CaliperBars.Length > 0
                    ? caliper.CaliperBars
                    : CaliperOverlayGeometryHelper.BuildDualEdgeCaliperBars(caliper);

                if (showDetails)
                {
                    foreach (LineSegmentOverlay segment in previewSegments)
                    {
                        context.DrawLineSegment(segment.Start.ToWpfPoint(), segment.End.ToWpfPoint(), barBrush, 1.2 / context.Scale, new DoubleCollection { 3, 2 }, opacity: 0.32);
                    }

                    foreach (LineSegmentOverlay segment in previewBars)
                    {
                        context.DrawLineSegment(segment.Start.ToWpfPoint(), segment.End.ToWpfPoint(), barBrush, 0.8 / context.Scale, opacity: 0.32);
                    }

                    DualEdgeCaliperRenderHelper.DrawDetectionMarkers(context, caliper, invalidBrush, rejectedBrush);
                    DualEdgeCaliperRenderHelper.DrawLegend(context, caliper, brush, invalidBrush, rejectedBrush);
                }

                // Always keep the operator's reference line visible. Detection adds a
                // separate green result line instead of replacing this geometry.
                context.DrawLineSegment(
                    caliper.P1.ToWpfPoint(),
                    caliper.P2.ToWpfPoint(),
                    brush,
                    (isSelected ? 3 : caliper.StrokeThickness) / context.Scale,
                    snapToDevicePixels: false);

                if (caliper.HasDetectedEdges)
                {
                    if (caliper.DetectedP1 != default || caliper.DetectedP2 != default)
                    {
                        context.DrawLineSegment(
                            caliper.DetectedP1.ToWpfPoint(),
                            caliper.DetectedP2.ToWpfPoint(),
                            Brushes.LimeGreen,
                            2.4 / context.Scale,
                            snapToDevicePixels: false);
                    }
                    context.DrawLineSegment(
                        caliper.Edge1Start.ToWpfPoint(),
                        caliper.Edge1End.ToWpfPoint(),
                        edgeBrush,
                        2.4 / context.Scale,
                        snapToDevicePixels: false);
                    context.DrawLineSegment(
                        caliper.Edge2Start.ToWpfPoint(),
                        caliper.Edge2End.ToWpfPoint(),
                        edgeBrush,
                        2.4 / context.Scale,
                        snapToDevicePixels: false);
                }

                if (isSelected)
                {
                    DrawResizeHandles(context, caliper);
                }

                if (showDetails)
                {
                    string summary = DualEdgeCaliperRenderHelper.BuildSummaryText(context, caliper);
                    context.DrawInfoText(
                        summary,
                        DualEdgeCaliperRenderHelper.GetSummaryAnchor(context, caliper, summary),
                        Brushes.White,
                        true);
                }
            }

            private static void DrawResizeHandles(RoiRenderContext context, CaliperMeasureRoi caliper)
            {
                double halfWidth = caliper.GetResolvedCaliperRegionLength() / 2;
                double halfHeight = caliper.CaliperSearchRange;
                StandardRoiLayoutHelper.DrawRotatedBoxHandles(context, caliper.CaliperCenter.ToWpfPoint(), halfWidth, halfHeight, caliper.CaliperAngleDegrees + 90);
            }
        }
    }
}
