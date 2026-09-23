using System;
namespace ImageViewer.Controls
{
    internal sealed class ImageViewerControlCompositionContext
    {
        public required ImageViewerCoreCompositionContext Core { get; init; }

        public required ImageViewerAnalysisCompositionContext Analysis { get; init; }

        public required ImageViewerSessionCompositionContext Session { get; init; }

        public required ImageViewerInteractionCompositionContext Interaction { get; init; }

        public required ImageViewerCommandCompositionContext Commands { get; init; }
    }

    internal sealed class ImageViewerCoreCompositionContext
    {
        public required Func<IImageViewStateController> CreateImageViewStateController { get; init; }

        public required Func<RoiSelectionStateController> CreateRoiSelectionStateController { get; init; }

        public required Func<RoiSelectionStateController, ViewModelController> CreateViewModelController { get; init; }

        public required Func<ViewportController> CreateViewportController { get; init; }

        public required Func<RoiSelectionStateController, ViewportController, ImageViewerDialogWorkflowService> CreateDialogWorkflowService { get; init; }

        public required Func<ImageViewerDialogWorkflowService, IImageViewStateController, ImageSourceController> CreateImageSourceController { get; init; }

        public required Func<ImageViewerDialogWorkflowService, RoiEditController> CreateRoiEditController { get; init; }

        public required Func<ImageViewerDialogWorkflowService, CalibrationController> CreateCalibrationController { get; init; }

        public required Func<ViewportController, ImageViewerSessionController, DroppedContentController> CreateDroppedContentController { get; init; }

        public required Func<ExternalImageSourceBindingController> CreateExternalImageSourceBindingController { get; init; }
    }

    internal sealed class ImageViewerAnalysisCompositionContext
    {
        public required Func<ImageViewerDialogWorkflowService, ImageViewerAnalysisComposition> CreateAnalysisComposition { get; init; }
    }

    internal sealed class ImageViewerSessionCompositionContext
    {
        public required Func<ImageViewerDialogWorkflowService, ViewportController, ImageViewerSessionComposition> CreateSessionComposition { get; init; }
    }

    internal sealed class ImageViewerInteractionCompositionContext
    {
        public required Func<ViewportController, RoiEditController, ImageViewerSessionController, ImageViewerAnalysisCoordinator, ImageViewerInteractionComposition> CreateInteractionComposition { get; init; }
    }

    internal sealed class ImageViewerCommandCompositionContext
    {
        public required Func<ImageViewerDialogWorkflowService, ImageViewerFeatureMenuCommandController> CreateFeatureMenuCommandController { get; init; }

        public required Func<ImageViewerDialogWorkflowService, RoiEditController, CalibrationController, ImageViewerRoiMenuCommandController> CreateRoiMenuCommandController { get; init; }

        public required Func<ViewportController, ImageViewerViewCommandController> CreateViewCommandController { get; init; }

        public required Func<ImageViewerModeCommandController> CreateModeCommandController { get; init; }
    }

    internal sealed class ImageViewerControlCompositionRoot
    {
        public static ImageViewerControlComposition Create(ImageViewer owner, ImageViewerDependencies dependencies)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(dependencies);

            var controllerFactory = new ImageViewer.ControllerFactory(owner, dependencies);

            return Create(new ImageViewerControlCompositionContext
            {
                Core = new ImageViewerCoreCompositionContext
                {
                    CreateImageViewStateController = controllerFactory.CreateImageViewStateController,
                    CreateRoiSelectionStateController = controllerFactory.CreateRoiSelectionStateController,
                    CreateViewModelController = controllerFactory.CreateViewModelController,
                    CreateViewportController = controllerFactory.CreateViewportController,
                    CreateDialogWorkflowService = controllerFactory.CreateDialogWorkflowService,
                    CreateImageSourceController = controllerFactory.CreateImageSourceController,
                    CreateRoiEditController = controllerFactory.CreateRoiEditController,
                    CreateCalibrationController = controllerFactory.CreateCalibrationController,
                    CreateDroppedContentController = controllerFactory.CreateDroppedContentController,
                    CreateExternalImageSourceBindingController = controllerFactory.CreateExternalImageSourceBindingController
                },
                Analysis = new ImageViewerAnalysisCompositionContext
                {
                    CreateAnalysisComposition = controllerFactory.CreateAnalysisComposition
                },
                Session = new ImageViewerSessionCompositionContext
                {
                    CreateSessionComposition = controllerFactory.CreateSessionComposition
                },
                Interaction = new ImageViewerInteractionCompositionContext
                {
                    CreateInteractionComposition = controllerFactory.CreateInteractionComposition
                },
                Commands = new ImageViewerCommandCompositionContext
                {
                    CreateFeatureMenuCommandController = controllerFactory.CreateFeatureMenuCommandController,
                    CreateRoiMenuCommandController = controllerFactory.CreateRoiMenuCommandController,
                    CreateViewCommandController = controllerFactory.CreateViewCommandController,
                    CreateModeCommandController = controllerFactory.CreateModeCommandController
                }
            });
        }

        internal static ImageViewerControlComposition Create(ImageViewerControlCompositionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            ImageViewerControlCompositionParts parts = BuildParts(context);
            ImageViewerControlComposition composition = CreateComposition(parts, context.Commands);
            ValidateWiring(parts, composition);
            return composition;
        }

