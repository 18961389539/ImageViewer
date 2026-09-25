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
                ? calibration.UndistortScaleFactor(center.Value.ToPointD())
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
            CaliperMeasureRoi caliper => Midpoint(caliper.P1.ToWpfPoint(), caliper.P2.ToWpfPoint()),
            LineCaliperMeasureRoi lineCaliper => Midpoint(lineCaliper.P1.ToWpfPoint(), lineCaliper.P2.ToWpfPoint()),
            ArrowAnnotationRoi arrow => Midpoint(arrow.P1.ToWpfPoint(), arrow.P2.ToWpfPoint()),
            LineMeasureRoi line => Midpoint(line.P1.ToWpfPoint(), line.P2.ToWpfPoint()),
            ArcCaliperMeasureRoi arcCaliper => arcCaliper.Center.ToWpfPoint(),
            CircularCaliperMeasureRoi circular => circular.Center.ToWpfPoint(),
            BlobAnalysisRoi blob => blob.Center.ToWpfPoint(),
            RotatedRect rect => rect.Center.ToWpfPoint(),
            FittedEllipseRoi fittedEllipse => fittedEllipse.Center.ToWpfPoint(),
            EllipseRoi ellipse => ellipse.Center.ToWpfPoint(),
            CircleRoi circle => circle.Center.ToWpfPoint(),
            RingRoi ring => ring.Center.ToWpfPoint(),
            PolygonRoi polygon when polygon.IsClosed && polygon.Points.Count >= 3 => GeometryUtils.GetPolygonMetrics(polygon.Points.ToWpfPointArray()).Centroid,
            PolygonRoi polygon => GeometryUtils.GetCentroid(polygon.Points.ToWpfPointArray()),
            PolylineRoi polyline => GeometryUtils.GetCentroid(polyline.Points.ToWpfPointArray()),
            AngleMeasureRoi angle => angle.Vertex.ToWpfPoint(),
            ArcMeasureRoi arc => arc.Center.ToWpfPoint(),
            PointToLineDistanceRoi pointToLine => pointToLine.Point.ToWpfPoint(),
            PointToCircleDistanceRoi pointToCircle => pointToCircle.Point.ToWpfPoint(),
            ConcentricityMeasureRoi concentricity => concentricity.MidCenter.ToWpfPoint(),
            CenterDistanceMeasureRoi centerDistance => centerDistance.MidCenter.ToWpfPoint(),
            ThreePointCircleMeasureRoi threePointCircle when threePointCircle.IsValid => threePointCircle.Center.ToWpfPoint(),
            PointAnnotationRoi point => point.Position.ToWpfPoint(),
            PointCoordinateMeasureRoi pointCoordinate => pointCoordinate.Position.ToWpfPoint(),
            TextAnnotationRoi text => text.Position.ToWpfPoint(),
            _ => null
        };

        private static Point Midpoint(Point a, Point b) => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
    }
}
