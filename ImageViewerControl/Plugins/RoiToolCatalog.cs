using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageViewer.Plugins
{
    internal static class RoiToolCatalog
    {
        public static bool ContainsFilteredTools(IRoiPlugin plugin)
        {
            ArgumentNullException.ThrowIfNull(plugin);
            return plugin.DrawingTools.Any(tool => !ShouldExpose(tool));
        }

        public static IReadOnlyList<RoiToolDescriptor> GetVisibleTools(IRoiPlugin plugin)
        {
            ArgumentNullException.ThrowIfNull(plugin);
            return plugin.DrawingTools
                .Where(ShouldExpose)
                .ToArray();
        }

        public static IEnumerable<RoiToolDescriptor> OrderVisibleTools(IEnumerable<IRoiPlugin> plugins)
        {
            ArgumentNullException.ThrowIfNull(plugins);
            return plugins
                .SelectMany(plugin => plugin.DrawingTools)
                .Where(ShouldExpose)
                .OrderBy(static tool => tool.MenuOrder)
                .ThenBy(static tool => tool.Header, StringComparer.CurrentCultureIgnoreCase);
        }

        private static bool ShouldExpose(RoiToolDescriptor tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            return tool.IsVisible;
        }
    }
}