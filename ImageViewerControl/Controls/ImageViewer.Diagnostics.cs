using System;
using System.Threading.Tasks;
using ImageViewer.Models;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private ImageViewerBackgroundOperationObserver? _backgroundOperationObserver;

        private void LogNonCriticalError(string context, Exception ex)
        {
            Logger.LogError(context, ex);
        }

        private ImageViewerBackgroundOperationObserver BackgroundOperationObserver =>
            _backgroundOperationObserver ??= new ImageViewerBackgroundOperationObserver(LogNonCriticalError);

        private void ShowNonCriticalError(string title, string message, Exception ex)
        {
            LogNonCriticalError(title, ex);
            DiagnosticErrorText = $"{title}: {message}";
            HasDiagnosticError = true;
            _dialogWorkflowService.ShowWarning(title, message);
        }

        private async Task RunUiOperationAsync(string operationName, Func<Task> operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
            ArgumentNullException.ThrowIfNull(operation);

            try
            {
                await operation();
            }
            catch (OperationCanceledException)
            {
                Logger.LogInfo($"UI operation canceled: {operationName}; category=Cancellation");
            }
            catch (Exception ex)
            {
                string category = ImageViewerExceptionClassifier.Classify(ex);
                Logger.LogError($"UI operation failed: {operationName}; category={category}", ex);
                DiagnosticErrorText = $"操作失败 [{category}]：{operationName}。";
                HasDiagnosticError = true;
                _dialogWorkflowService.ShowWarning("操作失败", $"操作“{operationName}”失败。请查看诊断信息。" );
            }
        }

        private async Task RunShutdownOperationAsync(string operationName, Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (OperationCanceledException)
            {
                Logger.LogInfo($"Shutdown operation canceled: {operationName}; category=Cancellation");
            }
            catch (Exception ex)
            {
                string category = ImageViewerExceptionClassifier.Classify(ex);
                Logger.LogError($"Shutdown operation failed: {operationName}; category={category}", ex);
            }
        }

        private void DismissDiagnosticError()
        {
            HasDiagnosticError = false;
            DiagnosticErrorText = string.Empty;
        }

        private static RoiStateCommand CreateStateCommand(RoiBase roi, RoiBase oldState, RoiBase newState)
        {
            return new RoiStateCommand(roi, oldState, newState);
        }
    }
}