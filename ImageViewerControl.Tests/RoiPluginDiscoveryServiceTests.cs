using System;
using System.Reflection;
using ImageViewer.Plugins;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class RoiPluginDiscoveryServiceTests
    {
        [Fact]
        public void RegisterFromAssemblies_WithoutRegistry_Throws()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
                RoiPluginDiscoveryService.RegisterFromAssemblies([Assembly.GetExecutingAssembly()], registry: null));

            Assert.Equal("registry", ex.ParamName);
        }

        [Fact]
        public void RegisterFromAssemblies_ContinuesAfterModuleFailure()
        {
            FailingRoiPluginModule.Reset();
            HealthyRoiPluginModule.Reset();

            RoiPluginDiscoveryResult result = RoiPluginDiscoveryService.RegisterFromAssemblies(
                [Assembly.GetExecutingAssembly()],
                new RoiPluginRegistry());

            Assert.True(FailingRoiPluginModule.WasCalled);
            Assert.True(HealthyRoiPluginModule.WasCalled);
            Assert.True(result.HasFailures);
            Assert.Contains(result.Failures, failure => failure.ModuleTypeName.Contains(nameof(FailingRoiPluginModule), StringComparison.Ordinal));
        }

        [Fact]
        public void RegisterFromAssemblies_StrictMode_ThrowsAfterCollectingFailures()
        {
            RoiPluginDiscoveryOptions options = new() { FailOnModuleRegistrationError = true };

            Assert.Throws<AggregateException>(() =>
                RoiPluginDiscoveryService.RegisterFromAssemblies(
                    [Assembly.GetExecutingAssembly()],
                    new RoiPluginRegistry(),
                    options));
        }

        public sealed class FailingRoiPluginModule : IRoiPluginModule
        {
            public static bool WasCalled { get; private set; }

            public static void Reset() => WasCalled = false;

            public void Register(RoiPluginRegistry registry)
            {
                WasCalled = true;
                throw new InvalidOperationException("Expected test failure.");
            }
        }

        public sealed class HealthyRoiPluginModule : IRoiPluginModule
        {
            public static bool WasCalled { get; private set; }

            public static void Reset() => WasCalled = false;

            public void Register(RoiPluginRegistry registry) => WasCalled = true;
        }
    }
}