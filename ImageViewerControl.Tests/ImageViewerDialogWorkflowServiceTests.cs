using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ImageViewer.Abstractions;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.ViewModels;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class ImageViewerDialogWorkflowServiceTests
    {
        [Fact]
        public void ShowRoiLabelDialog_UsesExistingLabelAsDefault()
        {
            var state = new WorkflowState();
            var adapter = new FakeDialogWorkflowAdapter();
            var service = new ImageViewerDialogWorkflowService(CreateDependencies(state), adapter);
            var roi = new LineMeasureRoi { Label = "original" };
            adapter.LabelResult = "updated";

            string? result = service.ShowRoiLabelDialog(roi);

            Assert.Equal("updated", result);
            Assert.Equal("original", adapter.LastDefaultValue);
        }

        [Fact]
        public void CalibrateSelectedRoi_WithLineMeasureRoi_AppliesCalibration()
        {
            var state = new WorkflowState();
            var adapter = new FakeDialogWorkflowAdapter();
            var service = new ImageViewerDialogWorkflowService(CreateDependencies(state), adapter);
            var roi = new LineMeasureRoi
            {
                P1 = new Point(0, 0),
                P2 = new Point(10, 0)
            };
            adapter.CalibrationResult = new CalibrationDialogResult(25d, "mm", 0.01, -0.002);

            service.CalibrateSelectedRoi(roi);

            Assert.Equal(2.5d, state.PixelSize, 5);
            Assert.Equal("mm", state.PhysicalUnit);
            Assert.Equal(0.01, state.K1, 6);
            Assert.Equal(-0.002, state.K2, 6);
        }

        [Fact]
        public async Task OpenImageAsync_AfterSuccessfulLoad_ClearsUndoHistory()
        {
            string imagePath = Path.Combine(Path.GetTempPath(), $"imageviewer-test-{Guid.NewGuid():N}.png");
            try
            {
                File.WriteAllBytes(imagePath, TinyPng);
                var state = new WorkflowState();
                var adapter = new FakeDialogWorkflowAdapter { OpenImageResult = imagePath };
                var service = new ImageViewerDialogWorkflowService(CreateDependencies(state), adapter);

                await service.OpenImageAsync();

                Assert.NotNull(state.LastImage);
                Assert.Equal(1, state.ClearUndoHistoryCount);
                Assert.Equal(100, state.LastLoadState!.Value.Progress);
            }
            finally
            {
                File.Delete(imagePath);
            }
        }

        private static ImageViewerDialogWorkflowDependencies CreateDependencies(WorkflowState state)
        {
            return new ImageViewerDialogWorkflowDependencies
            {
                ImageLoading = new ImageViewerDialogImageLoadWorkflow
                {
                    GetRetryCount = () => state.ImageLoadRetryCount,
                    GetRetryDelayMilliseconds = () => state.ImageLoadRetryDelayMilliseconds,
                    SetImage = source => state.LastImage = source,
                    SetImageLoadState = (isLoading, statusText, progress, canRetry) => state.LastLoadState = (isLoading, statusText, progress, canRetry),
                    FitToView = () => state.FitToViewCount++,
                    ClearUndoHistory = () => state.ClearUndoHistoryCount++,
                    ShowNonCriticalError = (title, message, ex) => state.Errors.Add((title, message, ex))
                },
                RoiEditing = new ImageViewerDialogRoiWorkflow
                {
                    GetPluginRegistry = () => state.PluginRegistry,
                    DrawRois = () => state.DrawRoiCount++,
                    DrawSelectedRoiLayer = () => state.DrawSelectedRoiLayerCount++,
                    HandleRoiEdited = roi => state.EditedRois.Add(roi),
                    CreateStateCommand = state.CreateStateCommand,
                    ExecuteUndoRedoCommand = command => state.ExecutedCommands.Add(command),
                    TryApplyCaliperDetection = roi =>
                    {
                        state.CaliperDetectionCount++;
                        return true;
                    },
                    TryApplyLineCaliperDetection = roi =>
                    {
                        state.LineCaliperDetectionCount++;
                        return true;
                    },
                    TryApplyCircularCaliperDetection = roi =>
                    {
                        state.CircularCaliperDetectionCount++;
                        return true;
                    }
                },
                Calibration = new ImageViewerDialogCalibrationWorkflow
                {
                    GetPhysicalUnit = () => state.PhysicalUnit,
                    ApplyCalibration = (pixelSize, unit, k1, k2) =>
                    {
                        state.PixelSize = pixelSize;
                        state.PhysicalUnit = unit;
                        state.K1 = k1;
                        state.K2 = k2;
                    }
                }
            };
        }

        private sealed class WorkflowState
        {
            public RoiPluginRegistry PluginRegistry { get; } = RoiPluginRegistry.CreateBuiltIn();

            public double PixelSize { get; set; } = 1.0;

            public string PhysicalUnit { get; set; } = "px";

            public double K1 { get; set; }

            public double K2 { get; set; }

            public int ImageLoadRetryCount { get; set; } = 2;

            public int ImageLoadRetryDelayMilliseconds { get; set; } = 250;

            public ImageSource? LastImage { get; set; }

            public (bool IsLoading, string StatusText, double Progress, bool CanRetry)? LastLoadState { get; set; }

            public int FitToViewCount { get; set; }

            public int ClearUndoHistoryCount { get; set; }

            public int DrawRoiCount { get; set; }

            public int DrawSelectedRoiLayerCount { get; set; }

            public int CaliperDetectionCount { get; set; }

            public int LineCaliperDetectionCount { get; set; }

            public int CircularCaliperDetectionCount { get; set; }

            public List<RoiBase> EditedRois { get; } = [];

            public List<IUndoRedoCommand> ExecutedCommands { get; } = [];

            public List<(string Title, string Message, Exception Exception)> Errors { get; } = [];

            public Func<RoiBase, RoiBase, RoiBase, IUndoRedoCommand?> CreateStateCommand { get; set; } = (_, _, _) => null;
        }

        private sealed class FakeDialogWorkflowAdapter : IImageViewerDialogWorkflowAdapter
        {
            public string? LabelResult { get; set; }

            public string? LastDefaultValue { get; private set; }

            public CalibrationDialogResult? CalibrationResult { get; set; }

            public string? ShowOpenImageDialog() => OpenImageResult;

            public string? OpenImageResult { get; set; }

            public string? ShowTextInput(string message, string defaultValue)
            {
                LastDefaultValue = defaultValue;
                return LabelResult;
            }

            public string? ShowSaveRoiDialog() => null;

            public string? ShowOpenRoiDialog() => null;

            public string? ShowSaveSessionDialog() => null;

            public string? ShowOpenSessionDialog() => null;

            public string? ShowSaveProjectPackageDialog() => null;

            public string? ShowSaveSnapshotDialog() => null;

            public string? ShowSaveAnalysisCsvDialog() => null;

            public CalibrationDialogResult? ShowCalibrationDialog(string currentUnit) => CalibrationResult;

            public CaliperMeasureRoi? ShowLineMeasureCaliperSettingsDialog(CaliperMeasureRoi roi, Action<CaliperMeasureRoi>? previewAction = null) => null;

            public LineCaliperMeasureRoi? ShowLineCaliperSettingsDialog(LineCaliperMeasureRoi roi, Action<LineCaliperMeasureRoi>? previewAction = null) => null;

            public CircularCaliperMeasureRoi? ShowCircularCaliperSettingsDialog(CircularCaliperMeasureRoi roi, Action<CircularCaliperMeasureRoi>? previewAction = null) => null;

            public void ShowPropertyEditor(string title, FrameworkElement editor) { }

            public void ShowReadOnlyText(string title, string text) { }

            public void ShowWarning(string title, string message) { }
        }

        /// <summary>1x1 透明 PNG，用于图像加载测试。</summary>
        private static readonly byte[] TinyPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
    }
}