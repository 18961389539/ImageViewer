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

        public required Func<CameraCalibration?> GetCalibration { get; init; }

        public required Action<CameraCalibration?> SetCalibration { get; init; }

        public required Func<ImageViewerViewportState> GetCurrentViewportState { get; init; }

        public required Func<string?> TryGetCurrentImagePath { get; init; }

        public required Action<string, bool> LoadImageFromFile { get; init; }

        public required Action<IReadOnlyList<RoiBase>> ReplaceAllRois { get; init; }

        public required Action<ImageViewerViewportState> ApplyViewportState { get; init; }

        public required Action DrawRois { get; init; }

        public required Action<string, string, Exception> ShowNonCriticalError { get; init; }

        public required Action<string, string> ShowWarning { get; init; }

        public required Action<string, StatusHintKind> ShowStatusHint { get; init; }

        public required Action ClearUndoHistory { get; init; }

        public required Action UpdateContextMenuState { get; init; }

        /// <summary>
        /// 加载时未能识别的 ROI 载荷。
        /// Chinese: 打开工程时由会话控制器写入，保存（含自动保存）时原样回写，避免缺插件导致标注被抹掉。
        /// English: ROI payloads carried over from the last load so saves write them back verbatim.
        /// </summary>
        public IReadOnlyList<RoiPersistenceData> UnresolvedRois { get; set; } = [];

        /// <summary>
        /// 采集当前需要落盘的一整份状态。
        /// Chinese: 供保存会话 / 导出项目包复用，避免在每个调用点重复逐参数拼装。
        /// English: Captures the full state to persist so every call site stops reassembling the same parameter list.
        /// </summary>
        public ImageViewerPersistenceSnapshot CaptureSnapshot()
        {
            ImageViewerViewportState viewportState = GetCurrentViewportState();
            return new ImageViewerPersistenceSnapshot(
                TryGetCurrentImagePath(),
                GetAllRois(),
                GetPixelSize(),
                GetPhysicalUnit(),
                viewportState.Scale,
                viewportState.TranslateX,
                viewportState.TranslateY,
                GetCalibration())
            {
                UnresolvedRois = UnresolvedRois
            };
        }
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

        public required Func<CameraCalibration?> GetCalibration { get; init; }

        public required IImageViewerSessionService SessionService { get; init; }

        public required Func<RoiPluginRegistry> GetPluginRegistry { get; init; }

        public required Action<string, Exception> LogNonCriticalError { get; init; }

        public required Action<string, StatusHintKind> ShowStatusHint { get; init; }

        /// <summary>
        /// 加载时未能识别的 ROI 载荷（与手动保存共用同一份来源）。
        /// </summary>
        public IReadOnlyList<RoiPersistenceData> UnresolvedRois { get; set; } = [];

        /// <summary>
        /// 采集当前需要落盘的一整份状态。
        /// Chinese: 自动保存与手动保存共用同一份载荷契约。
        /// English: Auto save and manual save share the same payload contract.
        /// </summary>
        public ImageViewerPersistenceSnapshot CaptureSnapshot()
        {
            ImageViewerViewportState viewportState = GetCurrentViewportState();
            return new ImageViewerPersistenceSnapshot(
                TryGetCurrentImagePath(),
                GetAllRois(),
                GetPixelSize(),
                GetPhysicalUnit(),
                viewportState.Scale,
                viewportState.TranslateX,
                viewportState.TranslateY,
                GetCalibration())
            {
                UnresolvedRois = UnresolvedRois
            };
        }
    }

    internal sealed class ImageViewerSessionController : IDisposable
    {
        private const string SessionProjectKind = "session";
        private const string PackageProjectKind = "package";
        private readonly ImageViewerSessionPersistenceWorkflow _persistence;
        private readonly ImageViewerAutoSaveWorkflow _autoSave;
        private readonly ImageViewerRecentProjectCatalog _recentProjectCatalog;
        private readonly ImageViewerAutoSaveController _autoSaveController;
        private readonly string _autoSaveDirectory;
        private string? _currentProjectPath;
        private string? _recoveryFilePath;
        private bool _recoveryPromptDismissed;
        private bool _isDirty;

        public event EventHandler? StateChanged;

        public ImageViewerSessionController(
            ImageViewerSessionControllerDependencies dependencies,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerSessionStoragePolicy sessionStoragePolicy)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            ArgumentNullException.ThrowIfNull(periodicTaskSchedulerFactory);
            ArgumentNullException.ThrowIfNull(sessionStoragePolicy);

            _persistence = dependencies.Persistence ?? throw new ArgumentNullException(nameof(dependencies));
            _autoSave = dependencies.AutoSave;
            _autoSaveDirectory = sessionStoragePolicy.AutoSaveDirectory;
            _recentProjectCatalog = new ImageViewerRecentProjectCatalog(_persistence.RecentProjectService, sessionStoragePolicy.RecentProjectsFilePath);
            _autoSaveController = new ImageViewerAutoSaveController(dependencies.AutoSave, periodicTaskSchedulerFactory, sessionStoragePolicy);
            RefreshRecoverySnapshot();
        }

        public bool IsAutoSaveEnabled => _autoSaveController.IsEnabled;

        public bool IsDirty => _isDirty;

        public bool HasRecoverySnapshot => !_recoveryPromptDismissed && _recoveryFilePath != null;

        public void MarkDirty()
        {
            if (_isDirty)
            {
                return;
            }

            _isDirty = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void MarkClean()
        {
            if (!_isDirty)
            {
                return;
            }

            _isDirty = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void DismissRecoveryPrompt()
        {
            if (_recoveryPromptDismissed || _recoveryFilePath == null)
            {
                return;
            }

            _recoveryPromptDismissed = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task SaveSessionAsync()
        {
            string? filePath = _persistence.ShowSaveSessionDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                await _persistence.SessionService.SaveToFileAsync(
                    filePath,
                    _persistence.CaptureSnapshot(),
                    _persistence.GetPluginRegistry());
                SetCurrentProject(filePath, SessionProjectKind);
                CompleteSuccessfulSave();
                _persistence.ShowStatusHint(UiText.Get("StatusSaveSessionSuccess"), StatusHintKind.Success);
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
                await _persistence.ProjectPackageService.ExportAsync(
                    filePath,
                    _persistence.CaptureSnapshot(),
                    _persistence.GetPluginRegistry());
                SetCurrentProject(filePath, PackageProjectKind);
                CompleteSuccessfulSave();
                _persistence.ShowStatusHint(UiText.Get("StatusExportPackageSuccess"), StatusHintKind.Success);
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

        public async Task RecoverLatestAutoSaveAsync()
        {
            RefreshRecoverySnapshot();
            string? recoveryFilePath = _recoveryFilePath;
            if (recoveryFilePath == null)
            {
                _persistence.ShowStatusHint(UiText.Get("StatusNoRecoverySnapshot"), StatusHintKind.Info);
                return;
            }

            try
            {
                ImageViewerSessionData session = await _persistence.SessionService.LoadFromFileAsync(
                    recoveryFilePath,
                    _persistence.GetPluginRegistry());

                ApplySession(session);
                _persistence.ClearUndoHistory();
                ReportUnresolvedRois(session.UnresolvedRois);
                _recoveryPromptDismissed = true;
                StateChanged?.Invoke(this, EventArgs.Empty);
                MarkDirty();
                _persistence.ShowStatusHint(UiText.Get("StatusRecoveryLoaded"), StatusHintKind.Success);
            }
            catch (Exception ex)
            {
                _persistence.ShowNonCriticalError(UiText.Get("ErrorRecoveryTitle"), UiText.Get("ErrorRecoveryMessage"), ex);
            }
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
                MarkClean();
                _persistence.ShowStatusHint(UiText.Get("StatusLoadSessionSuccess"), StatusHintKind.Success);
                ReportUnresolvedRois(session.UnresolvedRois);
            }
            catch (Exception ex)
            {
                _persistence.ShowNonCriticalError(UiText.Get("ErrorLoadProjectTitle"), UiText.Get("ErrorLoadProjectMessage"), ex);
            }
        }

        private void ApplySession(ImageViewerSessionData session)
        {
            if (!string.IsNullOrWhiteSpace(session.ImagePath))
            {
                if (File.Exists(session.ImagePath))
                {
                    _persistence.LoadImageFromFile(session.ImagePath, false);
                }
                else
                {
                    _persistence.ShowStatusHint(UiText.Format("StatusSessionImageMissing", session.ImagePath), StatusHintKind.Error);
                }
            }

            _persistence.ReplaceAllRois(session.Rois);
            _persistence.SetPixelSize(session.PixelSize);
            _persistence.SetPhysicalUnit(session.PhysicalUnit);
            _persistence.SetCalibration(session.Calibration);
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

        private void CompleteSuccessfulSave()
        {
            MarkClean();
            string defaultRecoveryPath = Path.Combine(_autoSaveDirectory, "autosave.ivsession");
            string[] recoveryPaths = new[] { _recoveryFilePath, defaultRecoveryPath }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (recoveryPaths.Length == 0)
            {
                return;
            }

            try
            {
                foreach (string recoveryPath in recoveryPaths)
                {
                    if (File.Exists(recoveryPath))
                    {
                        File.Delete(recoveryPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _persistence.ShowStatusHint(UiText.Get("StatusRecoveryCleanupFailed"), StatusHintKind.Info);
                _autoSave.LogNonCriticalError("Clean up recovered autosave", ex);
                return;
            }

            _recoveryFilePath = null;
            _recoveryPromptDismissed = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RefreshRecoverySnapshot()
        {
            try
            {
                _recoveryFilePath = Directory.Exists(_autoSaveDirectory)
                    ? Directory
                        .EnumerateFiles(_autoSaveDirectory, "*.ivsession", SearchOption.TopDirectoryOnly)
                        .Where(path => new FileInfo(path).Length > 0)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault()
                    : null;
            }
            catch
            {
                _recoveryFilePath = null;
            }
        }

        /// <summary>
        /// 上报并对齐"未识别标注"载荷。
        /// Chinese: 缺插件或类型键变更时，未识别的标注不再被静默丢弃——状态栏给出数量与类型提示，
        /// 同时把原始载荷保留在保存链路上，下次保存会原样回写，重新获得插件后仍可正常打开。
        /// English: Surfaces unresolved ROI payloads (missing plugin / renamed type key) with a status hint and keeps
        /// them on the save path so the next save writes them back verbatim instead of erasing them.
        /// </summary>
        private void ReportUnresolvedRois(IReadOnlyList<RoiPersistenceData> unresolvedRois)
        {
            _persistence.UnresolvedRois = unresolvedRois;
            _autoSave.UnresolvedRois = unresolvedRois;
            if (unresolvedRois.Count == 0)
            {
                return;
            }

            string typeNames = string.Join(
                ", ",
                unresolvedRois
                    .Select(item => item.Type)
                    .Where(type => !string.IsNullOrWhiteSpace(type))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

            _persistence.ShowStatusHint(
                UiText.Format("StatusProjectUnresolvedRois", unresolvedRois.Count, typeNames),
                StatusHintKind.Error);
        }
    }
}
