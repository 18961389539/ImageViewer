using System.Windows;
using System.Windows.Media;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Rendering
{
    internal static class DualEdgeCaliperRenderHelper
    {
        public static void DrawDetectionMarkers(RoiRenderContext context, CaliperMeasureRoi caliper, Brush invalidBrush, Brush rejectedBrush)
        {
            SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.InvalidCaliperMarkers, invalidBrush);
            SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.RejectedEdge1Markers, rejectedBrush);
            SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.RejectedEdge2Markers, rejectedBrush);
            SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.Edge1Markers, Brushes.Cyan);
            SingleEdgeCaliperRenderHelper.DrawEdgeMarkers(context, caliper.Edge2Markers, Brushes.Orange);
            SingleEdgeCaliperRenderHelper.DrawScoreOverlays(context, caliper.ScoreOverlays);
        }

        public static void DrawLegend(RoiRenderContext context, CaliperMeasureRoi caliper, Brush invalidBrush, Brush rejectedBrush)
        {
            if (!context.ShowCaliperScores)
            {
                return;
            }

            Point anchor = new(caliper.CaliperCenter.X + 10 / context.Scale, caliper.CaliperCenter.Y - 28 / context.Scale);
            SingleEdgeCaliperRenderHelper.DrawLegend(context, anchor, invalidBrush, rejectedBrush, showDiagnostics: true);
        }

        public static string BuildSummaryText(RoiRenderContext context, CaliperMeasureRoi caliper)
        {
            string geometryText = $"D:{context.FormatLength(GeometryUtils.Distance(caliper.P1.ToWpfPoint(), caliper.P2.ToWpfPoint()))}";
            return SingleEdgeCaliperRenderHelper.BuildSummaryText(caliper, geometryText, "Caliper", context.ShowCaliperScores);
        }

        /// <summary>
        /// Returns the anchor for the dual-edge summary label.
        /// The detected endpoints can move away from the original ROI center, so the label must follow
        /// the measured segment once detection has completed.
        /// </summary>
        public static Point GetSummaryAnchor(CaliperMeasureRoi caliper)
        {
            ArgumentNullException.ThrowIfNull(caliper);

            return GetMeasuredSegmentMidpoint(caliper);
        }

        /// <summary>
        /// Returns the summary anchor offset from the measured segment so the label does not cover it.
        /// The side with the smaller screen Y coordinate is chosen to keep the label visually above the line
        /// even when the image is zoomed or translated.
        /// </summary>
        public static Point GetSummaryAnchor(RoiRenderContext context, CaliperMeasureRoi caliper)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(caliper);

            Point midpoint = GetMeasuredSegmentMidpoint(caliper);
            if (!caliper.HasDetectedEdges)
            {
                return midpoint;
            }

            Vector direction = caliper.P2.ToWpfPoint() - caliper.P1.ToWpfPoint();
            if (direction.LengthSquared < 1e-6)
            {
                return midpoint;
            }

            direction.Normalize();
            Vector normal = new(-direction.Y, direction.X);
            double offset = Math.Max(12, context.InfoTextOffset) / Math.Max(context.Scale, 1e-6);
            Point first = midpoint + normal * offset;
            Point second = midpoint - normal * offset;
            return context.ToScreenPoint(first).Y <= context.ToScreenPoint(second).Y ? first : second;
        }

        private static Point GetMeasuredSegmentMidpoint(CaliperMeasureRoi caliper)
        {
            if (caliper.HasDetectedEdges)
            {
                return new Point(
                    (caliper.P1.X + caliper.P2.X) / 2,
                    (caliper.P1.Y + caliper.P2.Y) / 2);
            }

            return caliper.CaliperCenter.ToWpfPoint();
        }
    }
}
