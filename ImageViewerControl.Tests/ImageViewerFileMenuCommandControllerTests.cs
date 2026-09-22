using System.Threading.Tasks;
using System;
using ImageViewer.Controls;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class ImageViewerFileMenuCommandControllerTests
    {
        [Fact]
        public async Task ExecuteAsync_SaveSession_DelegatesAndRefreshesMenuState()
        {
            var host = new FakeFileMenuCommandHost();
            var controller = new ImageViewerFileMenuCommandController(host);

            await controller.ExecuteAsync(ImageViewerFileMenuCommand.SaveSession);

            Assert.Equal(1, host.SaveSessionCount);
            Assert.Equal(1, host.UpdateContextMenuStateCount);
        }

        [Fact]
        public async Task ExecuteAsync_OpenImage_DelegatesAndRefreshesMenuState()
        {
            var host = new FakeFileMenuCommandHost();
            var controller = new ImageViewerFileMenuCommandController(host);

            await controller.ExecuteAsync(ImageViewerFileMenuCommand.OpenImage);

            Assert.Equal(1, host.OpenImageCount);
            Assert.Equal(1, host.UpdateContextMenuStateCount);
        }

        [Fact]
        public async Task ExecuteAsync_ToggleAutoSave_DelegatesAndRefreshesMenuState()
        {
            var host = new FakeFileMenuCommandHost();
            var controller = new ImageViewerFileMenuCommandController(host);

            await controller.ExecuteAsync(ImageViewerFileMenuCommand.ToggleAutoSave);

            Assert.Equal(1, host.ToggleAutoSaveCount);
            Assert.Equal(1, host.UpdateContextMenuStateCount);
        }

        [Fact]
        public async Task OpenRecentProjectAsync_DelegatesAndRefreshesMenuState()
        {
            var host = new FakeFileMenuCommandHost();
            var controller = new ImageViewerFileMenuCommandController(host);

            await controller.OpenRecentProjectAsync("c:/demo.ivsession");

            Assert.Equal("c:/demo.ivsession", host.LastRecentProjectPath);
            Assert.Equal(1, host.UpdateContextMenuStateCount);
        }

        [Fact]
        public async Task ExecuteAsync_UnknownCommand_ThrowsArgumentOutOfRangeException()
        {
            var controller = new ImageViewerFileMenuCommandController(new FakeFileMenuCommandHost());

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                controller.ExecuteAsync((ImageViewerFileMenuCommand)int.MaxValue));
        }

        private sealed class FakeFileMenuCommandHost : IImageViewerFileMenuCommandHost
        {
            public int OpenImageCount { get; private set; }
            public int SaveSessionCount { get; private set; }
            public int ToggleAutoSaveCount { get; private set; }
            public int UpdateContextMenuStateCount { get; private set; }
            public string? LastRecentProjectPath { get; private set; }

            public Task ShowOpenImageDialogAsync()
            {
                OpenImageCount++;
                return Task.CompletedTask;
            }
            public Task OpenRecentProjectAsync(string filePath)
            {
                LastRecentProjectPath = filePath;
                return Task.CompletedTask;
            }
            public Task SaveRoisAsync() => Task.CompletedTask;
            public Task LoadRoisAsync() => Task.CompletedTask;
            public Task SaveSessionAsync()
            {
                SaveSessionCount++;
                return Task.CompletedTask;
            }

            public Task LoadSessionAsync() => Task.CompletedTask;
            public Task ExportProjectPackageAsync() => Task.CompletedTask;
            public void ToggleAutoSave() => ToggleAutoSaveCount++;
            public void UpdateContextMenuState() => UpdateContextMenuStateCount++;
        }
    }
}