using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CoreCircularDetectionResult = ImageViewer.Core.Analysis.CircularCaliperDetectionResult;
using CoreLineMeasureDetectionResult = ImageViewer.Core.Analysis.LineMeasureGradientDetectionResult;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerFeatureMenuCommandHostAdapter : IImageViewerFeatureMenuCommandHost
    {
        private readonly IImageViewerFeatureAnalysisCapability _analysis;
        private readonly IImageViewerFeatureMutationCapability _mutation;
        private readonly IImageViewerFeatureExportDataCapability _data;
        private readonly IImageViewerFeatureDialogCapability _dialogs;
        private readonly IImageViewerFeatureFeedbackCapability _feedback;
        private CancellationTokenSource? _batchExportCancellation;

        public ImageViewerFeatureMenuCommandHostAdapter(
            IImageViewerFeatureAnalysisCapability analysis,
            IImageViewerFeatureMutationCapability mutation,
            IImageViewerFeatureExportDataCapability data,
            IImageViewerFeatureDialogCapability dialogs,
            IImageViewerFeatureFeedbackCapability feedback)
        {
            _analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
            _mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        }

        public void RunGradientDetection()
        {
            if (_analysis.AnalysisBitmapSource is not BitmapSource bitmap || _analysis.SelectedRoi is not RoiBase selectedRoi)
            {
                return;
            }

            RoiBase oldState = selectedRoi.Clone();
            RoiBase? detectedRoi = selectedRoi switch
            {
                CaliperMeasureRoi line when ImageAnalysisService.TryDetectLineMeasureEdgesCore(bitmap, line, out CoreLineMeasureDetectionResult lineDetectionResult, _analysis.QualityProfile) => CreateDetectedLineMeasureRoi(line, lineDetectionResult),
                CircularCaliperMeasureRoi circular when ImageAnalysisService.TryDetectCircularCaliperEdgesCore(bitmap, circular, out CoreCircularDetectionResult circularDetectionResult, _analysis.QualityProfile) => CreateDetectedCircularCaliperRoi(circular, circularDetectionResult, _analysis.QualityProfile),
                _ => null
            };

            if (detectedRoi == null)
            {
                RoiBase? clearedRoi = CreateClearedDetectionRoi(selectedRoi);
                if (clearedRoi != null)
                {
                    IUndoRedoCommand? clearCommand = _mutation.CreateStateCommand(selectedRoi, oldState, clearedRoi);
                    if (clearCommand != null)
                    {
                        _mutation.ExecuteUndoRedoCommand(clearCommand);
                        _mutation.DrawRois();
                    }
                }

                return;
            }

            IUndoRedoCommand? command = _mutation.CreateStateCommand(selectedRoi, oldState, detectedRoi);
            if (command == null)
            {
                return;
            }

            _mutation.ExecuteUndoRedoCommand(command);
            _mutation.DrawRois();
        }

        private static RoiBase? CreateClearedDetectionRoi(RoiBase roi)
        {
            switch (roi)
            {
                case CaliperMeasureRoi caliper:
                    var clearedCaliper = (CaliperMeasureRoi)caliper.Clone();
                    clearedCaliper.ClearDetectedEdges();
                    return clearedCaliper;
                case CircularCaliperMeasureRoi circular when circular is not ArcCaliperMeasureRoi:
                    var clearedCircular = (CircularCaliperMeasureRoi)circular.Clone();
                    clearedCircular.ClearDetectedEdges();
                    clearedCircular.MarkQualityFailure();
                    return clearedCircular;
                default:
                    return null;
            }
        }

        public async Task ExportSnapshotAsync()
        {
            string? filePath = _dialogs.ShowSaveSnapshotDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                _data.RenderRoot.UpdateLayout();
                var bitmap = new RenderTargetBitmap(
                    Math.Max(1, (int)Math.Ceiling(_data.RenderRoot.ActualWidth)),
                    Math.Max(1, (int)Math.Ceiling(_data.RenderRoot.ActualHeight)),
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(_data.RenderRoot);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                await using var stream = File.Create(filePath);
                await Task.Run(() => encoder.Save(stream));
                _feedback.ShowStatusHint(UiText.Get("StatusExportPngSuccess"), StatusHintKind.Success);
            }
            catch (Exception ex)
            {
                _feedback.ShowNonCriticalError(UiText.Get("ErrorExportPngTitle"), UiText.Get("ErrorExportPngMessage"), ex);
            }
        }

        public async Task ExportAnalysisCsvAsync()
        {
            string? filePath = _dialogs.ShowSaveAnalysisCsvDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                var exportContext = new RoiAnalysisExportContext(
                    _data.CurrentImagePath,
                    _data.PluginRegistry,
                    _data.RenderSettings,
                    QualityProfile: _analysis.QualityProfile);
                await RoiAnalysisExportService.SaveCsvAsync(
                    filePath,
                    _data.AllRois,
                    _analysis.AnalysisBitmapSource,
                    _data.PixelSize,
                    _data.PhysicalUnit,
                    _data.Calibration,
                    exportContext: exportContext);
                _feedback.ShowStatusHint(UiText.Get("StatusExportCsvWithMetadataSuccess"), StatusHintKind.Success);
            }
            catch (Exception ex)
            {
                _feedback.ShowNonCriticalError(UiText.Get("ErrorExportAnalysisTitle"), UiText.Get("ErrorExportAnalysisMessage"), ex);
            }
        }

        public async Task ExportBatchAnalysisCsvAsync()
        {
            string[] imagePaths = _dialogs.ShowOpenBatchImageFilesDialog();
            if (imagePaths.Length == 0)
            {
                return;
            }

            string? outputPath = _dialogs.ShowSaveAnalysisCsvDialog();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            using var cancellation = new CancellationTokenSource();
            _batchExportCancellation = cancellation;
            try
            {
                var progress = new Progress<BatchRoiAnalysisExportProgress>(update =>
                {
                    string current = string.IsNullOrWhiteSpace(update.CurrentPath)
                        ? string.Empty
                        : $"：{Path.GetFileName(update.CurrentPath)}";
                    _feedback.ShowStatusHint(
                        $"{update.Phase}{current}（{update.CompletedFileCount}/{update.RequestedFileCount}，已生成 {update.ExportedRowCount} 条）",
                        StatusHintKind.Info);
                });
                BatchRoiAnalysisExportSummary summary = await BatchRoiAnalysisExportService.ExportAsync(
                    outputPath,
                    imagePaths,
                    _data.AllRois,
                    _data.PixelSize,
                    _data.PhysicalUnit,
                    _data.Calibration,
                    _data.PluginRegistry,
                    _data.RenderSettings,
                    qualityProfile: _analysis.QualityProfile,
                    cancellationToken: cancellation.Token,
                    progress: progress);
                _feedback.ShowStatusHint(
                    UiText.Format(
                        "StatusExportBatchCsvSuccess",
                        summary.RequestedFileCount,
                        summary.DecodedFileCount,
                        summary.FullySuccessfulFileCount,
                        summary.PartiallySuccessfulFileCount,
                        summary.AllRoiFailedFileCount,
                        summary.InputFailedFileCount,
                        summary.ExportedRowCount),
                    summary.FullySuccessfulFileCount == summary.RequestedFileCount
                        ? StatusHintKind.Success
                        : StatusHintKind.Info);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                _feedback.ShowStatusHint(
                    $"批量导出已取消，已完成结果已保存到 {Path.GetFileName(outputPath)} 及其摘要文件。",
                    StatusHintKind.Info);
            }
            catch (Exception ex)
            {
                _feedback.ShowNonCriticalError(UiText.Get("ErrorExportAnalysisTitle"), UiText.Get("ErrorExportBatchCsvMessage"), ex);
            }
            finally
            {
                if (ReferenceEquals(_batchExportCancellation, cancellation))
                {
                    _batchExportCancellation = null;
                }
            }
        }

        public bool CancelBatchAnalysisExport()
        {
            if (_batchExportCancellation is not { IsCancellationRequested: false } cancellation)
            {
                return false;
            }

            cancellation.Cancel();
            _feedback.ShowStatusHint("正在取消批量导出，当前文件完成后会保存已有结果。", StatusHintKind.Info);
            return true;
        }

        public void ShowAnalysisSummary()
        {
            string summary = RoiAnalysisExportService.BuildSummary(_data.AllRois, _analysis.AnalysisBitmapSource, _data.PixelSize, _data.PhysicalUnit, _data.Calibration);
            _dialogs.ShowReadOnlyText(UiText.Get("DialogAnalysisSummaryTitle"), summary);
        }

        public void UpdateContextMenuState() => _feedback.UpdateContextMenuState();

        private static CaliperMeasureRoi CreateDetectedLineMeasureRoi(CaliperMeasureRoi source, CoreLineMeasureDetectionResult detectionResult)
        {
            var detected = (CaliperMeasureRoi)source.Clone();
            RoiDetectionResultMapper.Apply(detected, detectionResult);
            return detected;
        }

        private static CircularCaliperMeasureRoi CreateDetectedCircularCaliperRoi(CircularCaliperMeasureRoi source, CoreCircularDetectionResult detectionResult, ImageAnalysisQualityProfile profile)
        {
            var detected = (CircularCaliperMeasureRoi)source.Clone();
            RoiDetectionResultMapper.Apply(detected, detectionResult, profile);
            return detected;
        }
    }

    internal sealed class ImageViewerFeatureAnalysisCapability : IImageViewerFeatureAnalysisCapability
    {
        private readonly ImageViewer _owner;

        public ImageViewerFeatureAnalysisCapability(ImageViewer owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public RoiBase? SelectedRoi => _owner.ViewerState.SelectedRoi;
        public BitmapSource? AnalysisBitmapSource => _owner.GetAnalysisBitmapSource();
        public ImageAnalysisQualityProfile QualityProfile => _owner.QualityProfile;
    }

    internal sealed class ImageViewerFeatureMutationCapability : IImageViewerFeatureMutationCapability
    {
        private readonly ImageViewer _owner;

        public ImageViewerFeatureMutationCapability(ImageViewer owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public IUndoRedoCommand? CreateStateCommand(RoiBase roi, RoiBase oldState, RoiBase newState)
            => ImageViewer.CreateStateCommand(roi, oldState, newState);

        public void ExecuteUndoRedoCommand(IUndoRedoCommand command)
            => _owner.ViewerState.UndoRedo.Execute(command);

        public void DrawRois() => _owner.DrawRois();
    }

    internal sealed class ImageViewerFeatureExportDataCapability : IImageViewerFeatureExportDataCapability
    {
        private readonly ImageViewer _owner;

        public ImageViewerFeatureExportDataCapability(ImageViewer owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public FrameworkElement RenderRoot => _owner.FeatureRenderRoot;
        public IReadOnlyList<RoiBase> AllRois => _owner.ViewerState.AllRois;
        public double PixelSize => _owner.PixelSize;
        public string PhysicalUnit => _owner.PhysicalUnit;
        public CameraCalibration? Calibration => _owner.Calibration;
        public string? CurrentImagePath => _owner._controlComposition.ViewportController.TryGetCurrentImagePath();
        public RoiPluginRegistry PluginRegistry => _owner.PluginRegistry;

        public IReadOnlyDictionary<string, string> RenderSettings => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["enableImagePyramid"] = _owner.RuntimeOptions.EnableImagePyramid.ToString(),
            ["autoSelectPyramidLevel"] = _owner.RuntimeOptions.AutoSelectPyramidLevel.ToString(),
            ["enableTiledRendering"] = _owner.RuntimeOptions.EnableTiledRendering.ToString(),
            ["prefetchAdjacentTiles"] = _owner.RuntimeOptions.PrefetchAdjacentTiles.ToString(),
            ["autoTuneLargeImageRendering"] = _owner.RuntimeOptions.AutoTuneLargeImageRendering.ToString(),
            ["tileCacheMaximumMegabytes"] = _owner.RuntimeOptions.TileCacheMaximumMegabytes.ToString(CultureInfo.InvariantCulture),
            ["tilePrefetchRadius"] = _owner.RuntimeOptions.TilePrefetchRadius.ToString(CultureInfo.InvariantCulture),
            ["enableGpuRendering"] = _owner.EnableGpuRendering.ToString(),
            ["pseudoColorPalette"] = _owner.PseudoColorPalette.ToString(),
            ["enableAsyncAnalysis"] = _owner.RuntimeOptions.EnableAsyncAnalysis.ToString()
        };
    }

    internal sealed class ImageViewerFeatureDialogCapability : IImageViewerFeatureDialogCapability
    {
        private readonly ImageViewer _owner;
        private readonly ImageViewerDialogWorkflowService _dialogWorkflowService;

        public ImageViewerFeatureDialogCapability(ImageViewer owner, ImageViewerDialogWorkflowService dialogWorkflowService)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _dialogWorkflowService = dialogWorkflowService ?? throw new ArgumentNullException(nameof(dialogWorkflowService));
        }

        public string? ShowSaveSnapshotDialog() => _dialogWorkflowService.ShowSaveSnapshotDialog();
        public string? ShowSaveAnalysisCsvDialog() => _dialogWorkflowService.ShowSaveAnalysisCsvDialog();
        public string[] ShowOpenBatchImageFilesDialog() => _owner.FileDialogService.ShowOpenImageFilesDialog(Window.GetWindow(_owner));
        public void ShowReadOnlyText(string title, string text) => _dialogWorkflowService.ShowReadOnlyText(title, text);
    }

    internal sealed class ImageViewerFeatureFeedbackCapability : IImageViewerFeatureFeedbackCapability
    {
        private readonly ImageViewer _owner;

        public ImageViewerFeatureFeedbackCapability(ImageViewer owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public void ShowNonCriticalError(string title, string message, Exception exception)
            => _owner.ShowNonCriticalError(title, message, exception);

        public void ShowStatusHint(string message, StatusHintKind kind) => _owner.ShowStatusHint(message, kind);
        public void UpdateContextMenuState() => _owner.UpdateContextMenuState();
    }

    public partial class ImageViewer
    {
        internal FrameworkElement FeatureRenderRoot => rootGrid;
    }
}
