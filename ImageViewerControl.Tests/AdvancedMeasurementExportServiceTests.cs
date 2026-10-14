using System.IO;
using System.Text.Json;
using ImageViewer.Core.Measurements;
using ImageViewer.Models;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests;

public class AdvancedMeasurementExportServiceTests
{
    [Fact]
    public void CsvRetainsFailuresSamplesAndEscapesLabels()
    {
        var definition = new AdvancedMeasurementDefinition { Name = "=1+1,\"label\"", Kind = AdvancedMeasurementKind.GapProfile };
        var result = new AdvancedMeasurementEvaluation
        {
            Id = definition.Id, Success = true, Metrics = [new("Minimum", 2, Unit: "mm")], Samples = [2, 3], Stations = [0, 1]
        };
        var failed = new AdvancedMeasurementEvaluation { Id = Guid.NewGuid(), Error = "Missing source" };
        string csv = AdvancedMeasurementExportService.BuildCsv([definition], [result, failed]);
        Assert.Contains("'=1+1,\"\"label\"\"", csv, StringComparison.Ordinal);
        Assert.Contains("Missing source", csv, StringComparison.Ordinal);
        Assert.Equal(6, csv.Split("\r\n", StringSplitOptions.None).Length);
        Assert.Contains("\"Sample\",\"3\",\"mm\",\"1\",\"1\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportIncludesDefinitionsSourceIdentityAndHash()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ImageViewerAdvancedExport-" + Guid.NewGuid());
        try
        {
            var first = new CircleRoi { Center = new(0, 0), Radius = 2 };
            var second = new CircleRoi { Center = new(10, 0), Radius = 2 };
            first.AdvancedMeasurements = [new() { Name = "gap", Kind = AdvancedMeasurementKind.ShortestDistance, Sources = [first.MeasurementId, second.MeasurementId] }];
            string path = Path.Combine(directory, "measurements.csv");
            await AdvancedMeasurementExportService.SaveAsync(path, [first, second], 0.5, "mm");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(path, ".metadata.json")));
            Assert.Equal(3, json.RootElement.GetProperty("results")[0].GetProperty("Metrics")[0].GetProperty("Value").GetDouble());
            Assert.Equal(first.MeasurementId, json.RootElement.GetProperty("definitions")[0].GetProperty("Sources")[0].GetGuid());
            Assert.Equal(64, json.RootElement.GetProperty("csvSha256").GetString()!.Length);
            Assert.Equal(2, json.RootElement.GetProperty("roiDocument").GetProperty("Items").GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CsvIncludesConstructionsAndKeepsSignedNumbersNumeric()
    {
        var construction = new AdvancedMeasurementDefinition { Name = "midpoint", Kind = AdvancedMeasurementKind.Midpoint };
        var distance = new AdvancedMeasurementDefinition { Kind = AdvancedMeasurementKind.ProjectedDistance };
        var constructed = new AdvancedMeasurementEvaluation { Id = construction.Id, Success = true, Geometry = [new(1, 2)] };
        var projected = new AdvancedMeasurementEvaluation { Id = distance.Id, Success = true, Metrics = [new("Distance", -2.5, Unit: "mm")] };
        string csv = AdvancedMeasurementExportService.BuildCsv([construction, distance], [constructed, projected]);
        Assert.Contains("\"Construction\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"-2.5\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("\"'-2.5\"", csv, StringComparison.Ordinal);
    }
}
