using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Abstractions;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    [Collection(WpfTestCollection.Name)]
    [Trait("Category", "E2E")]
    [Trait("Category", "Wpf")]
    public sealed class ImageViewerEndToEndTests
    {
        [Fact]
        public void LargeImageWorkflow_LoadsPyramidAndUsesTiledViewport()
        {
            WpfTestRunner.RunAsync(async () =>
            {
                using var viewer = new ImageViewer.Controls.ImageViewer();
                var window = new Window
                {
                    Width = 640,
                    Height = 480,
                    Content = viewer
                };

                try
                {
                    window.Show();
                    WpfTestRunner.DrainDispatcher();
                    window.UpdateLayout();

                    BitmapSource image = CreateBitmap(2001, 2001, 127);
                    viewer.SetImage(image);
                    Task prepare = Assert.IsAssignableFrom<Task>(WpfTestRunner.InvokePrivate(viewer, "PrepareAnalysisResourcesAsync", image));
                    await prepare;
                    WpfTestRunner.DrainDispatcher();

                    Assert.True(viewer.RuntimeOptions.EnableImagePyramid);
                    Assert.True(viewer.RuntimeOptions.EnableTiledRendering);
                    Assert.True(viewer.RuntimeOptions.AutoSelectPyramidLevel);
                    Assert.True(viewer._analysisState.PyramidLevels.Count > 1);
                    Assert.True(viewer._analysisState.LastRenderFrame.IsTiled);
                    Assert.InRange(viewer._analysisState.LastRenderFrame.Width, 1, 2001);
                    Assert.InRange(viewer._analysisState.LastRenderFrame.ScaleFactor, 0.0, 1.0);
                }
                finally
                {
                    window.Close();
                    WpfTestRunner.DrainDispatcher();
                }
            });
        }

        [Fact]
        public void AnalysisExportWorkflow_WritesSourceFingerprintAndRoiInputs()
        {
            WpfTestRunner.Run(() =>
            {
                string root = CreateTempRoot();
                try
                {
                    string sourcePath = Path.Combine(root, "source.png");
                    string csvPath = Path.Combine(root, "analysis.csv");
                    BitmapSource image = CreateBitmap(32, 24, 64);
                    SaveBitmap(sourcePath, image);
                    var roi = new CircleRoi
                    {
                        Center = new PointD(12, 10),
                        Radius = 4,
                        Label = "reference"
                    };
                    var settings = new Dictionary<string, string>
                    {
                        ["enableTiledRendering"] = "True",
                        ["pseudoColorPalette"] = "None"
                    };

                    RoiAnalysisExportService.SaveCsvAsync(
                        csvPath,
                        [roi],
                        image,
                        0.25,
                        "mm",
                        calibration: null,
                        exportContext: new RoiAnalysisExportContext(sourcePath, RoiPluginRegistry.CreateBuiltIn(), settings))
                        .GetAwaiter()
                        .GetResult();

                    string metadataPath = Path.ChangeExtension(csvPath, ".metadata.json");
                    Assert.True(File.Exists(csvPath));
                    Assert.True(File.Exists(metadataPath));
                    Assert.Contains("Type,Label", File.ReadAllText(csvPath));

                    using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                    JsonElement rootElement = metadata.RootElement;
                    Assert.Equal(2, rootElement.GetProperty("exportFormatVersion").GetInt32());
                    Assert.Equal(1, rootElement.GetProperty("roiCount").GetInt32());
                    Assert.Equal("mm", rootElement.GetProperty("calibration").GetProperty("physicalUnit").GetString());
                    Assert.Equal("True", rootElement.GetProperty("renderSettings").GetProperty("enableTiledRendering").GetString());
                    Assert.Equal(1, rootElement.GetProperty("roiDocument").GetProperty("Items").GetArrayLength());

                    string expectedSourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
                    Assert.Equal(expectedSourceHash, rootElement.GetProperty("source").GetProperty("sha256").GetString());
                    Assert.False(string.IsNullOrWhiteSpace(rootElement.GetProperty("result").GetProperty("csvSha256").GetString()));
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });
        }

        [Fact]
        public void RecoveryWorkflow_RestoresAutosavedRoiAndKeepsDocumentDirty()
        {
            WpfTestRunner.RunAsync(async () =>
            {
                string root = CreateTempRoot();
                var policy = new LocalAppDataImageViewerSessionStoragePolicy(root, TimeSpan.FromMinutes(5));
                RoiPluginRegistry registry = RoiPluginRegistry.CreateBuiltIn();
                var recoveredRoi = new CircleRoi
                {
                    Center = new PointD(8, 8),
                    Radius = 3,
                    Label = "recovered"
                };
                Directory.CreateDirectory(policy.AutoSaveDirectory);
                string recoveryPath = Path.Combine(policy.AutoSaveDirectory, "autosave.ivsession");
                new ImageViewerSessionService().SaveToFile(
                    recoveryPath,
                    new ImageViewerPersistenceSnapshot(null, [recoveredRoi], 0.5, "mm", 1.25, 4, 6, null),
                    registry);

                var defaultHostServices = ImageViewerHostDefaults.CreateHostServices();
                var hostServices = new ImageViewerHostServices(
                    defaultHostServices.DispatcherTimerFactory,
                    defaultHostServices.RefreshSchedulerFactory,
                    defaultHostServices.LatestTaskSchedulerFactory,
                    defaultHostServices.PeriodicTaskSchedulerFactory,
                    defaultHostServices.AnalysisDiagnostics,
                    policy);
                using var runtimeServices = ImageViewerTestServices.CreateRuntimeServices(sessionStoragePolicy: policy);
                using var host = ImageViewerTestServices.CreateHost(registry, runtimeServices, hostServices);
                using var viewer = host.CreateViewer();

                try
                {
                    Assert.True(viewer.HasRecoverySnapshot);

                    await viewer._controlComposition.SessionController.RecoverLatestAutoSaveAsync();

                    Assert.False(viewer.HasRecoverySnapshot);
                    Assert.True(viewer.IsDirty);
                    Assert.Single(viewer.ViewerState.AllRois);
                    Assert.Equal("recovered", viewer.ViewerState.AllRois.Single().Label);
                    Assert.Equal(0.5, viewer.PixelSize);
                    Assert.Equal("mm", viewer.PhysicalUnit);
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });
        }

        [Fact]
        public void MprWorkflow_NextSliceCommandUpdatesImageAnd3DPlane()
        {
            WpfTestRunner.Run(() =>
            {
                using var viewer = new AdaptiveImageViewer
                {
                    Volume = new VolumeData([
                        CreateBitmap(4, 3, 10),
                        CreateBitmap(4, 3, 40),
                        CreateBitmap(4, 3, 80)])
                };
                viewer.DisplayMode = AdaptiveDisplayMode.Coronal;
                int before = viewer.Volume3DViewer.CurrentCoronalSliceIndex;

                WpfTestRunner.InvokePrivate(viewer, "OnNextMprSliceClick", new Button(), new RoutedEventArgs());

                Assert.Equal(before + 1, viewer.Volume3DViewer.CurrentCoronalSliceIndex);
                Assert.IsType<ImageViewer.Controls.ImageViewer>(viewer.ActiveView);
                Assert.NotNull(viewer.ImageViewer.ImageSource);
            });
        }

        private static string CreateTempRoot()
        {
            string path = Path.Combine(Path.GetTempPath(), $"image-viewer-e2e-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static BitmapSource CreateBitmap(int width, int height, byte value)
        {
            BitmapSource bitmap = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Gray8,
                null,
                Enumerable.Repeat(value, width * height).Select(item => (byte)item).ToArray(),
                width);
            bitmap.Freeze();
            return bitmap;
        }

        private static void SaveBitmap(string path, BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }
    }
}
