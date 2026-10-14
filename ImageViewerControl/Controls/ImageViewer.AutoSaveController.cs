using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ImageViewer.Localization;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerAutoSaveController : IDisposable
    {
        private readonly ImageViewerAutoSaveWorkflow _workflow;
        private readonly IImageViewerPeriodicTaskScheduler _autoSaveScheduler;
        private readonly ImageViewerRecoveryIndex? _recoveryIndex;
        private readonly string _autoSaveDirectory;
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private CancellationTokenSource _lifecycleCancellationTokenSource = new();
        private string? _currentProjectPath;
        private bool _lastAutoSaveFailed;

        public ImageViewerAutoSaveController(
            ImageViewerAutoSaveWorkflow workflow,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerSessionStoragePolicy sessionStoragePolicy,
            string documentId)
            : this(workflow, periodicTaskSchedulerFactory, sessionStoragePolicy, documentId, recoveryIndex: null)
        {
        }

        public ImageViewerAutoSaveController(
            ImageViewerAutoSaveWorkflow workflow,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerSessionStoragePolicy sessionStoragePolicy,
            string documentId,
            ImageViewerRecoveryIndex? recoveryIndex)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            ArgumentNullException.ThrowIfNull(periodicTaskSchedulerFactory);
            ArgumentNullException.ThrowIfNull(sessionStoragePolicy);
            ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

            _autoSaveDirectory = sessionStoragePolicy.AutoSaveDirectory;
            _recoveryIndex = recoveryIndex;
            DocumentId = documentId;
            SessionInstanceId = Guid.NewGuid().ToString("N");
            TimeSpan autoSaveInterval = sessionStoragePolicy.AutoSaveInterval;
            _autoSaveScheduler = periodicTaskSchedulerFactory.Create(
                AutoSaveAsync,
                _workflow.Dispatcher,
                DispatcherPriority.Background,
                autoSaveInterval);
            _autoSaveScheduler.Start();
        }

        /// <summary>
        /// 本查看器实例的稳定文档身份。
        /// Chinese: 未保存项目时用它区分不同窗口/文档的自动保存文件，避免多窗口写入同一路径互相覆盖。
        /// 有项目路径时以路径哈希为准（同一工程在不同窗口打开应共享自动保存），此 ID 不参与。
        /// English: Stable per-viewer identity. Used to isolate the auto-save file when the document has no
        /// project path, so multiple windows no longer overwrite each other. Once a project path exists the
        /// path hash takes over (the same project opened twice should share the auto-save file).
        /// </summary>
        public string DocumentId { get; }

        /// <summary>
        /// 本"会话实例"的标识（每次构造新生成，与 <see cref="DocumentId"/> 无关）。
        /// Chinese: DocumentId 会因为文档获得项目路径而改变含义，不适合表达"这个窗口还开着吗"。
        /// 会话实例 id 一旦生成就不再变化，用于在恢复索引里标记"这份快照归谁所有"。
        /// English: Per-instance id used to mark ownership of auto-save snapshots. Unlike DocumentId it never
        /// changes for the lifetime of the window, so it can answer "is this snapshot still in use?".
        /// </summary>
        public string SessionInstanceId { get; }

        /// <summary>
        /// 本实例最后一次成功写盘的实际路径。
        /// Chinese: 保存会把自动保存文件名从 <c>autosave-{DocumentId}</c> 换成 <c>autosave-{名}-{哈希}</c>，
        /// 而 <see cref="GetAutoSaveFilePath"/> 返回的是"当前"名字。要清理本文档留下的旧文件，必须记住
        /// 上一次真正写过的路径——而不是去枚举整个目录（那会把别的窗口的自动保存一并删掉）。
        /// English: The exact path this instance last wrote. The auto-save file is renamed once the document gains
        /// a project path, so cleanup must use the recorded path instead of sweeping the whole directory.
        /// </summary>
        public string? LastWrittenAutoSavePath { get; private set; }

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

        /// <summary>
        /// 释放本实例在恢复索引里的所有权（正常关闭时调用）。
        /// Chinese: 窗口正常关闭后，它留下的自动保存只代表"上次拖到最后的状态"，不该再提示下一个窗口
        /// 去恢复一个已经被用户关掉的文档。崩溃/强杀走不到这里，其条目靠心跳过期变为可恢复。
        /// </summary>
        public void ReleaseRecoveryOwnership()
        {
            _recoveryIndex?.ReleaseOwnership(SessionInstanceId);
        }

        public bool IsEnabled { get; private set; } = true;

        public void SetCurrentProject(string? filePath)
        {
            _currentProjectPath = string.IsNullOrWhiteSpace(filePath)
                ? null
                : Path.GetFullPath(filePath);
        }

        public string GetAutoSaveFilePath()
        {
            return Path.Combine(_autoSaveDirectory, $"{GetAutoSaveFileName()}.ivsession");
        }

        public string GetLegacyAutoSaveFilePath()
        {
            if (string.IsNullOrWhiteSpace(_currentProjectPath))
            {
                return GetAutoSaveFilePath();
            }

            string baseName = Path.GetFileNameWithoutExtension(_currentProjectPath) ?? "autosave";
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalidChar, '_');
            }

            return Path.Combine(
                _autoSaveDirectory,
                $"{(string.IsNullOrWhiteSpace(baseName) ? "autosave" : baseName)}.ivsession");
        }

        public void Toggle()
        {
            IsEnabled = !IsEnabled;
            // 重新开启时重置失败标记，避免上次失败长时间抑制新失败提示
            if (IsEnabled)
            {
                _lastAutoSaveFailed = false;
            }
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
                string filePath = GetAutoSaveFilePath();
                await _workflow.SessionService.SaveToFileAsync(
                    filePath,
                    _workflow.CaptureSnapshot(),
                    _workflow.GetPluginRegistry(),
                    cancellationToken);
                _lastAutoSaveFailed = false;
                LastWrittenAutoSavePath = filePath;
                // 登记到恢复索引：DocumentId 每次启动都变，不登记的话重启后找不到这份未保存文档。
                // 同时带上所有者与心跳：另一个窗口据此判断"这份快照还在被使用"，不该提示恢复它。
                DateTimeOffset now = DateTimeOffset.UtcNow;
                _recoveryIndex?.Register(filePath, now, SessionInstanceId, now);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _workflow.LogNonCriticalError("Auto save failed", ex);
                if (_lastAutoSaveFailed)
                {
                    return;
                }

                _lastAutoSaveFailed = true;
                _workflow.ShowStatusHint(UiText.Get("StatusAutoSaveFailed"), StatusHintKind.Error);
            }
            finally
            {
                _saveGate.Release();
            }
        }

        private string GetAutoSaveFileName()
        {
            if (string.IsNullOrWhiteSpace(_currentProjectPath))
            {
                // 未保存项目：用文档 ID 区分实例，避免多窗口共享同一 Autosave 目录时互相覆盖。
                return $"autosave-{DocumentId}";
            }

            string baseName = Path.GetFileNameWithoutExtension(_currentProjectPath) ?? "autosave";
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalidChar, '_');
            }

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "autosave";
            }

            // Keep names readable while hashing the full path to prevent same-name projects
            // in different folders from overwriting one another.
            if (baseName.Length > 48)
            {
                baseName = baseName[..48];
            }

            string pathHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(_currentProjectPath)))
                .ToLowerInvariant()[..12];
            return $"autosave-{baseName}-{pathHash}";
        }
    }
}
