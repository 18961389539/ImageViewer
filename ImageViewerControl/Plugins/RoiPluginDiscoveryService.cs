using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using ImageViewer.Abstractions;

namespace ImageViewer.Plugins
{
    public static class RoiPluginDiscoveryService
    {
        public static RoiPluginDiscoveryResult DiscoverAndRegister(RoiPluginRegistry? registry = null, string? directoryPath = null, IImageViewerLogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(registry);
            var options = new RoiPluginDiscoveryOptions { PluginDirectoryPath = directoryPath };
            return DiscoverAndRegister(options, registry, logger);
        }

        public static RoiPluginDiscoveryResult DiscoverAndRegister(RoiPluginDiscoveryOptions? options, RoiPluginRegistry? registry = null, IImageViewerLogger? logger = null)
        {
            var targetRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            var effectiveOptions = options ?? new RoiPluginDiscoveryOptions();
            string scanDirectory = effectiveOptions.ResolvePluginDirectory(AppContext.BaseDirectory);
            return RegisterFromAssemblies(LoadAssemblies(scanDirectory, effectiveOptions, logger), targetRegistry, effectiveOptions, logger);
        }

        public static RoiPluginDiscoveryResult RegisterFromAssemblies(IEnumerable<Assembly> assemblies, RoiPluginRegistry? registry = null, RoiPluginDiscoveryOptions? options = null, IImageViewerLogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(assemblies);

            var targetRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            var effectiveOptions = options ?? new RoiPluginDiscoveryOptions();
            var disabledModuleTypeNames = new HashSet<string>(effectiveOptions.DisabledModuleTypeNames, StringComparer.OrdinalIgnoreCase);
            var registeredModuleTypeNames = new List<string>();
            var failures = new List<RoiPluginDiscoveryFailure>();

            var moduleTypes = assemblies
                .Distinct()
                .SelectMany(GetLoadableTypes)
                .Where(type =>
                    type is { IsAbstract: false, IsInterface: false } &&
                    typeof(IRoiPluginModule).IsAssignableFrom(type) &&
                    type.GetConstructor(Type.EmptyTypes) != null &&
                    !disabledModuleTypeNames.Contains(type.FullName ?? type.Name))
                .OrderBy(type => type.FullName, StringComparer.Ordinal);

            foreach (var moduleType in moduleTypes)
            {
                RoiPluginDiscoveryFailure? failure = RegisterModule(
                    moduleType,
                    targetRegistry,
                    effectiveOptions,
                    logger);
                if (failure == null)
                {
                    registeredModuleTypeNames.Add(moduleType.FullName ?? moduleType.Name);
                }
                else
                {
                    failures.Add(failure);
                }
            }

            ApplyPluginFilters(targetRegistry, effectiveOptions, null);
            var result = new RoiPluginDiscoveryResult(registeredModuleTypeNames, failures);
            if (effectiveOptions.FailOnModuleRegistrationError && result.HasFailures)
            {
                throw new AggregateException(
                    "One or more ROI plugin modules failed to register.",
                    result.Failures.Select(failure => failure.Exception));
            }

            return result;
        }

        private static RoiPluginDiscoveryFailure? RegisterModule(
            Type moduleType,
            RoiPluginRegistry registry,
            RoiPluginDiscoveryOptions options,
            IImageViewerLogger? logger)
        {
            var beforeKeys = new HashSet<string>(registry.RegisteredTypeKeys, StringComparer.OrdinalIgnoreCase);
            try
            {
                var module = (IRoiPluginModule)Activator.CreateInstance(moduleType)!;
                logger?.LogInfo(CreateLogMessage(moduleType, "Register", "Started"));
                module.Register(registry);
                ApplyPluginFilters(registry, options, beforeKeys);
                logger?.LogInfo(CreateLogMessage(moduleType, "Register", "Succeeded"));
                return null;
            }
            catch (Exception ex)
            {
                RemoveNewRegistrations(registry, beforeKeys);
                logger?.LogError(CreateLogMessage(moduleType, "Register", "Failed"), ex);
                return new RoiPluginDiscoveryFailure(moduleType.FullName ?? moduleType.Name, ex);
            }
        }

        private static void RemoveNewRegistrations(RoiPluginRegistry registry, HashSet<string> registeredBeforeModule)
        {
            foreach (string typeKey in registry.RegisteredTypeKeys
                .Where(key => !registeredBeforeModule.Contains(key))
                .ToArray())
            {
                registry.Unregister(typeKey);
            }
        }

        private static string CreateLogMessage(Type moduleType, string stage, string result)
        {
            string moduleName = moduleType.FullName ?? moduleType.Name;
            return $"PluginDiscovery module={moduleName} stage={stage} result={result}";
        }

        private static void ApplyPluginFilters(RoiPluginRegistry registry, RoiPluginDiscoveryOptions options, HashSet<string>? registeredBeforeModule)
        {
            var disabledKeys = new HashSet<string>(options.DisabledPluginTypeKeys, StringComparer.OrdinalIgnoreCase);
            disabledKeys.UnionWith(options.UnloadedPluginTypeKeys);

            if (disabledKeys.Count == 0)
            {
                return;
            }

            string[] keysToInspect = registeredBeforeModule == null
                ? registry.RegisteredTypeKeys.ToArray()
                : registry.RegisteredTypeKeys.Where(key => !registeredBeforeModule.Contains(key)).ToArray();

            foreach (var typeKey in keysToInspect)
            {
                if (disabledKeys.Contains(typeKey))
                {
                    registry.Unregister(typeKey);
                }
            }
        }

        private static IEnumerable<Assembly> LoadAssemblies(string directoryPath, RoiPluginDiscoveryOptions options, IImageViewerLogger? logger)
        {
            var disabledAssemblyNames = new HashSet<string>(options.DisabledAssemblyNames, StringComparer.OrdinalIgnoreCase);
            var allowedAssemblyNamePrefixes = options.AllowedAssemblyNamePrefixes
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .ToArray();
            var loadedAssemblies = options.ScanLoadedAssemblies
                ? AppDomain.CurrentDomain.GetAssemblies()
                    .Where(assembly => !assembly.IsDynamic)
                    .ToDictionary(assembly => assembly.FullName ?? assembly.GetName().Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in loadedAssemblies.Values)
            {
                yield return assembly;
            }

            if (!Directory.Exists(directoryPath))
            {
                yield break;
            }

            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly))
            {
                AssemblyName assemblyName;
                try
                {
                    assemblyName = AssemblyName.GetAssemblyName(filePath);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning($"Skipping non-.NET assembly '{filePath}': {ex.Message}");
                    continue;
                }

                string simpleName = assemblyName.Name ?? Path.GetFileNameWithoutExtension(filePath);
                if (disabledAssemblyNames.Contains(simpleName) ||
                    (allowedAssemblyNamePrefixes.Length > 0 &&
                     !allowedAssemblyNamePrefixes.Any(prefix => simpleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
                {
                    continue;
                }

                if (loadedAssemblies.Values.Any(assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName)))
                {
                    continue;
                }

                Assembly? assembly = null;
                try
                {
                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(filePath);
                }
                catch (Exception ex)
                {
                    logger?.LogError($"Failed to load plugin assembly '{filePath}'.", ex);
                }

                if (assembly != null)
                {
                    loadedAssemblies[assembly.FullName ?? assembly.GetName().Name ?? filePath] = assembly;
                    yield return assembly;
                }
            }
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.OfType<Type>().ToArray();
            }
        }
    }
}
