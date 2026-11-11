using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CoreCircularDetectionResult = ImageViewer.Core.Analysis.CircularCaliperDetectionResult;
using CoreLineCaliperDetectionResult = ImageViewer.Core.Analysis.LineCaliperDetectionResult;
using CoreLineMeasureDetectionResult = ImageViewer.Core.Analysis.LineMeasureGradientDetectionResult;
using ImageViewer.Core.Analysis;
using ImageViewer.Drawing;
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
            if (GetAnalysisBitmapSource() is BitmapSource bitmap)
            {
                EdgeSnapResult result = SnapPointToEdge(bitmap, seed, QualityProfile);
                if (result.Found)
                {
                    snapped = result.Point;
                    score = result.Score;
                    confidence = result.Confidence;
                    return true;
                }
            }

            snapped = default;
            score = 0;
            confidence = 0;
            return false;
        }

        /// <summary>
        /// 后台执行边缘吸附；线程亲和状态（分析位图、质量档）在调用线程上取好。
        /// Chinese: 分析位图来自冻结副本，后台只读像素，不触碰 UI 对象；未冻结时退回同步搜索，
        /// 保持旧行为而不是静默失去吸附。
        /// English: Thread-affine state is captured on the calling thread; the frozen analysis bitmap
        /// is safe to read on a worker. A non-frozen source falls back to the synchronous probe.
        /// </summary>
        private Task<EdgeSnapResult> SnapPointToEdgeAsync(Point seed, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<EdgeSnapResult>(cancellationToken);
            }

            BitmapSource? bitmap = GetAnalysisBitmapSource();
            ImageAnalysisQualityProfile profile = QualityProfile;
            if (bitmap is null)
            {
                return Task.FromResult(EdgeSnapResult.NotFound);
            }

            if (!bitmap.IsFrozen)
            {
                return Task.FromResult(SnapPointToEdge(bitmap, seed, profile));
            }

            return Task.Run(
                () => cancellationToken.IsCancellationRequested
                    ? EdgeSnapResult.NotFound
                    : SnapPointToEdge(bitmap, seed, profile),
                cancellationToken);
        }

        private static EdgeSnapResult SnapPointToEdge(BitmapSource bitmap, Point seed, ImageAnalysisQualityProfile profile)
            => ImageAnalysisService.TrySnapPointToEdge(bitmap, seed, out Point snapped, out double score, out double confidence, profile)
                ? new EdgeSnapResult(true, snapped, score, confidence)
                : EdgeSnapResult.NotFound;

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
            blobs = BlobAnalysisService.DetectBlobs(bitmap, blobRoi, blobRoi.UseOtsu, blobRoi.ManualThreshold, blobRoi.DetectDark, blobRoi.MinArea);
            return true;
        }

        private static void ApplyBlobAnalysisResult(BlobAnalysisRoi blobRoi, System.Collections.Generic.List<BlobFeature> blobs)
        {
            blobRoi.DetectedBlobs = blobs;
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
