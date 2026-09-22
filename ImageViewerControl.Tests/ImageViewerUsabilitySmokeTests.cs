using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImageViewer.Controls;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Wpf")]
    public class ImageViewerUsabilitySmokeTests
    {
        [Fact]
        public void ShowStatusHint_DisplaysMessageAndKind()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var statusBorder = WpfTestRunner.GetPrivateField<Border>(viewer, "statusHintBorder");
                var statusText = WpfTestRunner.GetPrivateField<TextBlock>(viewer, "statusHintTextBlock");

                viewer.ShowStatusHint("测试提示", StatusHintKind.Success, durationMs: 500);
                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Visible, statusBorder.Visibility);
                Assert.Equal("测试提示", statusText.Text);

                viewer.DismissStatusHint();
                WpfTestRunner.DrainDispatcher();
                Assert.Equal(Visibility.Collapsed, statusBorder.Visibility);
            });
        }

        [Fact]
        public void ShowStatusHint_IgnoresEmptyMessage()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var statusBorder = WpfTestRunner.GetPrivateField<Border>(viewer, "statusHintBorder");

                viewer.ShowStatusHint(string.Empty);
                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Collapsed, statusBorder.Visibility);
            });
        }

        [Fact]
        public void MenuSearch_MatchesShortcutGestureText()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var searchBox = WpfTestRunner.GetPrivateField<TextBox>(viewer, "menuSearchBox");
                var noResultsText = WpfTestRunner.GetPrivateField<TextBlock>(viewer, "menuSearchNoResultsText");

                searchBox.Text = "Ctrl+Shift+R";
                WpfTestRunner.DrainDispatcher();

                var rotateLeft = FindMenuItem(viewer, ImageViewerViewMenuTags.RotateLeft);
                Assert.NotNull(rotateLeft);
                Assert.Equal(Visibility.Visible, rotateLeft!.Visibility);
                Assert.Equal(Visibility.Collapsed, noResultsText.Visibility);
            });
        }

        [Fact]
        public void MenuSearch_MatchesExportKeywordsByTooltipOrHeader()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var searchBox = WpfTestRunner.GetPrivateField<TextBox>(viewer, "menuSearchBox");
                var noResultsText = WpfTestRunner.GetPrivateField<TextBlock>(viewer, "menuSearchNoResultsText");

                searchBox.Text = "PNG";
                WpfTestRunner.DrainDispatcher();

                var exportSnapshot = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "exportSnapshotMenuItem");
                Assert.Equal(Visibility.Visible, exportSnapshot.Visibility);
                Assert.Equal(Visibility.Collapsed, noResultsText.Visibility);
            });
        }

        [Fact]
        public void MenuSearch_MultipleKeywordsRequireAllWords()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var searchBox = WpfTestRunner.GetPrivateField<TextBox>(viewer, "menuSearchBox");
                var exportSnapshot = WpfTestRunner.GetPrivateField<MenuItem>(viewer, "exportSnapshotMenuItem");

                searchBox.Text = "导出 PNG";
                WpfTestRunner.DrainDispatcher();
                Assert.Equal(Visibility.Visible, exportSnapshot.Visibility);

                searchBox.Text = "导出 缩放";
                WpfTestRunner.DrainDispatcher();
                Assert.Equal(Visibility.Collapsed, exportSnapshot.Visibility);
            });
        }

        [Fact]
        public void MenuSearch_SeparatorIgnoredShortcut()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var searchBox = WpfTestRunner.GetPrivateField<TextBox>(viewer, "menuSearchBox");

                searchBox.Text = "CtrlShiftR";
                WpfTestRunner.DrainDispatcher();

                var rotateLeft = FindMenuItem(viewer, ImageViewerViewMenuTags.RotateLeft);
                Assert.NotNull(rotateLeft);
                Assert.Equal(Visibility.Visible, rotateLeft!.Visibility);
            });
        }

        [Fact]
        public void MenuSearch_ShowsMatchCountWhenResultsFound()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var searchBox = WpfTestRunner.GetPrivateField<TextBox>(viewer, "menuSearchBox");
                var matchCountText = WpfTestRunner.GetPrivateField<TextBlock>(viewer, "menuSearchMatchCountText");
                var noResultsText = WpfTestRunner.GetPrivateField<TextBlock>(viewer, "menuSearchNoResultsText");

                searchBox.Text = "PNG";
                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Visible, matchCountText.Visibility);
                Assert.StartsWith("找到 ", matchCountText.Text);
                Assert.Equal(Visibility.Collapsed, noResultsText.Visibility);
            });
        }

        [Fact]
        public void ShowZoomBadge_DisplaysNearCursorAndDismisses()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var zoomBadge = WpfTestRunner.GetPrivateField<Border>(viewer, "zoomBadgeBorder");
                var zoomText = WpfTestRunner.GetPrivateField<TextBlock>(viewer, "zoomBadgeTextBlock");

                viewer.ShowZoomBadge("缩放：150%", new Point(40, 30));
                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Visible, zoomBadge.Visibility);
                Assert.Equal("缩放：150%", zoomText.Text);

                viewer.DismissZoomBadge();
                WpfTestRunner.DrainDispatcher();
                Assert.Equal(Visibility.Collapsed, zoomBadge.Visibility);
            });
        }

        private static MenuItem? FindMenuItem(ImageViewer.Controls.ImageViewer viewer, ImageViewerViewMenuCommandTag tag)
        {
            var contextMenu = WpfTestRunner.GetPrivateField<ContextMenu>(viewer, "mainContextMenu");
            return FindMenuItem(contextMenu.Items.OfType<MenuItem>(), tag);
        }

        private static MenuItem? FindMenuItem(System.Collections.Generic.IEnumerable<MenuItem> menuItems, ImageViewerViewMenuCommandTag tag)
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
    }
}