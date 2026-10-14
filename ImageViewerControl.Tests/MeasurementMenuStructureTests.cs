using System.Linq;
using System.Windows.Controls;
using ImageViewer.Controls;
using ImageViewer.Localization;
using ImageViewer.Plugins;
using Xunit;

namespace ImageViewerControl.Tests;

[Collection(WpfTestCollection.Name)]
[Trait("Category", "Smoke")]
[Trait("Category", "Wpf")]
public sealed class MeasurementMenuStructureTests
{
    [Fact]
    public void FrequentCommandsRemainAtTheTopLevelAndSelectedShortcutsKeepTheirCommands()
    {
        WpfTestRunner.Run(() =>
        {
            using var viewer = new ImageViewer.Controls.ImageViewer();
            MenuItem[] rootItems = viewer.mainContextMenu.Items.OfType<MenuItem>().ToArray();

            Assert.Contains(viewer.openImageMenuItem, rootItems);
            Assert.Contains(viewer.saveSessionMenuItem, rootItems);
            Assert.Contains(viewer.undoMenuItem, rootItems);
            Assert.Contains(viewer.redoMenuItem, rootItems);
            Assert.Contains(viewer.fitToViewMenuItem, rootItems);
            Assert.Contains(viewer.actualSizeMenuItem, rootItems);
            Assert.Contains(viewer.resetViewMenuItem, rootItems);
            Assert.DoesNotContain(rootItems, item => Equals(item.Header, UiText.Get("MenuPrimaryActions")));

            MenuItem[] selectedShortcuts = viewer.contextRoiShortcutsMenuItem.Items.OfType<MenuItem>().ToArray();
            Assert.Contains(viewer.editPropertiesMenuItem, rootItems);
            Assert.Contains(viewer.setLabelMenuItem, selectedShortcuts);
            Assert.Contains(viewer.setColorMenuItem, selectedShortcuts);
            Assert.Contains(viewer.editCaliperSettingsMenuItem, selectedShortcuts);
            Assert.Contains(viewer.zoomToSelectionMenuItem, selectedShortcuts);
        });
    }

    [Fact]
    public void ImageDisplayAndPerformanceCommandsAreSeparated()
    {
        WpfTestRunner.Run(() =>
        {
            using var viewer = new ImageViewer.Controls.ImageViewer();
            MenuItem imageDisplay = FindRootMenu(viewer, UiText.Get("MenuImageDisplay"));
            MenuItem performance = FindRootMenu(viewer, UiText.Get("MenuPerformance"));

            Assert.Contains(viewer.pseudoColorMenuItem, imageDisplay.Items.OfType<MenuItem>());
            Assert.Contains(viewer.showHistogramMenuItem, imageDisplay.Items.OfType<MenuItem>());
            Assert.Contains(viewer.showProfileMenuItem, imageDisplay.Items.OfType<MenuItem>());
            Assert.Contains(viewer.enableImagePyramidMenuItem, performance.Items.OfType<MenuItem>());
            Assert.Contains(viewer.showRenderStatusMenuItem, performance.Items.OfType<MenuItem>());
            Assert.DoesNotContain(performance.Items.OfType<MenuItem>(), item => ReferenceEquals(item, viewer.pseudoColorMenuItem));
        });
    }

    [Fact]
    public void MeasurementMenuHasFiveTaskGroupsAndEveryAdvancedDescriptor()
    {
        WpfTestRunner.Run(() =>
        {
            using var viewer = new ImageViewer.Controls.ImageViewer();
            WpfTestRunner.InvokePrivate(viewer, "PopulateMeasurementMenu", viewer.AvailableDrawingTools);
            WpfTestRunner.InvokePrivate(viewer, "PopulateMeasurementMenu", viewer.AvailableDrawingTools);

            string[] groupKeys =
            [
                "MenuMeasurementBasic",
                "MenuMeasurementEdge",
                "MenuMeasurementGeometry",
                "MenuMeasurementShape",
                "MenuMeasurementArray"
            ];
            MenuItem[] groups = viewer.measureMenuItem.Items.OfType<MenuItem>().ToArray();
            foreach (string groupKey in groupKeys)
            {
                Assert.Contains(groups, item => Equals(item.Header, UiText.Get(groupKey)));
            }

            MenuItem[] advancedItems = Flatten(viewer.measureMenuItem).Where(item => item.Tag is ImageViewerAdvancedMeasurementMenuTag).ToArray();
            Assert.Equal(AdvancedMeasurementMenuCatalog.All.Count, advancedItems.Length);
            Assert.Equal(AdvancedMeasurementMenuCatalog.All.Count, advancedItems.Select(item => ((ImageViewerAdvancedMeasurementMenuTag)item.Tag).Kind).Distinct().Count());
            Assert.All(advancedItems.Where(item => !item.IsEnabled), item => Assert.False(string.IsNullOrWhiteSpace(item.ToolTip?.ToString())));
            Assert.Contains(groups.SelectMany(group => group.Items.OfType<MenuItem>()), item => ReferenceEquals(item, viewer.gradientDetectMenuItem));
        });
    }

    private static MenuItem FindRootMenu(ImageViewer.Controls.ImageViewer viewer, string header)
        => viewer.mainContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));

    private static IEnumerable<MenuItem> Flatten(ItemsControl items)
    {
        foreach (object item in items.Items)
        {
            if (item is not MenuItem menuItem)
            {
                continue;
            }

            yield return menuItem;
            foreach (MenuItem nested in Flatten(menuItem))
            {
                yield return nested;
            }
        }
    }
}