        private static ImageViewerControlCompositionParts BuildParts(ImageViewerControlCompositionContext context)
        {
            IImageViewStateController imageViewStateController = context.Core.CreateImageViewStateController();
            RoiSelectionStateController roiSelectionStateController = context.Core.CreateRoiSelectionStateController();
            ViewModelController viewModelController = context.Core.CreateViewModelController(roiSelectionStateController);
            ViewportController viewportController = context.Core.CreateViewportController();
            ImageViewerDialogWorkflowService dialogWorkflowService = context.Core.CreateDialogWorkflowService(roiSelectionStateController, viewportController);
            ImageViewerAnalysisComposition analysisComposition = context.Analysis.CreateAnalysisComposition(dialogWorkflowService);
            ImageViewerSessionComposition sessionComposition = context.Session.CreateSessionComposition(dialogWorkflowService, viewportController);
            ImageSourceController imageSourceController = context.Core.CreateImageSourceController(dialogWorkflowService, imageViewStateController);
            RoiEditController roiEditController = context.Core.CreateRoiEditController(dialogWorkflowService);
            CalibrationController calibrationController = context.Core.CreateCalibrationController(dialogWorkflowService);
            DroppedContentController droppedContentController = context.Core.CreateDroppedContentController(viewportController, sessionComposition.SessionController);
            ImageViewerInteractionComposition interactionComposition = context.Interaction.CreateInteractionComposition(viewportController, roiEditController, sessionComposition.SessionController, analysisComposition.AnalysisController);
            ImageViewerFeatureMenuCommandController featureMenuCommandController = context.Commands.CreateFeatureMenuCommandController(dialogWorkflowService);
            ExternalImageSourceBindingController externalImageSourceBindingController = context.Core.CreateExternalImageSourceBindingController();

            return new ImageViewerControlCompositionParts(
                dialogWorkflowService,
                imageViewStateController,
                imageSourceController,
                roiSelectionStateController,
                interactionComposition,
                viewModelController,
                roiEditController,
                featureMenuCommandController,
                viewportController,
                sessionComposition,
                calibrationController,
                droppedContentController,
                analysisComposition,
                externalImageSourceBindingController);
        }

        private static ImageViewerControlComposition CreateComposition(
            ImageViewerControlCompositionParts parts,
            ImageViewerCommandCompositionContext commandContext)
        {
            ArgumentNullException.ThrowIfNull(parts);
            ArgumentNullException.ThrowIfNull(commandContext);

            return new ImageViewerControlComposition(
                parts.DialogWorkflowService,
                parts.ImageViewStateController,
                parts.ImageSourceController,
                parts.RoiSelectionStateController,
                parts.InteractionComposition.InteractionController,
                parts.ViewModelController,
                parts.RoiEditController,
                commandContext.CreateRoiMenuCommandController(parts.DialogWorkflowService, parts.RoiEditController, parts.CalibrationController),
                parts.SessionComposition.FileMenuCommandController,
                parts.FeatureMenuCommandController,
                commandContext.CreateViewCommandController(parts.ViewportController),
                commandContext.CreateModeCommandController(),
                parts.ViewportController,
                parts.SessionComposition.SessionController,
                parts.SessionComposition.RoiPersistenceController,
                parts.CalibrationController,
                parts.DroppedContentController,
                parts.InteractionComposition.ContextMenuController,
                parts.AnalysisComposition.AnalysisController,
                parts.AnalysisComposition.AnalysisCommandController,
                parts.ExternalImageSourceBindingController);
        }

        /// <summary>
        /// 装配自检：验证扁平组合与各子组合引用的控制器为同一实例，
        /// 防止后续扩展时新增部件却接入不同实例导致状态不同步。
        /// Chinese: 在组合根装配完成后做引用一致性校验，接线错误时快速失败。
        /// English: Verifies referential consistency between the flattened composition and its sub-compositions after assembly.
        /// </summary>
        private static void ValidateWiring(ImageViewerControlCompositionParts parts, ImageViewerControlComposition composition)
        {
            RequireSameInstance(nameof(composition.SessionController), composition.SessionController, parts.SessionComposition.SessionController);
            RequireSameInstance(nameof(composition.RoiPersistenceController), composition.RoiPersistenceController, parts.SessionComposition.RoiPersistenceController);
            RequireSameInstance(nameof(composition.FileMenuCommandController), composition.FileMenuCommandController, parts.SessionComposition.FileMenuCommandController);
            RequireSameInstance(nameof(composition.AnalysisController), composition.AnalysisController, parts.AnalysisComposition.AnalysisController);
            RequireSameInstance(nameof(composition.AnalysisCommandController), composition.AnalysisCommandController, parts.AnalysisComposition.AnalysisCommandController);
            RequireSameInstance(nameof(composition.InteractionController), composition.InteractionController, parts.InteractionComposition.InteractionController);
            RequireSameInstance(nameof(composition.ContextMenuController), composition.ContextMenuController, parts.InteractionComposition.ContextMenuController);
        }

        private static void RequireSameInstance(string partName, object? composed, object? source)
        {
            if (!ReferenceEquals(composed, source))
            {
                throw new InvalidOperationException($"Composition wiring mismatch for '{partName}': the composition holds a different instance than the part it was built from.");
            }
        }
    }

    internal sealed record ImageViewerControlCompositionParts(
        ImageViewerDialogWorkflowService DialogWorkflowService,
        IImageViewStateController ImageViewStateController,
        ImageSourceController ImageSourceController,
        RoiSelectionStateController RoiSelectionStateController,
        ImageViewerInteractionComposition InteractionComposition,
        ViewModelController ViewModelController,
        RoiEditController RoiEditController,
        ImageViewerFeatureMenuCommandController FeatureMenuCommandController,
        ViewportController ViewportController,
        ImageViewerSessionComposition SessionComposition,
        CalibrationController CalibrationController,
        DroppedContentController DroppedContentController,
        ImageViewerAnalysisComposition AnalysisComposition,
        ExternalImageSourceBindingController ExternalImageSourceBindingController);
}