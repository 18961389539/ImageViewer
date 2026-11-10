using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using CoreCircularDetectionResult = ImageViewer.Core.Analysis.CircularCaliperDetectionResult;
using CoreLineCaliperDetectionResult = ImageViewer.Core.Analysis.LineCaliperDetectionResult;
using CoreLineMeasureDetectionResult = ImageViewer.Core.Analysis.LineMeasureGradientDetectionResult;
using ImageViewer.Core.Analysis;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;
using ImageViewer.Utils;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private const double ViewportPadding = 16;

        private delegate bool BitmapAnalysisDelegate<TTarget, TResult>(BitmapSource bitmap, TTarget target, out TResult result);

        public void ShowRoiProperties(RoiBase roi) => _dialogWorkflowService.ShowRoiProperties(roi);

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use ShowRoiProperties(RoiBase) instead.", false)]
        public void ShowRoiPropertiesDialog(RoiBase roi) => ShowRoiProperties(roi);

        private bool TryApplyCaliperDetection(CaliperMeasureRoi line, IntensityImage? intensity = null)
        {
            return TryApplyBitmapAnalysisCore<CaliperMeasureRoi, CoreLineMeasureDetectionResult>(
                line,
                static roi => roi.EnsureCaliperRegion(),
                static roi => roi.ClearDetectedEdges(),
                (BitmapSource bitmap, CaliperMeasureRoi roi, out CoreLineMeasureDetectionResult result) => ImageAnalysisService.TryDetectLineMeasureEdgesCore(bitmap, roi, out result, QualityProfile, intensity),
                RoiDetectionResultMapper.Apply);
        }

        private bool TryApplyLineCaliperDetection(LineCaliperMeasureRoi line, IntensityImage? intensity = null)
        {
            return TryApplyBitmapAnalysisCore<LineCaliperMeasureRoi, CoreLineCaliperDetectionResult>(
                line,
                prepare: null,
                static roi => roi.ClearDetectedLine(),
                (BitmapSource bitmap, LineCaliperMeasureRoi roi, out CoreLineCaliperDetectionResult result) => ImageAnalysisService.TryDetectLineCaliperEdgesCore(bitmap, roi, out result, QualityProfile, intensity),
                RoiDetectionResultMapper.Apply);
        }

        private bool TryApplyCircularCaliperDetection(CircularCaliperMeasureRoi caliper, IntensityImage? intensity = null)
        {
            bool success = TryApplyBitmapAnalysisCore<CircularCaliperMeasureRoi, CoreCircularDetectionResult>(
                caliper,
                prepare: null,
                static roi => roi.ClearDetectedEdges(),
                (BitmapSource bitmap, CircularCaliperMeasureRoi roi, out CoreCircularDetectionResult result) => ImageAnalysisService.TryDetectCircularCaliperEdgesCore(bitmap, roi, out result, QualityProfile, intensity),
                (roi, result) => RoiDetectionResultMapper.Apply(roi, result, QualityProfile));
            if (!success && caliper is not ArcCaliperMeasureRoi)
            {
                caliper.MarkQualityFailure();
            }

            return success;
        }

        private bool TryCreateAutomaticCircle(Point seed, out CircularCaliperMeasureRoi roi)
        {
            if (GetAnalysisBitmapSource() is BitmapSource bitmap &&
                ImageAnalysisService.TryDetectAutomaticCircle(bitmap, seed, out roi, QualityProfile))
            {
                return true;
            }

            // 静默失败会让操作员分不清"没点中"和"这里确实没有圆"，给出可重试的明确提示。
            ShowStatusHint(UiText.Get("StatusAutomaticCircleNotFound"), StatusHintKind.Error);
            roi = null!;
            return false;
        }

        private bool TrySnapPointToEdge(Point seed, out Point snapped, out double score, out double confidence)
        {
            if (GetAnalysisBitmapSource() is BitmapSource bitmap &&
                ImageAnalysisService.TrySnapPointToEdge(bitmap, seed, out snapped, out score, out confidence, QualityProfile))
            {
                return true;
            }

            snapped = default;
            score = 0;
            confidence = 0;
            return false;
        }

        private bool TryApplyArcCaliperDetection(ArcCaliperMeasureRoi caliper, IntensityImage? intensity = null)
        {
            return TryApplyCircularCaliperDetection(caliper, intensity);
        }

        private bool TryRefreshCaliperDetection(RoiBase? roi, IntensityImage? intensity = null)
        {
            return roi switch
            {
                CaliperMeasureRoi line => TryApplyCaliperDetection(line, intensity),
                LineCaliperMeasureRoi lineCaliper => TryApplyLineCaliperDetection(lineCaliper, intensity),
                CircularCaliperMeasureRoi circular => TryApplyCircularCaliperDetection(circular, intensity),
                BlobAnalysisRoi blob => TryApplyBlobAnalysis(blob),
                _ => false
            };
        }

        private bool TryApplyBlobAnalysis(BlobAnalysisRoi blobRoi)
        {
            return TryApplyBitmapAnalysisCore<BlobAnalysisRoi, System.Collections.Generic.List<BlobFeature>>(
                blobRoi,
                prepare: null,
                static roi => roi.DetectedBlobs?.Clear(),
                TryDetectBlobFeatures,
                ApplyBlobAnalysisResult);
        }

        private bool TryApplyBitmapAnalysisCore<TTarget, TResult>(
            TTarget target,
            Action<TTarget>? prepare,
            Action<TTarget> clearResult,
            BitmapAnalysisDelegate<TTarget, TResult> detector,
            Action<TTarget, TResult> applyDetectionResult)
        {
            prepare?.Invoke(target);
            if (GetAnalysisBitmapSource() is not BitmapSource bitmap)
            {
                clearResult(target);
                return false;
            }

            if (!detector(bitmap, target, out TResult detectionResult))
            {
                clearResult(target);
                return false;
            }

            applyDetectionResult(target, detectionResult);
            return true;
        }

        private static bool TryDetectBlobFeatures(BitmapSource bitmap, BlobAnalysisRoi blobRoi, out System.Collections.Generic.List<BlobFeature> blobs)
        {
            blobs = BlobAnalysisService.DetectBlobs(bitmap, GetBlobAnalysisBounds(blobRoi), blobRoi.UseOtsu, blobRoi.ManualThreshold, blobRoi.DetectDark, blobRoi.MinArea);
            return true;
        }

        private static void ApplyBlobAnalysisResult(BlobAnalysisRoi blobRoi, System.Collections.Generic.List<BlobFeature> blobs)
        {
            blobRoi.DetectedBlobs = blobs;
        }

        private static Rect GetBlobAnalysisBounds(BlobAnalysisRoi blobRoi)
        {
            var rect = new Rect(
                blobRoi.Center.X - blobRoi.Width / 2,
                blobRoi.Center.Y - blobRoi.Height / 2,
                blobRoi.Width,
                blobRoi.Height);

            if (blobRoi.Angle == 0)
            {
                return rect;
            }

            return Utils.GeometryUtils.GetBoundingBox(new[]
            {
                Utils.GeometryUtils.RotatePoint(new Point(rect.Left, rect.Top), blobRoi.Center.ToWpfPoint(), blobRoi.Angle),
                Utils.GeometryUtils.RotatePoint(new Point(rect.Right, rect.Top), blobRoi.Center.ToWpfPoint(), blobRoi.Angle),
                Utils.GeometryUtils.RotatePoint(new Point(rect.Right, rect.Bottom), blobRoi.Center.ToWpfPoint(), blobRoi.Angle),
                Utils.GeometryUtils.RotatePoint(new Point(rect.Left, rect.Bottom), blobRoi.Center.ToWpfPoint(), blobRoi.Angle)
            });
        }

        private void RefreshAllCaliperDetections()
        {
            // One conversion serves every caliper ROI in this pass; per-ROI conversion of a
            // large image otherwise dominates refresh time as the ROI count grows.
            IntensityImage? intensity = null;
            foreach (RoiBase roi in ViewModel.AllRois)
            {
                if (intensity is null &&
                    roi is CaliperMeasureRoi or LineCaliperMeasureRoi or CircularCaliperMeasureRoi &&
                    GetAnalysisBitmapSource() is BitmapSource bitmap)
                {
                    intensity = VisionAlgorithmEntryPoint.CreateIntensity(bitmap);
                }

                TryRefreshCaliperDetection(roi, intensity);
            }
        }

        private async void OnFeatureMenuCommandClick(object sender, RoutedEventArgs e)
        {
            if (TryGetTaggedCommand(sender, out ImageViewerFeatureMenuCommand command))
            {
                await RunUiOperationAsync($"功能命令 {command}", () => _featureMenuCommandController.ExecuteAsync(command));
            }
        }

    }
}
