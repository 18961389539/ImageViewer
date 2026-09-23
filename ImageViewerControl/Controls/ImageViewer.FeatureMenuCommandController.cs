using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerFeatureMenuCommandDependencies
    {
        public required FrameworkElement RenderRoot { get; init; }
        public required Func<RoiBase?> GetSelectedRoi { get; init; }
        public required Func<BitmapSource?> GetAnalysisBitmapSource { get; init; }
        public required Func<RoiBase, RoiBase, RoiBase, IUndoRedoCommand?> CreateStateCommand { get; init; }
        public required Action<IUndoRedoCommand> ExecuteUndoRedoCommand { get; init; }
        public required Action DrawRois { get; init; }
        public required Func<string?> ShowSaveSnapshotDialog { get; init; }
        public required Func<string?> ShowSaveAnalysisCsvDialog { get; init; }
        public required Action<string, string> ShowReadOnlyText { get; init; }
        public required Func<IReadOnlyList<RoiBase>> GetAllRois { get; init; }
        public required Func<double> GetPixelSize { get; init; }
        public required Func<string> GetPhysicalUnit { get; init; }
        public required Func<CameraCalibration?> GetCalibration { get; init; }
        public required Action<string, string, Exception> ShowNonCriticalError { get; init; }
        public required Action<string, StatusHintKind> ShowStatusHint { get; init; }
        public required Action UpdateContextMenuState { get; init; }
    }

    internal interface IImageViewerFeatureMenuCommandHost
    {
        void RunGradientDetection();

        Task ExportSnapshotAsync();

        Task ExportAnalysisCsvAsync();

        void ShowAnalysisSummary();

        void UpdateContextMenuState();
    }

    internal sealed class ImageViewerFeatureMenuCommandController : ImageViewerMenuCommandControllerBase<IImageViewerFeatureMenuCommandHost>
    {
        public ImageViewerFeatureMenuCommandController(IImageViewerFeatureMenuCommandHost host)
            : base(host, host.UpdateContextMenuState)
        {
        }

        public async Task ExecuteAsync(ImageViewerFeatureMenuCommand command)
        {
            switch (command)
            {
                case ImageViewerFeatureMenuCommand.GradientDetect:
                    Host.RunGradientDetection();
                    break;
                case ImageViewerFeatureMenuCommand.ExportSnapshot:
                    await Host.ExportSnapshotAsync();
                    break;
                case ImageViewerFeatureMenuCommand.ExportAnalysisCsv:
                    await Host.ExportAnalysisCsvAsync();
                    break;
                case ImageViewerFeatureMenuCommand.ShowAnalysisSummary:
                    Host.ShowAnalysisSummary();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }

            RefreshMenuState();
        }
    }
}