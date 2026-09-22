using System;
using ImageViewer.Models;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerRoiMenuCommandDependencies
    {
        public required Func<RoiBase?> GetSelectedRoi { get; init; }
        public required RoiEditController RoiEditController { get; init; }
        public required CalibrationController CalibrationController { get; init; }
        public required Action<RoiBase> ShowRoiProperties { get; init; }
        public required Action<RoiBase> ShowCaliperSettings { get; init; }
        public required Action UpdateContextMenuState { get; init; }
    }

    internal sealed class ImageViewerRoiMenuCommandHostAdapter : IImageViewerRoiMenuCommandHost
    {
        private readonly ImageViewerRoiMenuCommandDependencies _dependencies;

        public ImageViewerRoiMenuCommandHostAdapter(ImageViewerRoiMenuCommandDependencies dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public void Undo() => _dependencies.RoiEditController.Undo();

        public void Redo() => _dependencies.RoiEditController.Redo();

        public void DeleteSelected() => _dependencies.RoiEditController.DeleteSelected();

        public void ClearAll() => _dependencies.RoiEditController.ClearAll();

        public void EditSelectedProperties()
        {
            if (_dependencies.GetSelectedRoi() is RoiBase roi)
            {
                _dependencies.ShowRoiProperties(roi);
            }
        }

        public void SetSelectedLabel() => _dependencies.RoiEditController.SetSelectedLabel();

        public void SetSelectedColor(string colorName) => _dependencies.RoiEditController.SetSelectedColor(colorName);

        public void CalibrateSelectedRoi() => _dependencies.CalibrationController.CalibrateSelectedRoi();

        public void EditSelectedCaliperSettings()
        {
            if (_dependencies.GetSelectedRoi() is RoiBase roi)
            {
                _dependencies.ShowCaliperSettings(roi);
            }
        }

        public void UpdateContextMenuState() => _dependencies.UpdateContextMenuState();
    }
}