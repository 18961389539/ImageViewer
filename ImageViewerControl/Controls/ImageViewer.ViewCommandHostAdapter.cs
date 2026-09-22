using System;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerViewCommandHostAdapter : IImageViewerViewCommandHost
    {
        private readonly ImageViewerViewCommandDependencies _dependencies;

        public ImageViewerViewCommandHostAdapter(ImageViewerViewCommandDependencies dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public bool ShowPixelGrid
        {
            get => _dependencies.GetShowPixelGrid();
            set => _dependencies.SetShowPixelGrid(value);
        }

        public bool ShowCrosshair
        {
            get => _dependencies.GetShowCrosshair();
            set => _dependencies.SetShowCrosshair(value);
        }

        public bool ShowCaliperScores
        {
            get => _dependencies.GetShowCaliperScores();
            set => _dependencies.SetShowCaliperScores(value);
        }

        public bool ShowInfoPanel
        {
            get => _dependencies.GetShowInfoPanel();
            set => _dependencies.SetShowInfoPanel(value);
        }

        public bool ShowHistogram
        {
            get => _dependencies.GetShowHistogram();
            set => _dependencies.SetShowHistogram(value);
        }

        public bool ShowProfile
        {
            get => _dependencies.GetShowProfile();
            set => _dependencies.SetShowProfile(value);
        }

        public bool ShowScaleBar
        {
            get => _dependencies.GetShowScaleBar();
            set => _dependencies.SetShowScaleBar(value);
        }

        public bool ShowRoiList
        {
            get => _dependencies.GetShowRoiList();
            set => _dependencies.SetShowRoiList(value);
        }

        public bool ShowSnapGrid
        {
            get => _dependencies.GetShowSnapGrid();
            set => _dependencies.SetShowSnapGrid(value);
        }

        public bool EnableSnapToGrid
        {
            get => _dependencies.GetEnableSnapToGrid();
            set => _dependencies.SetEnableSnapToGrid(value);
        }

        public void FitToView() => _dependencies.FitToView();

        public void ResetView() => _dependencies.ResetView();

        public void ShowFullImage() => _dependencies.ShowFullImage();

        public void SetActualSize() => _dependencies.SetActualSize();

        public void ZoomIn() => _dependencies.ZoomIn();

        public void ZoomOut() => _dependencies.ZoomOut();

        public void ZoomToSelection() => _dependencies.ZoomToSelection();

        public void RotateLeft() => _dependencies.RotateLeft();

        public void RotateRight() => _dependencies.RotateRight();

        public void FlipHorizontal() => _dependencies.FlipHorizontal();

        public void FlipVertical() => _dependencies.FlipVertical();
    }
}