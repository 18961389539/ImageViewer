using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerFeatureMenuCommandHostAdapter : IImageViewerFeatureMenuCommandHost
    {
        private readonly ImageViewerFeatureMenuCommandDependencies _dependencies;

        public ImageViewerFeatureMenuCommandHostAdapter(ImageViewerFeatureMenuCommandDependencies dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public void RunGradientDetection()
        {
            if (_dependencies.GetAnalysisBitmapSource() is not BitmapSource bitmap || _dependencies.GetSelectedRoi() is not RoiBase selectedRoi)
            {
                return;
            }

            RoiBase oldState = selectedRoi.Clone();
            RoiBase? detectedRoi = selectedRoi switch
            {
                CaliperMeasureRoi line when ImageAnalysisService.TryDetectLineMeasureEdges(bitmap, line, out LineMeasureGradientDetectionResult lineDetectionResult, _dependencies.GetQualityProfile()) => CreateDetectedLineMeasureRoi(line, lineDetectionResult),
                CircularCaliperMeasureRoi circular when ImageAnalysisService.TryDetectCircularCaliperEdges(bitmap, circular, out CircularCaliperDetectionResult circularDetectionResult, _dependencies.GetQualityProfile()) => CreateDetectedCircularCaliperRoi(circular, circularDetectionResult, _dependencies.GetQualityProfile()),
                _ => null
            };

            if (detectedRoi == null)
            {
                RoiBase? clearedRoi = CreateClearedDetectionRoi(selectedRoi);
                if (clearedRoi != null)
                {
                    IUndoRedoCommand? clearCommand = _dependencies.CreateStateCommand(selectedRoi, oldState, clearedRoi);
                    if (clearCommand != null)
                    {
                        _dependencies.ExecuteUndoRedoCommand(clearCommand);
                        _dependencies.DrawRois();
                    }
                }

                return;
            }

            IUndoRedoCommand? command = _dependencies.CreateStateCommand(selectedRoi, oldState, detectedRoi);
            if (command == null)
            {
                return;
            }

            _dependencies.ExecuteUndoRedoCommand(command);
            _dependencies.DrawRois();
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
            string? filePath = _dependencies.ShowSaveSnapshotDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                _dependencies.RenderRoot.UpdateLayout();
                var bitmap = new RenderTargetBitmap(
                    Math.Max(1, (int)Math.Ceiling(_dependencies.RenderRoot.ActualWidth)),
                    Math.Max(1, (int)Math.Ceiling(_dependencies.RenderRoot.ActualHeight)),
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(_dependencies.RenderRoot);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                await using var stream = File.Create(filePath);
                await Task.Run(() => encoder.Save(stream));
                _dependencies.ShowStatusHint(UiText.Get("StatusExportPngSuccess"), StatusHintKind.Success);
            }
            catch (Exception ex)
            {
                _dependencies.ShowNonCriticalError(UiText.Get("ErrorExportPngTitle"), UiText.Get("ErrorExportPngMessage"), ex);
            }
        }

        public async Task ExportAnalysisCsvAsync()
        {
            string? filePath = _dependencies.ShowSaveAnalysisCsvDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                var exportContext = new RoiAnalysisExportContext(
                    _dependencies.GetCurrentImagePath(),
                    _dependencies.GetPluginRegistry(),
                    _dependencies.GetRenderSettings(),
                    QualityProfile: _dependencies.GetQualityProfile());
                await RoiAnalysisExportService.SaveCsvAsync(
                    filePath,
                    _dependencies.GetAllRois(),
                    _dependencies.GetAnalysisBitmapSource(),
                    _dependencies.GetPixelSize(),
                    _dependencies.GetPhysicalUnit(),
                    _dependencies.GetCalibration(),
                    exportContext: exportContext);
                _dependencies.ShowStatusHint(UiText.Get("StatusExportCsvWithMetadataSuccess"), StatusHintKind.Success);
            }
            catch (Exception ex)
            {
                _dependencies.ShowNonCriticalError(UiText.Get("ErrorExportAnalysisTitle"), UiText.Get("ErrorExportAnalysisMessage"), ex);
            }
        }

        public async Task ExportBatchAnalysisCsvAsync()
        {
            string[] imagePaths = _dependencies.ShowOpenBatchImageFilesDialog();
            if (imagePaths.Length == 0)
            {
                return;
            }

            string? outputPath = _dependencies.ShowSaveAnalysisCsvDialog();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            try
            {
                BatchRoiAnalysisExportSummary summary = await BatchRoiAnalysisExportService.ExportAsync(
                    outputPath,
                    imagePaths,
                    _dependencies.GetAllRois(),
                    _dependencies.GetPixelSize(),
                    _dependencies.GetPhysicalUnit(),
                    _dependencies.GetCalibration(),
                    _dependencies.GetPluginRegistry(),
                    _dependencies.GetRenderSettings(),
                    qualityProfile: _dependencies.GetQualityProfile());
                _dependencies.ShowStatusHint(
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
            catch (Exception ex)
            {
                _dependencies.ShowNonCriticalError(UiText.Get("ErrorExportAnalysisTitle"), UiText.Get("ErrorExportBatchCsvMessage"), ex);
            }
        }

        public void ShowAnalysisSummary()
        {
            string summary = RoiAnalysisExportService.BuildSummary(_dependencies.GetAllRois(), _dependencies.GetAnalysisBitmapSource(), _dependencies.GetPixelSize(), _dependencies.GetPhysicalUnit(), _dependencies.GetCalibration());
            _dependencies.ShowReadOnlyText(UiText.Get("DialogAnalysisSummaryTitle"), summary);
        }

        public void UpdateContextMenuState() => _dependencies.UpdateContextMenuState();

        private static CaliperMeasureRoi CreateDetectedLineMeasureRoi(CaliperMeasureRoi source, LineMeasureGradientDetectionResult detectionResult)
        {
            var detected = (CaliperMeasureRoi)source.Clone();
            RoiDetectionResultMapper.Apply(detected, detectionResult);
            return detected;
        }

        private static CircularCaliperMeasureRoi CreateDetectedCircularCaliperRoi(CircularCaliperMeasureRoi source, CircularCaliperDetectionResult detectionResult, ImageAnalysisQualityProfile profile)
        {
            var detected = (CircularCaliperMeasureRoi)source.Clone();
            RoiDetectionResultMapper.Apply(detected, detectionResult, profile);
            return detected;
        }
    }
}
