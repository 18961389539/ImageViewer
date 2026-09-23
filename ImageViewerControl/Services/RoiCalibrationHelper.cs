using System.Windows;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Services
{
    /// <summary>
    /// 畸变校正辅助：按 ROI 的几何中心计算长度/面积校正因子。
    /// Chinese: 校正中心按测量几何取(线段中点、圆/弧圆心、包围盒中心等)，
    /// 使信息面板与导出数值与"以 ROI 几何中心为基准的局部畸变"保持一致。
    /// English: Computes distortion corrections centered on the ROI geometry.
    /// </summary>
    internal static class RoiCalibrationHelper
    {
        /// <summary>长度校正因子（无标定或中心未知时为 1.0）。</summary>
        public static double GetLengthCorrection(RoiBase roi, CameraCalibration? calibration)
        {
            Point? center = GetMeasurementCenter(roi);
            return center.HasValue && calibration is { IsEnabled: true }
                ? calibration.UndistortScaleFactor(center.Value)
                : 1.0;
        }

        /// <summary>面积校正因子（长度校正因子的平方，各向同性畸变下成立）。</summary>
        public static double GetAreaCorrection(RoiBase roi, CameraCalibration? calibration)
        {
            double correction = GetLengthCorrection(roi, calibration);
            return correction * correction;
        }

        /// <summary>
        /// 获取 ROI 的测量几何中心；无长度/面积测量语义的 ROI 返回 null。
        /// </summary>
        public static Point? GetMeasurementCenter(RoiBase roi) => roi switch
        {
            CaliperMeasureRoi caliper => Midpoint(caliper.P1, caliper.P2),
            LineCaliperMeasureRoi lineCaliper => Midpoint(lineCaliper.P1, lineCaliper.P2),
            ArrowAnnotationRoi arrow => Midpoint(arrow.P1, arrow.P2),
            LineMeasureRoi line => Midpoint(line.P1, line.P2),
            ArcCaliperMeasureRoi arcCaliper => arcCaliper.Center,
            CircularCaliperMeasureRoi circular => circular.Center,
            BlobAnalysisRoi blob => blob.Center,
            RotatedRect rect => rect.Center,
            FittedEllipseRoi fittedEllipse => fittedEllipse.Center,
            EllipseRoi ellipse => ellipse.Center,
            CircleRoi circle => circle.Center,
            RingRoi ring => ring.Center,
            PolygonRoi polygon => GeometryUtils.GetCentroid(polygon.Points),
            PolylineRoi polyline => GeometryUtils.GetCentroid(polyline.Points),
            AngleMeasureRoi angle => angle.Vertex,
            ArcMeasureRoi arc => arc.Center,
            PointToLineDistanceRoi pointToLine => pointToLine.Point,
            PointToCircleDistanceRoi pointToCircle => pointToCircle.Point,
            ConcentricityMeasureRoi concentricity => concentricity.MidCenter,
            PointAnnotationRoi point => point.Position,
            TextAnnotationRoi text => text.Position,
            _ => null
        };

        private static Point Midpoint(Point a, Point b) => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
    }
}