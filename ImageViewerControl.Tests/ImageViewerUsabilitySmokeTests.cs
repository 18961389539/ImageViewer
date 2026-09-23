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
    public class ImageViewerUsabilitySmokeTests
    {
        [Fact]
        public void ShowStatusHint_DisplaysMessageAndKind()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var statusBorder = viewer.statusHintBorder;
                var statusText = viewer.statusHintTextBlock;

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
                var statusBorder = viewer.statusHintBorder;

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
                var searchBox = viewer.menuSearchBox;
                var noResultsText = viewer.menuSearchNoResultsText;

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
                var searchBox = viewer.menuSearchBox;
                var noResultsText = viewer.menuSearchNoResultsText;

                searchBox.Text = "PNG";
                WpfTestRunner.DrainDispatcher();

                var exportSnapshot = viewer.exportSnapshotMenuItem;
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
                var searchBox = viewer.menuSearchBox;
                var exportSnapshot = viewer.exportSnapshotMenuItem;

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
                var searchBox = viewer.menuSearchBox;

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
                var searchBox = viewer.menuSearchBox;
                var matchCountText = viewer.menuSearchMatchCountText;
                var noResultsText = viewer.menuSearchNoResultsText;

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
                var zoomBadge = viewer.zoomBadgeBorder;
                var zoomText = viewer.zoomBadgeTextBlock;

                viewer.ShowZoomBadge("缩放：150%", new Point(40, 30));
                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Visible, zoomBadge.Visibility);
                Assert.Equal("缩放：150%", zoomText.Text);

                viewer.DismissZoomBadge();
                WpfTestRunner.DrainDispatcher();
                Assert.Equal(Visibility.Collapsed, zoomBadge.Visibility);
            });
        }

        [Fact]
        public void ShowToolbar_DefaultsHiddenAndTogglesVisible()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var toolbarPanel = viewer.toolbarPanel;
                var toolbarMenuItem = viewer.showToolbarMenuItem;

                Assert.False(viewer.ShowToolbar);
                Assert.Equal(Visibility.Collapsed, toolbarPanel.Visibility);

                WpfTestRunner.InvokePrivate(viewer, "OnViewCommandMenuClick", new MenuItem { Tag = ImageViewerViewMenuTags.ToggleToolbar }, new RoutedEventArgs());
                WpfTestRunner.DrainDispatcher();

                Assert.True(viewer.ShowToolbar);
                Assert.Equal(Visibility.Visible, toolbarPanel.Visibility);
                Assert.True(toolbarMenuItem.IsChecked);
            });
        }

        [Fact]
        public void StatusBar_WithoutImage_ShowsNoDataLoaded()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var statusBorder = viewer.statusBarBorder;
                var statusText = viewer.statusBarTextBlock;

                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Visible, statusBorder.Visibility);
                Assert.Equal(UiText.Get("StatusNoDataLoaded"), statusText.Text);
            });
        }

        [Fact]
        public void StatusBar_WithImage_ShowsDimensionsAndPath()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var statusBorder = viewer.statusBarBorder;
                var statusText = viewer.statusBarTextBlock;

                viewer.ImageSource = CreateBitmap(9);
                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Visible, statusBorder.Visibility);
                string expected = UiText.Format("StatusBarImageSizeOnly", 2, 2);
                Assert.Equal(expected, statusText.Text);
            });
        }

        [Fact]
        public void StatusBar_WithPathImage_ShowsFilePath()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var statusText = viewer.statusBarTextBlock;

                string path = CreateTempPngPath();
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new System.Uri(path, System.UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.None;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    viewer.ImageSource = bitmap;
                    WpfTestRunner.DrainDispatcher();

                    Assert.Contains(System.IO.Path.GetFullPath(path), statusText.Text);
                    Assert.Contains("2 × 2", statusText.Text);
                }
                finally
                {
                    System.IO.File.Delete(path);
                }
            });
        }

        [Fact]
        public void Automation_SetsAccessibleNamesOnCoreElements()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var imageElement = viewer.image;
                var roiList = viewer.roiListBox;
                TextBlock? zoomLevel = FindZoomLevelTextBlock(viewer);

                Assert.Equal(UiText.Get("ViewerAccessibleName"), AutomationProperties.GetName(viewer));
                Assert.Equal(UiText.Get("ImageDisplayAreaName"), AutomationProperties.GetName(imageElement));
                Assert.Equal(UiText.Get("RoiListAccessibleName"), AutomationProperties.GetName(roiList));
                Assert.NotNull(zoomLevel);
                Assert.Equal(UiText.Get("ZoomLevelDisplayAccessibleName"), AutomationProperties.GetName(zoomLevel));
            });
        }

        [Fact]
        public void ShowStatusHint_Success_ShowsSuccessIcon()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var icon = viewer.statusHintIconTextBlock;

                viewer.ShowStatusHint("成功", StatusHintKind.Success, durationMs: 500);
                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Visible, icon.Visibility);
                Assert.Equal("✓", icon.Text);

                viewer.DismissStatusHint();
            });
        }

        [Fact]
        public void ShowStatusHint_Error_ShowsErrorIcon()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var icon = viewer.statusHintIconTextBlock;
                var text = viewer.statusHintTextBlock;

                viewer.ShowStatusHint("失败", StatusHintKind.Error, durationMs: 500);
                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Visible, icon.Visibility);
                Assert.Equal("⚠", icon.Text);
                Assert.Equal("失败", text.Text);

                viewer.DismissStatusHint();
            });
        }

        [Fact]
        public void MenuSearch_CountsLeafCommandsNotGroups()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                viewer.menuSearchBox.Text = "Ctrl+Z";
                WpfTestRunner.DrainDispatcher();

                Assert.Equal(Visibility.Visible, viewer.undoMenuItem.Visibility);
                // 撤销、删除、清空三项的 ToolTip/InputGestureText 均提及 Ctrl+Z，应按叶子命令计数为 3
                Assert.StartsWith("找到 3", viewer.menuSearchMatchCountText.Text);
            });
        }

        private static ImageSource CreateBitmap(byte value)
        {
            return BitmapSource.Create(
                2,
                2,
                96,
                96,
                PixelFormats.Gray8,
                null,
                new[] { value, value, value, value },
                2);
        }

        private static string CreateTempPngPath()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"iv-statusbar-{System.Guid.NewGuid():N}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create((BitmapSource)CreateBitmap(9)));
            using var stream = System.IO.File.Create(path);
            encoder.Save(stream);
            return path;
        }

        private static TextBlock? FindZoomLevelTextBlock(ImageViewer.Controls.ImageViewer viewer)
        {
            var toolbarPanel = viewer.toolbarPanel;
            return FindVisualDescendants<TextBlock>(toolbarPanel).FirstOrDefault(text => ReferenceEquals(AutomationProperties.GetName(text), UiText.Get("ZoomLevelDisplayAccessibleName")));
        }

        private static System.Collections.Generic.IReadOnlyList<T> FindVisualDescendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            var matches = new System.Collections.Generic.List<T>();
            Traverse(root, matches);
            return matches;
        }

        private static void Traverse<T>(DependencyObject root, System.Collections.Generic.ICollection<T> matches)
            where T : DependencyObject
        {
            if (root is T typed)
            {
                matches.Add(typed);
            }

            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                Traverse(System.Windows.Media.VisualTreeHelper.GetChild(root, index), matches);
            }
        }

        private static MenuItem? FindMenuItem(ImageViewer.Controls.ImageViewer viewer, ImageViewerViewMenuCommandTag tag)
        {
            var contextMenu = viewer.mainContextMenu;
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