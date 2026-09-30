using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer : UserControl, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// ImageViewer 控件：主要入口类
        /// Chinese: 该类实现了图像查看器控件的 UI 互动绑定、依赖属性以及键盘/鼠标事件的初始化。
        /// English: Main ImageViewer control partial class that wires up dependency properties and input handlers.
        /// </summary>
        private readonly ImageViewerInteractionManipulationState _interactionManipulationState = new();
        private readonly ImageViewerHostState _hostState;

        /// <summary>
        /// 宿主租约：viewer 与创建它的宿主之间唯一的生命周期连接。
        /// Chinese: 无论走 DI 还是手工构造，viewer 都只在 <see cref="ReleaseHostLease"/> 归还租约一次；
        /// 拥有 runtime 的宿主在最后一个租约归还时才释放 runtime。
        /// English: The single lifetime link between a viewer and the host that created it, returned exactly once.
        /// </summary>
        private ImageViewerHost? _hostLease;

        private const double MinScale = 0.1;
        private const double MaxScale = 100;
        internal readonly ImageViewerControlComposition _controlComposition;
        internal readonly ImageViewerAnalysisState _analysisState = new();
        private readonly IImageViewerLatestTaskScheduler _infoPanelStatisticsScheduler;
        private readonly ImageViewerLifetime _lifetime;
        private int _imageRotation;
        private bool _flipImageHorizontally;
        private bool _flipImageVertically;
        private ImageAnalysisQualityProfile _qualityProfile = ImageAnalysisQualityProfile.Default;

        public ImageViewer()
            : this(ImageViewerHost.CreateDefault())
        {
        }

        /// <summary>
        /// 由宿主创建 viewer 的唯一构造函数。
        /// Chinese: 建立宿主租约（登记一个 viewer），因此必须与 <see cref="ReleaseHostLease"/> 成对出现。
        /// English: The only constructor that establishes a host lease; always paired with <see cref="ReleaseHostLease"/>.
        /// </summary>
        internal ImageViewer(ImageViewerHost host)
            : this((host ?? throw new ArgumentNullException(nameof(host))).Dependencies)
        {
            host.RegisterViewer();
            _hostLease = host;
        }

        /// <summary>
        /// 用现成的依赖集直接构造 viewer。
        /// Chinese: 这条路径不建立宿主租约：调用方创建了依赖集，就由调用方负责释放其中的 runtime services。
        /// 只要不是自己创建依赖集，请改用 <see cref="ImageViewerHost.CreateViewer"/> 或容器解析。
        /// English: This overload creates no host lease: the caller that built the dependencies also owns the release
        /// of their runtime services. Prefer <see cref="ImageViewerHost.CreateViewer"/> unless you built the dependencies yourself.
        /// </summary>
        public ImageViewer(ImageViewerDependencies dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            ImageViewerBootstrapState bootstrapState = CreateBootstrapState(dependencies);
            _hostState = bootstrapState.HostState;
            _hostState.ViewModel.QualityProfile = _qualityProfile;
            _viewportOverlayRefreshScheduler = bootstrapState.ViewportOverlayRefreshScheduler;
            _analysisRefreshScheduler = bootstrapState.AnalysisRefreshScheduler;
            _infoPanelStatisticsScheduler = bootstrapState.InfoPanelStatisticsScheduler;
            _controlComposition = dependencies.CreateControlComposition(this);
            _lifetime = new ImageViewerLifetime(CreateLifetimeRegistrations(_controlComposition));
            CompleteBootstrap();
        }

        /// <summary>
        /// Quality thresholds owned by this viewer instance.
        /// </summary>
        public ImageAnalysisQualityProfile QualityProfile
        {
            get => _qualityProfile;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                value.Validate();
                _qualityProfile = value;
                if (_hostState is not null)
                {
                    _hostState.ViewModel.QualityProfile = value;
                }
            }
        }

        private void ResetView() => _viewCommandController.Execute(ImageViewerViewCommand.ResetView);

        internal void RotateImageLeft()
        {
            SetImageRotation(_imageRotation - 90);
            ShowStatusHint(UiText.Get("StatusRotatedLeft"), StatusHintKind.Success);
        }

        internal void RotateImageRight()
        {
            SetImageRotation(_imageRotation + 90);
            ShowStatusHint(UiText.Get("StatusRotatedRight"), StatusHintKind.Success);
        }

        internal void FlipImageHorizontal()
        {
            _flipImageHorizontally = !_flipImageHorizontally;
            ApplyImageOrientation();
            ShowStatusHint(UiText.Get("StatusFlippedHorizontal"), StatusHintKind.Success);
        }

        internal void FlipImageVertical()
        {
            _flipImageVertically = !_flipImageVertically;
            ApplyImageOrientation();
            ShowStatusHint(UiText.Get("StatusFlippedVertical"), StatusHintKind.Success);
        }

        private void SetImageRotation(int angle)
        {
            _imageRotation = ((angle % 360) + 360) % 360;
            ApplyImageOrientation();
        }

        internal void ResetImageOrientation()
        {
            _imageRotation = 0;
            _flipImageHorizontally = false;
            _flipImageVertically = false;
            ApplyImageOrientation();
        }

        internal void ApplyImageOrientation()
        {
            double centerX = imageContainer.Width / 2;
            double centerY = imageContainer.Height / 2;
            orientationScaleTransform.CenterX = centerX;
            orientationScaleTransform.CenterY = centerY;
            orientationScaleTransform.ScaleX = _flipImageHorizontally ? -1 : 1;
            orientationScaleTransform.ScaleY = _flipImageVertically ? -1 : 1;
            orientationRotateTransform.CenterX = centerX;
            orientationRotateTransform.CenterY = centerY;
            orientationRotateTransform.Angle = _imageRotation;
        }

        public void SetImage(ImageSource source)
        {
            ImageSource = source;
        }

        /// <summary>
        /// 添加一个 ROI，并把操作纳入撤销栈。
        /// Chinese: 供宿主或分析流程提交程序生成的 ROI；与鼠标绘制使用同一撤销语义。
        /// English: Adds a programmatically generated ROI through the same undo stack used by interactive drawing.
        /// </summary>
        public bool AddRoi(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);
            if (ViewerState.PluginRegistry.FindByRoi(roi) == null)
            {
                return false;
            }

            ViewerState.UndoRedo.Execute(new AddRoiCommand(roi, ViewerState));
            ViewerState.SelectedRoi = roi;
            DrawRois();
            UpdateContextMenuState();
            return true;
        }

        internal void MarkDocumentDirty()
        {
            _controlComposition.SessionController.MarkDirty();
        }

        /// <summary>
        /// Marks the currently displayed document as the clean baseline for a host-provided initial load.
        /// </summary>
        public void MarkDocumentClean()
        {
            _controlComposition.SessionController.MarkClean();
        }

        internal void SetImageLoadState(bool isLoading, string statusText, double progress, bool canRetry)
        {
            IsImageLoading = isLoading;
            ImageLoadStatusText = statusText;
            ImageLoadProgress = Math.Clamp(progress, 0, 100);
            CanRetryImageLoad = canRetry;
            CanCancelImageLoad = isLoading && !canRetry;
            ImageLoadHasError = canRetry && isLoading;
        }

        public Task RetryLastImageLoadAsync() => _controlComposition.DialogWorkflowService.RetryLastImageLoadAsync();

        /// <summary>
        /// 取消当前图像加载，不影响当前已经显示的图像。
        /// </summary>
        public void CancelImageLoad() => _controlComposition.DialogWorkflowService.CancelImageLoad();

        /// <summary>
        /// 请求宿主保存当前会话；用户取消保存对话框时任务仍会正常完成，宿主可通过
        /// <see cref="IsDirty"/> 判断是否允许继续关闭窗口。
        /// </summary>
        public Task SaveSessionAsync() => _fileMenuCommandController.ExecuteAsync(ImageViewerFileMenuCommand.SaveSession);

        public void Dispose()
        {
            CancelImageLoad();
            _controlComposition.SessionController.StateChanged -= OnSessionStateChanged;
            try
            {
                _lifetime.Dispose();
            }
            finally
            {
                ReleaseHostLease();
            }

            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            GC.SuppressFinalize(this);
            return default;
        }

        /// <summary>
        /// 归还宿主租约，且只归还一次。
        /// Chinese: 拥有 runtime 的宿主在这里放行最后一个 viewer 后释放 runtime；借用 runtime 的宿主在这里什么也不做。
        /// English: Returns the host lease exactly once. An owning host releases its runtime services when the last lease returns.
        /// </summary>
        private void ReleaseHostLease()
        {
            ImageViewerHost? host = Interlocked.Exchange(ref _hostLease, null);
            host?.ReleaseViewer();
        }

        private void OnSessionStateChanged(object? sender, EventArgs e)
        {
            IsDirty = _controlComposition.SessionController.IsDirty;
            HasRecoverySnapshot = _controlComposition.SessionController.HasRecoverySnapshot;
            UpdateStatusBar();
        }
    }
}
