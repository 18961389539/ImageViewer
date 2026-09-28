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
            double? measuredValue = GetMeasuredValue(roi, pixelSize, calibration, detectionStatus);
            MeasurementTolerance? tolerance = roi.Tolerance is { IsEnabled: true } configured
                ? configured
                : null;

            RoiSpecificationStatus specificationStatus;
            if (calibration is { IsEnabled: true, IsValid: false })
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
                tolerance?.ToleranceMinus);
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
                    => GeometryUtils.Distance(lineCaliper.P1.ToWpfPoint(), lineCaliper.P2.ToWpfPoint()),
                LineCaliperMeasureRoi => null,
                CircularCaliperMeasureRoi circular when circular.HasDetectedEdges && IsDetectionAcceptableForMeasurement(detectionStatus)
                    => circular.Radius,
                CircularCaliperMeasureRoi => null,
                LineMeasureRoi line => GeometryUtils.Distance(line.P1.ToWpfPoint(), line.P2.ToWpfPoint()),
                ArcMeasureRoi arc when arc.IsValid => arc.Radius,
                ThreePointCircleMeasureRoi threePointCircle when threePointCircle.IsValid => threePointCircle.Radius,
                CircleRoi circle => circle.Radius,
                CenterDistanceMeasureRoi centerDistance => centerDistance.CenterDistance,
                _ => null
            };

            if (!pixelValue.HasValue || !double.IsFinite(pixelValue.Value) || pixelValue.Value < 0)
            {
                return null;
            }

            double result = pixelValue.Value * correction * pixelSize;
            return double.IsFinite(result) ? result : null;
        }

        private static bool IsDetectionAcceptableForMeasurement(RoiDetectionStatus status)
        {
            return status is RoiDetectionStatus.Passed or RoiDetectionStatus.Review;
        }
    }
}
