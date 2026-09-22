using System;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerModeCommandHostAdapter : IImageViewerModeCommandHost
    {
        private readonly ImageViewerModeCommandDependencies _dependencies;

        public ImageViewerModeCommandHostAdapter(ImageViewerModeCommandDependencies dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public void StartRectangleMode() => _dependencies.StartRectangleMode();

        public void StartEllipseMode() => _dependencies.StartEllipseMode();

        public void StartCircleMode() => _dependencies.StartCircleMode();

        public void StartPolygonMode() => _dependencies.StartPolygonMode();

        public void StartPolylineMode() => _dependencies.StartPolylineMode();

        public void StartFreehandMode() => _dependencies.StartFreehandMode();

        public void StartPointAnnotationMode() => _dependencies.StartPointAnnotationMode();

        public void StartTextAnnotationMode() => _dependencies.StartTextAnnotationMode();

        public void StartLineMeasureMode() => _dependencies.StartLineMeasureMode();

        public void StartAngleMeasureMode() => _dependencies.StartAngleMeasureMode();
    }
}