using System;
using ImageViewer.Localization;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        internal sealed class CommandControllerAssembler
        {
            private readonly ImageViewer _owner;

            public CommandControllerAssembler(ImageViewer owner)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public ImageViewerFeatureMenuCommandController CreateFeatureMenuCommandController(ImageViewerDialogWorkflowService dialogWorkflowService)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);

                return new ImageViewerFeatureMenuCommandController(
                    new ImageViewerFeatureMenuCommandHostAdapter(
                        new ImageViewerFeatureAnalysisCapability(_owner),
                        new ImageViewerFeatureMutationCapability(_owner),
                        new ImageViewerFeatureExportDataCapability(_owner),
                        new ImageViewerFeatureDialogCapability(_owner, dialogWorkflowService),
                        new ImageViewerFeatureFeedbackCapability(_owner)));
            }

            public ImageViewerRoiMenuCommandController CreateRoiMenuCommandController(
                ImageViewerDialogWorkflowService dialogWorkflowService,
                RoiEditController roiEditController,
                CalibrationController calibrationController)
            {
                ArgumentNullException.ThrowIfNull(dialogWorkflowService);
                ArgumentNullException.ThrowIfNull(roiEditController);
                ArgumentNullException.ThrowIfNull(calibrationController);

                return new ImageViewerRoiMenuCommandController(
                    new ImageViewerRoiMenuCommandHostAdapter(
                        new ImageViewerRoiSelectionCapability(_owner),
                        new ImageViewerRoiEditingCapability(roiEditController, calibrationController, dialogWorkflowService),
                        new ImageViewerContextMenuCapability(_owner)));
            }

            public ImageViewerViewCommandController CreateViewCommandController(ViewportController viewportController)
            {
                ArgumentNullException.ThrowIfNull(viewportController);
                var options = new ImageViewerViewOptionsAdapter(_owner);
                var viewport = new ImageViewerViewportOperationsAdapter(_owner, viewportController);
                var imageTransform = new ImageViewerImageTransformOperationsAdapter(_owner);
                var host = new ImageViewerViewCommandHostAdapter(options, viewport, imageTransform);
                return new ImageViewerViewCommandController(host);
            }

            public ImageViewerModeCommandController CreateModeCommandController()
            {
                return new ImageViewerModeCommandController(new ImageViewerModeCommandHostAdapter(_owner));
            }
        }
    }
}
