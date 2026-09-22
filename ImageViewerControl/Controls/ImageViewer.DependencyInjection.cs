using System;
using ImageViewer.Abstractions;
using ImageViewer.Dialogs;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImageViewer.Controls
{
    public static class ImageViewerServiceCollectionExtensions
    {
        public static IServiceCollection AddImageViewerRuntimeServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddPluginAndSessionServices(services);
            AddRenderServices(services);
            AddRuntimeHostServices(services);

            return services;
        }

        private static void AddPluginAndSessionServices(IServiceCollection services)
        {
            services.TryAdd(ServiceDescriptor.Singleton<RoiPluginRegistry>(static _ => ImageViewerPluginRegistryBootstrap.CreateDefault()));
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerSessionStoragePolicy>(static _ => new LocalAppDataImageViewerSessionStoragePolicy()));
            services.TryAddSingleton<IImageViewerDialogService, ImageViewerDialogService>();
            services.TryAddSingleton<IImageViewerFileDialogService, ImageViewerFileDialogService>();
            services.TryAddSingleton<IImageViewerLogger, TraceImageViewerLogger>();
            services.TryAddSingleton<IImageViewerViewportService, ImageViewerViewportService>();
            services.TryAddSingleton<IImageViewerSessionService, ImageViewerSessionService>();
            services.TryAddSingleton<IImageViewerRecentProjectService, ImageViewerRecentProjectService>();
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerProjectPackageService>(static serviceProvider =>
                new ImageViewerProjectPackageService(
                    serviceProvider.GetRequiredService<IImageViewerSessionService>(),
                    serviceProvider.GetRequiredService<IImageViewerSessionStoragePolicy>())));
        }

        private static void AddRenderServices(IServiceCollection services)
        {
            services.TryAddTransient<ImageViewerRenderService>();
            services.TryAdd(ServiceDescriptor.Transient<IImageViewerRenderService>(static serviceProvider =>
                serviceProvider.GetRequiredService<ImageViewerRenderService>()));
            services.TryAdd(ServiceDescriptor.Transient<IImageViewerDisplayRenderService>(static serviceProvider =>
                serviceProvider.GetRequiredService<ImageViewerRenderService>()));
            services.TryAdd(ServiceDescriptor.Transient<IImageViewerFrameRenderService>(static serviceProvider =>
                serviceProvider.GetRequiredService<ImageViewerRenderService>()));
            services.TryAdd(ServiceDescriptor.Transient<IImageViewerAnalysisRenderService>(static serviceProvider =>
                serviceProvider.GetRequiredService<ImageViewerRenderService>()));
        }

        private static void AddRuntimeHostServices(IServiceCollection services)
        {
            services.TryAddSingleton<ISelectedRoiDetectionService>(static _ => SelectedRoiDetectionService.Default);
            services.TryAdd(ServiceDescriptor.Transient<ImageViewerRuntimeServices>(static serviceProvider =>
                ImageViewerHostDefaults.CreateRuntimeServices(serviceProvider)));
        }

        public static IServiceCollection AddImageViewerHostServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddHostSchedulingServices(services);
            AddHostAnalysisServices(services);

            return services;
        }

        private static void AddHostSchedulingServices(IServiceCollection services)
        {
            services.TryAddSingleton<IImageViewerDispatcherTimerFactory, WpfImageViewerDispatcherTimerFactory>();
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerRefreshSchedulerFactory>(static serviceProvider =>
                new DispatcherImageViewerRefreshSchedulerFactory(serviceProvider.GetRequiredService<IImageViewerDispatcherTimerFactory>())));
            services.TryAddSingleton<IImageViewerLatestTaskSchedulerFactory, LatestImageViewerTaskSchedulerFactory>();
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerPeriodicTaskSchedulerFactory>(static serviceProvider =>
                new DispatcherImageViewerPeriodicTaskSchedulerFactory(
                    serviceProvider.GetRequiredService<IImageViewerDispatcherTimerFactory>(),
                    exception => serviceProvider.GetRequiredService<IImageViewerLogger>().LogError("Periodic image viewer task failed.", exception))));
        }

        private static void AddHostAnalysisServices(IServiceCollection services)
        {
            services.TryAddSingleton<IImageViewerTelemetry, DiagnosticsImageViewerTelemetry>();
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerAnalysisDiagnostics>(static serviceProvider =>
                new LoggerImageViewerAnalysisDiagnostics(serviceProvider.GetRequiredService<IImageViewerTelemetry>())));
            services.TryAdd(ServiceDescriptor.Singleton<ImageViewerHostServices>(static serviceProvider =>
                ImageViewerHostDefaults.CreateHostServices(serviceProvider)));
        }

        public static IServiceCollection AddImageViewerMicrosoftLogging(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.Replace(ServiceDescriptor.Singleton<IImageViewerLogger, MicrosoftExtensionsImageViewerLogger>());
            return services;
        }

        public static IServiceCollection AddImageViewerHost(this IServiceCollection services, Action<ImageViewerHostBuilder>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddImageViewerRuntimeServices();
            services.AddImageViewerHostServices();
            services.TryAdd(ServiceDescriptor.Transient<ImageViewerHost>(serviceProvider =>
            {
                var builder = new ImageViewerHostBuilder().UseServiceProvider(serviceProvider);
                configure?.Invoke(builder);
                return builder.Build();
            }));
            services.TryAddTransient<IImageViewerFactory>(static serviceProvider =>
                new ImageViewerFactory(serviceProvider.GetRequiredService<ImageViewerHost>()));
            services.TryAdd(ServiceDescriptor.Transient<ImageViewer>(static serviceProvider =>
                serviceProvider.GetRequiredService<ImageViewerHost>().CreateViewer()));

            return services;
        }
    }
}