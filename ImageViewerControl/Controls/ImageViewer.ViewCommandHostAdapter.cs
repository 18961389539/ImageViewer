using System;
using ImageViewer.Localization;

namespace ImageViewer.Controls
{
    /// <summary>
    /// 组合视图命令所需的最小能力接口。
    /// Chinese: 命令控制器只依赖视图选项、视口操作和图像变换三个能力，避免使用包含大量委托的依赖对象。
    /// English: Composes the smallest capabilities needed by view commands instead of accepting a large delegate bag.
    /// </summary>
    internal sealed class ImageViewerViewCommandHostAdapter : IImageViewerViewCommandHost
    {
        private readonly IImageViewerViewOptions _options;
        private readonly IImageViewerViewportOperations _viewport;
        private readonly IImageViewerImageTransformOperations _imageTransform;

        public ImageViewerViewCommandHostAdapter(
            IImageViewerViewOptions options,
            IImageViewerViewportOperations viewport,
            IImageViewerImageTransformOperations imageTransform)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            _imageTransform = imageTransform ?? throw new ArgumentNullException(nameof(imageTransform));
        }

        public bool ShowPixelGrid
        {
            get => _options.ShowPixelGrid;
            set => _options.ShowPixelGrid = value;
        }

        public bool ShowCrosshair
        {
            get => _options.ShowCrosshair;
            set => _options.ShowCrosshair = value;
        }

        public bool ShowCaliperScores
        {
            get => _options.ShowCaliperScores;
            set => _options.ShowCaliperScores = value;
        }

        public bool ShowInfoPanel
        {
            get => _options.ShowInfoPanel;
            set => _options.ShowInfoPanel = value;
        }

        public bool ShowHistogram
        {
            get => _options.ShowHistogram;
            set => _options.ShowHistogram = value;
        }

        public bool ShowProfile
        {
            get => _options.ShowProfile;
            set => _options.ShowProfile = value;
        }

        public bool ShowScaleBar
        {
            get => _options.ShowScaleBar;
            set => _options.ShowScaleBar = value;
        }

        public bool ShowRoiList
        {
            get => _options.ShowRoiList;
            set => _options.ShowRoiList = value;
        }

        public bool ShowToolbar
        {
            get => _options.ShowToolbar;
            set => _options.ShowToolbar = value;
        }

        public bool ShowSnapGrid
        {
            get => _options.ShowSnapGrid;
            set => _options.ShowSnapGrid = value;
        }

        public bool EnableSnapToGrid
        {
            get => _options.EnableSnapToGrid;
            set => _options.EnableSnapToGrid = value;
        }

        public void FitToView() => _viewport.FitToView();

        public void FillToView() => _viewport.FillToView();

        public void ResetView() => _viewport.ResetView();

        public void ShowFullImage() => _viewport.ShowFullImage();

        public void SetActualSize() => _viewport.SetActualSize();

        public void ZoomIn() => _viewport.ZoomIn();

        public void ZoomOut() => _viewport.ZoomOut();

        public void ZoomToSelection() => _viewport.ZoomToSelection();

        public void RotateLeft() => _imageTransform.RotateLeft();

        public void RotateRight() => _imageTransform.RotateRight();

        public void FlipHorizontal() => _imageTransform.FlipHorizontal();

        public void FlipVertical() => _imageTransform.FlipVertical();
    }

    internal sealed class ImageViewerViewOptionsAdapter : IImageViewerViewOptions
    {
        private readonly ImageViewer _owner;

        public ImageViewerViewOptionsAdapter(ImageViewer owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public bool ShowPixelGrid
        {
            get => _owner.ShowPixelGrid;
            set => _owner.ShowPixelGrid = value;
        }

        public bool ShowCrosshair
        {
            get => _owner.ShowCrosshair;
            set => _owner.ShowCrosshair = value;
        }

        public bool ShowCaliperScores
        {
            get => _owner.ShowCaliperScores;
            set => _owner.ShowCaliperScores = value;
        }

        public bool ShowInfoPanel
        {
            get => _owner.ShowInfoPanel;
            set => _owner.ShowInfoPanel = value;
        }

        public bool ShowHistogram
        {
            get => _owner.ShowHistogram;
            set => _owner.ShowHistogram = value;
        }

        public bool ShowProfile
        {
            get => _owner.ShowProfile;
            set => _owner.ShowProfile = value;
        }

        public bool ShowScaleBar
        {
            get => _owner.ShowScaleBar;
            set => _owner.ShowScaleBar = value;
        }

        public bool ShowRoiList
        {
            get => _owner.ShowRoiList;
            set => _owner.ShowRoiList = value;
        }

        public bool ShowToolbar
        {
            get => _owner.ShowToolbar;
            set => _owner.ShowToolbar = value;
        }

        public bool ShowSnapGrid
        {
            get => _owner.ShowSnapGrid;
            set => _owner.ShowSnapGrid = value;
        }

        public bool EnableSnapToGrid
        {
            get => _owner.EnableSnapToGrid;
            set => _owner.EnableSnapToGrid = value;
        }
    }

    internal sealed class ImageViewerViewportOperationsAdapter : IImageViewerViewportOperations
    {
        private readonly ImageViewer _owner;
        private readonly ViewportController _viewportController;

        public ImageViewerViewportOperationsAdapter(ImageViewer owner, ViewportController viewportController)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _viewportController = viewportController ?? throw new ArgumentNullException(nameof(viewportController));
        }

        public void FitToView()
        {
            _viewportController.FitToView();
            _owner.ShowStatusHint(UiText.Get("StatusFitToView"), StatusHintKind.Success);
        }

        public void FillToView()
        {
            _viewportController.FillToView();
            _owner.ShowStatusHint(UiText.Get("StatusFillToView"), StatusHintKind.Success);
        }

        public void ResetView()
        {
            _viewportController.ResetView();
            _owner.ResetImageOrientation();
            _owner.ShowStatusHint(UiText.Get("StatusResetViewDone"), StatusHintKind.Success);
        }

        public void ShowFullImage()
        {
            _viewportController.ShowFullImage();
            _owner.ShowStatusHint(UiText.Get("StatusShowFullImage"), StatusHintKind.Success);
        }

        public void SetActualSize()
        {
            _viewportController.SetActualSize();
            _owner.ShowStatusHint(UiText.Get("StatusActualSize"), StatusHintKind.Success);
        }

        public void ZoomIn() => _owner.ZoomAtViewportCenter(1.25);

        public void ZoomOut() => _owner.ZoomAtViewportCenter(0.8);

        public void ZoomToSelection() => _viewportController.ZoomToSelection();
    }

    internal sealed class ImageViewerImageTransformOperationsAdapter : IImageViewerImageTransformOperations
    {
        private readonly ImageViewer _owner;

        public ImageViewerImageTransformOperationsAdapter(ImageViewer owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public void RotateLeft() => _owner.RotateImageLeft();

        public void RotateRight() => _owner.RotateImageRight();

        public void FlipHorizontal() => _owner.FlipImageHorizontal();

        public void FlipVertical() => _owner.FlipImageVertical();
    }
}
