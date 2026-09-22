using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ImageViewer.Abstractions;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Integration")]
    [Trait("Category", "Wpf")]
    public class ImageViewerSessionControllerTests
    {
        [Fact]
        public void SaveSessionAsync_SavesStateAndTracksRecentProject()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    var sessionService = new RecordingSessionService();
                    var recentProjectService = new RecordingRecentProjectService();
                    var packageService = new RecordingProjectPackageService();
                    var schedulerFactory = new RecordingPeriodicTaskSchedulerFactory();
                    var host = new RecordingSessionHost(sessionService, recentProjectService, packageService)
                    {
                        SaveSessionDialogPath = Path.Combine(rootPath, "sample.ivsession"),
                        CurrentImagePath = Path.Combine(rootPath, "source.png"),
                        AllRois = [new LineMeasureRoi()],
                        PixelSize = 0.25,
                        PhysicalUnit = "mm",
                        CurrentViewportState = new ImageViewerViewportState(1.5, 12, 18)
                    };

                    using var controller = CreateController(host, schedulerFactory, rootPath);

                    controller.SaveSessionAsync().GetAwaiter().GetResult();

                    Assert.Equal(host.SaveSessionDialogPath, sessionService.LastSaveFilePath);
                    Assert.Same(host.PluginRegistry, sessionService.LastSavePluginRegistry);

                    RecentImageViewerProject remembered = Assert.Single(recentProjectService.LastSavedItems!);
                    Assert.Equal("session", remembered.ProjectKind);
                    Assert.Equal(Path.GetFullPath(host.SaveSessionDialogPath!), remembered.Path);

                    ImageViewerDynamicMenuItem recentMenuItem = Assert.Single(controller.GetRecentProjectMenuItems());
                    Assert.Equal(remembered.Path, Assert.IsType<ImageViewerRecentProjectMenuTag>(recentMenuItem.Tag).ProjectPath);
                    Assert.Equal(1, host.UpdateContextMenuStateCount);
                    Assert.True(schedulerFactory.Scheduler.StartCalled);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void OpenProjectAsync_WhenFileIsMissing_RemovesRecentProjectAndWarns()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    string missingFilePath = Path.Combine(rootPath, "missing.ivsession");
                    var sessionService = new RecordingSessionService();
                    var recentProjectService = new RecordingRecentProjectService();
                    recentProjectService.Seed(new RecentImageViewerProject("missing", missingFilePath, "session", DateTimeOffset.UtcNow));
                    var host = new RecordingSessionHost(sessionService, recentProjectService, new RecordingProjectPackageService());

                    using var controller = CreateController(
                        host,
                        new RecordingPeriodicTaskSchedulerFactory(),
                        rootPath);

                    controller.OpenProjectAsync(missingFilePath).GetAwaiter().GetResult();

                    Assert.Empty(recentProjectService.LastSavedItems!);
                    Assert.Single(host.Warnings);
                    Assert.Equal(1, host.UpdateContextMenuStateCount);
                    Assert.Null(sessionService.LastLoadFilePath);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void AutoSave_WhenThereIsNoContent_DoesNotWriteSession()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    var sessionService = new RecordingSessionService();
                    var host = new RecordingSessionHost(sessionService, new RecordingRecentProjectService(), new RecordingProjectPackageService())
                    {
                        HasContent = false
                    };
                    var schedulerFactory = new RecordingPeriodicTaskSchedulerFactory();

                    using var controller = CreateController(host, schedulerFactory, rootPath);
                    schedulerFactory.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();

                    Assert.Null(sessionService.LastSaveFilePath);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void AutoSave_WhenDisabled_DoesNotWriteSession()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    var sessionService = new RecordingSessionService();
                    var host = new RecordingSessionHost(sessionService, new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    var schedulerFactory = new RecordingPeriodicTaskSchedulerFactory();

                    using var controller = CreateController(host, schedulerFactory, rootPath);
                    controller.ToggleAutoSave();
                    schedulerFactory.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();

                    Assert.Null(sessionService.LastSaveFilePath);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void OpenProjectAsync_SessionFile_LoadsAndAppliesSessionState()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    string sessionFilePath = Path.Combine(rootPath, "project.ivsession");
                    string imageFilePath = Path.Combine(rootPath, "image.png");
                    File.WriteAllText(sessionFilePath, "session");
                    File.WriteAllText(imageFilePath, "image");

                    var sessionService = new RecordingSessionService
                    {
                        LoadResult = new ImageViewerSessionData(
                            "session",
                            DateTimeOffset.UtcNow,
                            imageFilePath,
                            [new LineMeasureRoi()],
                            1.75,
                            "um",
                            2.5,
                            30,
                            40)
                    };
                    var recentProjectService = new RecordingRecentProjectService();
                    var host = new RecordingSessionHost(sessionService, recentProjectService, new RecordingProjectPackageService());

                    using var controller = CreateController(host, new RecordingPeriodicTaskSchedulerFactory(), rootPath);

                    controller.OpenProjectAsync(sessionFilePath).GetAwaiter().GetResult();

                    Assert.Equal(sessionFilePath, sessionService.LastLoadFilePath);
                    Assert.Equal(imageFilePath, host.LastLoadedImagePath);
                    Assert.False(host.LastLoadImageFitToView);
                    Assert.Single(host.LastReplacedRois!);
                    Assert.Equal(1.75, host.PixelSize);
                    Assert.Equal("um", host.PhysicalUnit);
                    Assert.Equal(new ImageViewerViewportState(2.5, 30, 40), host.LastAppliedViewportState);
                    Assert.Equal(1, host.DrawRoisCallCount);
                    Assert.Equal(1, host.ClearUndoHistoryCount);

                    RecentImageViewerProject remembered = Assert.Single(recentProjectService.LastSavedItems!);
                    Assert.Equal("session", remembered.ProjectKind);
                    Assert.Equal(2, host.UpdateContextMenuStateCount);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void ExportProjectPackageAsync_UsesPackageServiceAndTracksPackageProject()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    var sessionService = new RecordingSessionService();
                    var recentProjectService = new RecordingRecentProjectService();
                    var packageService = new RecordingProjectPackageService();
                    var host = new RecordingSessionHost(sessionService, recentProjectService, packageService)
                    {
                        SaveProjectPackageDialogPath = Path.Combine(rootPath, "sample.ivpkg"),
                        CurrentImagePath = Path.Combine(rootPath, "source.png"),
                        AllRois = [new LineMeasureRoi()],
                        PixelSize = 0.5,
                        PhysicalUnit = "mm",
                        CurrentViewportState = new ImageViewerViewportState(1.2, 4, 6)
                    };

                    using var controller = CreateController(host, new RecordingPeriodicTaskSchedulerFactory(), rootPath);

                    controller.ExportProjectPackageAsync().GetAwaiter().GetResult();

                    Assert.Equal(host.SaveProjectPackageDialogPath, packageService.LastExportFilePath);
                    Assert.Same(host.PluginRegistry, packageService.LastExportPluginRegistry);

                    RecentImageViewerProject remembered = Assert.Single(recentProjectService.LastSavedItems!);
                    Assert.Equal("package", remembered.ProjectKind);
                    Assert.Equal(Path.GetFullPath(host.SaveProjectPackageDialogPath!), remembered.Path);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void ToggleAutoSave_FlipsEnabledState()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    var host = new RecordingSessionHost(new RecordingSessionService(), new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    using var controller = CreateController(host, new RecordingPeriodicTaskSchedulerFactory(), rootPath);

                    bool initialState = controller.IsAutoSaveEnabled;

                    controller.ToggleAutoSave();

                    Assert.NotEqual(initialState, controller.IsAutoSaveEnabled);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        private static string CreateTempRoot()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return rootPath;
        }

        private static ImageViewerSessionController CreateController(
            RecordingSessionHost host,
            RecordingPeriodicTaskSchedulerFactory schedulerFactory,
            string rootPath)
        {
            return new ImageViewerSessionController(
                CreateDependencies(host),
                schedulerFactory,
                new LocalAppDataImageViewerSessionStoragePolicy(rootPath, TimeSpan.FromSeconds(5)));
        }

        private static ImageViewerSessionControllerDependencies CreateDependencies(RecordingSessionHost host)
        {
            return new ImageViewerSessionControllerDependencies
            {
                Persistence = new ImageViewerSessionPersistenceWorkflow
                {
                    ShowSaveSessionDialog = () => host.SaveSessionDialogPath,
                    ShowOpenSessionDialog = () => host.OpenSessionDialogPath,
                    ShowSaveProjectPackageDialog = () => host.SaveProjectPackageDialogPath,
                    SessionService = host.SessionService,
                    RecentProjectService = host.RecentProjectService,
                    ProjectPackageService = host.ProjectPackageService,
                    GetPluginRegistry = () => host.PluginRegistry,
                    GetAllRois = () => host.AllRois,
                    GetPixelSize = () => host.PixelSize,
                    SetPixelSize = value => host.PixelSize = value,
                    GetPhysicalUnit = () => host.PhysicalUnit,
                    SetPhysicalUnit = value => host.PhysicalUnit = value,
                    GetCurrentViewportState = () => host.CurrentViewportState,
                    TryGetCurrentImagePath = () => host.CurrentImagePath,
                    LoadImageFromFile = host.LoadImageFromFile,
                    ReplaceAllRois = host.ReplaceAllRois,
                    ApplyViewportState = host.ApplyViewportState,
                    DrawRois = host.DrawRois,
                    ShowNonCriticalError = host.ShowNonCriticalError,
                    ShowWarning = host.ShowWarning,
                    ShowStatusHint = message => host.StatusHints.Add(message),
                    ClearUndoHistory = () => host.ClearUndoHistoryCount++,
                    UpdateContextMenuState = host.UpdateContextMenuState
                },
                AutoSave = new ImageViewerAutoSaveWorkflow
                {
                    Dispatcher = host.Dispatcher,
                    HasContent = () => host.HasContent,
                    GetCurrentViewportState = () => host.CurrentViewportState,
                    TryGetCurrentImagePath = () => host.CurrentImagePath,
                    GetAllRois = () => host.AllRois,
                    GetPixelSize = () => host.PixelSize,
                    GetPhysicalUnit = () => host.PhysicalUnit,
                    SessionService = host.SessionService,
                    GetPluginRegistry = () => host.PluginRegistry,
                    LogNonCriticalError = host.LogNonCriticalError
                }
            };
        }

        private sealed class RecordingSessionHost
        {
            public RecordingSessionHost(
                IImageViewerSessionService sessionService,
                IImageViewerRecentProjectService recentProjectService,
                IImageViewerProjectPackageService projectPackageService)
            {
                SessionService = sessionService;
                RecentProjectService = recentProjectService;
                ProjectPackageService = projectPackageService;
            }

            public Dispatcher Dispatcher { get; } = Dispatcher.CurrentDispatcher;

            public string? SaveSessionDialogPath { get; set; }

            public string? OpenSessionDialogPath { get; set; }

            public string? SaveProjectPackageDialogPath { get; set; }

            public IImageViewerSessionService SessionService { get; }

            public IImageViewerRecentProjectService RecentProjectService { get; }

            public IImageViewerProjectPackageService ProjectPackageService { get; }

            public RoiPluginRegistry PluginRegistry { get; } = RoiPluginRegistry.CreateBuiltIn();

            public IReadOnlyList<RoiBase> AllRois { get; set; } = [];

            public double PixelSize { get; set; }

            public string PhysicalUnit { get; set; } = "px";

            public ImageViewerViewportState CurrentViewportState { get; set; } = new(1, 0, 0);

            public bool HasContent { get; set; } = true;

            public string? CurrentImagePath { get; set; }

            public string? LastLoadedImagePath { get; private set; }

            public bool LastLoadImageFitToView { get; private set; }

            public IReadOnlyList<RoiBase>? LastReplacedRois { get; private set; }

            public ImageViewerViewportState? LastAppliedViewportState { get; private set; }

            public int DrawRoisCallCount { get; private set; }

            public int UpdateContextMenuStateCount { get; private set; }

            public List<(string Title, string Message)> Warnings { get; } = [];

            public List<(string Title, string Message, Exception Exception)> Errors { get; } = [];

            public List<string> StatusHints { get; } = [];

            public int ClearUndoHistoryCount { get; set; }

            public List<(string Context, Exception Exception)> LoggedErrors { get; } = [];

            public string? ShowSaveSessionDialog() => SaveSessionDialogPath;

            public string? ShowOpenSessionDialog() => OpenSessionDialogPath;

            public string? ShowSaveProjectPackageDialog() => SaveProjectPackageDialogPath;

            public string? TryGetCurrentImagePath() => CurrentImagePath;

            public void LoadImageFromFile(string filePath, bool fitToView)
            {
                LastLoadedImagePath = filePath;
                LastLoadImageFitToView = fitToView;
            }

            public void ReplaceAllRois(IReadOnlyList<RoiBase> rois)
            {
                LastReplacedRois = rois;
            }

            public void ApplyViewportState(ImageViewerViewportState state)
            {
                LastAppliedViewportState = state;
            }

            public void DrawRois()
            {
                DrawRoisCallCount++;
            }

            public void ShowNonCriticalError(string title, string message, Exception ex)
            {
                Errors.Add((title, message, ex));
            }

            public void LogNonCriticalError(string context, Exception ex)
            {
                LoggedErrors.Add((context, ex));
            }

            public void ShowWarning(string title, string message)
            {
                Warnings.Add((title, message));
            }

            public void UpdateContextMenuState()
            {
                UpdateContextMenuStateCount++;
            }
        }

        private sealed class RecordingSessionService : IImageViewerSessionService
        {
            public string? LastSaveFilePath { get; private set; }

            public RoiPluginRegistry? LastSavePluginRegistry { get; private set; }

            public string? LastLoadFilePath { get; private set; }

            public RoiPluginRegistry? LastLoadPluginRegistry { get; private set; }

            public ImageViewerSessionData LoadResult { get; set; } = new(
                "session",
                DateTimeOffset.UtcNow,
                null,
                [],
                1,
                "px",
                1,
                0,
                0);

            public void SaveToFile(string filePath, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null)
            {
                throw new NotSupportedException();
            }

            public Task SaveToFileAsync(string filePath, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
            {
                LastSaveFilePath = filePath;
                LastSavePluginRegistry = pluginRegistry;
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, "session");
                return Task.CompletedTask;
            }

            public string SerializeSession(string? sessionName, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null)
            {
                throw new NotSupportedException();
            }

            public ImageViewerSessionData LoadFromFile(string filePath, RoiPluginRegistry? pluginRegistry = null)
            {
                throw new NotSupportedException();
            }

            public Task<ImageViewerSessionData> LoadFromFileAsync(string filePath, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
            {
                LastLoadFilePath = filePath;
                LastLoadPluginRegistry = pluginRegistry;
                return Task.FromResult(LoadResult);
            }

            public ImageViewerSessionData LoadFromJson(string sessionJson, string? sessionBaseDirectory = null, RoiPluginRegistry? pluginRegistry = null)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class RecordingProjectPackageService : IImageViewerProjectPackageService
        {
            public string? LastExportFilePath { get; private set; }

            public RoiPluginRegistry? LastExportPluginRegistry { get; private set; }

            public string? LastLoadFilePath { get; private set; }

            public RoiPluginRegistry? LastLoadPluginRegistry { get; private set; }

            public ImageViewerSessionData LoadResult { get; set; } = new(
                "package",
                DateTimeOffset.UtcNow,
                null,
                [],
                1,
                "px",
                1,
                0,
                0);

            public Task ExportAsync(string packagePath, string? imagePath, IEnumerable<RoiBase> rois, double pixelSize, string? physicalUnit, double scale, double translateX, double translateY, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
            {
                LastExportFilePath = packagePath;
                LastExportPluginRegistry = pluginRegistry;
                Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
                File.WriteAllText(packagePath, "package");
                return Task.CompletedTask;
            }

            public Task<ImageViewerSessionData> LoadAsync(string packagePath, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
            {
                LastLoadFilePath = packagePath;
                LastLoadPluginRegistry = pluginRegistry;
                return Task.FromResult(LoadResult);
            }
        }

        private sealed class RecordingRecentProjectService : IImageViewerRecentProjectService
        {
            private IReadOnlyList<RecentImageViewerProject> _items = [];

            public IReadOnlyList<RecentImageViewerProject>? LastSavedItems { get; private set; }

            public void Seed(params RecentImageViewerProject[] items)
            {
                _items = items;
            }

            public IReadOnlyList<RecentImageViewerProject> Load(string filePath, int maxCount = 10)
            {
                return _items;
            }

            public void Save(string filePath, IEnumerable<RecentImageViewerProject> items)
            {
                _items = items.ToArray();
                LastSavedItems = _items;
            }

            public IReadOnlyList<RecentImageViewerProject> Touch(IEnumerable<RecentImageViewerProject> items, string filePath, string projectKind, int maxCount = 10)
            {
                return new ImageViewerRecentProjectService().Touch(items, filePath, projectKind, maxCount);
            }
        }

        private sealed class RecordingPeriodicTaskSchedulerFactory : IImageViewerPeriodicTaskSchedulerFactory
        {
            public RecordingPeriodicTaskScheduler Scheduler { get; } = new();

            public IImageViewerPeriodicTaskScheduler Create(Func<Task> callback, Dispatcher dispatcher, DispatcherPriority priority, TimeSpan interval)
            {
                Scheduler.Callback = callback;
                return Scheduler;
            }
        }

        private sealed class RecordingPeriodicTaskScheduler : IImageViewerPeriodicTaskScheduler
        {
            public Func<Task>? Callback { get; set; }

            public bool StartCalled { get; private set; }

            public bool StopCalled { get; private set; }

            public void Start()
            {
                StartCalled = true;
            }

            public void StopScheduling()
            {
                StopCalled = true;
            }

            public Task DrainAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public void Dispose()
            {
            }

            public Task InvokeCallbackAsync()
            {
                return Callback?.Invoke() ?? Task.CompletedTask;
            }
        }
    }
}