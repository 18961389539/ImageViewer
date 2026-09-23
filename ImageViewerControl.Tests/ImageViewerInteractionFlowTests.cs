using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.ViewModels;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    public class ImageViewerInteractionFlowTests
    {
        [Fact]
        public void PointerInteractionFlow_HandleMouseWheel_ZoomsAtMousePosition()
        {
            WpfTestRunner.Run(() =>
            {
                var host = new FakePointerInteractionHost();
                var flow = new PointerInteractionFlow(host);

                flow.HandleMouseWheel(new Point(12, 34), 120);

                Assert.Equal(new Point(12, 34), host.LastZoomPoint);
                Assert.Equal(1.1, host.LastZoomFactor, 3);
            });
        }

        [Fact]
        public void SelectionInteractionFlow_HandleRightClick_SelectsHitRoiAndShowsProperties()
        {
            WpfTestRunner.Run(() =>
            {
                var hitRoi = new LineMeasureRoi();
                var host = new FakeSelectionInteractionHost
                {
                    HitTestResult = hitRoi
                };
                var flow = new SelectionInteractionFlow(host);

                bool handled = flow.HandleRightClick(new Point(8, 9));

                Assert.True(handled);
                Assert.Same(hitRoi, host.SelectedRoi);
                Assert.Equal(1, host.DrawRoisCallCount);
                Assert.Same(hitRoi, host.LastPropertiesDialogRoi);
            });
        }

        [Fact]
        public void EditInteractionFlow_HandleKeyInput_WithShiftArrow_MovesRoiAndRefreshes()
        {
            WpfTestRunner.Run(() =>
            {
                var selectedRoi = new LineMeasureRoi();
                var host = new FakeEditInteractionHost();
                host.ViewModel.SelectedRoi = selectedRoi;
                var flow = new EditInteractionFlow(host);

                bool handled = flow.HandleKeyInput(Key.Right, isCtrlPressed: false, isShiftPressed: true);

                Assert.True(handled);
                Assert.Same(selectedRoi, host.LastMoveRoi);
                Assert.Equal(10, host.LastMoveDx, 3);
                Assert.Equal(0, host.LastMoveDy, 3);
                Assert.Same(selectedRoi, host.LastRefreshedRoi);
                Assert.Equal(1, host.DrawRoisCallCount);
            });
        }

        [Fact]
        public void ViewShortcut_CtrlF_MapsToFitToView()
        {
            bool handled = InteractionController.TryGetViewShortcut(Key.F, isShiftPressed: false, out ImageViewerViewCommand command);

            Assert.True(handled);
            Assert.Equal(ImageViewerViewCommand.FitToView, command);
        }

        [Fact]
        public void ViewShortcut_UnmappedKey_ReturnsFalse()
        {
            bool handled = InteractionController.TryGetViewShortcut(Key.A, isShiftPressed: false, out ImageViewerViewCommand command);

            Assert.False(handled);
        }

        [Fact]
        public void EditInteractionFlow_TryBeginEdit_WhenLockedRoiHit_SelectsWithoutDragging()
        {
            WpfTestRunner.Run(() =>
            {
                var lockedRoi = new LineMeasureRoi { IsLocked = true };
                var host = new FakeEditInteractionHost
                {
                    HitTestResult = lockedRoi
                };
                var flow = new EditInteractionFlow(host);

                bool handled = flow.TryBeginEdit(new Point(4, 5), isRightButtonPressed: false);

                Assert.True(handled);
                Assert.Same(lockedRoi, host.ViewModel.SelectedRoi);
                Assert.Equal(1, host.DrawRoisCallCount);
                Assert.False(host.CaptureRootMouseCalled);
                Assert.False(host.ManipulationState.IsRoiDragging);
            });
        }

        private sealed class FakePointerInteractionHost : IImageViewerPointerInteractionHost
        {
            public ImageViewerInteractionManipulationState ManipulationState { get; } = new();

            public bool IsToolInteractionActive { get; set; }

            public bool IsRootMouseCaptured { get; set; }

            public BitmapSource? AnalysisBitmapSource { get; set; }

            public Point LastZoomPoint { get; private set; }

            public double LastZoomFactor { get; private set; }

            public void CaptureRootMouse()
            {
                IsRootMouseCaptured = true;
            }

            public void ReleaseRootMouse()
            {
                IsRootMouseCaptured = false;
            }

            public void ZoomAt(Point mousePosition, double zoomFactor)
            {
                LastZoomPoint = mousePosition;
                LastZoomFactor = zoomFactor;
            }

            public void TranslateBy(Vector delta)
            {
            }

            public Point SnapPoint(Point point)
            {
                return point;
            }

            public ResizeHandle GetHandleAt(Point point)
            {
                return ResizeHandle.None;
            }

            public int GetPolygonPointIndexAt(Point point)
            {
                return -1;
            }

            public RoiBase? HitTest(Point point)
            {
                return null;
            }

            public void SetCoordinateText(string text)
            {
            }

            public void SetCursor(Cursor cursor)
            {
            }

            public void UpdateCrosshair(double x, double y)
            {
            }
        }

        private sealed class FakeSelectionInteractionHost : IImageViewerSelectionInteractionHost
        {
            public bool IsToolInteractionActive { get; set; }

            public RoiBase? SelectedRoi { get; set; }

            public RoiBase? HitTestResult { get; set; }

            public int DrawRoisCallCount { get; private set; }

            public RoiBase? LastPropertiesDialogRoi { get; private set; }

            public RoiBase? HitTest(Point point)
            {
                return HitTestResult;
            }

            public void ExitCurrentMode()
            {
            }

            public void DrawRois()
            {
                DrawRoisCallCount++;
            }

            public void ShowRoiProperties(RoiBase roi)
            {
                LastPropertiesDialogRoi = roi;
            }
        }

        private sealed class FakeEditInteractionHost : IImageViewerEditInteractionHost
        {
            public ImageViewerViewModel ViewModel { get; } = new(RoiPluginRegistry.CreateBuiltIn());

            public ImageViewerInteractionManipulationState ManipulationState { get; } = new();

            public bool IsRootMouseCaptured { get; set; }

            public bool IsToolInteractionActive { get; set; }

            public bool RemoveSelectedRoiResult { get; set; }

            public List<string> StatusHints { get; } = [];

            public RoiBase? HitTestResult { get; set; }

            public int DrawRoisCallCount { get; private set; }

            public bool CaptureRootMouseCalled { get; private set; }

            public RoiBase? LastRefreshedRoi { get; private set; }

            public RoiBase? LastMoveRoi { get; private set; }

            public double LastMoveDx { get; private set; }

            public double LastMoveDy { get; private set; }

            public void CaptureRootMouse()
            {
                CaptureRootMouseCalled = true;
                IsRootMouseCaptured = true;
            }

            public void ReleaseRootMouse()
            {
                IsRootMouseCaptured = false;
            }

            public bool RemoveSelectedRoi()
            {
                return RemoveSelectedRoiResult;
            }

            public ResizeHandle GetHandleAt(Point point)
            {
                return ResizeHandle.None;
            }

            public int GetPolygonPointIndexAt(Point point)
            {
                return -1;
            }

            public int GetPolygonSegmentAt(Point point)
            {
                return -1;
            }

            public RoiBase? HitTest(Point point)
            {
                return HitTestResult;
            }

            public void TryRefreshCaliperDetection(RoiBase? roi)
            {
                LastRefreshedRoi = roi;
            }

            public void DrawRois()
            {
                DrawRoisCallCount++;
            }

            public void DrawSelectedRoiLayer()
            {
            }

            public void UpdateInfoPanel(bool force)
            {
            }

            public void ExitCurrentMode()
            {
            }

            public void ResizeRoi(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos)
            {
            }

            public IUndoRedoCommand? CreateStateCommand(RoiBase roi, RoiBase oldState, RoiBase newState)
            {
                return null;
            }

            public void MoveRoi(RoiBase roi, double dx, double dy)
            {
                LastMoveRoi = roi;
                LastMoveDx = dx;
                LastMoveDy = dy;
            }

            public void ShowStatusHint(string message, StatusHintKind kind)
            {
                StatusHints.Add(message);
            }
        }

        [Fact]
        public void EditInteractionFlow_EscapeWhileDrawing_ShowsCancellationHint()
        {
            WpfTestRunner.Run(() =>
            {
                var host = new FakeEditInteractionHost { IsToolInteractionActive = true };
                var flow = new EditInteractionFlow(host);

                bool handled = flow.HandleKeyInput(Key.Escape, isCtrlPressed: false, isShiftPressed: false);

                Assert.True(handled);
                Assert.Contains("已取消绘制", host.StatusHints);
            });
        }

        [Fact]
        public void EditInteractionFlow_Undo_ShowsUndoneHint()
        {
            WpfTestRunner.Run(() =>
            {
                var host = new FakeEditInteractionHost();
                host.ViewModel.UndoRedo.Execute(new AddRoiCommand(new LineMeasureRoi(), host.ViewModel));
                var flow = new EditInteractionFlow(host);

                bool handled = flow.HandleKeyInput(Key.Z, isCtrlPressed: true, isShiftPressed: false);

                Assert.True(handled);
                Assert.Contains("已撤销", host.StatusHints);
            });
        }

        [Fact]
        public void EditInteractionFlow_DeleteSelectedRoi_ShowsDeletedHint()
        {
            WpfTestRunner.Run(() =>
            {
                var host = new FakeEditInteractionHost { RemoveSelectedRoiResult = true };
                var flow = new EditInteractionFlow(host);

                bool handled = flow.HandleKeyInput(Key.Delete, isCtrlPressed: false, isShiftPressed: false);

                Assert.True(handled);
                Assert.Contains("已删除所选 ROI，可按 Ctrl+Z 撤销", host.StatusHints);
            });
        }
    }
}