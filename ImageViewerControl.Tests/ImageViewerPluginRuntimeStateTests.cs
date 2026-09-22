using ImageViewer.Controls;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class ImageViewerPluginRuntimeStateTests
    {
        [Fact]
        public void PluginRegistry_Switch_RebuildsPluginBoundServicesAndUpdatesViewModel()
        {
            RoiPluginRegistry initialRegistry = RoiPluginRegistry.CreateBuiltIn();
            RoiPluginRegistry updatedRegistry = RoiPluginRegistry.CreateBuiltIn();
            var state = new ImageViewerPluginRuntimeState(initialRegistry, SelectedRoiDetectionService.Default);

            var initialInteraction = state.RoiInteraction;
            var initialRenderer = state.RoiRenderer;

            state.PluginRegistry = updatedRegistry;

            Assert.Same(updatedRegistry, state.PluginRegistry);
            Assert.Same(updatedRegistry, state.ViewModel.PluginRegistry);
            Assert.NotSame(initialInteraction, state.RoiInteraction);
            Assert.NotSame(initialRenderer, state.RoiRenderer);
        }

        [Fact]
        public void PluginRegistry_SameInstance_DoesNotRebuildPluginBoundServices()
        {
            RoiPluginRegistry pluginRegistry = RoiPluginRegistry.CreateBuiltIn();
            var state = new ImageViewerPluginRuntimeState(pluginRegistry, SelectedRoiDetectionService.Default);

            var initialInteraction = state.RoiInteraction;
            var initialRenderer = state.RoiRenderer;

            state.PluginRegistry = pluginRegistry;

            Assert.Same(initialInteraction, state.RoiInteraction);
            Assert.Same(initialRenderer, state.RoiRenderer);
            Assert.Same(pluginRegistry, state.ViewModel.PluginRegistry);
        }
    }
}