using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerAutoSaveController : IDisposable
    {
        private readonly ImageViewerAutoSaveWorkflow _workflow;
        private readonly IImageViewerPeriodicTaskScheduler _autoSaveScheduler;
        private readonly string _autoSaveDirectory;
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private CancellationTokenSource _lifecycleCancellationTokenSource = new();
        private string? _currentProjectPath;

        public ImageViewerAutoSaveController(
            ImageViewerAutoSaveWorkflow workflow,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerSessionStoragePolicy sessionStoragePolicy)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            ArgumentNullException.ThrowIfNull(periodicTaskSchedulerFactory);
            ArgumentNullException.ThrowIfNull(sessionStoragePolicy);

            _autoSaveDirectory = sessionStoragePolicy.AutoSaveDirectory;
            _autoSaveScheduler = periodicTaskSchedulerFactory.Create(
                AutoSaveAsync,
                _workflow.Dispatcher,
                DispatcherPriority.Background,
                sessionStoragePolicy.AutoSaveInterval);
            _autoSaveScheduler.Start();
        }

        public void Start()
        {
            if (_lifecycleCancellationTokenSource.IsCancellationRequested)
            {
                _lifecycleCancellationTokenSource.Dispose();
                _lifecycleCancellationTokenSource = new CancellationTokenSource();
            }

            _autoSaveScheduler.Start();
        }

        public void StopScheduling()
        {
            _lifecycleCancellationTokenSource.Cancel();
            _autoSaveScheduler.StopScheduling();
        }

        public Task DrainAsync(CancellationToken cancellationToken = default)
        {
            return _autoSaveScheduler.DrainAsync(cancellationToken);
        }

        public bool IsEnabled { get; private set; } = true;

        public void SetCurrentProject(string? filePath)
        {
            _currentProjectPath = string.IsNullOrWhiteSpace(filePath)
                ? null
                : Path.GetFullPath(filePath);
        }

        public void Toggle()
        {
            IsEnabled = !IsEnabled;
        }

        public void Dispose()
        {
            StopScheduling();
            _autoSaveScheduler.Dispose();
            _lifecycleCancellationTokenSource.Dispose();
            _saveGate.Dispose();
        }

        private async Task AutoSaveAsync()
        {
            if (!IsEnabled || !_workflow.HasContent())
            {
                return;
            }

            CancellationToken cancellationToken = _lifecycleCancellationTokenSource.Token;
            if (!await _saveGate.WaitAsync(0, cancellationToken))
            {
                return;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(_autoSaveDirectory);
                string filePath = Path.Combine(_autoSaveDirectory, $"{GetAutoSaveFileName()}.ivsession");
                ImageViewerViewportState viewportState = _workflow.GetCurrentViewportState();
                await _workflow.SessionService.SaveToFileAsync(
                    filePath,
                    _workflow.TryGetCurrentImagePath(),
                    _workflow.GetAllRois(),
                    _workflow.GetPixelSize(),
                    _workflow.GetPhysicalUnit(),
                    viewportState.Scale,
                    viewportState.TranslateX,
                    viewportState.TranslateY,
                    _workflow.GetPluginRegistry(),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _workflow.LogNonCriticalError("Auto save failed", ex);
            }
            finally
            {
                _saveGate.Release();
            }
        }

        private string GetAutoSaveFileName()
        {
            string baseName = Path.GetFileNameWithoutExtension(_currentProjectPath) ?? "autosave";
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(baseName) ? "autosave" : baseName;
        }
    }
}