using System;
using System.Collections.Generic;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerViewCommandController : ImageViewerCommandControllerBase<IImageViewerViewCommandHost>
    {
        private readonly IReadOnlyDictionary<ImageViewerViewCommand, Action> _actions;

        public ImageViewerViewCommandController(IImageViewerViewCommandHost host)
            : base(host)
        {
            _actions = new Dictionary<ImageViewerViewCommand, Action>
            {
                [ImageViewerViewCommand.TogglePixelGrid] = () => Host.ShowPixelGrid = !Host.ShowPixelGrid,
                [ImageViewerViewCommand.ToggleCrosshair] = () => Host.ShowCrosshair = !Host.ShowCrosshair,
                [ImageViewerViewCommand.ToggleCaliperScores] = () => Host.ShowCaliperScores = !Host.ShowCaliperScores,
                [ImageViewerViewCommand.ToggleInfoPanel] = () => Host.ShowInfoPanel = !Host.ShowInfoPanel,
                [ImageViewerViewCommand.ToggleHistogram] = () => Host.ShowHistogram = !Host.ShowHistogram,
                [ImageViewerViewCommand.ToggleProfile] = () => Host.ShowProfile = !Host.ShowProfile,
                [ImageViewerViewCommand.ToggleScaleBar] = () => Host.ShowScaleBar = !Host.ShowScaleBar,
                [ImageViewerViewCommand.ToggleRoiList] = () => Host.ShowRoiList = !Host.ShowRoiList,
                [ImageViewerViewCommand.ToggleToolbar] = () => Host.ShowToolbar = !Host.ShowToolbar,
                [ImageViewerViewCommand.ToggleSnapGrid] = () => Host.ShowSnapGrid = !Host.ShowSnapGrid,
                [ImageViewerViewCommand.ToggleSnapToGrid] = () => Host.EnableSnapToGrid = !Host.EnableSnapToGrid,
                [ImageViewerViewCommand.FitToView] = Host.FitToView,
                [ImageViewerViewCommand.ActualSize] = Host.SetActualSize,
                [ImageViewerViewCommand.ZoomIn] = Host.ZoomIn,
                [ImageViewerViewCommand.ZoomOut] = Host.ZoomOut,
                [ImageViewerViewCommand.ZoomToSelection] = Host.ZoomToSelection,
                [ImageViewerViewCommand.ResetView] = Host.ResetView,
                [ImageViewerViewCommand.ShowFullImage] = Host.ShowFullImage,
                [ImageViewerViewCommand.RotateLeft] = Host.RotateLeft,
                [ImageViewerViewCommand.RotateRight] = Host.RotateRight,
                [ImageViewerViewCommand.FlipHorizontal] = Host.FlipHorizontal,
                [ImageViewerViewCommand.FlipVertical] = Host.FlipVertical
            };

            if (_actions.Count != Enum.GetValues<ImageViewerViewCommand>().Length)
            {
                throw new InvalidOperationException("The view command action map is incomplete.");
            }
        }

        public void Execute(ImageViewerViewCommand command)
        {
            if (!_actions.TryGetValue(command, out Action? action))
            {
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }

            action();
        }
    }
}