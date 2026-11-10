using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageViewer.Abstractions;
using ImageViewer.Core.Analysis;
using ImageViewer.Core.Display;
using ImageViewer.Core.Geometry;
using ImageViewer.Core.Persistence;
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

        public required Func<string?> ShowSaveRecipeDialog { get; init; }

        public required Func<string?> ShowOpenRecipeDialog { get; init; }

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

        public Func<ImageAnalysisQualityProfile> GetQualityProfile { get; init; } = static () => ImageAnalysisQualityProfile.Default;

        public Action<ImageAnalysisQualityProfile> SetQualityProfile { get; init; } = static _ => { };

        public required Action<CameraCalibration?> SetCalibration { get; init; }

        /// <summary>
        /// 采集当前显示调节（随会话/项目包一起存取）。
        /// Chinese: 带默认值而不是 required —— 快照构造点分散（会话、项目包、恢复索引、测试替身），
        /// 未接线时退回"透传"，保存出来的就是一项不改变显示的设置，比编译不过或丢状态都好。
        /// </summary>
        public Func<DisplayAdjustment> GetDisplay { get; init; } = static () => DisplayAdjustment.Identity;

        public Action<DisplayAdjustment> SetDisplay { get; init; } = static _ => { };

        /// <summary>
        /// 当前工件坐标系（可为 null = 未定义，此时所有映射退化为恒等）。
        /// Chinese: 与 ROI 一样属于"当前测量方案"的一部分，随配方一起存取。
        /// </summary>
        public Func<WorkpieceCoordinateSystem?> GetWorkpieceCoordinateSystem { get; init; } = static () => null;

        public Action<WorkpieceCoordinateSystem?> SetWorkpieceCoordinateSystem { get; init; } = static _ => { };

        /// <summary>
        /// 保存配方时手动捕获的参考图像基准点，最多两个，顺序固定。
        /// </summary>
        public Func<IReadOnlyList<RecipeDatumPoint>> GetRecipeDatumPoints { get; init; } = static () => Array.Empty<RecipeDatumPoint>();

        public Func<IReadOnlyList<RecipeDatumPoint>> GetCurrentRecipeDatumPoints { get; init; } = static () => Array.Empty<RecipeDatumPoint>();

        public Func<object?> GetCurrentImageIdentity { get; init; } = static () => null;

        public Func<PointD?> GetSelectedRecipeDatumPosition { get; init; } = static () => null;

        public Action<int, RecipeDatumPoint> SetRecipeDatumPoint { get; init; } = static (_, _) => { };

        public Action<IReadOnlyList<RecipeDatumPoint>> SetRecipeDatumPoints { get; init; } = static _ => { };

        public Action<int, RecipeDatumPoint> SetCurrentRecipeDatumPoint { get; init; } = static (_, _) => { };

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
        /// Chinese: 供保存会话 / 导出项目包复用。ROI→DTO 的转换必须在这里（UI 线程）一次完成，
        /// 之后序列化可以在后台线程跑而不与编辑器竞争：快照不再引用活 ROI 集合。
        /// English: Captures the full state to persist. The ROI-to-DTO conversion happens here, on the UI thread,
        /// so the serialization step can run off-thread without racing the editor.
        /// </summary>
        public ImageViewerPersistenceSnapshot CaptureSnapshot()
        {
            ImageViewerViewportState viewportState = GetCurrentViewportState();
            return new ImageViewerPersistenceSnapshot(
                TryGetCurrentImagePath(),
                RoiPersistenceService.CreateDocument(
                    GetAllRois(),
                    GetPixelSize(),
                    GetPhysicalUnit(),
                    GetPluginRegistry(),
                    UnresolvedRois,
                    GetQualityProfile()),
                viewportState.Scale,
                viewportState.TranslateX,
                viewportState.TranslateY,
                GetCalibration())
            {
                Display = GetDisplay()
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

        public Func<DisplayAdjustment> GetDisplay { get; init; } = static () => DisplayAdjustment.Identity;

        public Func<ImageAnalysisQualityProfile> GetQualityProfile { get; init; } = static () => ImageAnalysisQualityProfile.Default;

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
        /// Chinese: 自动保存与手动保存共用同一份载荷契约；同样在 UI 线程上完成 ROI→DTO 转换。
        /// English: Auto save and manual save share the same payload contract; the ROI-to-DTO conversion also happens here.
        /// </summary>
        public ImageViewerPersistenceSnapshot CaptureSnapshot()
        {
            ImageViewerViewportState viewportState = GetCurrentViewportState();
            return new ImageViewerPersistenceSnapshot(
                TryGetCurrentImagePath(),
                RoiPersistenceService.CreateDocument(
                    GetAllRois(),
                    GetPixelSize(),
                    GetPhysicalUnit(),
                    GetPluginRegistry(),
                    UnresolvedRois,
                    GetQualityProfile()),
                viewportState.Scale,
                viewportState.TranslateX,
                viewportState.TranslateY,
                GetCalibration())
            {
                Display = GetDisplay()
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
        private readonly ImageViewerRecoveryIndex _recoveryIndex;
        private readonly string _autoSaveDirectory;
        private readonly string _documentId;
        private readonly string _sessionInstanceId;
        private string? _currentProjectPath;
        private string? _recoveryFilePath;
        private bool _recoveryPromptDismissed;
        private bool _isDirty;

        /// <summary>
        /// 判定"某份快照是否仍被活动实例持有"的心跳窗口。
        /// Chinese: 自动保存每 <c>AutoSaveInterval</c> 刷新一次心跳，因此心跳只要在若干个间隔内就算"还活着"。
        /// 取 3 倍间隔、下限 30 秒，避免间隔极短（测试里 5s）时把仍在运行的窗口误判成已退出。
        /// </summary>
        private readonly TimeSpan _liveHeartbeatWindow;

        public event EventHandler? StateChanged;

        public ImageViewerSessionController(
            ImageViewerSessionControllerDependencies dependencies,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerSessionStoragePolicy sessionStoragePolicy)
            : this(dependencies, periodicTaskSchedulerFactory, sessionStoragePolicy, Guid.NewGuid().ToString("N"))
        {
        }

        public ImageViewerSessionController(
            ImageViewerSessionControllerDependencies dependencies,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerSessionStoragePolicy sessionStoragePolicy,
            string documentId)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            ArgumentNullException.ThrowIfNull(periodicTaskSchedulerFactory);
            ArgumentNullException.ThrowIfNull(sessionStoragePolicy);
            ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

            _persistence = dependencies.Persistence ?? throw new ArgumentNullException(nameof(dependencies));
            _autoSave = dependencies.AutoSave;
            _autoSaveDirectory = sessionStoragePolicy.AutoSaveDirectory;
            _documentId = documentId;
            _recoveryIndex = new ImageViewerRecoveryIndex(_autoSaveDirectory);
            _recentProjectCatalog = new ImageViewerRecentProjectCatalog(_persistence.RecentProjectService, sessionStoragePolicy.RecentProjectsFilePath);
            _autoSaveController = new ImageViewerAutoSaveController(dependencies.AutoSave, periodicTaskSchedulerFactory, sessionStoragePolicy, documentId, _recoveryIndex);
            _sessionInstanceId = _autoSaveController.SessionInstanceId;
            _liveHeartbeatWindow = ComputeLiveHeartbeatWindow(sessionStoragePolicy.AutoSaveInterval);
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

        /// <summary>
        /// 把当前测量方案保存为配方（工件坐标系 + 基准点 + 标定 + 测量项）。
        /// Chinese: 配方与"会话/项目包"的区别是**可交付、可复现**——它刻意不带图像路径与视口状态，
        /// 以便同一个测量方案用在任意一张新图上。也因此保存后不改"当前项目"，避免把配方误当成会话。
        /// English: A recipe is a deliverable plan, not a session: it carries no image path or viewport,
        /// so it can be reused on any new image.
        /// </summary>
        public async Task SaveRecipeAsync()
        {
            string? filePath = _persistence.ShowSaveRecipeDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                IReadOnlyList<RoiBase> rois = _persistence.GetAllRois();
                if (rois.Count == 0)
                {
                    // 没有测量项的配方没有复现价值；提示而不是写出一个空文件让人以为成功了。
                    _persistence.ShowStatusHint(UiText.Get("StatusRecipeNoRois"), StatusHintKind.Info);
                    return;
                }

                MeasurementRecipeDocument recipe = MeasurementRecipeService.Create(
                    name: Path.GetFileNameWithoutExtension(filePath),
                    description: null,
                    rois: rois,
                    pixelSize: _persistence.GetPixelSize(),
                    physicalUnit: _persistence.GetPhysicalUnit(),
                    pluginRegistry: _persistence.GetPluginRegistry(),
                    coordinateSystem: _persistence.GetWorkpieceCoordinateSystem(),
                    datumPoints: _persistence.GetRecipeDatumPoints(),
                    calibration: _persistence.GetCalibration(),
                    qualityProfile: _persistence.GetQualityProfile());

                await MeasurementRecipeService.SaveToFileAsync(filePath, recipe).ConfigureAwait(true);
                _persistence.ShowStatusHint(
                    UiText.Format("StatusRecipeSaveSuccess", recipe.EnabledItemCount, Path.GetFileName(filePath)),
                    StatusHintKind.Success);
            }
            catch (Exception ex)
            {
                _persistence.ShowNonCriticalError(
                    UiText.Get("ErrorSaveRecipeTitle"),
                    UiText.Get("ErrorSaveRecipeMessage"),
                    ex);
            }
        }

        public void CaptureRecipeDatumPoint(int index, bool currentImage)
        {
            if (index is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (_persistence.GetSelectedRecipeDatumPosition() is not PointD position)
            {
                _persistence.ShowStatusHint(UiText.Get("StatusRecipeDatumSelectPoint"), StatusHintKind.Error);
                return;
            }

            var point = new RecipeDatumPoint
            {
                Name = index == 0 ? "Datum 1" : "Datum 2",
                X = position.X,
                Y = position.Y
            };
            if (currentImage)
            {
                _persistence.SetCurrentRecipeDatumPoint(index, point);
                _persistence.ShowStatusHint(
                    UiText.Format("StatusRecipeCurrentDatumCaptured", index + 1, position.X, position.Y),
                    StatusHintKind.Success);
            }
            else
            {
                _persistence.SetRecipeDatumPoint(index, point);
                _persistence.ShowStatusHint(
                    UiText.Format("StatusRecipeReferenceDatumCaptured", index + 1, position.X, position.Y),
                    StatusHintKind.Success);
            }
        }

        /// <summary>
        /// 载入配方并应用其测量项、标定与坐标系。
        /// Chinese: 载入后立刻报一次可判定性——"载入配方 ≠ 可以出判定"，缺标定/缺公差必须当场说清楚，
        /// 否则操作员会以为配方一切就绪、直接开始出报告。
        /// </summary>
        public async Task LoadRecipeAsync()
        {
            await LoadRecipeAsync(applyAlignment: false).ConfigureAwait(true);
        }

        public async Task LoadAlignedRecipeAsync()
        {
            await LoadRecipeAsync(applyAlignment: true).ConfigureAwait(true);
        }

        private async Task LoadRecipeAsync(bool applyAlignment)
        {
            string? filePath = _persistence.ShowOpenRecipeDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                object? sourceIdentity = _persistence.GetCurrentImageIdentity();
                IReadOnlyList<RecipeDatumPoint> currentDatumPoints = applyAlignment
                    ? _persistence.GetCurrentRecipeDatumPoints()
                        .Select(point => new RecipeDatumPoint { Name = point.Name, X = point.X, Y = point.Y })
                        .ToArray()
                    : Array.Empty<RecipeDatumPoint>();

                MeasurementRecipeDocument recipe = await Task.Run(
                    () => MeasurementRecipeService.LoadFromFile(filePath)).ConfigureAwait(true);

                if (applyAlignment)
                {
                    if (!ReferenceEquals(sourceIdentity, _persistence.GetCurrentImageIdentity()))
                    {
                        _persistence.ShowStatusHint(UiText.Get("StatusRecipeImageChangedDuringAlignment"), StatusHintKind.Error);
                        return;
                    }

                    if (!recipe.CanLocalize || !HasDistinctPair(recipe.DatumPoints)
                        || currentDatumPoints.Count < 2 || !HasDistinctPair(currentDatumPoints))
                    {
                        _persistence.ShowStatusHint(UiText.Get("StatusRecipeAlignmentDatumsRequired"), StatusHintKind.Error);
                        return;
                    }
                }

                RoiDocument roiDocument = applyAlignment
                    ? MeasurementRecipeService.BuildAlignedRoiDocument(
                        recipe,
                        new PointD(currentDatumPoints[0].X, currentDatumPoints[0].Y),
                        new PointD(currentDatumPoints[1].X, currentDatumPoints[1].Y))
                    : MeasurementRecipeService.BuildRoiDocument(recipe);

                RoiDocumentLoadResult restored = RoiPersistenceService.CreateRois(
                    roiDocument,
                    _persistence.GetPluginRegistry());

                if (applyAlignment && restored.UnresolvedItems.Count > 0)
                {
                    throw new InvalidOperationException(
                        UiText.Format("StatusRecipeUnsupportedRoiCount", restored.UnresolvedItems.Count));
                }

                WorkpieceCoordinateSystem? coordinateSystem = recipe.CoordinateSystem;
                if (applyAlignment)
                {
                    coordinateSystem = WorkpieceCoordinateSystem.Localize(
                        new PointD(currentDatumPoints[0].X, currentDatumPoints[0].Y),
                        new PointD(currentDatumPoints[1].X, currentDatumPoints[1].Y),
                        new PointD(recipe.DatumPoints[0].X, recipe.DatumPoints[0].Y),
                        new PointD(recipe.DatumPoints[1].X, recipe.DatumPoints[1].Y),
                        recipe.CoordinateSystem?.Name);
                }

                _persistence.ReplaceAllRois(restored.Rois);
                _persistence.SetPixelSize(recipe.PixelSize);
                if (!string.IsNullOrWhiteSpace(recipe.PhysicalUnit))
                {
                    _persistence.SetPhysicalUnit(recipe.PhysicalUnit);
                }

                _persistence.SetCalibration(recipe.Calibration);
                _persistence.SetWorkpieceCoordinateSystem(coordinateSystem);
                _persistence.SetRecipeDatumPoints(applyAlignment ? currentDatumPoints : recipe.DatumPoints);
                _persistence.DrawRois();
                _persistence.UpdateContextMenuState();

                MeasurementWorkflowReadiness readiness = MeasurementRecipeService.EvaluateReadiness(recipe);
                StatusHintKind kind = readiness.IsBlocked
                    ? StatusHintKind.Error
                    : readiness.HasWarnings ? StatusHintKind.Info : StatusHintKind.Success;
                _persistence.ShowStatusHint(
                    UiText.Format(
                        "StatusRecipeLoadSuccess",
                        restored.Rois.Count,
                        MeasurementWorkflowReadinessFormatter.FormatSingleLine(readiness))
                        + (applyAlignment ? " " + UiText.Get("StatusRecipeManualAlignmentApplied") : string.Empty),
                    kind);
            }
            catch (Exception ex)
            {
                if (applyAlignment && ex is InvalidOperationException or NotSupportedException or ArgumentOutOfRangeException)
                {
                    _persistence.ShowStatusHint(UiText.Format("StatusRecipeAlignmentFailed", ex.Message), StatusHintKind.Error);
                    return;
                }

                _persistence.ShowNonCriticalError(
                    UiText.Get("ErrorLoadRecipeTitle"),
                    UiText.Get("ErrorLoadRecipeMessage"),
                    ex);
            }
        }

        private static bool HasDistinctPair(IReadOnlyList<RecipeDatumPoint> points)
        {
            return points.Count == 2
                && double.IsFinite(points[0].X) && double.IsFinite(points[0].Y)
                && double.IsFinite(points[1].X) && double.IsFinite(points[1].Y)
                && (Math.Abs(points[1].X - points[0].X) > 1e-12
                    || Math.Abs(points[1].Y - points[0].Y) > 1e-12);
        }

        public async Task RecoverLatestAutoSaveAsync()
        {
            RefreshRecoverySnapshot();
            IReadOnlyList<string> recoveryCandidates = GetRecoveryCandidates();
            if (recoveryCandidates.Count == 0)
            {
                _persistence.ShowStatusHint(UiText.Get("StatusNoRecoverySnapshot"), StatusHintKind.Info);
                return;
            }

            Exception? lastLoadException = null;
            string? missingImagePath = null;
            foreach (string recoveryFilePath in recoveryCandidates)
            {
                try
                {
                    ImageViewerSessionData session = await _persistence.SessionService.LoadFromFileAsync(
                        recoveryFilePath,
                        _persistence.GetPluginRegistry());

                    // Skip stale entries whose image was removed, then try an older candidate.
                    if (!string.IsNullOrWhiteSpace(session.ImagePath) && !File.Exists(session.ImagePath))
                    {
                        missingImagePath = session.ImagePath;
                        continue;
                    }

                    if (!ApplySession(session))
                    {
                        continue;
                    }

                    _recoveryFilePath = recoveryFilePath;
                    _persistence.ClearUndoHistory();
                    ReportUnresolvedRois(session.UnresolvedRois);
                    _recoveryPromptDismissed = true;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    MarkDirty();
                    _persistence.ShowStatusHint(UiText.Get("StatusRecoveryLoaded"), StatusHintKind.Success);
                    return;
                }
                catch (Exception ex)
                {
                    lastLoadException = ex;
                    _autoSave.LogNonCriticalError("Load autosave candidate", ex);
                }
            }

            _recoveryFilePath = null;
            _recoveryPromptDismissed = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            if (lastLoadException != null)
            {
                _persistence.ShowNonCriticalError(
                    UiText.Get("ErrorRecoveryTitle"),
                    UiText.Get("ErrorRecoveryMessage"),
                    lastLoadException);
            }
            else if (missingImagePath != null)
            {
                _persistence.ShowStatusHint(
                    UiText.Format("StatusSessionImageMissing", missingImagePath),
                    StatusHintKind.Error);
            }
            else
            {
                _persistence.ShowStatusHint(UiText.Get("StatusNoRecoverySnapshot"), StatusHintKind.Info);
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

        /// <summary>
        /// 正常关闭时释放本实例在恢复索引里的所有权。
        /// Chinese: 用户主动关掉的窗口不该在下次启动时弹出"恢复未保存文档"——那份快照只是它退出前的
        /// 最后状态。异常退出（崩溃/强杀）不会走到这里，条目保留并在心跳过期后可恢复。
        /// </summary>
        public void ReleaseRecoveryOwnership()
        {
            _autoSaveController.ReleaseRecoveryOwnership();
        }

        public void Dispose()
        {
            _autoSaveController.ReleaseRecoveryOwnership();
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

                if (!ApplySession(session))
                {
                    return;
                }
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

        private bool ApplySession(ImageViewerSessionData session)
        {
            if (!string.IsNullOrWhiteSpace(session.ImagePath))
            {
                if (!File.Exists(session.ImagePath))
                {
                    _persistence.ShowStatusHint(UiText.Format("StatusSessionImageMissing", session.ImagePath), StatusHintKind.Error);
                    return false;
                }

                _persistence.LoadImageFromFile(session.ImagePath, false);
            }

            _persistence.ReplaceAllRois(session.Rois);
            _persistence.SetPixelSize(session.PixelSize);
            _persistence.SetPhysicalUnit(session.PhysicalUnit);
            _persistence.SetQualityProfile(session.QualityProfile);
            _persistence.SetCalibration(session.Calibration);
            _persistence.SetDisplay(session.Display);
            _persistence.ApplyViewportState(new ImageViewerViewportState(session.Scale, session.TranslateX, session.TranslateY));
            _persistence.DrawRois();
            _persistence.UpdateContextMenuState();
            return true;
        }

        private void SetCurrentProject(string filePath, string projectKind)
        {
            _currentProjectPath = Path.GetFullPath(filePath);
            _autoSaveController.SetCurrentProject(_currentProjectPath);
            _recentProjectCatalog.Remember(_currentProjectPath, projectKind);
            _persistence.UpdateContextMenuState();
        }

        /// <summary>
        /// 成功保存/导出后需要清理与摘除索引的候选路径。
        /// Chinese: 保存会改变自动保存的文件名（未保存 → autosave-{id}，保存后 → autosave-{名}-{哈希}），
        /// 所以只按"当前"文件名找会漏掉保存前那份。这里用 <see cref="ImageViewerAutoSaveController.LastWrittenAutoSavePath"/>
        /// 记住的"本文档真正写过的路径"来补齐——**绝不枚举整个目录**：同目录下还躺着其它窗口/文档的
        /// 自动保存，按 <c>autosave-*.ivsession</c> 通配清理会把别人的恢复文件一并删掉（保存一个文档
        /// 却毁掉另一个文档的恢复数据）。
        /// English: Cleanup covers only files this document produced. Earlier versions swept every
        /// <c>autosave-*.ivsession</c> in the directory, so saving one document deleted another window's recovery file.
        /// </summary>
        private string[] GetRecoveryCleanupPaths()
        {
            var paths = new List<string>
            {
                _autoSaveController.GetAutoSaveFilePath(),
                _autoSaveController.GetLegacyAutoSaveFilePath(),
                Path.Combine(_autoSaveDirectory, "autosave.ivsession")
            };

            // 保存改名前的实际写盘路径（本文档自己的文件，与通配枚举无关）。
            if (!string.IsNullOrWhiteSpace(_autoSaveController.LastWrittenAutoSavePath))
            {
                paths.Add(_autoSaveController.LastWrittenAutoSavePath!);
            }

            if (!string.IsNullOrWhiteSpace(_recoveryFilePath))
            {
                paths.Add(_recoveryFilePath);
            }

            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void CompleteSuccessfulSave()
        {
            MarkClean();
            string[] recoveryPaths = GetRecoveryCleanupPaths();
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

            // 文件已删 + 已经从"未保存"变成"已保存"：索引里的登记要一并摘掉，
            // 否则下次启动索引会指向不存在的路径，或把已保存的文档又列成待恢复文档。
            ForgetRecoveryIndexEntries(recoveryPaths);
            _recoveryFilePath = null;
            _recoveryPromptDismissed = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 从恢复索引里摘除已不再需要恢复的自动保存文件。
        /// Chinese: 成功保存/导出后，这份文档已经落到了用户指定的路径，重启时不该再提示"恢复未保存文档"。
        /// English: Drops auto-save files from the recovery index once the document has been saved for real.
        /// </summary>
        private void ForgetRecoveryIndexEntries(IEnumerable<string> recoveryPaths)
        {
            foreach (string recoveryPath in recoveryPaths)
            {
                _recoveryIndex.Unregister(recoveryPath);
            }
        }

        private void RefreshRecoverySnapshot()
        {
            IReadOnlyList<string> candidates = GetRecoveryCandidates();
            _recoveryFilePath = candidates.Count == 0 ? null : candidates[0];
        }

        private IReadOnlyList<string> GetRecoveryCandidates()
        {
            if (!Directory.Exists(_autoSaveDirectory))
            {
                return [];
            }

            try
            {
                // 只认"本文档"的自动保存文件：未保存项目时是 autosave-<DocumentId>.ivsession，
                // 已保存项目时是 autosave-<名>-<路径哈希>.ivsession。此前扫描目录下全部
                // *.ivsession，多个窗口会互相把对方的自动保存当成自己的恢复候选（先到先得），
                // 结果既可能恢复成别人的文档，也会把对方的候选从列表里顶掉。
                // 旧版遗留的无身份文件（autosave.ivsession / 旧命名）仅在"尚未有任何本文档
                // 候选"时作为兜底接受，保证升级用户的存量恢复记录不丢。
                string ownFileName = Path.GetFileName(_autoSaveController.GetAutoSaveFilePath());
                string[] candidates = Directory
                    .EnumerateFiles(_autoSaveDirectory, "*.ivsession", SearchOption.TopDirectoryOnly)
                    .Where(path =>
                    {
                        string fileName = Path.GetFileName(path);
                        return !fileName.StartsWith('.') &&
                               !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
                               HasContent(path);
                    })
                    .OrderByDescending(GetLastWriteTimeUtc)
                    .ToArray();

                string[] ownCandidates = candidates
                    .Where(path => string.Equals(Path.GetFileName(path), ownFileName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (ownCandidates.Length > 0)
                {
                    return ownCandidates;
                }

                // 重启后 DocumentId 是新的，自己的文件名根本不存在；靠持久化索引认出"上一次运行遗留的
                // 待恢复文档"。只接受索引里明确登记过、且当前磁盘上确实存在的文件——不退回"扫目录全收"，
                // 否则同目录下别的窗口的自动保存又会被误当成自己的候选。
                string[] indexedCandidates = GetIndexedRecoveryCandidates(candidates);
                if (indexedCandidates.Length > 0)
                {
                    return indexedCandidates;
                }

                return candidates
                    .Where(path => IsLegacyUnidentifiedAutoSave(Path.GetFileName(path)))
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// 从磁盘上的候选里挑出被恢复索引登记过、且**不再被活动实例持有**的那些（保持原有 mtime 倒序）。
        /// Chinese: 索引是"本机遗留的未保存文档"的清单。两类条目不该出现在候选里：
        /// ① 已正常保存过的文档（成功保存时已从索引摘除）；
        /// ② 仍被某个活着的窗口持有的文档——它只是"另一个窗口正开着"，不是"待恢复的遗留文档"。
        /// 后者靠 <see cref="ImageViewerRecoveryIndexEntry.OwnerInstanceId"/> + 新鲜心跳识别，
        /// 因此崩溃/强杀的实例（心跳过期）仍然可恢复。
        /// </summary>
        private string[] GetIndexedRecoveryCandidates(string[] onDiskCandidates)
        {
            IReadOnlyList<ImageViewerRecoveryIndexEntry> indexedEntries = _recoveryIndex.Prune();
            if (indexedEntries.Count == 0)
            {
                return [];
            }

            DateTimeOffset heartbeatThreshold = DateTimeOffset.UtcNow - _liveHeartbeatWindow;
            var recoverablePaths = new HashSet<string>(
                indexedEntries
                    .Where(entry => IsRecoverable(entry, heartbeatThreshold))
                    .Select(entry => entry.Path),
                StringComparer.OrdinalIgnoreCase);

            return onDiskCandidates
                .Where(path => recoverablePaths.Contains(Path.GetFullPath(path)))
                .ToArray();
        }

        /// <summary>
        /// 这份快照是否可被本窗口恢复。
        /// Chinese: 同一实例（自己的）不是"要恢复的遗留文档"；其它实例只要心跳还新鲜就视为"仍在使用"。
        /// 没有所有者信息的历史条目（升级前写的）没有心跳可比，按"可恢复"处理以保住存量数据。
        /// English: A snapshot is recoverable when it is not owned by this instance and its owner's heartbeat
        /// is stale. Legacy entries without ownership info are treated as recoverable.
        /// </summary>
        private bool IsRecoverable(ImageViewerRecoveryIndexEntry entry, DateTimeOffset heartbeatThreshold)
        {
            if (string.Equals(entry.OwnerInstanceId, _sessionInstanceId, StringComparison.Ordinal))
            {
                return false;
            }

            if (entry.LastHeartbeatUtc is not DateTimeOffset heartbeat)
            {
                return true;
            }

            // 心跳落在阈值之内 = 写它的窗口还活着（或刚刚才写），不该提示恢复。
            return heartbeat < heartbeatThreshold;
        }

        private static TimeSpan ComputeLiveHeartbeatWindow(TimeSpan autoSaveInterval)
        {
            TimeSpan window = autoSaveInterval <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(30)
                : autoSaveInterval * 3;
            return window < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : window;
        }

        /// <summary>
        /// 旧版遗留的、不带文档身份信息的自动保存文件名。
        /// Chinese: 仅用于升级兜底——过去所有未保存项目都写同一个 "autosave.ivsession"。
        /// 一旦本窗口产生了自己的候选，就不再回退到这些共享文件，避免多窗口互相抢占。
        /// English: Legacy shared names kept only as an upgrade fallback (older builds wrote every unsaved
        /// document to "autosave.ivsession"). Once this window has its own candidate we stop falling back.
        /// </summary>
        private static bool IsLegacyUnidentifiedAutoSave(string fileName)
        {
            return string.Equals(fileName, "autosave.ivsession", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasContent(string filePath)
        {
            try
            {
                return new FileInfo(filePath).Length > 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static DateTime GetLastWriteTimeUtc(string filePath)
        {
            try
            {
                return File.GetLastWriteTimeUtc(filePath);
            }
            catch (IOException)
            {
                return DateTime.MinValue;
            }
            catch (UnauthorizedAccessException)
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// 上报并对齐"未识别标注"载荷。
        /// Chinese: 缺插件或类型键变更时，未识别的标注不再被静默丢弃——状态栏给出数量与类型提示，
        /// 同时把原始载荷保留在保存链路上，下次保存会原样回写，重新获得插件后仍可正常打开。
        /// English: Surfaces unresolved ROI payloads (missing plugin / renamed type key) with a status hint and keeps
        /// them on the save path so the next save writes them back verbatim instead of erasing them.
        /// </summary>
        /// <summary>
        /// 追加未识别 ROI 载荷（不替换已有载荷）。
        /// Chinese: 插件卸载会把被移除的 ROI 序列化成 unresolved 载荷追加到这里；加载会话时 ReportUnresolvedRois
        /// 是整体替换，两者语义不同——替换会抹掉之前追加的载荷。
        /// English: Appends payloads without discarding the ones already carried from the last load.
        /// </summary>
        internal void AppendUnresolvedRois(IReadOnlyList<RoiPersistenceData> payloads)
        {
            if (payloads.Count == 0)
            {
                return;
            }

            var merged = new List<RoiPersistenceData>(_persistence.UnresolvedRois);
            merged.AddRange(payloads);
            _persistence.UnresolvedRois = merged;
            _autoSave.UnresolvedRois = merged;
        }

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
