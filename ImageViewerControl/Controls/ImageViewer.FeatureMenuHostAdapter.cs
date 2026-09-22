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
                CaliperMeasureRoi line when ImageAnalysisService.TryDetectLineMeasureEdges(bitmap, line, out LineMeasureGradientDetectionResult lineDetectionResult) => CreateDetectedLineMeasureRoi(line, lineDetectionResult),
                CircularCaliperMeasureRoi circular when ImageAnalysisService.TryDetectCircularCaliperEdges(bitmap, circular, out CircularCaliperDetectionResult circularDetectionResult) => CreateDetectedCircularCaliperRoi(circular, circularDetectionResult),
                _ => null
            };

            if (detectedRoi == null)
            {
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
                _dependencies.ShowStatusHint(UiText.Get("StatusExportPngSuccess"));
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
                await RoiAnalysisExportService.SaveCsvAsync(filePath, _dependencies.GetAllRois(), _dependencies.GetAnalysisBitmapSource(), _dependencies.GetPixelSize(), _dependencies.GetPhysicalUnit());
                _dependencies.ShowStatusHint(UiText.Get("StatusExportCsvSuccess"));
            }
            catch (Exception ex)
            {
                _dependencies.ShowNonCriticalError(UiText.Get("ErrorExportAnalysisTitle"), UiText.Get("ErrorExportAnalysisMessage"), ex);
            }
        }

        public void ShowAnalysisSummary()
        {
            string summary = RoiAnalysisExportService.BuildSummary(_dependencies.GetAllRois(), _dependencies.GetAnalysisBitmapSource(), _dependencies.GetPixelSize(), _dependencies.GetPhysicalUnit());
            _dependencies.ShowReadOnlyText(UiText.Get("DialogAnalysisSummaryTitle"), summary);
        }

        public void UpdateContextMenuState() => _dependencies.UpdateContextMenuState();

        private static CaliperMeasureRoi CreateDetectedLineMeasureRoi(CaliperMeasureRoi source, LineMeasureGradientDetectionResult detectionResult)
        {
            var detected = (CaliperMeasureRoi)source.Clone();
            RoiDetectionResultMapper.Apply(detected, detectionResult);
            return detected;
        }

        private static CircularCaliperMeasureRoi CreateDetectedCircularCaliperRoi(CircularCaliperMeasureRoi source, CircularCaliperDetectionResult detectionResult)
        {
            var detected = (CircularCaliperMeasureRoi)source.Clone();
            RoiDetectionResultMapper.Apply(detected, detectionResult);
            return detected;
        }
    }
}