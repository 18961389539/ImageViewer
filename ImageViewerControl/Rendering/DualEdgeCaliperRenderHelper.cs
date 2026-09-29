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
