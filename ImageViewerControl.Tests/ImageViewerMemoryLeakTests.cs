using System;
using System.Windows;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.ViewModels;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "Leak")]
    [Trait("Category", "Wpf")]
    public class ImageViewerMemoryLeakTests
    {
        /// <summary>
        /// 控件 Dispose 后，且无外部强引用时，应能被 GC 完整回收（无事件/调度器/静态缓存泄漏）。
        /// </summary>
        [Fact]
        public void Dispose_ImageViewer_CanBeGarbageCollected()
        {
            WeakReference? reference = null;
            WpfTestRunner.Run(() =>
            {
                reference = CreateDisposedViewerWeakReference();
            });

            ForceGarbageCollection();

            Assert.False(reference!.IsAlive, "Dispose 后的 ImageViewer 仍被引用，存在内存泄漏。");
        }

        /// <summary>
        /// 仅作冒烟：作用域内的控件实例仍被强引用，弱引用 IsAlive 应当为 true，
        /// 验证弱引用测试机制本身不会误报（同 GC 测试搭配使用）。
        /// </summary>
        [Fact]
        public void AliveViewer_RemainsReachable_BeforeGc()
        {
            WpfTestRunner.Run(() =>
            {
                var viewer = new ImageViewer.Controls.ImageViewer();
                var reference = new WeakReference(viewer);
                Assert.True(reference.IsAlive, "作用域内强引用仍存活，弱引用 IsAlive 应为 true。");
            });
        }

        /// <summary>
        /// 撤销历史清空逻辑由命令层与会话/ROI 控制器测试覆盖（见 ImageViewerSessionControllerTests）；
        /// 此处验证 UndoRedoManager.Clear 会清空两个栈，作为长期运行内存有界性的底层保证。
        /// </summary>
        [Fact]
        public void UndoRedoManager_Clear_EmptiesStacks()
        {
            WpfTestRunner.Run(() =>
            {
                var manager = new UndoRedoManager();
                var viewModel = new ImageViewerViewModel(RoiPluginRegistry.CreateBuiltIn());
                manager.Execute(new AddRoiCommand(new LineMeasureRoi(), viewModel));
                Assert.True(manager.CanUndo);

                manager.Clear();

                Assert.False(manager.CanUndo);
                Assert.False(manager.CanRedo);
            });
        }

        private static WeakReference CreateDisposedViewerWeakReference()
        {
            var viewer = new ImageViewer.Controls.ImageViewer();
            viewer.Dispose();
            return new WeakReference(viewer);
        }

        private static WeakReference CreateAliveViewerWeakReference()
        {
            var viewer = new ImageViewer.Controls.ImageViewer();
            return new WeakReference(viewer);
        }

        private static void ForceGarbageCollection()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
    }
}