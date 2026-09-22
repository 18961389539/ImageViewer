using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageViewer.Controls;
using ImageViewer.Dialogs;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewerDemo;
using ImageViewerDemo.Localization;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Integration")]
    [Trait("Category", "Wpf")]
    public class ImageViewerUiIntegrationTests
    {
        [Fact]
        public void ContextMenu_GroupsAdvancedCommandsIntoNestedMenus()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var contextMenu = viewer.mainContextMenu;

                MenuItem analysisMenu = contextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, UiText.Get("MenuAnalysisAndExport")));
                MenuItem performanceMenu = contextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, UiText.Get("MenuLargeImageAndPerformance")));
                MenuItem renderMenu = contextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, UiText.Get("MenuRenderAndPseudoColor")));

                Assert.Contains(analysisMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, UiText.Get("MenuAnalysisRun")));
                Assert.Contains(performanceMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, UiText.Get("MenuAdvancedPerformance")));
                Assert.Contains(renderMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, UiText.Get("MenuPseudoColorStrategy")));
            });
        }

        [Fact]
        public void ContextMenu_GroupsRoiAndMeasurementCommandsIntoOneSection()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var contextMenu = viewer.mainContextMenu;

                MenuItem roiMenu = contextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, UiText.Get("MenuRoiOperations")));
                MenuItem measureMenu = roiMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, UiText.Get("MenuMeasure")));

                Assert.Contains(roiMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, UiText.Get("MenuDraw")));
                Assert.Contains(roiMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, UiText.Get("MenuShowRoiList")));
                Assert.Contains(roiMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, UiText.Get("MenuZoomToSelectedRoi")));
                Assert.Contains(measureMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, UiText.Get("MenuGradientAlignCaliper")));
                Assert.Contains(measureMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, UiText.Get("ToolFittedEllipse")));
                Assert.DoesNotContain(roiMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, UiText.Get("MenuGradientAlignCaliper")));
            });
        }

        [Theory]
        [InlineData("line-measure", "LineMeasureCaliperDialogTitle")]
        [InlineData("line", "LineCaliperDialogTitle")]
        [InlineData("circular", "CircularCaliperDialogTitle")]
        public void CaliperDialogs_CollapseAdvancedParameters(string dialogKind, string titleKey)
        {
            WpfTestRunner.Run(() =>
            {
                Window dialog = CreateCaliperDialog(dialogKind);
                try
                {
                    dialog.Show();
                    WpfTestRunner.DrainDispatcher();

                    Expander expander = FindVisualDescendants<Expander>(dialog).Single();
                    Button previewButton = FindVisualDescendants<Button>(dialog).Single(button => Equals(button.Content, UiText.Get("DialogPreviewNow")));

                    Assert.Equal(UiText.Get(titleKey), dialog.Title);
                    Assert.Equal(UiText.Get("DialogAdvancedParameters"), expander.Header);
                    Assert.False(expander.IsExpanded);
                    Assert.True(previewButton.IsVisible);
                }
                finally
                {
                    dialog.Close();
                    WpfTestRunner.DrainDispatcher();
                }
            });
        }

        [Fact]
        public void RoiDisplayName_UsesLocalizedTypeNames()
        {
            var roi = new CircleRoi();

            Assert.Equal(UiText.Get("RoiDisplayCircle"), roi.DisplayName);

            roi.Label = "检查点";

            Assert.Equal($"{UiText.Get("RoiDisplayCircle")}: 检查点", roi.DisplayName);
        }

        [Fact]
        public void DemoMainWindow_UsesLocalizedEntryPointCopy()
        {
            WpfTestRunner.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    window.Show();
                    WpfTestRunner.DrainDispatcher();

                    Assert.Equal(DemoText.Get("WindowTitle"), window.Title);
                    Assert.Contains(FindVisualDescendants<TextBlock>(window), textBlock => textBlock.Text == DemoText.Get("ToolbarHint"));
                    Assert.Contains(FindVisualDescendants<Button>(window), button => Equals(button.Content, DemoText.Get("OpenImageButton")));
                }
                finally
                {
                    window.Close();
                    WpfTestRunner.DrainDispatcher();
                }
            });
        }

        private static Window CreateCaliperDialog(string dialogKind)
        {
            return dialogKind switch
            {
                "line-measure" => new LineMeasureCaliperSettingsDialog(new CaliperMeasureRoi(), previewAction: null),
                "line" => new LineCaliperSettingsDialog(new LineCaliperMeasureRoi(), previewAction: null),
                "circular" => new CircularCaliperSettingsDialog(new CircularCaliperMeasureRoi(), previewAction: null),
                _ => throw new ArgumentOutOfRangeException(nameof(dialogKind), dialogKind, null)
            };
        }

        private static IReadOnlyList<T> FindVisualDescendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            var matches = new List<T>();
            Traverse(root, matches);
            return matches;
        }

        private static void Traverse<T>(DependencyObject root, ICollection<T> matches)
            where T : DependencyObject
        {
            if (root is T typed)
            {
                matches.Add(typed);
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                Traverse(VisualTreeHelper.GetChild(root, index), matches);
            }
        }
    }
}