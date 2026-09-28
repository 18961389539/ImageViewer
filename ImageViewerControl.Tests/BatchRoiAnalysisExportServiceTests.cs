using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class BatchRoiAnalysisExportServiceTests
    {
        [Fact]
        public async Task ExportAsync_CombinesRowsAndKeepsPerFileFailures()
        {
            string root = Path.Combine(Path.GetTempPath(), $"image-viewer-batch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                string firstPath = Path.Combine(root, "first.png");
                SaveBitmap(firstPath, CreateBitmap(20, 16, 80));
                string missingPath = Path.Combine(root, "missing.png");
                string csvPath = Path.Combine(root, "batch.csv");
                var roi = new CircleRoi { Center = new PointD(8, 7), Radius = 3, Label = "target" };

                BatchRoiAnalysisExportSummary summary = await BatchRoiAnalysisExportService.ExportAsync(
                    csvPath,
                    [firstPath, missingPath],
                    [roi],
                    pixelSize: 1,
                    physicalUnit: null);

                Assert.Equal(2, summary.RequestedFileCount);
                Assert.Equal(1, summary.ProcessedFileCount);
                Assert.Equal(1, summary.FailedFileCount);
                Assert.Equal(2, summary.ExportedRowCount);
                Assert.Single(summary.Errors);
                string csv = File.ReadAllText(csvPath);
                string metadataPath = Path.ChangeExtension(csvPath, ".metadata.json");
                Assert.True(File.Exists(metadataPath));
                Assert.Contains("Source,Status,Error,Type,Label", csv);
                Assert.Contains("first.png", csv);
                Assert.Contains("Geometry", csv);
                Assert.Contains("missing.png", csv);
                Assert.Contains("InputMissing", csv);
                Assert.Contains("Failed", csv);
                Assert.Contains("NotEvaluable", csv);

                using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                JsonElement inputRecords = metadata.RootElement.GetProperty("inputRecords");
                Assert.Equal(2, inputRecords.GetArrayLength());
                JsonElement firstRecord = inputRecords.EnumerateArray().First(record =>
                    record.GetProperty("path").GetString()!.EndsWith("first.png", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("Processed", firstRecord.GetProperty("status").GetString());
                Assert.Equal(20, firstRecord.GetProperty("pixelWidth").GetInt32());
                Assert.Equal(16, firstRecord.GetProperty("pixelHeight").GetInt32());
                Assert.False(string.IsNullOrWhiteSpace(firstRecord.GetProperty("sha256").GetString()));
                JsonElement missingRecord = inputRecords.EnumerateArray().First(record =>
                    record.GetProperty("path").GetString()!.EndsWith("missing.png", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("InputMissing", missingRecord.GetProperty("status").GetString());
                Assert.Contains("找不到输入图像。", missingRecord.GetProperty("error").GetString());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task ExportAsync_EmitsRowsAndTraceForUnreadableInput()
        {
            string root = Path.Combine(Path.GetTempPath(), $"image-viewer-batch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                string invalidPath = Path.Combine(root, "invalid.png");
                File.WriteAllText(invalidPath, "not an image");
                string csvPath = Path.Combine(root, "batch.csv");
                var rois = new RoiBase[]
                {
                    new CircleRoi { Center = new PointD(8, 7), Radius = 3, Label = "target" },
                    new CircleRoi { Center = new PointD(4, 5), Radius = 2, Label = "second" }
                };

                BatchRoiAnalysisExportSummary summary = await BatchRoiAnalysisExportService.ExportAsync(
                    csvPath,
                    [invalidPath],
                    rois,
                    pixelSize: 1,
                    physicalUnit: null);

                Assert.Equal(1, summary.RequestedFileCount);
                Assert.Equal(0, summary.ProcessedFileCount);
                Assert.Equal(1, summary.FailedFileCount);
                Assert.Equal(2, summary.ExportedRowCount);
                string csv = File.ReadAllText(csvPath);
                Assert.Equal(2, csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length - 1);
                Assert.Equal(2, csv.Split("DecodeFailed", StringSplitOptions.None).Length - 1);

                using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(csvPath, ".metadata.json")));
                JsonElement record = Assert.Single(metadata.RootElement.GetProperty("inputRecords").EnumerateArray());
                Assert.Equal("DecodeFailed", record.GetProperty("status").GetString());
                Assert.Equal(2, record.GetProperty("rowCount").GetInt32());
                Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("sha256").GetString()));
                Assert.True(record.GetProperty("lengthBytes").GetInt64() > 0);
                Assert.False(record.GetProperty("error").GetString() is null or "");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task ExportAsync_ClearsTemplateDetectionBeforeEachImage()
        {
            string root = Path.Combine(Path.GetTempPath(), $"image-viewer-batch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                string imagePath = Path.Combine(root, "blank.png");
                SaveBitmap(imagePath, CreateBitmap(40, 30, 100));
                string csvPath = Path.Combine(root, "batch.csv");
                var roi = new CaliperMeasureRoi
                {
                    P1 = new PointD(10, 15),
                    P2 = new PointD(20, 15),
                    Tolerance = new MeasurementTolerance { Nominal = 10, TolerancePlus = 0.1, ToleranceMinus = 0.1 }
                };
                roi.SetDetectedEdges(new PointD(10, 15), new PointD(10, 20), new PointD(20, 15), new PointD(20, 20));
                roi.ApplyWidthMeasurement(new CaliperWidthMeasurementResult([10], centerDistance: 10));

                await BatchRoiAnalysisExportService.ExportAsync(
                    csvPath,
                    [imagePath],
                    [roi],
                    pixelSize: 1,
                    physicalUnit: "mm");

                string csv = File.ReadAllText(csvPath);
                Assert.Contains("DetectionFailed", csv);
                Assert.Contains("\"NotMeasured\",\"NotEvaluable\",,10", csv, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task ExportAsync_KeepsMalformedPathAsPerItemFailure()
        {
            string root = Path.Combine(Path.GetTempPath(), $"image-viewer-batch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                string csvPath = Path.Combine(root, "batch.csv");
                string malformedPath = "bad\0path.png";
                BatchRoiAnalysisExportSummary summary = await BatchRoiAnalysisExportService.ExportAsync(
                    csvPath,
                    [malformedPath],
                    [new CircleRoi { Label = "target" }],
                    pixelSize: 1,
                    physicalUnit: "px");

                Assert.Equal(1, summary.RequestedFileCount);
                Assert.Equal(1, summary.FailedFileCount);
                Assert.Contains("InputMissing", File.ReadAllText(csvPath));
                using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(csvPath, ".metadata.json")));
                Assert.Equal(malformedPath, Assert.Single(metadata.RootElement.GetProperty("inputRecords").EnumerateArray()).GetProperty("path").GetString());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task ExportAsync_PreservesDuplicateRequestedInputsForTraceability()
        {
            string root = Path.Combine(Path.GetTempPath(), $"image-viewer-batch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                string imagePath = Path.Combine(root, "same.png");
                SaveBitmap(imagePath, CreateBitmap(8, 8, 120));
                string csvPath = Path.Combine(root, "batch.csv");

                BatchRoiAnalysisExportSummary summary = await BatchRoiAnalysisExportService.ExportAsync(
                    csvPath,
                    [imagePath, imagePath],
                    [new CircleRoi { Label = "target" }],
                    pixelSize: 1,
                    physicalUnit: "px");

                Assert.Equal(2, summary.RequestedFileCount);
                Assert.Equal(2, summary.ProcessedFileCount);
                Assert.Equal(2, summary.ExportedRowCount);
                using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(csvPath, ".metadata.json")));
                Assert.Equal(2, metadata.RootElement.GetProperty("inputRecords").GetArrayLength());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static BitmapSource CreateBitmap(int width, int height, byte value)
        {
            return BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Gray8,
                null,
                Enumerable.Repeat(value, width * height).ToArray(),
                width);
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
