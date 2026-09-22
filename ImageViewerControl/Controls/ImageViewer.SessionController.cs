using System;
using System.IO;
using System.Threading.Tasks;
using ImageViewer.Abstractions;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using System.Windows.Threading;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerSessionControllerDependencies
    {
        public required ImageViewerSessionPersistenceWorkflow Persistence { get; init; }

        public required ImageViewerAutoSaveWorkflow AutoSave { get; init; }
    }

    internal sealed class ImageViewerSessionPersistenceWorkflow
    {
        public required Func<string?> ShowSaveSessionDialog { get; init; }

        public required Func<string?> ShowOpenSessionDialog { get; init; }

        public required Func<string?> ShowSaveProjectPackageDialog { get; init; }

        public required IImageViewerSessionService SessionService { get; init; }

        public required IImageViewerRecentProjectService RecentProjectService { get; init; }

        public required IImageViewerProjectPackageService ProjectPackageService { get; init; }

        public required Func<RoiPluginRegistry> GetPluginRegistry { get; init; }

        public required Func<IReadOnlyList<RoiBase>> GetAllRois { get; init; }

        public required Func<double> GetPixelSize { get; init; }

        public required Action<double> SetPixelSize { get; init; }

        public required Func<string> GetPhysicalUnit { get; init; }

        public required Action<string> SetPhysicalUnit { get; init; }

        public required Func<ImageViewerViewportState> GetCurrentViewportState { get; init; }

        public required Func<string?> TryGetCurrentImagePath { get; init; }

        public required Action<string, bool> LoadImageFromFile { get; init; }

        public required Action<IReadOnlyList<RoiBase>> ReplaceAllRois { get; init; }

        public required Action<ImageViewerViewportState> ApplyViewportState { get; init; }

        public required Action DrawRois { get; init; }

        public required Action<string, string, Exception> ShowNonCriticalError { get; init; }

        public required Action<string, string> ShowWarning { get; init; }

        public required Action<string> ShowStatusHint { get; init; }

        public required Action ClearUndoHistory { get; init; }

        public required Action UpdateContextMenuState { get; init; }
    }

    internal sealed class ImageViewerAutoSaveWorkflow
    {
        public required Dispatcher Dispatcher { get; init; }

        public required Func<bool> HasContent { get; init; }

        public required Func<ImageViewerViewportState> GetCurrentViewportState { get; init; }

        public required Func<string?> TryGetCurrentImagePath { get; init; }

        public required Func<IReadOnlyList<RoiBase>> GetAllRois { get; init; }

        public required Func<double> GetPixelSize { get; init; }

        public required Func<string> GetPhysicalUnit { get; init; }

        public required IImageViewerSessionService SessionService { get; init; }

        public required Func<RoiPluginRegistry> GetPluginRegistry { get; init; }

        public required Action<string, Exception> LogNonCriticalError { get; init; }

        public required Action<string> ShowStatusHint { get; init; }
    }

    internal sealed class ImageViewerSessionController : IDisposable
    {
        private const string SessionProjectKind = "session";
        private const string PackageProjectKind = "package";
        private readonly ImageViewerSessionPersistenceWorkflow _persistence;
        private readonly ImageViewerRecentProjectCatalog _recentProjectCatalog;
        private readonly ImageViewerAutoSaveController _autoSaveController;
        private string? _currentProjectPath;

        public ImageViewerSessionController(
            ImageViewerSessionControllerDependencies dependencies,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerSessionStoragePolicy sessionStoragePolicy)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            ArgumentNullException.ThrowIfNull(periodicTaskSchedulerFactory);
            ArgumentNullException.ThrowIfNull(sessionStoragePolicy);

            _persistence = dependencies.Persistence ?? throw new ArgumentNullException(nameof(dependencies));
            _recentProjectCatalog = new ImageViewerRecentProjectCatalog(_persistence.RecentProjectService, sessionStoragePolicy.RecentProjectsFilePath);
            _autoSaveController = new ImageViewerAutoSaveController(dependencies.AutoSave, periodicTaskSchedulerFactory, sessionStoragePolicy);
        }

        public bool IsAutoSaveEnabled => _autoSaveController.IsEnabled;

        public async Task SaveSessionAsync()
        {
            string? filePath = _persistence.ShowSaveSessionDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                ImageViewerViewportState viewportState = _persistence.GetCurrentViewportState();
                await _persistence.SessionService.SaveToFileAsync(
                    filePath,
                    _persistence.TryGetCurrentImagePath(),
                    _persistence.GetAllRois(),
                    _persistence.GetPixelSize(),
                    _persistence.GetPhysicalUnit(),
                    viewportState.Scale,
                    viewportState.TranslateX,
                    viewportState.TranslateY,
                    _persistence.GetPluginRegistry());
                SetCurrentProject(filePath, SessionProjectKind);
                _persistence.ShowStatusHint(UiText.Get("StatusSaveSessionSuccess"));
            }
            catch (Exception ex)
            {
                _persistence.ShowNonCriticalError(UiText.Get("ErrorSaveSessionTitle"), UiText.Get("ErrorSaveSessionMessage"), ex);
            }
        }

        public async Task LoadProjectAsync()
        {
            string? filePath = _persistence.ShowOpenSessionDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await OpenProjectAsync(filePath);
        }

        public async Task ExportProjectPackageAsync()
        {
            string? filePath = _persistence.ShowSaveProjectPackageDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                ImageViewerViewportState viewportState = _persistence.GetCurrentViewportState();
                await _persistence.ProjectPackageService.ExportAsync(
                    filePath,
                    _persistence.TryGetCurrentImagePath(),
                    _persistence.GetAllRois(),
                    _persistence.GetPixelSize(),
                    _persistence.GetPhysicalUnit(),
                    viewportState.Scale,
                    viewportState.TranslateX,
                    viewportState.TranslateY,
                    _persistence.GetPluginRegistry());
                SetCurrentProject(filePath, PackageProjectKind);
                _persistence.ShowStatusHint(UiText.Get("StatusExportPackageSuccess"));
            }
            catch (Exception ex)
            {
                _persistence.ShowNonCriticalError(UiText.Get("ErrorExportProjectPackageTitle"), UiText.Get("ErrorExportProjectPackageMessage"), ex);
            }
        }

        public IReadOnlyList<ImageViewerDynamicMenuItem> GetRecentProjectMenuItems()
        {
            return _recentProjectCatalog.GetMenuItems();
        }

        public Task OpenRecentProjectAsync(string filePath)
        {
            return OpenProjectAsync(filePath);
        }

        public void ToggleAutoSave()
        {
            _autoSaveController.Toggle();
        }

        public void StartAutoSave()
        {
            _autoSaveController.Start();
        }

        public void StopAutoSave()
        {
            _autoSaveController.StopScheduling();
        }

        public Task DrainAutoSaveAsync(CancellationToken cancellationToken = default)
        {
            return _autoSaveController.DrainAsync(cancellationToken);
        }

        public void Dispose()
        {
            _autoSaveController.Dispose();
        }

        public async Task OpenProjectAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _recentProjectCatalog.RemoveMissing(filePath);
                _persistence.ShowWarning(UiText.Get("WarningFileMissingTitle"), UiText.Get("WarningRecentProjectRemoved"));
                _persistence.UpdateContextMenuState();
                return;
            }

            try
            {
                ImageViewerSessionData session = string.Equals(Path.GetExtension(filePath), ".ivpkg", StringComparison.OrdinalIgnoreCase)
                    ? await _persistence.ProjectPackageService.LoadAsync(filePath, _persistence.GetPluginRegistry())
                    : await _persistence.SessionService.LoadFromFileAsync(filePath, _persistence.GetPluginRegistry());

                ApplySession(session);
                SetCurrentProject(filePath, string.Equals(Path.GetExtension(filePath), ".ivpkg", StringComparison.OrdinalIgnoreCase) ? PackageProjectKind : SessionProjectKind);
                _persistence.ClearUndoHistory();
                _persistence.ShowStatusHint(UiText.Get("StatusLoadSessionSuccess"));
            }
            catch (Exception ex)
            {
                _persistence.ShowNonCriticalError(UiText.Get("ErrorLoadProjectTitle"), UiText.Get("ErrorLoadProjectMessage"), ex);
            }
        }

        private void ApplySession(ImageViewerSessionData session)
        {
            if (!string.IsNullOrWhiteSpace(session.ImagePath) && File.Exists(session.ImagePath))
            {
                _persistence.LoadImageFromFile(session.ImagePath, false);
            }

            _persistence.ReplaceAllRois(session.Rois);
            _persistence.SetPixelSize(session.PixelSize);
            _persistence.SetPhysicalUnit(session.PhysicalUnit);
            _persistence.ApplyViewportState(new ImageViewerViewportState(session.Scale, session.TranslateX, session.TranslateY));
            _persistence.DrawRois();
            _persistence.UpdateContextMenuState();
        }

        private void SetCurrentProject(string filePath, string projectKind)
        {
            _currentProjectPath = Path.GetFullPath(filePath);
            _autoSaveController.SetCurrentProject(_currentProjectPath);
            _recentProjectCatalog.Remember(_currentProjectPath, projectKind);
            _persistence.UpdateContextMenuState();
        }
    }
}