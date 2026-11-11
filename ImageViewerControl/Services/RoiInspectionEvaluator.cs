using System;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Services
{
    /// <summary>
    /// 统一计算 ROI 的检测质量和尺寸公差判定。
    ///
    /// 检测质量是算法证据的门控结果；尺寸判定只在检测证据可接受且配置了公差时进行。
    /// </summary>
    public static class RoiInspectionEvaluator
    {
        public static RoiInspectionResult EvaluateInputFailure(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);
            MeasurementTolerance? tolerance = roi.Tolerance is { IsEnabled: true } configured
                ? configured
                : null;
            return new RoiInspectionResult(
                RoiDetectionStatus.Failed,
                RoiSpecificationStatus.NotEvaluable,
                null,
                tolerance?.Nominal,
                tolerance?.TolerancePlus,
                tolerance?.ToleranceMinus);
        }

        public static RoiInspectionResult Evaluate(RoiBase roi, double pixelSize, CameraCalibration? calibration = null)
        {
            ArgumentNullException.ThrowIfNull(roi);

            RoiDetectionStatus detectionStatus = GetDetectionStatus(roi);
            DistortionAudit audit = RoiCalibrationHelper.AuditRoi(roi, calibration);
            double? measuredValue = audit.HasNonConvergedPoints ? null : GetMeasuredValue(roi, pixelSize, calibration, detectionStatus);
            MeasurementTolerance? tolerance = roi.Tolerance is { IsEnabled: true } configured
                ? configured
                : null;

            RoiSpecificationStatus specificationStatus;
            if (audit.HasNonConvergedPoints || calibration is { IsEnabled: true, IsValid: false })
            {
                // A configured but malformed calibration must never silently fall
                // back to raw pixels for a dimensional pass/fail decision.
                specificationStatus = RoiSpecificationStatus.NotEvaluable;
                measuredValue = null;
            }
            else if (tolerance is { IsValid: false })
            {
                specificationStatus = RoiSpecificationStatus.NotEvaluable;
            }
            else if (tolerance == null)
            {
                specificationStatus = measuredValue.HasValue
                    ? RoiSpecificationStatus.NotConfigured
                    : RoiSpecificationStatus.NotApplicable;
            }
            else if (!measuredValue.HasValue || !IsDetectionAcceptableForSpecification(detectionStatus))
            {
                specificationStatus = RoiSpecificationStatus.NotEvaluable;
            }
            else
            {
                specificationStatus = tolerance.IsWithinTolerance(measuredValue.Value)
                    ? RoiSpecificationStatus.Passed
                    : RoiSpecificationStatus.Failed;
            }

            return new RoiInspectionResult(
                detectionStatus,
                specificationStatus,
                measuredValue,
                tolerance?.Nominal,
                tolerance?.TolerancePlus,
                tolerance?.ToleranceMinus)
            {
                DistortionAudit = audit.HasPoints ? audit : null
            };
        }

        private static RoiDetectionStatus GetDetectionStatus(RoiBase roi)
        {
            switch (roi)
            {
                case CircularCaliperMeasureRoi circular when circular is not ArcCaliperMeasureRoi:
                    return circular.QualityStatus switch
                    {
                        CircularCaliperQualityStatus.Passed => RoiDetectionStatus.Passed,
                        CircularCaliperQualityStatus.Review => RoiDetectionStatus.Review,
                        CircularCaliperQualityStatus.Failed => RoiDetectionStatus.Failed,
                        _ => RoiDetectionStatus.NotMeasured
                    };
                case ArcCaliperMeasureRoi arc:
                    return arc.HasDetectedEdges ? RoiDetectionStatus.Passed : RoiDetectionStatus.NotMeasured;
                case CaliperMeasureRoi caliper:
                    return caliper.HasDetectedEdges ? RoiDetectionStatus.Passed : RoiDetectionStatus.NotMeasured;
                case LineCaliperMeasureRoi lineCaliper:
                    return lineCaliper.HasDetectedLine ? RoiDetectionStatus.Passed : RoiDetectionStatus.NotMeasured;
                default:
                    return RoiDetectionStatus.NotApplicable;
            }
        }

        private static bool IsDetectionAcceptableForSpecification(RoiDetectionStatus status)
        {
            return status is RoiDetectionStatus.NotApplicable or RoiDetectionStatus.Passed;
        }

        private static double? GetMeasuredValue(RoiBase roi, double pixelSize, CameraCalibration? calibration, RoiDetectionStatus detectionStatus)
        {
            if (!double.IsFinite(pixelSize) || pixelSize <= 0)
            {
                return null;
            }

            if (calibration is { IsEnabled: true, IsValid: false })
            {
                return null;
            }

            double correction = RoiCalibrationHelper.GetLengthCorrection(roi, calibration);
            if (!double.IsFinite(correction) || correction <= 0)
            {
                return null;
            }

            double? pixelValue = roi switch
            {
                CaliperMeasureRoi caliper when caliper.WidthMeasurement.IsMeasured && IsDetectionAcceptableForMeasurement(detectionStatus)
                    => caliper.WidthMeasurement.CenterDistance,
                CaliperMeasureRoi => null,
                LineCaliperMeasureRoi lineCaliper when lineCaliper.HasDetectedLine && IsDetectionAcceptableForMeasurement(detectionStatus)
                    => GeometryUtils.Distance(lineCaliper.MeasurementP1.ToWpfPoint(), lineCaliper.MeasurementP2.ToWpfPoint()),
                LineCaliperMeasureRoi => null,
                // 弧形卡尺必须先于圆卡尺匹配（它是圆形卡尺的子类），否则会落到圆卡尺分支给出半径。
                // 主测量值取弧长（= 半径 × 扫掠弧度），这是弧形卡尺的唯一长度量。
                ArcCaliperMeasureRoi arcCaliper when arcCaliper.HasDetectedEdges && IsDetectionAcceptableForMeasurement(detectionStatus)
                    => arcCaliper.MeasurementRadius * Math.Abs(arcCaliper.SweepAngle) * Math.PI / 180.0,
                ArcCaliperMeasureRoi => null,
                CircularCaliperMeasureRoi circular when circular.HasDetectedEdges && IsDetectionAcceptableForMeasurement(detectionStatus)
                    => circular.MeasurementRadius,
                CircularCaliperMeasureRoi => null,
                LineMeasureRoi line => GeometryUtils.Distance(line.P1.ToWpfPoint(), line.P2.ToWpfPoint()),
                ArcMeasureRoi arc when arc.IsValid => arc.Radius,
                ThreePointCircleMeasureRoi threePointCircle when threePointCircle.IsValid => threePointCircle.Radius,
                CircleRoi circle => circle.Radius,
                CenterDistanceMeasureRoi centerDistance => centerDistance.CenterDistance,
                _ => null
            };

            if (pixelValue.HasValue && double.IsFinite(pixelValue.Value) && pixelValue.Value >= 0)
            {
                double result = pixelValue.Value * correction * pixelSize;
                return double.IsFinite(result) ? result : null;
            }

            // 角度量（度）不随 PixelSize 缩放，也不做畸变校正——它们本来就是方向差。
            return GetAngularMeasuredValue(roi);
        }

        /// <summary>
        /// 角度类 ROI 的主测量值（单位：度）。
        /// Chinese: 过去这五类 ROI 不在测量值分支里，测得值恒为 null，导致"配了公差也永远 NotEvaluable"。
        /// 这里补齐它们的**主测量值**定义（与信息面板显示的量一致），使其公差判定真正生效。
        /// English: Angular ROIs previously produced no measured value at all, so a configured tolerance was
        /// silently ignored. These branches give each one the same primary quantity the info panel shows.
        /// </summary>
        private static double? GetAngularMeasuredValue(RoiBase roi)
        {
            double? degrees = roi switch
            {
                ParallelismMeasureRoi parallelism => parallelism.AngleDifference,
                PerpendicularityMeasureRoi perpendicularity => perpendicularity.PerpendicularityError,
                ConcentricityMeasureRoi concentricity => MeasureZones.ConcentricityZoneDiameter(concentricity),
                AngleMeasureRoi angle => GeometryUtils.SmallestAngle(
                    angle.P1.ToWpfPoint(),
                    angle.Vertex.ToWpfPoint(),
                    angle.P2.ToWpfPoint()),
                _ => null
            };

            return degrees.HasValue && double.IsFinite(degrees.Value) && degrees.Value >= 0
                ? degrees
                : null;
        }

        private static bool IsDetectionAcceptableForMeasurement(RoiDetectionStatus status)
        {
            return status is RoiDetectionStatus.Passed or RoiDetectionStatus.Review;
        }
    }
}
