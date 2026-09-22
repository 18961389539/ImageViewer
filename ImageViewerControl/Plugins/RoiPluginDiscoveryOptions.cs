using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageViewer.Services;

namespace ImageViewer.Plugins
{
    public sealed class RoiPluginDiscoveryOptions
    {
        public string? PluginDirectoryPath { get; set; }

        public List<string> DisabledAssemblyNames { get; set; } = new();

        public List<string> AllowedAssemblyNamePrefixes { get; set; } = new();

        public bool ScanLoadedAssemblies { get; set; }

        public bool FailOnModuleRegistrationError { get; set; }

        public List<string> DisabledModuleTypeNames { get; set; } = new();

        public List<string> DisabledPluginTypeKeys { get; set; } = new();

        public List<string> UnloadedPluginTypeKeys { get; set; } = new();

        public static RoiPluginDiscoveryOptions Load(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (!File.Exists(filePath))
            {
                return new RoiPluginDiscoveryOptions();
            }

            string json = File.ReadAllText(filePath);
            return System.Text.Json.JsonSerializer.Deserialize(json, ImageViewerJsonSerializationContext.Default.RoiPluginDiscoveryOptions) ?? new RoiPluginDiscoveryOptions();
        }

        public static async Task<RoiPluginDiscoveryOptions> LoadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (!File.Exists(filePath))
            {
                return new RoiPluginDiscoveryOptions();
            }

            string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return System.Text.Json.JsonSerializer.Deserialize(json, ImageViewerJsonSerializationContext.Default.RoiPluginDiscoveryOptions) ?? new RoiPluginDiscoveryOptions();
        }

        public string ResolvePluginDirectory(string baseDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

            if (string.IsNullOrWhiteSpace(PluginDirectoryPath))
            {
                return baseDirectory;
            }

            return Path.IsPathRooted(PluginDirectoryPath)
                ? PluginDirectoryPath
                : Path.GetFullPath(Path.Combine(baseDirectory, PluginDirectoryPath));
        }
    }
}
