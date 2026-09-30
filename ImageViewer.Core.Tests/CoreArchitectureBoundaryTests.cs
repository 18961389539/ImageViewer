using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImageViewer.Common;
using ImageViewer.Core.Measurements;
using ImageViewer.Core.Volume;
using ImageViewer.Models;
using ImageViewer.Core.Persistence;
using Xunit;

namespace ImageViewer.Core.Tests;

public class CoreArchitectureBoundaryTests
{
    [Fact]
    public void CoreAssembly_DoesNotReferenceWpfOrRenderingBackends()
    {
        string[] forbiddenAssemblyNames =
        [
            "PresentationCore",
            "PresentationFramework",
            "WindowsBase",
            "HelixToolkit",
            "JLVision"
        ];

        string[] references = typeof(MeasurementStatistics).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            references,
            reference => forbiddenAssemblyNames.Any(forbidden =>
            reference.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void CoreProject_DoesNotOptIntoWpfOrReferenceUiProjects()
    {
        string projectDirectory = FindProjectDirectory("ImageViewer.Core.csproj");
        string projectFile = File.ReadAllText(Path.Combine(projectDirectory, "ImageViewer.Core.csproj"));

        Assert.DoesNotContain("<UseWPF>true", projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PresentationCore", projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PresentationFramework", projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HelixToolkit", projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JLVision", projectFile, StringComparison.OrdinalIgnoreCase);

        string[] sourceFiles = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) && !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (string sourceFile in sourceFiles)
        {
            string source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain("using System.Windows", source, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Windows.Media", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CoreNamespaces_AreLayeredInsteadOfBorrowingUiOrPluginNamespaces()
    {
        // Core 的持久化契约曾借用 ImageViewer.Services / ImageViewer.Plugins 命名空间，
        // 造成“领域层反向依赖插件/UI 语义”的错觉。归位后这些命名空间在 Core 里必须彻底消失。
        string[] forbiddenNamespaces =
        [
            "ImageViewer.Services",
            "ImageViewer.Plugins",
            "ImageViewer.Controls",
            "ImageViewer.ViewModels",
            "ImageViewer.Localization"
        ];

        string[] violations = typeof(MeasurementStatistics).Assembly
            .GetTypes()
            .Select(type => type.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .Distinct()
            .Where(ns => forbiddenNamespaces.Any(forbidden => ns != null && (ns == forbidden || ns.StartsWith(forbidden + ".", StringComparison.Ordinal))))
            .ToArray()!;

        Assert.Empty(violations);
    }

    [Fact]
    public void SharedContracts_AreOwnedByCoreAssembly()
    {
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(PointD).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(RoiPersistenceData).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(ImageAnalysisQualityProfile).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(RoiInspectionResult).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(RoiStatistics).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(BlobFeature).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(CameraCalibration).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(RoiDocument).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(ImageViewerSessionDocument).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(VolumeSliceProvenance).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(VolumeSliceOrderMetadata).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(VolumeSliceOrientation).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(ImageViewer.Core.Analysis.CircularCaliperDetectionResult).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(ImageViewer.Core.Analysis.LineCaliperDetectionResult).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(ImageViewer.Core.Analysis.LineMeasureGradientDetectionResult).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(DetectedLineSegment).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(LineFitGeometry).Assembly);

        // 2026-09-30 下沉：ROI 领域基础类型不再属于控件程序集，插件契约才可能脱离 WPF。
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(RoiBase).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(RoiColor).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(RoiColors).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(BaseViewModel).Assembly);
        Assert.Equal(typeof(MeasurementStatistics).Assembly, typeof(RoiDisplayNameResolver).Assembly);
    }

    [Fact]
    public void SessionSchema_CanRoundTripWithoutWpf()
    {
        var document = new ImageViewerSessionDocument
        {
            Version = 2,
            SessionName = "core",
            RoiDocument = new RoiDocument { PixelSize = 0.25, PhysicalUnit = "mm" },
            Calibration = new CameraCalibration { PrincipalX = 12, PrincipalY = 8, NormalizationRadius = 10 }
        };

        string json = JsonSerializer.Serialize(document, ImageViewerCoreJsonSerializationContext.Default.ImageViewerSessionDocument);
        ImageViewerSessionDocument roundTrip = JsonSerializer.Deserialize(json, ImageViewerCoreJsonSerializationContext.Default.ImageViewerSessionDocument)!;

        Assert.Equal("core", roundTrip.SessionName);
        Assert.Equal(0.25, roundTrip.RoiDocument!.PixelSize);
        Assert.Equal("mm", roundTrip.RoiDocument.PhysicalUnit);
        Assert.Equal(12, roundTrip.Calibration!.PrincipalX);
    }

    private static string FindProjectDirectory(string projectFileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "ImageViewer.Core", projectFileName);
            if (File.Exists(candidate))
            {
                return directory.FullName + "\\ImageViewer.Core";
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(projectFileName);
    }
}
