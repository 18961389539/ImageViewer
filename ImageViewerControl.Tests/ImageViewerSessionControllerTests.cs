using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ImageViewer.Abstractions;
using ImageViewer.Controls;
using ImageViewer.Localization;
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
        public void AutoSave_WhenSaveFails_ShowsNonBlockingHint()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    var sessionService = new RecordingSessionService { ThrowOnSave = true };
                    var host = new RecordingSessionHost(sessionService, new RecordingRecentProjectService(), new RecordingProjectPackageService())
                    {
                        HasContent = true
                    };
                    var schedulerFactory = new RecordingPeriodicTaskSchedulerFactory();

                    using var controller = CreateController(host, schedulerFactory, rootPath);
                    schedulerFactory.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();

                    Assert.Contains(UiText.Get("StatusAutoSaveFailed"), host.StatusHints);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void AutoSave_WhenSaveSucceedsAfterFailure_ResetsFailureHint()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    var sessionService = new RecordingSessionService();
                    var host = new RecordingSessionHost(sessionService, new RecordingRecentProjectService(), new RecordingProjectPackageService())
                    {
                        HasContent = true
                    };
                    var schedulerFactory = new RecordingPeriodicTaskSchedulerFactory();

                    using var controller = CreateController(host, schedulerFactory, rootPath);

                    sessionService.ThrowOnSave = true;
                    schedulerFactory.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();
                    Assert.Single(host.StatusHints.Where(hint => hint == UiText.Get("StatusAutoSaveFailed")));

                    sessionService.ThrowOnSave = false;
                    schedulerFactory.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();
                    schedulerFactory.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();

                    Assert.Single(host.StatusHints.Where(hint => hint == UiText.Get("StatusAutoSaveFailed")));
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void AutoSave_WhenReEnabledAfterFailure_AllowsNextFailureHint()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    var sessionService = new RecordingSessionService { ThrowOnSave = true };
                    var host = new RecordingSessionHost(sessionService, new RecordingRecentProjectService(), new RecordingProjectPackageService())
                    {
                        HasContent = true
                    };
                    var schedulerFactory = new RecordingPeriodicTaskSchedulerFactory();

                    using var controller = CreateController(host, schedulerFactory, rootPath);

                    // 第一次失败提示一次
                    schedulerFactory.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();
                    Assert.Single(host.StatusHints.Where(hint => hint == UiText.Get("StatusAutoSaveFailed")));

                    // 关闭后再重新开启：失败标记应被重置，下一次失败可再次提示
                    controller.ToggleAutoSave();
                    controller.ToggleAutoSave();
                    schedulerFactory.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();

                    Assert.Equal(
                        2,
                        host.StatusHints.Count(hint => hint == UiText.Get("StatusAutoSaveFailed")));
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void AutoSave_WithoutProjectPath_StillWritesSessionUsingFallbackName()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    // 从未保存过项目：_currentProjectPath 为 null，验证自动保存仍能真正写入而不崩溃
                    var sessionService = new RecordingSessionService();
                    var host = new RecordingSessionHost(sessionService, new RecordingRecentProjectService(), new RecordingProjectPackageService())
                    {
                        HasContent = true
                    };
                    var schedulerFactory = new RecordingPeriodicTaskSchedulerFactory();

                    using var controller = CreateController(host, schedulerFactory, rootPath);

                    schedulerFactory.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();

                    Assert.NotNull(sessionService.LastSaveFilePath);
                    Assert.Empty(host.LoggedErrors);
                    Assert.Empty(host.StatusHints);
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
                            40,
                            null)
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

        [Fact]
        public void RecoverLatestAutoSave_DiscoversUnsavedDocumentFromPreviousRun()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    // 第一次运行：一个未保存文档自动保存后**异常退出**（崩溃/强杀，走不到正常关闭的清理路径）。
                    // 遗留的标志是"索引里有一条，但所有者心跳已经过期"——用过期时间戳直接写索引来模拟。
                    var firstService = new RecordingSessionService();
                    var firstHost = new RecordingSessionHost(firstService, new RecordingRecentProjectService(), new RecordingProjectPackageService()) { HasContent = true };
                    var firstScheduler = new RecordingPeriodicTaskSchedulerFactory();
                    using (var firstController = CreateController(firstHost, firstScheduler, rootPath, "run-one-document"))
                    {
                        firstScheduler.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();
                    }

                    string autoSaveFileName = Path.GetFileName(firstService.LastSaveFilePath!);
                    Assert.Equal("autosave-run-one-document.ivsession", autoSaveFileName);

                    // 模拟上一次运行崩溃：把心跳改成很久以前（进程没来得及清理所有权）。
                    WriteStaleHeartbeatEntry(rootPath, firstService.LastSaveFilePath!, "crashed-run-one");

                    // 第二次运行：全新的 DocumentId（模拟重启），必须仍能发现上次崩溃留下的未保存文档。
                    string sourceImage = Path.Combine(rootPath, "source.png");
                    File.WriteAllText(sourceImage, "image");
                    var secondService = new RecordingSessionService
                    {
                        LoadResult = new ImageViewerSessionData(
                            sourceImage,
                            DateTimeOffset.UtcNow,
                            null,
                            [],
                            1.0,
                            "px",
                            1.0,
                            0,
                            0,
                            null)
                    };
                    var secondHost = new RecordingSessionHost(secondService, new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    using var secondController = CreateController(secondHost, new RecordingPeriodicTaskSchedulerFactory(), rootPath, "run-two-document");

                    Assert.True(secondController.HasRecoverySnapshot);

                    secondController.RecoverLatestAutoSaveAsync().GetAwaiter().GetResult();

                    Assert.EndsWith(autoSaveFileName, secondService.LastLoadFilePath, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void RecoverLatestAutoSave_DoesNotOfferSnapshotStillHeldByALiveWindow()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    // A 窗口保持打开并自动保存（心跳新鲜）。
                    var serviceA = new RecordingSessionService();
                    var hostA = new RecordingSessionHost(serviceA, new RecordingRecentProjectService(), new RecordingProjectPackageService()) { HasContent = true };
                    var schedulerA = new RecordingPeriodicTaskSchedulerFactory();
                    using var controllerA = CreateController(hostA, schedulerA, rootPath, "document-a");

                    schedulerA.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();
                    string autoSavePathA = serviceA.LastSaveFilePath!;
                    Assert.True(File.Exists(autoSavePathA));

                    // B 窗口随后创建：A 的文档仍在另一个窗口里开着，不能把它的快照提示给 B 恢复。
                    var hostB = new RecordingSessionHost(new RecordingSessionService(), new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    using var controllerB = CreateController(hostB, new RecordingPeriodicTaskSchedulerFactory(), rootPath, "document-b");

                    Assert.False(controllerB.HasRecoverySnapshot);
                    Assert.True(File.Exists(autoSavePathA));
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void RecoverLatestAutoSave_DoesNotOfferDocumentsAlreadySavedForReal()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    var firstService = new RecordingSessionService();
                    var firstHost = new RecordingSessionHost(firstService, new RecordingRecentProjectService(), new RecordingProjectPackageService()) { HasContent = true };
                    var firstScheduler = new RecordingPeriodicTaskSchedulerFactory();
                    using var firstController = CreateController(firstHost, firstScheduler, rootPath, "run-one-document");

                    // 自动保存一次（进入索引），随后用户真正保存到磁盘并清理了自动保存。
                    firstScheduler.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();
                    string autoSavePath = firstService.LastSaveFilePath!;
                    Assert.True(File.Exists(autoSavePath));

                    firstHost.SaveSessionDialogPath = Path.Combine(rootPath, "saved.ivsession");
                    firstController.SaveSessionAsync().GetAwaiter().GetResult();

                    Assert.False(File.Exists(autoSavePath), $"自动保存文件应被清理，但仍存在：{autoSavePath}");

                    // 重启：已经保存过的文档不应再被当成待恢复文档。
                    var secondHost = new RecordingSessionHost(new RecordingSessionService(), new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    using var secondController = CreateController(secondHost, new RecordingPeriodicTaskSchedulerFactory(), rootPath, "run-two-document");

                    Assert.False(secondController.HasRecoverySnapshot);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void RecoverLatestAutoSave_IgnoresUnindexedAutoSaves()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    // 目录里躺着一个自动保存，但恢复索引里没有登记它（例如是别的进程/手工放进来的）。
                    // 重启后不应把未登记的文件当作自己的恢复候选——索引才是"本机遗留文档"的权威来源。
                    string autoSaveDirectory = Path.Combine(rootPath, "Autosave");
                    Directory.CreateDirectory(autoSaveDirectory);
                    File.WriteAllText(Path.Combine(autoSaveDirectory, "autosave-unindexed.ivsession"), "unindexed");

                    var host = new RecordingSessionHost(new RecordingSessionService(), new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    using var controller = CreateController(host, new RecordingPeriodicTaskSchedulerFactory(), rootPath, "run-two-document");

                    Assert.False(controller.HasRecoverySnapshot);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void SaveSessionAsync_OnlyCleansUpItsOwnAutoSave_NotOtherDocumentsRecoveryFiles()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    // 两个窗口共用同一个 Autosave 目录，各自自动保存一次。
                    var firstService = new RecordingSessionService();
                    var secondService = new RecordingSessionService();
                    var firstHost = new RecordingSessionHost(firstService, new RecordingRecentProjectService(), new RecordingProjectPackageService()) { HasContent = true };
                    var secondHost = new RecordingSessionHost(secondService, new RecordingRecentProjectService(), new RecordingProjectPackageService()) { HasContent = true };
                    var firstScheduler = new RecordingPeriodicTaskSchedulerFactory();
                    var secondScheduler = new RecordingPeriodicTaskSchedulerFactory();

                    using var firstController = CreateController(firstHost, firstScheduler, rootPath, "document-one");
                    using var secondController = CreateController(secondHost, secondScheduler, rootPath, "document-two");

                    firstScheduler.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();
                    secondScheduler.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();

                    string firstAutoSave = firstService.LastSaveFilePath!;
                    string secondAutoSave = secondService.LastSaveFilePath!;
                    Assert.True(File.Exists(firstAutoSave));
                    Assert.True(File.Exists(secondAutoSave));

                    // 第一个窗口真正保存到磁盘：只应清理它自己的自动保存，绝不能碰第二个窗口的恢复文件。
                    firstHost.SaveSessionDialogPath = Path.Combine(rootPath, "saved.ivsession");
                    firstController.SaveSessionAsync().GetAwaiter().GetResult();

                    Assert.False(File.Exists(firstAutoSave), $"本文档的自动保存应被清理：{firstAutoSave}");
                    Assert.True(File.Exists(secondAutoSave), $"其它文档的恢复文件必须保留，但已被删除：{secondAutoSave}");
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void SaveSessionAsync_CleansUpPreSaveAutoSaveAfterTheFileIsRenamed()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    // 未保存文档先自动保存（autosave-{id}），随后真正保存：文件名会变成 autosave-{名}-{哈希}，
                    // 保存前那份不能留成孤儿。
                    var service = new RecordingSessionService();
                    var host = new RecordingSessionHost(service, new RecordingRecentProjectService(), new RecordingProjectPackageService()) { HasContent = true };
                    var scheduler = new RecordingPeriodicTaskSchedulerFactory();

                    using var controller = CreateController(host, scheduler, rootPath, "document-one");

                    scheduler.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();
                    string preSaveAutoSave = service.LastSaveFilePath!;
                    Assert.True(File.Exists(preSaveAutoSave));

                    host.SaveSessionDialogPath = Path.Combine(rootPath, "saved.ivsession");
                    controller.SaveSessionAsync().GetAwaiter().GetResult();

                    Assert.False(File.Exists(preSaveAutoSave), $"保存前的自动保存必须被清理：{preSaveAutoSave}");
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        /// <summary>
        /// 模拟"上一次运行崩溃"：索引里留一条登记，但所有者心跳早已过期。
        /// Chinese: 正常关闭会清理所有权，所以"重启后发现遗留文档"的前提是上次**非正常结束**——
        /// 这里直接改写索引文件来构造这个状态（不经过控制器，避免引入正常退出路径）。
        /// </summary>
        private static void WriteStaleHeartbeatEntry(string rootPath, string autoSaveFilePath, string ownerInstanceId)
        {
            string indexFilePath = Path.Combine(rootPath, "Autosave", "recovery-index.json");
            Directory.CreateDirectory(Path.GetDirectoryName(indexFilePath)!);
            var index = new ImageViewerRecoveryIndex(Path.Combine(rootPath, "Autosave"));
            index.Register(
                autoSaveFilePath,
                DateTimeOffset.UtcNow.AddMinutes(-10),
                ownerInstanceId,
                DateTimeOffset.UtcNow.AddMinutes(-10));
            Assert.True(File.Exists(indexFilePath));
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

        private static ImageViewerSessionController CreateController(
            RecordingSessionHost host,
            RecordingPeriodicTaskSchedulerFactory schedulerFactory,
            string rootPath,
            string documentId)
        {
            return new ImageViewerSessionController(
                CreateDependencies(host),
                schedulerFactory,
                new LocalAppDataImageViewerSessionStoragePolicy(rootPath, TimeSpan.FromSeconds(5)),
                documentId);
        }

        [Fact]
        public void CaptureSnapshot_DetachesRoiPayloadFromTheLiveCollection()
        {
            var liveRois = new List<RoiBase> { new CircleRoi { Label = "live", Center = new PointD(1, 2), Radius = 3 } };
            var host = new RecordingSessionHost(
                new RecordingSessionService(),
                new RecordingRecentProjectService(),
                new RecordingProjectPackageService())
            {
                AllRois = liveRois,
                PixelSize = 0.5,
                PhysicalUnit = "mm",
                CurrentImagePath = "source.png"
            };
            ImageViewerSessionControllerDependencies dependencies = CreateDependencies(host);

            ImageViewerPersistenceSnapshot snapshot = dependencies.Persistence.CaptureSnapshot();

            RoiPersistenceData captured = Assert.Single(snapshot.RoiDocument.Items);
            Assert.Equal("circle", captured.Type);
            Assert.Equal("live", captured.Label);

            // 快照之后编辑器继续改动活集合：新增一个 ROI，并改掉已有的半径。
            liveRois.Add(new CircleRoi { Label = "added-later" });
            ((CircleRoi)liveRois[0]).Radius = 42;

            Assert.Single(snapshot.RoiDocument.Items);
            Assert.Equal("live", snapshot.RoiDocument.Items[0].Label);

            // 重新采集才会看到新增项——说明差异来自真正的采集，而不是快照还引用着活集合。
            ImageViewerPersistenceSnapshot second = dependencies.Persistence.CaptureSnapshot();
            Assert.Equal(2, second.RoiDocument.Items.Count);
        }

        [Fact]
        public void CaptureSnapshot_BakesMeasurementContextIntoTheDocument()
        {
            var host = new RecordingSessionHost(
                new RecordingSessionService(),
                new RecordingRecentProjectService(),
                new RecordingProjectPackageService())
            {
                AllRois = [new CircleRoi()],
                PixelSize = 0.25,
                PhysicalUnit = "mm"
            };
            ImageViewerSessionControllerDependencies dependencies = CreateDependencies(host);

            ImageViewerPersistenceSnapshot snapshot = dependencies.Persistence.CaptureSnapshot();

            host.PixelSize = 9.0;
            host.PhysicalUnit = "um";

            Assert.Equal(0.25, snapshot.RoiDocument.PixelSize);
            Assert.Equal("mm", snapshot.RoiDocument.PhysicalUnit);
        }

        [Fact]
        public void AutoSave_WithoutProjectPath_TwoDocumentsUseDistinctFiles()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    // 两个未保存文档共用同一个 Autosave 目录（默认策略），必须写各自的文件。
                    var firstService = new RecordingSessionService();
                    var secondService = new RecordingSessionService();
                    var firstHost = new RecordingSessionHost(firstService, new RecordingRecentProjectService(), new RecordingProjectPackageService()) { HasContent = true };
                    var secondHost = new RecordingSessionHost(secondService, new RecordingRecentProjectService(), new RecordingProjectPackageService()) { HasContent = true };
                    var firstScheduler = new RecordingPeriodicTaskSchedulerFactory();
                    var secondScheduler = new RecordingPeriodicTaskSchedulerFactory();

                    using var firstController = CreateController(firstHost, firstScheduler, rootPath, "document-one");
                    using var secondController = CreateController(secondHost, secondScheduler, rootPath, "document-two");

                    firstScheduler.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();
                    secondScheduler.Scheduler.InvokeCallbackAsync().GetAwaiter().GetResult();

                    Assert.NotNull(firstService.LastSaveFilePath);
                    Assert.NotNull(secondService.LastSaveFilePath);
                    Assert.NotEqual(
                        Path.GetFileName(firstService.LastSaveFilePath),
                        Path.GetFileName(secondService.LastSaveFilePath));
                    Assert.Equal("autosave-document-one.ivsession", Path.GetFileName(firstService.LastSaveFilePath));
                    Assert.Equal("autosave-document-two.ivsession", Path.GetFileName(secondService.LastSaveFilePath));
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void RecoverLatestAutoSave_DoesNotAdoptAnotherDocumentsAutoSave()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    string autoSaveDirectory = Path.Combine(rootPath, "Autosave");
                    Directory.CreateDirectory(autoSaveDirectory);
                    // 另一个窗口（document-other）留下的自动保存，内容非空。
                    string foreignAutoSave = Path.Combine(autoSaveDirectory, "autosave-document-other.ivsession");
                    File.WriteAllText(foreignAutoSave, "foreign");

                    var sessionService = new RecordingSessionService();
                    var host = new RecordingSessionHost(sessionService, new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    var schedulerFactory = new RecordingPeriodicTaskSchedulerFactory();

                    using var controller = CreateController(host, schedulerFactory, rootPath, "document-mine");

                    // 本窗口只有别的文档的自动保存：不应把它当成自己的恢复候选。
                    Assert.False(controller.HasRecoverySnapshot);
                    Assert.Null(sessionService.LastLoadFilePath);

                    controller.RecoverLatestAutoSaveAsync().GetAwaiter().GetResult();

                    Assert.Null(sessionService.LastLoadFilePath);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void RecoverLatestAutoSave_AcceptsLegacySharedAutoSaveWhenNoOwnCandidateExists()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = CreateTempRoot();
                try
                {
                    string autoSaveDirectory = Path.Combine(rootPath, "Autosave");
                    Directory.CreateDirectory(autoSaveDirectory);
                    // 旧版本遗留的共享文件名（所有未保存文档都写它）。
                    string legacyAutoSave = Path.Combine(autoSaveDirectory, "autosave.ivsession");
                    File.WriteAllText(legacyAutoSave, "legacy");

                    // ApplySession 会校验图片存在，给一个真实文件。
                    string sourceImage = Path.Combine(rootPath, "source.png");
                    File.WriteAllText(sourceImage, "image");

                    var sessionService = new RecordingSessionService
                    {
                        LoadResult = new ImageViewerSessionData(
                            sourceImage,
                            DateTimeOffset.UtcNow,
                            null,
                            [],
                            1.0,
                            "px",
                            1.0,
                            0,
                            0,
                            null)
                    };
                    var host = new RecordingSessionHost(sessionService, new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    var schedulerFactory = new RecordingPeriodicTaskSchedulerFactory();

                    using var controller = CreateController(host, schedulerFactory, rootPath, "document-mine");

                    // 升级兜底：没有自己的候选时，仍认旧的共享文件。
                    Assert.True(controller.HasRecoverySnapshot);

                    controller.RecoverLatestAutoSaveAsync().GetAwaiter().GetResult();

                    Assert.Equal(legacyAutoSave, sessionService.LastLoadFilePath);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void SaveRecipeAsync_WritesDeliverablePlanWithoutBakingInImagePath()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = Path.Combine(Path.GetTempPath(), "iv-recipe-save-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                try
                {
                    var host = new RecordingSessionHost(new RecordingSessionService(), new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    host.SaveRecipeDialogPath = Path.Combine(rootPath, "plan.ivrecipe");
                    host.AllRois =
                    [
                        new LineMeasureRoi
                        {
                            Label = "总长",
                            P1 = new PointD(0, 0),
                            P2 = new PointD(100, 0),
                            Tolerance = new MeasurementTolerance { Nominal = 12.5, TolerancePlus = 0.1, ToleranceMinus = 0.1 }
                        }
                    ];

                    using var controller = CreateController(host, new RecordingPeriodicTaskSchedulerFactory(), rootPath, "document-mine");
                    controller.SaveRecipeAsync().GetAwaiter().GetResult();

                    Assert.True(File.Exists(host.SaveRecipeDialogPath));
                    MeasurementRecipeDocument recipe = MeasurementRecipeService.LoadFromFile(host.SaveRecipeDialogPath);

                    Assert.Single(recipe.Items);
                    Assert.Equal("总长", recipe.Items[0].DisplayName);
                    Assert.Equal(12.5, recipe.Items[0].Tolerance!.Nominal);
                    Assert.NotNull(recipe.Items[0].Template);

                    // 配方必须"可交付"：不能把本文档的图像路径烘进测量方案，否则换一张图就失效。
                    Assert.DoesNotContain(host.CurrentImagePath ?? "never", File.ReadAllText(host.SaveRecipeDialogPath));
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void SaveRecipeAsync_WithoutRois_WarnsAndWritesNothing()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = Path.Combine(Path.GetTempPath(), "iv-recipe-empty-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                try
                {
                    var host = new RecordingSessionHost(new RecordingSessionService(), new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    host.SaveRecipeDialogPath = Path.Combine(rootPath, "empty.ivrecipe");
                    host.AllRois = [];

                    using var controller = CreateController(host, new RecordingPeriodicTaskSchedulerFactory(), rootPath, "document-mine");
                    controller.SaveRecipeAsync().GetAwaiter().GetResult();

                    // 写一个空配方比不写更糟：操作员会以为方案已保存。
                    Assert.False(File.Exists(host.SaveRecipeDialogPath));
                    Assert.Contains(UiText.Get("StatusRecipeNoRois"), host.StatusHints);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void LoadRecipeAsync_RestoresRoisToleranceScaleAndReportsReadiness()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = Path.Combine(Path.GetTempPath(), "iv-recipe-load-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                try
                {
                    var host = new RecordingSessionHost(new RecordingSessionService(), new RecordingRecentProjectService(), new RecordingProjectPackageService());
                    string recipePath = Path.Combine(rootPath, "loaded.ivrecipe");
                    MeasurementRecipeDocument recipe = MeasurementRecipeService.Create(
                        "配方",
                        null,
                        [
                            new LineMeasureRoi
                            {
                                Label = "总长",
                                P1 = new PointD(0, 0),
                                P2 = new PointD(100, 0),
                                Tolerance = new MeasurementTolerance { Nominal = 12.5, TolerancePlus = 0.1, ToleranceMinus = 0.1 }
                            }
                        ],
                        0.05,
                        "mm",
                        host.PluginRegistry,
                        calibration: new CameraCalibration { K1 = 0.01, NormalizationRadius = 500, CalibrationId = "C-9" });
                    MeasurementRecipeService.SaveToFile(recipePath, recipe);

                    host.OpenRecipeDialogPath = recipePath;
                    using var controller = CreateController(host, new RecordingPeriodicTaskSchedulerFactory(), rootPath, "document-mine");

                    controller.LoadRecipeAsync().GetAwaiter().GetResult();

                    Assert.NotNull(host.LastReplacedRois);
                    Assert.Single(host.LastReplacedRois!);
                    Assert.Equal(0.05, host.PixelSize);
                    Assert.Equal("mm", host.PhysicalUnit);
                    Assert.Equal("C-9", host.Calibration?.CalibrationId);

                    // "载入配方"必须当场给出可判定性，而不是让操作员以为一切就绪。
                    Assert.NotEmpty(host.StatusHints);
                    Assert.Equal(1, host.DrawRoisCallCount);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void LoadAlignedRecipeAsync_TransformsRoisAndRebasesReferenceDatums()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = Path.Combine(Path.GetTempPath(), "iv-recipe-align-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                try
                {
                    var host = new RecordingSessionHost(new RecordingSessionService(), new RecordingRecentProjectService(), new RecordingProjectPackageService())
                    {
                        CurrentRecipeDatumPoints =
                        [
                            new RecipeDatumPoint { Name = "D1", X = 300, Y = 400 },
                            new RecipeDatumPoint { Name = "D2", X = 300, Y = 500 }
                        ]
                    };
                    string recipePath = Path.Combine(rootPath, "aligned.ivrecipe");
                    MeasurementRecipeDocument recipe = MeasurementRecipeService.Create(
                        "配方",
                        null,
                        [new LineMeasureRoi { P1 = new PointD(120, 100), P2 = new PointD(140, 100) }],
                        0.2,
                        "mm",
                        host.PluginRegistry,
                        datumPoints:
                        [
                            new RecipeDatumPoint { Name = "D1", X = 100, Y = 100 },
                            new RecipeDatumPoint { Name = "D2", X = 200, Y = 100 }
                        ]);
                    MeasurementRecipeService.SaveToFile(recipePath, recipe);
                    host.OpenRecipeDialogPath = recipePath;

                    using var controller = CreateController(host, new RecordingPeriodicTaskSchedulerFactory(), rootPath, "document-align");
                    controller.LoadAlignedRecipeAsync().GetAwaiter().GetResult();

                    Assert.NotNull(host.LastReplacedRois);
                    var line = Assert.IsType<LineMeasureRoi>(Assert.Single(host.LastReplacedRois!));
                    Assert.Equal(new PointD(300, 420), line.P1);
                    Assert.Equal(new PointD(300, 440), line.P2);
                    Assert.Equal(0.2, host.PixelSize);
                    Assert.Equal("mm", host.PhysicalUnit);
                    Assert.Equal(300, host.RecipeDatumPoints[0].X);
                    Assert.Equal(400, host.RecipeDatumPoints[0].Y);
                    Assert.Contains(host.StatusHints, hint => hint.Contains(UiText.Get("StatusRecipeManualAlignmentApplied"), StringComparison.Ordinal));

                    host.SaveRecipeDialogPath = Path.Combine(rootPath, "rebased.ivrecipe");
                    controller.SaveRecipeAsync().GetAwaiter().GetResult();
                    MeasurementRecipeDocument rebased = MeasurementRecipeService.LoadFromFile(host.SaveRecipeDialogPath);
                    Assert.Equal(300, rebased.DatumPoints[0].X);
                    Assert.Equal(400, rebased.DatumPoints[0].Y);
                    Assert.Equal(300, rebased.DatumPoints[1].X);
                    Assert.Equal(500, rebased.DatumPoints[1].Y);
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
        }

        [Fact]
        public void LoadAlignedRecipeAsync_UnsupportedGeometryLeavesExistingStateUnchanged()
        {
            WpfTestRunner.Run(() =>
            {
                string rootPath = Path.Combine(Path.GetTempPath(), "iv-recipe-align-invalid-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                try
                {
                    var oldRoi = new LineMeasureRoi { P1 = new PointD(1, 2), P2 = new PointD(3, 4) };
                    var host = new RecordingSessionHost(new RecordingSessionService(), new RecordingRecentProjectService(), new RecordingProjectPackageService())
                    {
                        AllRois = [oldRoi],
                        CurrentRecipeDatumPoints =
                        [
                            new RecipeDatumPoint { Name = "D1", X = 30, Y = 40 },
                            new RecipeDatumPoint { Name = "D2", X = 30, Y = 50 }
                        ]
                    };
                    string recipePath = Path.Combine(rootPath, "unsupported.ivrecipe");
                    MeasurementRecipeService.SaveToFile(recipePath, new MeasurementRecipeDocument
                    {
                        Version = MeasurementRecipeDocument.CurrentVersion,
                        PixelSize = 0.5,
                        PhysicalUnit = "mm",
                        DatumPoints =
                        [
                            new RecipeDatumPoint { Name = "D1", X = 10, Y = 20 },
                            new RecipeDatumPoint { Name = "D2", X = 20, Y = 20 }
                        ],
                        Items =
                        {
                            new MeasurementRecipeItem
                            {
                                Key = "plugin#0",
                                RoiTypeKey = "plugin-roi",
                                Template = new RoiPersistenceData { Type = "plugin-roi", Position = new RoiPersistencePoint { X = 4, Y = 5 } }
                            }
                        }
                    });
                    host.OpenRecipeDialogPath = recipePath;

                    using var controller = CreateController(host, new RecordingPeriodicTaskSchedulerFactory(), rootPath, "document-align-invalid");
                    host.CurrentRecipeDatumPoints =
                    [
                        new RecipeDatumPoint { Name = "D1", X = 30, Y = 40 },
                        new RecipeDatumPoint { Name = "D2", X = 30, Y = 40 }
                    ];
                    controller.LoadAlignedRecipeAsync().GetAwaiter().GetResult();
                    Assert.Null(host.LastReplacedRois);
                    Assert.Same(oldRoi, Assert.Single(host.AllRois));
                    Assert.Equal(1.0, host.PixelSize);

                    host.CurrentRecipeDatumPoints =
                    [
                        new RecipeDatumPoint { Name = "D1", X = 30, Y = 40 },
                        new RecipeDatumPoint { Name = "D2", X = 30, Y = 50 }
                    ];
                    controller.LoadAlignedRecipeAsync().GetAwaiter().GetResult();

                    Assert.Null(host.LastReplacedRois);
                    Assert.Same(oldRoi, Assert.Single(host.AllRois));
                    Assert.Equal(1.0, host.PixelSize);
                    Assert.Contains(host.StatusHints, hint => hint.Contains(UiText.Get("StatusRecipeAlignmentDatumsRequired"), StringComparison.Ordinal));
                    Assert.Contains(host.StatusHints, hint => hint.Contains(UiText.Get("StatusRecipeAlignmentFailed").Split('{')[0], StringComparison.Ordinal));
                }
                finally
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            });
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
                    ShowSaveRecipeDialog = () => host.SaveRecipeDialogPath,
                    ShowOpenRecipeDialog = () => host.OpenRecipeDialogPath,
                    SessionService = host.SessionService,
                    RecentProjectService = host.RecentProjectService,
                    ProjectPackageService = host.ProjectPackageService,
                    GetPluginRegistry = () => host.PluginRegistry,
                    GetAllRois = () => host.AllRois,
                    GetPixelSize = () => host.PixelSize,
                    SetPixelSize = value => host.PixelSize = value,
                    GetPhysicalUnit = () => host.PhysicalUnit,
                    SetPhysicalUnit = value => host.PhysicalUnit = value,
                    GetCalibration = () => host.Calibration,
                    SetCalibration = value => host.Calibration = value,
                    GetRecipeDatumPoints = () => host.RecipeDatumPoints,
                    GetCurrentRecipeDatumPoints = () => host.CurrentRecipeDatumPoints,
                    GetCurrentImageIdentity = () => host.CurrentImageIdentity,
                    SetRecipeDatumPoints = value => host.RecipeDatumPoints = value.ToArray(),
                    GetCurrentViewportState = () => host.CurrentViewportState,
                    TryGetCurrentImagePath = () => host.CurrentImagePath,
                    LoadImageFromFile = host.LoadImageFromFile,
                    ReplaceAllRois = host.ReplaceAllRois,
                    ApplyViewportState = host.ApplyViewportState,
                    DrawRois = host.DrawRois,
                    ShowNonCriticalError = host.ShowNonCriticalError,
                    ShowWarning = host.ShowWarning,
                    ShowStatusHint = (message, _) => host.StatusHints.Add(message),
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
                    GetCalibration = () => host.Calibration,
                    SessionService = host.SessionService,
                    GetPluginRegistry = () => host.PluginRegistry,
                    LogNonCriticalError = host.LogNonCriticalError,
                    ShowStatusHint = (message, _) => host.StatusHints.Add(message)
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

            public string? SaveRecipeDialogPath { get; set; }

            public string? OpenRecipeDialogPath { get; set; }

            public IImageViewerSessionService SessionService { get; }

            public IImageViewerRecentProjectService RecentProjectService { get; }

            public IImageViewerProjectPackageService ProjectPackageService { get; }

            public RoiPluginRegistry PluginRegistry { get; } = RoiPluginRegistry.CreateBuiltIn();

            public IReadOnlyList<RoiBase> AllRois { get; set; } = [];

            public IReadOnlyList<RecipeDatumPoint> RecipeDatumPoints { get; set; } = [];

            public IReadOnlyList<RecipeDatumPoint> CurrentRecipeDatumPoints { get; set; } = [];

            public object CurrentImageIdentity { get; set; } = new object();

            // 与真实控件的默认标定一致（像素尺寸必须为有限正值，快照阶段就会校验）。
            public double PixelSize { get; set; } = 1.0;

            public string PhysicalUnit { get; set; } = "px";

            public CameraCalibration? Calibration { get; set; }

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
                AllRois = rois;
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

            public bool ThrowOnSave { get; set; }

            public ImageViewerSessionData LoadResult { get; set; } = new(
                "session",
                DateTimeOffset.UtcNow,
                null,
                [],
                1,
                "px",
                1,
                0,
                0,
                null);

            public void SaveToFile(string filePath, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry pluginRegistry)
            {
                throw new NotSupportedException();
            }

            public Task SaveToFileAsync(string filePath, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry pluginRegistry, CancellationToken cancellationToken = default)
            {
                if (ThrowOnSave)
                {
                    return Task.FromException(new IOException("disk full"));
                }

                LastSaveFilePath = filePath;
                LastSavePluginRegistry = pluginRegistry;
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, "session");
                return Task.CompletedTask;
            }

            public string SerializeSession(string? sessionName, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry pluginRegistry)
            {
                throw new NotSupportedException();
            }

            public ImageViewerSessionData LoadFromFile(string filePath, RoiPluginRegistry pluginRegistry)
            {
                throw new NotSupportedException();
            }

            public Task<ImageViewerSessionData> LoadFromFileAsync(string filePath, RoiPluginRegistry pluginRegistry, CancellationToken cancellationToken = default)
            {
                LastLoadFilePath = filePath;
                LastLoadPluginRegistry = pluginRegistry;
                return Task.FromResult(LoadResult);
            }

            public ImageViewerSessionData LoadFromJson(string sessionJson, RoiPluginRegistry pluginRegistry, string? sessionBaseDirectory = null)
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
                0,
                null);

            public Task ExportAsync(string packagePath, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry pluginRegistry, CancellationToken cancellationToken = default)
            {
                LastExportFilePath = packagePath;
                LastExportPluginRegistry = pluginRegistry;
                Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
                File.WriteAllText(packagePath, "package");
                return Task.CompletedTask;
            }

            public Task<ImageViewerSessionData> LoadAsync(string packagePath, RoiPluginRegistry pluginRegistry, CancellationToken cancellationToken = default)
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
