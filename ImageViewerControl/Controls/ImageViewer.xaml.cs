using System;
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
        private readonly ImageViewerHost? _ownedHost;
        private const double MinScale = 0.1;
        private const double MaxScale = 100;
        internal readonly ImageViewerControlComposition _controlComposition;
        internal readonly ImageViewerAnalysisState _analysisState = new();
        private readonly IImageViewerLatestTaskScheduler _infoPanelStatisticsScheduler;
        private readonly ImageViewerLifetime _lifetime;
        private int _imageRotation;
        private bool _flipImageHorizontally;
        private bool _flipImageVertically;

        public ImageViewer()
            : this(ImageViewerHost.CreateDefault())
        {
        }

        private ImageViewer(ImageViewerHost host)
            : this(host.Dependencies)
        {
            _ownedHost = host ?? throw new ArgumentNullException(nameof(host));
        }

        public ImageViewer(ImageViewerDependencies dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            ImageViewerBootstrapState bootstrapState = CreateBootstrapState(dependencies);
            _hostState = bootstrapState.HostState;
            _viewportOverlayRefreshScheduler = bootstrapState.ViewportOverlayRefreshScheduler;
            _analysisRefreshScheduler = bootstrapState.AnalysisRefreshScheduler;
            _infoPanelStatisticsScheduler = bootstrapState.InfoPanelStatisticsScheduler;
            _controlComposition = dependencies.CreateControlComposition(this);
            _lifetime = new ImageViewerLifetime(CreateLifetimeRegistrations(_controlComposition));
            CompleteBootstrap();
        }

        private void ResetView() => _viewCommandController.Execute(ImageViewerViewCommand.ResetView);

        private void RotateImageLeft()
        {
            SetImageRotation(_imageRotation - 90);
            ShowStatusHint(UiText.Get("StatusRotatedLeft"), StatusHintKind.Success);
        }

        private void RotateImageRight()
        {
            SetImageRotation(_imageRotation + 90);
            ShowStatusHint(UiText.Get("StatusRotatedRight"), StatusHintKind.Success);
        }

        private void FlipImageHorizontal()
        {
            _flipImageHorizontally = !_flipImageHorizontally;
            ApplyImageOrientation();
            ShowStatusHint(UiText.Get("StatusFlippedHorizontal"), StatusHintKind.Success);
        }

        private void FlipImageVertical()
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

        private void ResetImageOrientation()
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

        internal void SetImageLoadState(bool isLoading, string statusText, double progress, bool canRetry)
        {
            IsImageLoading = isLoading;
            ImageLoadStatusText = statusText;
            ImageLoadProgress = Math.Clamp(progress, 0, 100);
            CanRetryImageLoad = canRetry;
            ImageLoadHasError = canRetry && isLoading;
        }

        public Task RetryLastImageLoadAsync() => _controlComposition.DialogWorkflowService.RetryLastImageLoadAsync();

        public void Dispose()
        {
            _controlComposition.SessionController.StateChanged -= OnSessionStateChanged;
            _lifetime.Dispose();
            _ownedHost?.Dispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            _controlComposition.SessionController.StateChanged -= OnSessionStateChanged;
            _lifetime.Dispose();
            if (_ownedHost != null)
            {
                await _ownedHost.DisposeAsync().ConfigureAwait(false);
            }

            GC.SuppressFinalize(this);
        }

        private void OnSessionStateChanged(object? sender, EventArgs e)
        {
            IsDirty = _controlComposition.SessionController.IsDirty;
            HasRecoverySnapshot = _controlComposition.SessionController.HasRecoverySnapshot;
            UpdateStatusBar();
        }
    }
}
