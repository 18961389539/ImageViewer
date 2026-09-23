using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Abstractions;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using ImageViewer.Utils;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerDialogWorkflowDependencies
    {
        public required ImageViewerDialogImageLoadWorkflow ImageLoading { get; init; }

        public required ImageViewerDialogRoiWorkflow RoiEditing { get; init; }

        public required ImageViewerDialogCalibrationWorkflow Calibration { get; init; }
    }

    internal sealed class ImageViewerDialogImageLoadWorkflow
    {
        public required Func<int> GetRetryCount { get; init; }

        public required Func<int> GetRetryDelayMilliseconds { get; init; }

        public required Action<ImageSource> SetImage { get; init; }

        public required Action<bool, string, double, bool> SetImageLoadState { get; init; }

        public required Action FitToView { get; init; }

        public required Action ClearUndoHistory { get; init; }

        public required Action<string, string, Exception> ShowNonCriticalError { get; init; }
    }

    internal sealed class ImageViewerDialogRoiWorkflow
    {
        public required Func<RoiPluginRegistry> GetPluginRegistry { get; init; }

        public required Action DrawRois { get; init; }

        public required Action DrawSelectedRoiLayer { get; init; }

        public required Action<RoiBase> HandleRoiEdited { get; init; }

        public required Func<RoiBase, RoiBase, RoiBase, IUndoRedoCommand?> CreateStateCommand { get; init; }

        public required Action<IUndoRedoCommand> ExecuteUndoRedoCommand { get; init; }

        public required Func<CaliperMeasureRoi, bool> TryApplyCaliperDetection { get; init; }

        public required Func<LineCaliperMeasureRoi, bool> TryApplyLineCaliperDetection { get; init; }

        public required Func<CircularCaliperMeasureRoi, bool> TryApplyCircularCaliperDetection { get; init; }
    }

    internal sealed class ImageViewerDialogCalibrationWorkflow
    {
        public required Func<string> GetPhysicalUnit { get; init; }

        public required Action<double, string, double, double> ApplyCalibration { get; init; }
    }

    internal sealed class ImageViewerDialogWorkflowService
    {
        private readonly ImageViewerDialogWorkflowDependencies _dependencies;
        private readonly IImageViewerDialogWorkflowAdapter _adapter;
        private string? _lastFailedImagePath;

        public ImageViewerDialogWorkflowService(ImageViewerDialogWorkflowDependencies dependencies, IImageViewerDialogWorkflowAdapter adapter)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public async Task OpenImageAsync()
        {
            string? filePath = _adapter.ShowOpenImageDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await OpenImageFromPathAsync(filePath);
        }

        public async Task RetryLastImageLoadAsync()
        {
            if (string.IsNullOrWhiteSpace(_lastFailedImagePath))
            {
                return;
            }

            await OpenImageFromPathAsync(_lastFailedImagePath);
        }

        private async Task OpenImageFromPathAsync(string filePath)
        {
            ImageViewerDialogImageLoadWorkflow imageLoading = _dependencies.ImageLoading;
            int retryCount = Math.Max(0, imageLoading.GetRetryCount());
            int retryDelayMilliseconds = Math.Max(0, imageLoading.GetRetryDelayMilliseconds());
            Exception? lastException = null;

            imageLoading.SetImageLoadState(true, UiText.Get("ImageLoadStatusStarting"), 5, false);

            for (int attempt = 0; attempt <= retryCount; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        imageLoading.SetImageLoadState(true, UiText.Get("ImageLoadStatusRetrying"), 15 + attempt * 10, false);
                        if (retryDelayMilliseconds > 0)
                        {
                            await Task.Delay(retryDelayMilliseconds);
                        }
                    }

                    imageLoading.SetImageLoadState(true, UiText.Get("ImageLoadStatusDecoding"), 45, false);
                    BitmapImage bitmap = await Task.Run(() => CreateBitmapFromFile(filePath));
                    imageLoading.SetImageLoadState(true, UiText.Get("ImageLoadStatusApplying"), 85, false);
                    imageLoading.SetImage(bitmap);
                    imageLoading.FitToView();
                    imageLoading.ClearUndoHistory();
                    imageLoading.SetImageLoadState(false, UiText.Get("ImageLoadStatusReady"), 100, false);
                    _lastFailedImagePath = null;
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt < retryCount)
                    {
                        continue;
                    }
                }
            }

            _lastFailedImagePath = filePath;
            imageLoading.SetImageLoadState(true, UiText.Get("ImageLoadStatusFailed"), 0, true);
            imageLoading.ShowNonCriticalError(UiText.Get("ErrorOpenImageTitle"), UiText.Get("ErrorOpenImageMessage"), lastException ?? new InvalidOperationException("Image load failed."));
        }

        public string? ShowTextInput(string message, string defaultValue)
        {
            return _adapter.ShowTextInput(message, defaultValue);
        }

        public string? ShowSaveRoiDialog()
        {
            return _adapter.ShowSaveRoiDialog();
        }

        public string? ShowOpenRoiDialog()
        {
            return _adapter.ShowOpenRoiDialog();
        }

        public string? ShowSaveSessionDialog()
        {
            return _adapter.ShowSaveSessionDialog();
        }

        public string? ShowOpenSessionDialog()
        {
            return _adapter.ShowOpenSessionDialog();
        }

        public string? ShowSaveProjectPackageDialog()
        {
            return _adapter.ShowSaveProjectPackageDialog();
        }

        public string? ShowSaveSnapshotDialog()
        {
            return _adapter.ShowSaveSnapshotDialog();
        }

        public string? ShowSaveAnalysisCsvDialog()
        {
            return _adapter.ShowSaveAnalysisCsvDialog();
        }

        public void ShowReadOnlyText(string title, string text)
        {
            _adapter.ShowReadOnlyText(title, text);
        }

        public void ShowWarning(string title, string message)
        {
            _adapter.ShowWarning(title, message);
        }

        public void ShowRoiProperties(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);
            ImageViewerDialogRoiWorkflow roiEditing = _dependencies.RoiEditing;

            if (roi is CaliperMeasureRoi or LineCaliperMeasureRoi or CircularCaliperMeasureRoi)
            {
                ShowCaliperSettings(roi);
                return;
            }

            FrameworkElement? editor = roiEditing.GetPluginRegistry().FindByRoi(roi)?.CreatePropertyEditor(roi);
            if (editor == null)
            {
                return;
            }

            _adapter.ShowPropertyEditor(UiText.Format("DialogEditRoiTitle", roi.DisplayTypeName), editor);
            roiEditing.HandleRoiEdited(roi);
        }

        public void ShowCaliperSettings(RoiBase roi)
        {
            switch (roi)
            {
                case LineCaliperMeasureRoi lineCaliper:
                    ShowCaliperSettingsDialogCore(lineCaliper, _adapter.ShowLineCaliperSettingsDialog, CreateConfiguredLineCaliperState);
                    return;
                case CircularCaliperMeasureRoi circularCaliper:
                    ShowCaliperSettingsDialogCore(circularCaliper, _adapter.ShowCircularCaliperSettingsDialog, CreateConfiguredCircularCaliperState);
                    return;
                case CaliperMeasureRoi line:
                    ShowCaliperSettingsDialogCore(line, _adapter.ShowLineMeasureCaliperSettingsDialog, CreateConfiguredCaliperState);
                    return;
            }
        }

        public string? ShowRoiLabelDialog(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);
            return ShowTextInput(UiText.Get("DialogRoiLabelPrompt"), roi.Label);
        }

        public void CalibrateSelectedRoi(RoiBase? selectedRoi)
        {
            Point p1;
            Point p2;

            switch (selectedRoi)
            {
                case CaliperMeasureRoi caliper:
                    p1 = caliper.P1;
                    p2 = caliper.P2;
                    break;
                case LineMeasureRoi line:
                    p1 = line.P1;
                    p2 = line.P2;
                    break;
                default:
                    return;
            }

            double pixelDistance = GeometryUtils.Distance(p1, p2);
            if (pixelDistance <= 0)
            {
                return;
            }

            var calibration = _adapter.ShowCalibrationDialog(_dependencies.Calibration.GetPhysicalUnit());
            if (calibration != null)
            {
                _dependencies.Calibration.ApplyCalibration(
                    calibration.Value.Length / pixelDistance,
                    calibration.Value.Unit,
                    calibration.Value.K1,
                    calibration.Value.K2);
            }
        }

        private void ShowCaliperSettingsDialogCore<TCaliper>(
            TCaliper roi,
            Func<TCaliper, Action<TCaliper>?, TCaliper?> showDialog,
            Func<TCaliper, TCaliper, TCaliper> createConfiguredState)
            where TCaliper : RoiBase
        {
            ImageViewerDialogRoiWorkflow roiEditing = _dependencies.RoiEditing;
            var originalState = (TCaliper)roi.Clone();
            TCaliper? configuredState = showDialog(
                roi,
                preview => PreviewConfiguredCaliper(roi, preview, createConfiguredState));
            if (configuredState == null)
            {
                roi.ApplyFrom(originalState);
                roiEditing.DrawRois();
                return;
            }

            RoiBase newState = createConfiguredState(originalState, configuredState);
            roi.ApplyFrom(originalState);

            IUndoRedoCommand? command = roiEditing.CreateStateCommand(roi, originalState, newState);
            if (command != null)
            {
                roiEditing.ExecuteUndoRedoCommand(command);
            }

            roiEditing.DrawRois();
        }

        private void PreviewConfiguredCaliper<TCaliper>(
            TCaliper targetCaliper,
            TCaliper configuredCaliper,
            Func<TCaliper, TCaliper, TCaliper> createConfiguredState)
            where TCaliper : RoiBase
        {
            TCaliper previewState = createConfiguredState(targetCaliper, configuredCaliper);
            targetCaliper.ApplyFrom(previewState);
            _dependencies.RoiEditing.DrawSelectedRoiLayer();
        }

        private CaliperMeasureRoi CreateConfiguredCaliperState(CaliperMeasureRoi geometrySource, CaliperMeasureRoi configuredLine)
        {
            var state = (CaliperMeasureRoi)configuredLine.Clone();
            CopyLineGeometry(geometrySource, state);
            CopyCommonRoiState(geometrySource, state);
            state.ClearDetectedEdges();
            _dependencies.RoiEditing.TryApplyCaliperDetection(state);
            return state;
        }

        private CircularCaliperMeasureRoi CreateConfiguredCircularCaliperState(CircularCaliperMeasureRoi geometrySource, CircularCaliperMeasureRoi configuredCaliper)
        {
            return CreateConfiguredSingleEdgeCaliperState(
                geometrySource,
                configuredCaliper,
                static (source, target) => CopyCircleGeometry(source, target),
                static roi => roi.ClearDetectedEdges(),
                _dependencies.RoiEditing.TryApplyCircularCaliperDetection);
        }

        private LineCaliperMeasureRoi CreateConfiguredLineCaliperState(LineCaliperMeasureRoi geometrySource, LineCaliperMeasureRoi configuredLine)
        {
            return CreateConfiguredSingleEdgeCaliperState(
                geometrySource,
                configuredLine,
                static (source, target) => CopyLineGeometry(source, target),
                static roi => roi.ClearDetectedLine(),
                _dependencies.RoiEditing.TryApplyLineCaliperDetection);
        }

        private static TCaliper CreateConfiguredSingleEdgeCaliperState<TCaliper>(
            TCaliper geometrySource,
            TCaliper configuredCaliper,
            Action<TCaliper, TCaliper> copyGeometry,
            Action<TCaliper> clearDetection,
            Func<TCaliper, bool> tryApplyDetection)
            where TCaliper : RoiBase, ISingleEdgeCaliperRoi
        {
            var state = (TCaliper)configuredCaliper.Clone();
            copyGeometry(geometrySource, state);
            CopyCommonRoiState(geometrySource, state);
            clearDetection(state);
            tryApplyDetection(state);
            return state;
        }

        private static void CopyCommonRoiState(RoiBase source, RoiBase target)
        {
            target.Label = source.Label;
            RoiVisualState.Capture(source).ApplyTo(target, includeSelection: false);
        }

        private static void CopyLineGeometry(LineMeasureRoi source, LineMeasureRoi target)
        {
            target.P1 = source.P1;
            target.P2 = source.P2;
        }

        private static void CopyCircleGeometry(CircleRoi source, CircleRoi target)
        {
            target.Center = source.Center;
            target.Radius = source.Radius;
        }

        private static BitmapImage CreateBitmapFromFile(string filePath)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}