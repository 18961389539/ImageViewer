using System.Windows;
using System.Windows.Controls;
using ImageViewer.Core.Measurements;
using ImageViewer.Localization;
using ImageViewer.Plugins;

namespace ImageViewer.Controls;

public partial class ImageViewer
{
    private static readonly string[] MeasurementGroupKeys =
    [
        "MenuMeasurementBasic",
        "MenuMeasurementEdge",
        "MenuMeasurementGeometry",
        "MenuMeasurementShape",
        "MenuMeasurementArray"
    ];

    /// <summary>Rebuilds the task-oriented measurement menu from the registered measurement tools.</summary>
    /// <param name="tools">All registered drawing and measurement tool descriptors.</param>
    private void PopulateMeasurementMenu(IReadOnlyList<RoiToolDescriptor> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        measureMenuItem.Items.Remove(gradientDetectMenuItem);
        foreach (MenuItem existingGroup in measureMenuItem.Items.OfType<MenuItem>())
        {
            existingGroup.Items.Remove(gradientDetectMenuItem);
        }

        measureMenuItem.Items.Clear();
        Dictionary<string, MenuItem> groups = MeasurementGroupKeys.ToDictionary(
            key => key,
            key => new MenuItem { Header = UiText.Get(key) },
            StringComparer.Ordinal);

        foreach (RoiToolDescriptor tool in tools)
        {
            if (!tool.IsMeasurement)
            {
                continue;
            }

            ImageViewerDynamicMenuItem descriptor = ImageViewerDynamicMenuItem.FromRoiTool(tool);
            MenuItem item = CreateDynamicMenuItem(descriptor);
            item.Click += OnRoiDrawingToolClick;
            groups[ResolveMeasurementGroup(tool)].Items.Add(item);
        }

        groups["MenuMeasurementEdge"].Items.Add(gradientDetectMenuItem);
        groups["MenuMeasurementEdge"].Items.Add(CreateDetectionRecommendationMenuItem());

        foreach (AdvancedMeasurementMenuDescriptor descriptor in AdvancedMeasurementMenuCatalog.All)
        {
            MenuItem item = new()
            {
                Header = UiText.Get(descriptor.HeaderKey),
                Tag = new ImageViewerAdvancedMeasurementMenuTag(descriptor.Kind),
                ToolTip = UiText.Get(descriptor.AliasesKey)
            };
            MeasurementMenuAvailability availability = AdvancedMeasurementMenuCatalog.GetAvailability(
                descriptor.Kind,
                ViewerState.AllRois,
                GetAnalysisBitmapSource() is not null,
                ViewerState.SelectedRoi);
            item.IsEnabled = availability.IsEnabled;
            if (!availability.IsEnabled && availability.ReasonKey is { } reasonKey)
            {
                item.ToolTip = UiText.Get(reasonKey);
            }

            item.Click += OnAdvancedMeasurementOperationMenuClick;
            groups[descriptor.GroupKey].Items.Add(item);
        }

        foreach (MenuItem group in groups.Values)
        {
            if (group.Items.Count > 0)
            {
                measureMenuItem.Items.Add(group);
            }
        }

        measureMenuItem.Visibility = measureMenuItem.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyMenuItemContentAlignment(measureMenuItem);
    }

    private static string ResolveMeasurementGroup(RoiToolDescriptor tool)
    {
        string name = tool.Header;

        if (Matches(name, "ToolAutomaticEdgePoint") || Matches(name, "ToolAutomaticCircle") ||
            Matches(name, "ToolCircularCaliper") || Matches(name, "ToolArcCaliper") || Matches(name, "ToolLineCaliper"))
        {
            return "MenuMeasurementEdge";
        }

        if (Matches(name, "ToolPointToLineDistance") || Matches(name, "ToolPointToCircleDistance") ||
            Matches(name, "ToolParallelism") || Matches(name, "ToolPerpendicularity") || Matches(name, "ToolConcentricity") ||
            Matches(name, "ToolCenterDistance"))
        {
            return "MenuMeasurementGeometry";
        }

        if (Matches(name, "ToolThreePointCircle") || Matches(name, "ToolArcMeasure") || Matches(name, "ToolFittedEllipse") ||
            Matches(name, "ToolAreaMeasure") || Matches(name, "ToolPolylineMeasure"))
        {
            return "MenuMeasurementShape";
        }

        if (name.Contains("pitch", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("array", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("periodic", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("distribution", StringComparison.OrdinalIgnoreCase))
        {
            return "MenuMeasurementArray";
        }

        return "MenuMeasurementBasic";
    }

    private static bool Matches(string value, string resourceKey)
    {
        return string.Equals(value, UiText.Get(resourceKey), StringComparison.OrdinalIgnoreCase);
    }

    private void OnAdvancedMeasurementOperationMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: ImageViewerAdvancedMeasurementMenuTag tag })
        {
            ShowAdvancedMeasurements(tag.Kind, ViewerState.SelectedRoi);
        }
    }
}
