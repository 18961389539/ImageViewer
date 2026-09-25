using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ImageViewer.Drawing;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use StartLineMeasureMode() or StartCaliperMeasureMode() for explicit measure-mode entry.", false)]
        public void StartMeasureMode()
        {
            StartCaliperMeasureMode();
        }

        public void StartRoiMode()
        {
            StartDraw(BuiltInDrawControllers.RotatedRect);
        }

        public void StartBlobAnalysisMode()
        {
            StartDraw(BuiltInDrawControllers.BlobAnalysis);
        }

        public void StartCircleRoiMode()
        {
            StartDraw(BuiltInDrawControllers.Circle);
        }

        public void StartRingRoiMode()
        {
            StartDraw(BuiltInDrawControllers.Ring);
        }

        public void StartCircularCaliperMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.CircularCaliper);
        }

        public void StartAutomaticCircleMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.AutomaticCircle);
        }

        public void StartArcCaliperMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.ArcCaliper);
        }

        public void StartLineCaliperMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.LineCaliper);
        }

        public void StartPointAnnotationMode()
        {
            StartDraw(BuiltInDrawControllers.PointAnnotation);
        }

        public void StartPointCoordinateMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.PointCoordinate);
        }

        public void StartAutomaticEdgePointMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.AutomaticEdgePoint);
        }

        public void StartArrowAnnotationMode()
        {
            StartDraw(BuiltInDrawControllers.ArrowAnnotation);
        }

        public void StartTextAnnotationMode()
        {
            StartDraw(BuiltInDrawControllers.TextAnnotation);
        }

        public void StartPolylineRoiMode(bool freehand)
        {
            StartDraw(freehand ? BuiltInDrawControllers.FreehandPolyline : BuiltInDrawControllers.Polyline);
        }

        public void StartPolygonRoiMode()
        {
            StartDraw(BuiltInDrawControllers.Polygon);
        }

        public void StartAreaMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.Polygon);
        }

        public void StartPolylineMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.Polyline);
        }

        /// <summary>
        /// 进入外部落点模式：每次点击由调用方提供的工厂创建 ROI。
        /// Chinese: 落点使用未吸附的原始坐标，与既有行为一致。
        /// English: Enters external placement mode: each click creates an ROI from the caller-supplied
        /// factory. The placement uses the raw (unsnapped) coordinate, matching the existing behavior.
        /// </summary>
        public void StartPlacementMode(Func<Point, RoiBase?> createRoi, Cursor? cursor = null)
        {
            ArgumentNullException.ThrowIfNull(createRoi);

            StartDraw(new RoiDrawController(
                cursor ?? Cursors.Cross,
                () => new ClickPlaceDrawSession<RoiBase>(
                    (_, position) => createRoi(position),
                    useSnappedPosition: false,
                    handlesEvent: true)));
        }

        public void StartFitEllipseMode()
        {
            StartDraw(BuiltInDrawControllers.FittedEllipse);
        }

        public void StartLineMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.LineMeasure);
        }

        public void StartCaliperMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.CaliperMeasure);
        }

        public void StartAngleMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.AngleMeasure);
        }

        public void StartArcMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.ArcMeasure);
        }

        public void StartThreePointCircleMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.ThreePointCircle);
        }

        public void StartPointToLineMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.PointToLineDistance);
        }

        public void StartPointToCircleMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.PointToCircleDistance);
        }

        public void StartParallelismMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.Parallelism);
        }

        public void StartPerpendicularityMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.Perpendicularity);
        }

        public void StartConcentricityMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.Concentricity);
        }

        public void StartCenterDistanceMeasureMode()
        {
            StartDraw(BuiltInDrawControllers.CenterDistance);
        }

    }
}
