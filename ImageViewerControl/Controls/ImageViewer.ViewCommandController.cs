using System;
using System.Collections.Generic;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerViewCommandController
    {
        private readonly IImageViewerViewCommandHost _host;
        private readonly IReadOnlyDictionary<ImageViewerViewCommand, Action> _actions;

        public ImageViewerViewCommandController(IImageViewerViewCommandHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _actions = new Dictionary<ImageViewerViewCommand, Action>
            {
                [ImageViewerViewCommand.TogglePixelGrid] = () => _host.ShowPixelGrid = !_host.ShowPixelGrid,
                [ImageViewerViewCommand.ToggleCrosshair] = () => _host.ShowCrosshair = !_host.ShowCrosshair,
                [ImageViewerViewCommand.ToggleCaliperScores] = () => _host.ShowCaliperScores = !_host.ShowCaliperScores,
                [ImageViewerViewCommand.ToggleInfoPanel] = () => _host.ShowInfoPanel = !_host.ShowInfoPanel,
                [ImageViewerViewCommand.ToggleHistogram] = () => _host.ShowHistogram = !_host.ShowHistogram,
                [ImageViewerViewCommand.ToggleProfile] = () => _host.ShowProfile = !_host.ShowProfile,
                [ImageViewerViewCommand.ToggleScaleBar] = () => _host.ShowScaleBar = !_host.ShowScaleBar,
                [ImageViewerViewCommand.ToggleRoiList] = () => _host.ShowRoiList = !_host.ShowRoiList,
                [ImageViewerViewCommand.ToggleSnapGrid] = () => _host.ShowSnapGrid = !_host.ShowSnapGrid,
                [ImageViewerViewCommand.ToggleSnapToGrid] = () => _host.EnableSnapToGrid = !_host.EnableSnapToGrid,
                [ImageViewerViewCommand.FitToView] = _host.FitToView,
                [ImageViewerViewCommand.ActualSize] = _host.SetActualSize,
                [ImageViewerViewCommand.ZoomIn] = _host.ZoomIn,
                [ImageViewerViewCommand.ZoomOut] = _host.ZoomOut,
                [ImageViewerViewCommand.ZoomToSelection] = _host.ZoomToSelection,
                [ImageViewerViewCommand.ResetView] = _host.ResetView,
                [ImageViewerViewCommand.ShowFullImage] = _host.ShowFullImage,
                [ImageViewerViewCommand.RotateLeft] = _host.RotateLeft,
                [ImageViewerViewCommand.RotateRight] = _host.RotateRight,
                [ImageViewerViewCommand.FlipHorizontal] = _host.FlipHorizontal,
                [ImageViewerViewCommand.FlipVertical] = _host.FlipVertical
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