using System;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerControlCompositionRoot
    {
        /// <summary>
        /// 装配控件所需的全部控制器。
        /// Chinese: 直接使用各装配器（Assembler）构造组合，不再经由 ControllerFactory 逐方法转发，
        /// 也不再把同一批构造签名重复声明成 Func 委托；新增控制器只需改对应装配器与这里的一处调用。
        /// English: Composes the control directly from the assemblers. The pure-forwarding controller factory and the
        /// duplicated Func signature declarations it required were removed.
        /// </summary>
        public static ImageViewerControlComposition Create(ImageViewer owner, ImageViewerDependencies dependencies)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(dependencies);

            var coreAssembler = new ImageViewer.CoreControllerAssembler(owner, dependencies);
            var analysisAssembler = new ImageViewer.AnalysisCompositionAssembler(owner);
            var sessionAssembler = new ImageViewer.SessionCompositionAssembler(owner);
            var interactionAssembler = new ImageViewer.InteractionCompositionAssembler(owner);
            var commandAssembler = new ImageViewer.CommandControllerAssembler(owner);

            ImageViewerControlCompositionParts parts = BuildParts(coreAssembler, analysisAssembler, sessionAssembler, interactionAssembler, commandAssembler);
            ImageViewerControlComposition composition = CreateComposition(parts, commandAssembler);
            ValidateWiring(parts, composition);
            return composition;
        }

        private static ImageViewerControlCompositionParts BuildParts(
            ImageViewer.CoreControllerAssembler coreAssembler,
            ImageViewer.AnalysisCompositionAssembler analysisAssembler,
            ImageViewer.SessionCompositionAssembler sessionAssembler,
            ImageViewer.InteractionCompositionAssembler interactionAssembler,
            ImageViewer.CommandControllerAssembler commandAssembler)
        {
            IImageViewStateController imageViewStateController = coreAssembler.CreateImageViewStateController();
            RoiSelectionStateController roiSelectionStateController = coreAssembler.CreateRoiSelectionStateController();
            ViewModelController viewModelController = coreAssembler.CreateViewModelController(roiSelectionStateController);
            ViewportController viewportController = coreAssembler.CreateViewportController();
            ImageViewerDialogWorkflowService dialogWorkflowService = coreAssembler.CreateDialogWorkflowService(roiSelectionStateController, viewportController);
            ImageViewerAnalysisComposition analysisComposition = analysisAssembler.CreateAnalysisComposition(dialogWorkflowService);
            ImageViewerSessionComposition sessionComposition = sessionAssembler.CreateSessionComposition(dialogWorkflowService, viewportController);
            ImageSourceController imageSourceController = coreAssembler.CreateImageSourceController(dialogWorkflowService, imageViewStateController);
            RoiEditController roiEditController = coreAssembler.CreateRoiEditController(dialogWorkflowService);
            CalibrationController calibrationController = coreAssembler.CreateCalibrationController(dialogWorkflowService);
            DroppedContentController droppedContentController = coreAssembler.CreateDroppedContentController(viewportController, sessionComposition.SessionController);
            ImageViewerInteractionComposition interactionComposition = interactionAssembler.CreateInteractionComposition(viewportController, roiEditController, sessionComposition.SessionController, analysisComposition.AnalysisController);
            ImageViewerFeatureMenuCommandController featureMenuCommandController = commandAssembler.CreateFeatureMenuCommandController(dialogWorkflowService);
            ExternalImageSourceBindingController externalImageSourceBindingController = coreAssembler.CreateExternalImageSourceBindingController();

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
            ImageViewer.CommandControllerAssembler commandAssembler)
        {
            ArgumentNullException.ThrowIfNull(parts);
            ArgumentNullException.ThrowIfNull(commandAssembler);

            return new ImageViewerControlComposition(
                parts.DialogWorkflowService,
                parts.ImageViewStateController,
                parts.ImageSourceController,
                parts.RoiSelectionStateController,
                parts.InteractionComposition.InteractionController,
                parts.ViewModelController,
                parts.RoiEditController,
                commandAssembler.CreateRoiMenuCommandController(parts.DialogWorkflowService, parts.RoiEditController, parts.CalibrationController),
                parts.SessionComposition.FileMenuCommandController,
                parts.FeatureMenuCommandController,
                commandAssembler.CreateViewCommandController(parts.ViewportController),
                commandAssembler.CreateModeCommandController(),
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