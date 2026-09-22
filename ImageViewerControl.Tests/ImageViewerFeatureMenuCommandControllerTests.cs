using System.Threading.Tasks;
using System;
using ImageViewer.Controls;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class ImageViewerFeatureMenuCommandControllerTests
    {
        [Fact]
        public async Task ExecuteAsync_GradientDetect_DelegatesAndRefreshesMenuState()
        {
            var host = new FakeFeatureMenuCommandHost();
            var controller = new ImageViewerFeatureMenuCommandController(host);

            await controller.ExecuteAsync(ImageViewerFeatureMenuCommand.GradientDetect);

            Assert.Equal(1, host.RunGradientDetectionCount);
            Assert.Equal(1, host.UpdateContextMenuStateCount);
        }

        [Fact]
        public async Task ExecuteAsync_ExportSnapshot_AwaitsAndRefreshesMenuState()
        {
            var host = new FakeFeatureMenuCommandHost();
            var controller = new ImageViewerFeatureMenuCommandController(host);

            await controller.ExecuteAsync(ImageViewerFeatureMenuCommand.ExportSnapshot);

            Assert.Equal(1, host.ExportSnapshotCount);
            Assert.Equal(1, host.UpdateContextMenuStateCount);
        }

        [Fact]
        public async Task ExecuteAsync_UnknownCommand_ThrowsArgumentOutOfRangeException()
        {
            var controller = new ImageViewerFeatureMenuCommandController(new FakeFeatureMenuCommandHost());

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                controller.ExecuteAsync((ImageViewerFeatureMenuCommand)int.MaxValue));
        }

        private sealed class FakeFeatureMenuCommandHost : IImageViewerFeatureMenuCommandHost
        {
            public int RunGradientDetectionCount { get; private set; }
            public int ExportSnapshotCount { get; private set; }
            public int ExportAnalysisCsvCount { get; private set; }
            public int ShowAnalysisSummaryCount { get; private set; }
            public int UpdateContextMenuStateCount { get; private set; }

            public void RunGradientDetection() => RunGradientDetectionCount++;

            public Task ExportSnapshotAsync()
            {
                ExportSnapshotCount++;
                return Task.CompletedTask;
            }

            public Task ExportAnalysisCsvAsync()
            {
                ExportAnalysisCsvCount++;
                return Task.CompletedTask;
            }

            public void ShowAnalysisSummary() => ShowAnalysisSummaryCount++;

            public void UpdateContextMenuState() => UpdateContextMenuStateCount++;
        }
    }
}