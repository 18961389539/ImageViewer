using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageViewer.Abstractions;
using ImageViewer.Controls;
using ImageViewer.Dialogs;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using ImageViewer.ViewModels;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Smoke")]
    public class ImageViewerWpfLifecycleSmokeTests
    {
        [Fact]
        public void Instantiation_AttachesExternalImageBindingController()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                BitmapSource bitmap = CreateBitmap(pixelWidth: 5, pixelHeight: 4);

                viewer.DataContext = new TestImageSourceProvider(bitmap);
                WpfTestRunner.DrainDispatcher();

                Assert.Same(bitmap, viewer.ImageSource);
            });
        }

        [Fact]
        public void Dispose_DetachesExternalImageBindingController()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();

                viewer.Dispose();
                viewer.DataContext = new TestImageSourceProvider(CreateBitmap(pixelWidth: 6, pixelHeight: 3));
                WpfTestRunner.DrainDispatcher();

                Assert.Null(viewer.ImageSource);
            });
        }

        [Fact]
        public void Loaded_WhenImageWasAssignedBeforeLoad_AppliesPendingImageInitialization()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                Canvas imageContainer = viewer.imageContainer;
                BitmapSource bitmap = CreateBitmap(pixelWidth: 12, pixelHeight: 8);
                var window = new Window
                {
                    Width = 320,
                    Height = 240,
                    Content = viewer
                };

                viewer.SetImage(bitmap);
                Assert.True(double.IsNaN(imageContainer.Width));

                try
                {
                    window.Show();
                    WpfTestRunner.DrainDispatcher();

                    Assert.True(viewer.IsLoaded);
                    Assert.Equal(12, imageContainer.Width);
                    Assert.Equal(8, imageContainer.Height);
                }
                finally
                {
                    window.Close();
                    WpfTestRunner.DrainDispatcher();
                }
            });
        }

        [Fact]
        public void SetImageWhenViewerAlreadyLoadedPreservesCurrentViewportState()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var window = new Window
                {
                    Width = 320,
                    Height = 240,
                    Content = viewer
                };

                try
                {
                    window.Show();
                    WpfTestRunner.DrainDispatcher();

                    ImageViewerControlComposition composition = viewer._controlComposition;
                    viewer.SetImage(CreateBitmap(pixelWidth: 12, pixelHeight: 8));
                    WpfTestRunner.DrainDispatcher();

                    ImageViewerViewportState expectedState = new(2.5, 30, 40);
                    composition.ViewportController.ApplyViewportState(expectedState);
                    WpfTestRunner.DrainDispatcher();

                    viewer.SetImage(CreateBitmap(pixelWidth: 20, pixelHeight: 10));
                    WpfTestRunner.DrainDispatcher();

                    Assert.Equal(expectedState, composition.ViewportController.CurrentState);
                }
                finally
                {
                    window.Close();
                    WpfTestRunner.DrainDispatcher();
                }
            });
        }

        [Fact]
        public void Host_CreateViewer_UsesSuppliedHostDependencies()
        {
            WpfTestRunner.Run(() =>
            {
                RoiPluginRegistry pluginRegistry = RoiPluginRegistry.CreateBuiltIn();
                ImageViewerRuntimeServices runtimeServices = ImageViewerTestServices.CreateRuntimeServices();
                ImageViewerHostServices hostServices = ImageViewerTestServices.CreateHostServices(
                    new RecordingDispatcherTimerFactory(),
                    new RecordingRefreshSchedulerFactory(),
                    new RecordingLatestTaskSchedulerFactory(),
                    new RecordingPeriodicTaskSchedulerFactory(),
                    new RecordingAnalysisDiagnostics());
                ImageViewerHost host = ImageViewerTestServices.CreateHost(pluginRegistry, runtimeServices, hostServices);

                using var viewer = host.CreateViewer();

                Assert.Same(pluginRegistry, viewer.PluginRegistry);
                Assert.Same(runtimeServices, viewer.RuntimeServices);
                Assert.Same(hostServices, viewer.HostServices);
            });
        }

        [Fact]
        public void Host_CreateViewer_UsesConfiguredRefreshSchedulerFactoryForRefreshPaths()
        {
            WpfTestRunner.Run(() =>
            {
                var refreshSchedulerFactory = new RecordingRefreshSchedulerFactory();
                RoiPluginRegistry pluginRegistry = RoiPluginRegistry.CreateBuiltIn();
                ImageViewerHost host = ImageViewerTestServices.CreateHost(
                    pluginRegistry,
                    ImageViewerTestServices.CreateRuntimeServices(),
                    ImageViewerTestServices.CreateHostServices(
                        new RecordingDispatcherTimerFactory(),
                        refreshSchedulerFactory,
                        new RecordingLatestTaskSchedulerFactory(),
                        new RecordingPeriodicTaskSchedulerFactory(),
                        new RecordingAnalysisDiagnostics()));

                using var viewer = host.CreateViewer();

                Assert.Collection(
                    refreshSchedulerFactory.CreateRequests,
                    request =>
                    {
                        Assert.Equal(DispatcherPriority.Render, request.Priority);
                        Assert.Equal(TimeSpan.FromMilliseconds(16), request.Interval);
                        Assert.Equal("Deferred", request.Kind);
                    },
                    request =>
                    {
                        Assert.Equal(DispatcherPriority.Background, request.Priority);
                        Assert.Equal(TimeSpan.FromMilliseconds(48), request.Interval);
                        Assert.Equal("Forced", request.Kind);
                    });
            });
        }

        [Fact]
        public void RefreshEntryPoints_DelegateToConfiguredRefreshSchedulers()
        {
            WpfTestRunner.Run(() =>
            {
                var refreshSchedulerFactory = new RecordingRefreshSchedulerFactory();
                RoiPluginRegistry pluginRegistry = RoiPluginRegistry.CreateBuiltIn();
                ImageViewerHost host = ImageViewerTestServices.CreateHost(
                    pluginRegistry,
                    ImageViewerTestServices.CreateRuntimeServices(),
                    ImageViewerTestServices.CreateHostServices(
                        new RecordingDispatcherTimerFactory(),
                        refreshSchedulerFactory,
                        new RecordingLatestTaskSchedulerFactory(),
                        new RecordingPeriodicTaskSchedulerFactory(),
                        new RecordingAnalysisDiagnostics()));

                using var viewer = host.CreateViewer();

                WpfTestRunner.InvokePrivate(viewer, "RequestViewportOverlayRefresh", true);
                WpfTestRunner.InvokePrivate(viewer, "BeginViewportOverlayBatch");
                WpfTestRunner.InvokePrivate(viewer, "EndViewportOverlayBatch", false);

                WpfTestRunner.InvokePrivate(viewer, "RequestAnalysisRefresh", true, false);
                WpfTestRunner.InvokePrivate(viewer, "BeginAnalysisRefreshBatch");
                WpfTestRunner.InvokePrivate(viewer, "EndAnalysisRefreshBatch", true);

                Assert.Collection(
                    refreshSchedulerFactory.DeferredScheduler!.Requests,
                    immediate => Assert.True(immediate));
                Assert.Equal(1, refreshSchedulerFactory.DeferredScheduler.BeginBatchCallCount);
                Assert.Collection(
                    refreshSchedulerFactory.DeferredScheduler.EndBatchRequests,
                    immediate => Assert.False(immediate));

                Assert.Collection(
                    refreshSchedulerFactory.ForcedScheduler!.Requests,
                    request =>
                    {
                        Assert.True(request.Force);
                        Assert.False(request.Immediate);
                    });
                Assert.Equal(1, refreshSchedulerFactory.ForcedScheduler.BeginBatchCallCount);
                Assert.Collection(
                    refreshSchedulerFactory.ForcedScheduler.EndBatchRequests,
                    immediate => Assert.True(immediate));
            });
        }

        [Fact]
        public void Host_CreateViewer_UsesConfiguredAnalysisDiagnostics()
        {
            WpfTestRunner.Run(() =>
            {
                var logger = new RecordingLogger();
                var diagnostics = new RecordingAnalysisDiagnostics();
                RoiPluginRegistry pluginRegistry = RoiPluginRegistry.CreateBuiltIn();
                ImageViewerHost host = ImageViewerTestServices.CreateHost(
                    pluginRegistry,
                    ImageViewerTestServices.CreateRuntimeServices(logger),
                    ImageViewerTestServices.CreateHostServices(
                        new RecordingDispatcherTimerFactory(),
                        new RecordingRefreshSchedulerFactory(),
                        new RecordingLatestTaskSchedulerFactory(),
                        new RecordingPeriodicTaskSchedulerFactory(),
                        diagnostics));

                using var viewer = host.CreateViewer();
                ImageViewerControlComposition composition = viewer._controlComposition;
                ImageViewerAnalysisCoordinator analysisController = composition.AnalysisController;
                IImageViewerAnalysisErrorSink errorSink = analysisController._errorSink;
                var exception = new InvalidOperationException("boom");

                errorSink.LogNonCriticalError("analysis failed", exception);

                Assert.Collection(
                    diagnostics.Entries,
                    entry =>
                    {
                        Assert.Same(logger, entry.Logger);
                        Assert.Equal("analysis failed", entry.Message);
                        Assert.Same(exception, entry.Exception);
                    });
            });
        }

        [Fact]
        public void Host_CreateViewer_UsesConfiguredLatestAndPeriodicSchedulers()
        {
            WpfTestRunner.Run(() =>
            {
                var latestTaskSchedulerFactory = new RecordingLatestTaskSchedulerFactory();
                var periodicTaskSchedulerFactory = new RecordingPeriodicTaskSchedulerFactory();
                RoiPluginRegistry pluginRegistry = RoiPluginRegistry.CreateBuiltIn();
                ImageViewerHost host = ImageViewerTestServices.CreateHost(
                    pluginRegistry,
                    ImageViewerTestServices.CreateRuntimeServices(),
                    ImageViewerTestServices.CreateHostServices(
                        new RecordingDispatcherTimerFactory(),
                        new RecordingRefreshSchedulerFactory(),
                        latestTaskSchedulerFactory,
                        periodicTaskSchedulerFactory,
                        new RecordingAnalysisDiagnostics()));

                using var viewer = host.CreateViewer();

                Assert.Equal(1, latestTaskSchedulerFactory.CreateCallCount);
                Assert.NotNull(latestTaskSchedulerFactory.Scheduler);
                Assert.Collection(
                    periodicTaskSchedulerFactory.CreateRequests,
                    request =>
                    {
                        Assert.Equal(DispatcherPriority.Background, request.Priority);
                        Assert.Equal(TimeSpan.FromSeconds(30), request.Interval);
                    });
                Assert.NotNull(periodicTaskSchedulerFactory.Scheduler);
                Assert.Equal(1, periodicTaskSchedulerFactory.Scheduler!.StartCallCount);

                viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, viewer));

                Assert.Equal(1, periodicTaskSchedulerFactory.Scheduler.StopCallCount);

                viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, viewer));

                Assert.Equal(2, periodicTaskSchedulerFactory.Scheduler.StartCallCount);
            });
        }

        [Fact]
        public void InfoPanelEntryPoints_DelegateToConfiguredLatestTaskScheduler()
        {
            WpfTestRunner.Run(() =>
            {
                var latestTaskSchedulerFactory = new RecordingLatestTaskSchedulerFactory();
                RoiPluginRegistry pluginRegistry = RoiPluginRegistry.CreateBuiltIn();
                ImageViewerHost host = ImageViewerTestServices.CreateHost(
                    pluginRegistry,
                    ImageViewerTestServices.CreateRuntimeServices(),
                    ImageViewerTestServices.CreateHostServices(
                        new RecordingDispatcherTimerFactory(),
                        new RecordingRefreshSchedulerFactory(),
                        latestTaskSchedulerFactory,
                        new RecordingPeriodicTaskSchedulerFactory(),
                        new RecordingAnalysisDiagnostics()));

                using var viewer = host.CreateViewer();
                var roi = new CircleRoi
                {
                    Center = new PointD(4, 4),
                    Radius = 2
                };

                viewer.ViewerState.SelectedRoi = roi;
                Task queueTask = Assert.IsAssignableFrom<Task>(WpfTestRunner.InvokePrivate(
                    viewer,
                    "QueueInfoPanelStatisticsUpdateAsync",
                    roi,
                    roi.Clone(),
                    CreateBitmap(pixelWidth: 8, pixelHeight: 8),
                    "roi",
                    35));
                queueTask.GetAwaiter().GetResult();
                WpfTestRunner.InvokePrivate(viewer, "CancelPendingInfoPanelUpdate");

                RecordingLatestTaskScheduler scheduler = Assert.IsType<RecordingLatestTaskScheduler>(latestTaskSchedulerFactory.Scheduler);
                Assert.Collection(scheduler.DelayRequests, delay => Assert.Equal(35, delay));
                Assert.Equal(1, scheduler.CancelCallCount);
            });
        }

        [Fact]
        public void DroppingImageFile_ClearsUndoHistory()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                DroppedContentController droppedContentController = GetDroppedContentController(viewer);
                PopulateUndoHistory(viewer);
                Assert.True(viewer.ViewerState.UndoRedo.CanUndo);

                string imagePath = CreateTempPngPath();
                try
                {
                    var data = new DataObject();
                    data.SetData(DataFormats.FileDrop, new[] { imagePath });
                    DragEventArgs args = CreateDropArgs(data, target: viewer);

                    droppedContentController.HandleDropAsync(args).GetAwaiter().GetResult();
                    WpfTestRunner.DrainDispatcher();

                    Assert.False(viewer.ViewerState.UndoRedo.CanUndo, "拖放打开新图像后撤销历史应被清空。");
                }
                finally
                {
                    File.Delete(imagePath);
                }
            });
        }

        private static DroppedContentController GetDroppedContentController(ImageViewer.Controls.ImageViewer viewer)
        {
            var composition = viewer._controlComposition;
            return composition.DroppedContentController;
        }

        /// <summary>
        /// DragEventArgs 的公开构造函数在 .NET Core WPF 中为 internal，测试通过反射调用内部构造函数构造拖放事件。
        /// </summary>
        private static DragEventArgs CreateDropArgs(IDataObject data, DependencyObject target)
        {
            System.Reflection.ConstructorInfo constructor = typeof(DragEventArgs).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                [typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point)],
                null)
                ?? throw new InvalidOperationException("DragEventArgs internal constructor was not found.");

            return (DragEventArgs)constructor.Invoke([data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point(0, 0)]);
        }

        private static void PopulateUndoHistory(ImageViewer.Controls.ImageViewer viewer)
        {
            viewer.ViewerState.UndoRedo.Execute(new AddRoiCommand(new LineMeasureRoi(), viewer.ViewerState));
        }

        private static string CreateTempPngPath()
        {
            string path = Path.Combine(Path.GetTempPath(), $"iv-drop-{Guid.NewGuid():N}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(CreateBitmap(pixelWidth: 8, pixelHeight: 8)));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }

        private static BitmapSource CreateBitmap(int pixelWidth, int pixelHeight)
        {
            return BitmapSource.Create(
                pixelWidth,
                pixelHeight,
                96,
                96,
                PixelFormats.Gray8,
                null,
                new byte[pixelWidth * pixelHeight],
                pixelWidth);
        }

        private sealed class RecordingDispatcherTimerFactory : IImageViewerDispatcherTimerFactory
        {
            private readonly List<TimerRequest> _requests = [];

            public IReadOnlyList<TimerRequest> Requests => _requests;

            public DispatcherTimer Create(Dispatcher dispatcher, DispatcherPriority priority, TimeSpan interval)
            {
                _requests.Add(new TimerRequest(priority, interval));
                return new DispatcherTimer(priority, dispatcher)
                {
                    Interval = interval
                };
            }
        }

        private sealed record TimerRequest(DispatcherPriority Priority, TimeSpan Interval);

        private sealed class RecordingRefreshSchedulerFactory : IImageViewerRefreshSchedulerFactory
        {
            private readonly List<RefreshSchedulerCreateRequest> _createRequests = [];

            public IReadOnlyList<RefreshSchedulerCreateRequest> CreateRequests => _createRequests;

            public RecordingDeferredRefreshScheduler? DeferredScheduler { get; private set; }

            public RecordingForcedRefreshScheduler? ForcedScheduler { get; private set; }

            public IImageViewerDeferredRefreshScheduler CreateDeferred(Action refreshAction, Dispatcher dispatcher, DispatcherPriority priority, TimeSpan interval)
            {
                DeferredScheduler = new RecordingDeferredRefreshScheduler();
                _createRequests.Add(new RefreshSchedulerCreateRequest("Deferred", priority, interval));
                return DeferredScheduler;
            }

            public IImageViewerForcedRefreshScheduler CreateForced(Action<bool> refreshAction, Dispatcher dispatcher, DispatcherPriority priority, TimeSpan interval)
            {
                ForcedScheduler = new RecordingForcedRefreshScheduler();
                _createRequests.Add(new RefreshSchedulerCreateRequest("Forced", priority, interval));
                return ForcedScheduler;
            }
        }

        private sealed record RefreshSchedulerCreateRequest(string Kind, DispatcherPriority Priority, TimeSpan Interval);

        private sealed class RecordingDeferredRefreshScheduler : IImageViewerDeferredRefreshScheduler
        {
            private readonly List<bool> _requests = [];
            private readonly List<bool> _endBatchRequests = [];

            public IReadOnlyList<bool> Requests => _requests;

            public IReadOnlyList<bool> EndBatchRequests => _endBatchRequests;

            public int BeginBatchCallCount { get; private set; }

            public void StopScheduling()
            {
            }

            public void Request(bool immediate = false)
            {
                _requests.Add(immediate);
            }

            public void BeginBatch()
            {
                BeginBatchCallCount++;
            }

            public void EndBatch(bool immediate = false)
            {
                _endBatchRequests.Add(immediate);
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingForcedRefreshScheduler : IImageViewerForcedRefreshScheduler
        {
            private readonly List<ForcedRefreshRequest> _requests = [];
            private readonly List<bool> _endBatchRequests = [];

            public IReadOnlyList<ForcedRefreshRequest> Requests => _requests;

            public IReadOnlyList<bool> EndBatchRequests => _endBatchRequests;

            public int BeginBatchCallCount { get; private set; }

            public void StopScheduling()
            {
            }

            public void Request(bool force = false, bool immediate = false)
            {
                _requests.Add(new ForcedRefreshRequest(force, immediate));
            }

            public void BeginBatch()
            {
                BeginBatchCallCount++;
            }

            public void EndBatch(bool immediate = false)
            {
                _endBatchRequests.Add(immediate);
            }

            public void Dispose()
            {
            }
        }

        private sealed record ForcedRefreshRequest(bool Force, bool Immediate);

        private sealed class RecordingLatestTaskSchedulerFactory : IImageViewerLatestTaskSchedulerFactory
        {
            public int CreateCallCount { get; private set; }

            public RecordingLatestTaskScheduler? Scheduler { get; private set; }

            public IImageViewerLatestTaskScheduler Create()
            {
                CreateCallCount++;
                Scheduler = new RecordingLatestTaskScheduler();
                return Scheduler;
            }
        }

        private sealed class RecordingLatestTaskScheduler : IImageViewerLatestTaskScheduler
        {
            private readonly List<int> _delayRequests = [];

            public IReadOnlyList<int> DelayRequests => _delayRequests;

            public int CancelCallCount { get; private set; }

            public Task ScheduleAsync(Func<CancellationToken, Task> work, int delayMilliseconds = 0)
            {
                _delayRequests.Add(delayMilliseconds);
                return Task.CompletedTask;
            }

            public void Cancel()
            {
                CancelCallCount++;
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingPeriodicTaskSchedulerFactory : IImageViewerPeriodicTaskSchedulerFactory
        {
            private readonly List<PeriodicTaskSchedulerCreateRequest> _createRequests = [];

            public IReadOnlyList<PeriodicTaskSchedulerCreateRequest> CreateRequests => _createRequests;

            public RecordingPeriodicTaskScheduler? Scheduler { get; private set; }

            public IImageViewerPeriodicTaskScheduler Create(Func<Task> callback, Dispatcher dispatcher, DispatcherPriority priority, TimeSpan interval)
            {
                Scheduler = new RecordingPeriodicTaskScheduler();
                _createRequests.Add(new PeriodicTaskSchedulerCreateRequest(priority, interval));
                return Scheduler;
            }
        }

        private sealed record PeriodicTaskSchedulerCreateRequest(DispatcherPriority Priority, TimeSpan Interval);

        private sealed class RecordingPeriodicTaskScheduler : IImageViewerPeriodicTaskScheduler
        {
            public int StartCallCount { get; private set; }

            public int StopCallCount { get; private set; }

            public int DisposeCallCount { get; private set; }

            public void Start()
            {
                StartCallCount++;
            }

            public void StopScheduling()
            {
                StopCallCount++;
            }

            public Task DrainAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }

        private sealed class TestImageSourceProvider : IImageViewerImageSourceProvider
        {
            public TestImageSourceProvider(ImageSource imageSource)
            {
                ViewerImage = imageSource;
            }

            public ImageSource? ViewerImage { get; }
        }
    }
}