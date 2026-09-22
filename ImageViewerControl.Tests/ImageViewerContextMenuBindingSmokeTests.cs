using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Controls;
using ImageViewer.Localization;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Wpf")]
    public class ImageViewerContextMenuBindingSmokeTests
    {
        [Fact]
        public void ViewCommandClick_UpdatesBoundMenuCheckState()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var menuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "showInfoPanelMenuItem");

                WpfTestRunner.InvokePrivate(viewer, "OnViewCommandMenuClick", new MenuItem { Tag = ImageViewerViewMenuTags.ToggleInfoPanel }, new RoutedEventArgs());
                WpfTestRunner.DrainDispatcher();

                Assert.True(viewer.ShowInfoPanel);
                Assert.True(menuItem.IsChecked);
            });
        }

        [Fact]
        public void AnalysisCommandClick_UpdatesPseudoColorBinding()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var pseudoColorMenu = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "pseudoColorMenuItem");

                WpfTestRunner.InvokePrivate(viewer, "OnAnalysisCommandMenuClick", new MenuItem { Tag = ImageViewerAnalysisMenuTags.SetPseudoColorPaletteHot }, new RoutedEventArgs());
                WpfTestRunner.DrainDispatcher();

                MenuItem hotItem = pseudoColorMenu.Items.OfType<MenuItem>().Single(item => ReferenceEquals(item.Tag, ImageViewerAnalysisMenuTags.SetPseudoColorPaletteHot));
                MenuItem noneItem = pseudoColorMenu.Items.OfType<MenuItem>().Single(item => ReferenceEquals(item.Tag, ImageViewerAnalysisMenuTags.SetPseudoColorPaletteNone));

                Assert.Equal(ImageViewer.Services.PseudoColorPalette.Hot, viewer.PseudoColorPalette);
                Assert.True(hotItem.IsChecked);
                Assert.False(noneItem.IsChecked);
            });
        }

        [Fact]
        public void XamlMenuItems_UseStronglyTypedCommandTags()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var viewMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "showInfoPanelMenuItem");
                var analysisMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "showRenderStatusMenuItem");
                var roiMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "undoMenuItem");
                var fileMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "saveSessionMenuItem");
                var featureMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "exportSnapshotMenuItem");

                Assert.IsType<ImageViewerViewMenuCommandTag>(viewMenuItem.Tag);
                Assert.IsType<ImageViewerAnalysisMenuCommandTag>(analysisMenuItem.Tag);
                Assert.IsType<ImageViewerRoiMenuCommandTag>(roiMenuItem.Tag);
                Assert.IsType<ImageViewerFileMenuCommandTag>(fileMenuItem.Tag);
                Assert.IsType<ImageViewerFeatureMenuCommandTag>(featureMenuItem.Tag);
                Assert.DoesNotContain(new[] { viewMenuItem.Tag, analysisMenuItem.Tag, roiMenuItem.Tag, fileMenuItem.Tag, featureMenuItem.Tag }, tag => tag is string);
            });
        }

        [Fact]
        public void DynamicMenuItems_UseStronglyTypedTags()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var drawMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "drawRoiMenuItem");

                WpfTestRunner.InvokePrivate(viewer, "RefreshRoiDrawingMenuItems");
                WpfTestRunner.InvokePrivate(viewer, "UpdateContextMenuState");
                WpfTestRunner.DrainDispatcher();

                MenuItem firstDrawingTool = drawMenuItem.Items.OfType<MenuItem>().First();
                var menuState = Assert.IsType<ImageViewerMenuStateSnapshot>(viewer.MenuState);

                Assert.IsType<ImageViewerRoiToolMenuTag>(firstDrawingTool.Tag);
                Assert.All(menuState.File.RecentProjects, item => Assert.IsType<ImageViewerRecentProjectMenuTag>(item.Tag));
            });
        }

        [Fact]
        public void ContextMenuStateBinding_EnablesImageCommandsWhenImageLoaded()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var fitToViewMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "fitToViewMenuItem");
                var exportSnapshotMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "exportSnapshotMenuItem");

                WpfTestRunner.InvokePrivate(viewer, "UpdateContextMenuState");
                WpfTestRunner.DrainDispatcher();

                Assert.False(fitToViewMenuItem.IsEnabled);
                Assert.False(exportSnapshotMenuItem.IsEnabled);

                viewer.SetImage(CreateBitmap());
                WpfTestRunner.InvokePrivate(viewer, "UpdateContextMenuState");
                WpfTestRunner.DrainDispatcher();

                Assert.True(fitToViewMenuItem.IsEnabled);
                Assert.True(exportSnapshotMenuItem.IsEnabled);
            });
        }

        [Fact]
        public void ViewMenuItems_ShowImplementedKeyboardShortcuts()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var resetViewMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "resetViewMenuItem");
                var openImageMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "openImageMenuItem");

                Assert.Equal("Ctrl+O", openImageMenuItem.InputGestureText);
                Assert.Equal("Ctrl+0", resetViewMenuItem.InputGestureText);
                Assert.Equal("Ctrl+F", FindMenuItem(viewer, ImageViewerViewMenuTags.FitToView).InputGestureText);
                Assert.Equal("Ctrl+Shift+R", FindMenuItem(viewer, ImageViewerViewMenuTags.RotateLeft).InputGestureText);
                Assert.Equal("Ctrl+R", FindMenuItem(viewer, ImageViewerViewMenuTags.RotateRight).InputGestureText);
                Assert.Equal("Ctrl+H", FindMenuItem(viewer, ImageViewerViewMenuTags.FlipHorizontal).InputGestureText);
                Assert.Equal("Ctrl+Shift+H", FindMenuItem(viewer, ImageViewerViewMenuTags.FlipVertical).InputGestureText);
            });
        }

        [Fact]
        public void MenuSearch_NoMatchingCommand_ShowsFeedbackAndEscClearsSearch()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var searchBox = WpfTestRunner.GetPrivateField<TextBox>(viewer, "menuSearchBox");
                var noResultsText = WpfTestRunner.GetPrivateField<TextBlock>(viewer, "menuSearchNoResultsText");

                searchBox.Text = "no-such-command";
                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Visible, noResultsText.Visibility);

                WpfTestRunner.InvokePrivate(viewer, "ClearMenuSearchOrClose");

                Assert.Equal(string.Empty, searchBox.Text);
                Assert.Equal(Visibility.Collapsed, noResultsText.Visibility);
            });
        }

        [Fact]
        public void StatusFeedback_ExposesAccessibleNamesAndLiveAnnouncements()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var dismissButton = WpfTestRunner.GetPrivateField<Button>(viewer, "diagnosticErrorDismissButton");
                var diagnosticText = WpfTestRunner.GetPrivateField<TextBlock>(viewer, "diagnosticErrorTextBlock");
                var loadStatus = WpfTestRunner.GetPrivateField<TextBlock>(viewer, "imageLoadStatusTextBlock");

                Assert.Equal(UiText.Get("MenuDismissError"), AutomationProperties.GetName(dismissButton));
                Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(diagnosticText));
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(loadStatus));
            });
        }

        [Fact]
        public void DestructiveRoiCommands_AdvertiseUndoRecovery()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var deleteMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "deleteRoiMenuItem");
                var clearAllMenuItem = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "clearAllMenuItem");

                Assert.Equal(UiText.Get("RoiDestructiveActionUndoHint"), deleteMenuItem.ToolTip);
                Assert.Equal(UiText.Get("RoiDestructiveActionUndoHint"), clearAllMenuItem.ToolTip);
            });
        }

        private static MenuItem FindMenuItem(ImageViewer.Controls.ImageViewer viewer, ImageViewerViewMenuCommandTag tag)
        {
            var contextMenu = WpfTestRunner.GetPrivateField<ContextMenu>(viewer, "mainContextMenu");
            return FindMenuItem(contextMenu.Items.OfType<MenuItem>(), tag)
                ?? throw new Xunit.Sdk.XunitException("Menu item was not found.");
        }

        private static MenuItem? FindMenuItem(IEnumerable<MenuItem> menuItems, ImageViewerViewMenuCommandTag tag)
        {
            foreach (MenuItem menuItem in menuItems)
            {
                if (ReferenceEquals(menuItem.Tag, tag))
                {
                    return menuItem;
                }

                MenuItem? child = FindMenuItem(menuItem.Items.OfType<MenuItem>(), tag);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static BitmapSource CreateBitmap()
        {
            return BitmapSource.Create(
                pixelWidth: 1,
                pixelHeight: 1,
                dpiX: 96,
                dpiY: 96,
                pixelFormat: PixelFormats.Gray8,
                palette: null,
                pixels: new byte[] { 0 },
                stride: 1);
        }
    }
}