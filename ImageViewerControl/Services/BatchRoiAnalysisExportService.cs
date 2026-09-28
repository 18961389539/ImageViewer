using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    /// <summary>
    /// 多图复用当前 ROI 模板执行测量并合并导出。
    /// Chinese: 几何 ROI 直接复用；卡尺 ROI 会在每张图上重新检测，单张失败不会中断整个批次，
    /// 且缺失或不可读输入仍会为每个 ROI 输出带错误状态的结果行。
    /// English: Reuses the current ROI templates across images; caliper ROIs are re-detected per image and
    /// an individual failure does not abort the batch. Missing or unreadable inputs still produce one
    /// error row per ROI so the export remains auditable.
    /// </summary>
    public static class BatchRoiAnalysisExportService
    {
        private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

        public static Task<BatchRoiAnalysisExportSummary> ExportAsync(
            string outputPath,
            IEnumerable<string> imagePaths,
            IEnumerable<RoiBase> roiTemplates,
            double pixelSize,
            string? physicalUnit,
            CameraCalibration? calibration = null,
            RoiPluginRegistry? pluginRegistry = null,
            IReadOnlyDictionary<string, string>? renderSettings = null,
            CancellationToken cancellationToken = default)
        {
            return ExportAsync(outputPath, imagePaths, roiTemplates, pixelSize, physicalUnit, calibration, pluginRegistry, renderSettings, qualityProfile: null, cancellationToken);
        }

        public static async Task<BatchRoiAnalysisExportSummary> ExportAsync(
            string outputPath,
            IEnumerable<string> imagePaths,
            IEnumerable<RoiBase> roiTemplates,
            double pixelSize,
            string? physicalUnit,
            CameraCalibration? calibration,
            RoiPluginRegistry? pluginRegistry,
            IReadOnlyDictionary<string, string>? renderSettings,
            ImageAnalysisQualityProfile? qualityProfile,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentNullException.ThrowIfNull(imagePaths);
            ArgumentNullException.ThrowIfNull(roiTemplates);
            ImageViewerValidation.ValidatePixelSize(pixelSize);
            ImageAnalysisQualityProfile quality = qualityProfile ?? ImageAnalysisQualityProfile.Default;
            quality.Validate();

            string[] sources = imagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .ToArray();
            RoiBase[] templates = roiTemplates.Select(roi => roi.Clone()).ToArray();
            if (sources.Length == 0)
            {
                throw new ArgumentException("至少需要一张输入图像。", nameof(imagePaths));
            }

            var rows = new List<RoiAnalysisBatchCsvRow>();
            var errors = new List<string>();
            var inputTraces = new List<BatchInputTrace>(sources.Length);
            int processedFileCount = 0;
            int decodedFileCount = 0;
            int fullySuccessfulFileCount = 0;
            int partiallySuccessfulFileCount = 0;
            int allRoiFailedFileCount = 0;
            int inputFailedFileCount = 0;

            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                string sourcePath = sources[sourceIndex];
                cancellationToken.ThrowIfCancellationRequested();
                BatchInputTrace inputTrace = await CaptureInputTraceAsync(sourceIndex, sourcePath, cancellationToken).ConfigureAwait(false);
                inputTraces.Add(inputTrace);
                bool bitmapLoaded = false;
                try
                {
                    if (!inputTrace.Exists)
                    {
                        throw new FileNotFoundException("找不到输入图像。", sourcePath);
                    }

                    BitmapSource bitmap = await LoadBitmapAsync(sourcePath, cancellationToken).ConfigureAwait(false);
                    bitmapLoaded = true;
                    decodedFileCount++;
                    inputTrace.PixelWidth = bitmap.PixelWidth;
                    inputTrace.PixelHeight = bitmap.PixelHeight;
                    RoiAnalysisBatchCsvRow[] sourceRows = await Task.Run(
                        () => AnalyzeSource(sourcePath, bitmap, templates, pixelSize, physicalUnit, calibration, quality, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    rows.AddRange(sourceRows);
                    inputTrace.RowCount = sourceRows.Length;
                    inputTrace.RowErrors.AddRange(sourceRows
                        .Where(row => !string.IsNullOrWhiteSpace(row.Error))
                        .Select(row => $"{row.Roi.Label}: {row.Error}"));
                    bool allRowsSuccessful = sourceRows.All(row => string.IsNullOrWhiteSpace(row.Error));
                    bool anyRowsSuccessful = sourceRows.Any(row => string.IsNullOrWhiteSpace(row.Error));
                    if (allRowsSuccessful)
                    {
                        fullySuccessfulFileCount++;
                        processedFileCount++;
                        inputTrace.Status = "Processed";
                    }
                    else if (anyRowsSuccessful)
                    {
                        partiallySuccessfulFileCount++;
                        inputTrace.Status = "PartiallyProcessed";
                        inputTrace.Error = "部分 ROI 分析失败。";
                    }
                    else
                    {
                        allRoiFailedFileCount++;
                        inputTrace.Status = "AnalysisFailed";
                        inputTrace.Error = "所有 ROI 分析失败。";
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    string status = bitmapLoaded ? "AnalysisFailed" : File.Exists(sourcePath) ? "DecodeFailed" : "InputMissing";
                    string error = ex.Message;
                    inputTrace.Status = status;
                    inputTrace.Error = error;
                    inputTrace.RowCount = templates.Length;
                    rows.AddRange(CreateFailureRows(sourcePath, status, error, templates, pixelSize, physicalUnit, calibration));
                    errors.Add($"{Path.GetFileName(sourcePath)}: {error}");
                    if (bitmapLoaded)
                    {
                        allRoiFailedFileCount++;
                    }
                    else
                    {
                        inputFailedFileCount++;
                    }
                }
            }

            string csv = RoiAnalysisExportService.BuildBatchCsv(rows);
            await ImageViewerAtomicFile.WriteAllTextAsync(outputPath, csv, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            string metadataPath = Path.ChangeExtension(outputPath, ".metadata.json");
            string csvHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv))).ToLowerInvariant();
            var metadata = new Dictionary<string, object?>
            {
                ["exportFormatVersion"] = 2,
                ["batch"] = true,
                ["exportedAtUtc"] = DateTimeOffset.UtcNow,
                ["result"] = new Dictionary<string, object?>
                {
                    ["csvSha256"] = csvHash,
                    ["requestedFileCount"] = sources.Length,
                    ["processedFileCount"] = processedFileCount,
                    ["failedFileCount"] = sources.Length - processedFileCount,
                    ["decodedFileCount"] = decodedFileCount,
                    ["fullySuccessfulFileCount"] = fullySuccessfulFileCount,
                    ["partiallySuccessfulFileCount"] = partiallySuccessfulFileCount,
                    ["allRoiFailedFileCount"] = allRoiFailedFileCount,
                    ["inputFailedFileCount"] = inputFailedFileCount,
                    ["exportedRowCount"] = rows.Count,
                    ["errors"] = errors
                },
                // Keep the original path-only field for consumers of metadata version 2.
                ["inputs"] = sources,
                // Detailed records provide an auditable entry for every requested input,
                // including missing, unreadable, and successfully processed files.
                ["inputRecords"] = inputTraces.Select(trace => trace.ToMetadata()).ToArray(),
                ["calibration"] = new Dictionary<string, object?>
                {
                    ["pixelSize"] = pixelSize,
                    ["physicalUnit"] = string.IsNullOrWhiteSpace(physicalUnit) ? "px" : physicalUnit,
                    ["camera"] = calibration
                },
                ["qualityThresholds"] = quality,
                ["roiCount"] = templates.Length,
                ["roiTypes"] = templates
                    .GroupBy(roi => roi.RoiTypeName)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                ["renderSettings"] = renderSettings,
                ["hasRoiPersistenceRegistry"] = pluginRegistry != null
            };
            await ImageViewerAtomicFile.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, MetadataJsonOptions), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            return new BatchRoiAnalysisExportSummary(
                sources.Length,
                processedFileCount,
                sources.Length - processedFileCount,
                rows.Count,
                errors)
            {
                DecodedFileCount = decodedFileCount,
                FullySuccessfulFileCount = fullySuccessfulFileCount,
                PartiallySuccessfulFileCount = partiallySuccessfulFileCount,
                AllRoiFailedFileCount = allRoiFailedFileCount,
                InputFailedFileCount = inputFailedFileCount
            };
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                // Keep malformed paths as traceable input records; the per-file loop
                // will classify them as InputMissing/DecodeFailed instead of aborting the batch.
                return path;
            }
            catch (NotSupportedException)
            {
                return path;
            }
        }

        private static RoiAnalysisBatchCsvRow[] AnalyzeSource(
            string sourcePath,
            BitmapSource bitmap,
            IReadOnlyList<RoiBase> templates,
            double pixelSize,
            string? physicalUnit,
            CameraCalibration? calibration,
            ImageAnalysisQualityProfile quality,
            CancellationToken cancellationToken)
        {
            var rows = new RoiAnalysisBatchCsvRow[templates.Count];
            for (int i = 0; i < templates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RoiBase roi = templates[i].Clone();
                string status = "Geometry";
                string? error = null;

                try
                {
                    ClearPreviousDetectionState(roi);
                    bool measured = TryDetectRoi(bitmap, roi, quality);
                    if (roi is CaliperMeasureRoi or LineCaliperMeasureRoi or CircularCaliperMeasureRoi)
                    {
                        status = measured ? "Measured" : "DetectionFailed";
                        error = measured ? null : "未找到足够可靠的边缘。";
                    }
                }
                catch (Exception ex)
                {
                    status = "DetectionFailed";
                    error = ex.Message;
                }

                rows[i] = new RoiAnalysisBatchCsvRow(sourcePath, status, error, roi, bitmap, pixelSize, physicalUnit, calibration);
            }

            return rows;
        }

        private static void ClearPreviousDetectionState(RoiBase roi)
        {
            switch (roi)
            {
                case CaliperMeasureRoi dualEdge:
                    dualEdge.ClearDetectedEdges();
                    break;
                case LineCaliperMeasureRoi line:
                    line.ClearDetectedLine();
                    break;
                case CircularCaliperMeasureRoi circular:
                    circular.ClearDetectedEdges();
                    break;
            }
        }

        private static RoiAnalysisBatchCsvRow[] CreateFailureRows(
            string sourcePath,
            string status,
            string error,
            IReadOnlyList<RoiBase> templates,
            double pixelSize,
            string? physicalUnit,
            CameraCalibration? calibration)
        {
            return templates
                .Select(template => new RoiAnalysisBatchCsvRow(
                    sourcePath,
                    status,
                    error,
                    template.Clone(),
                    Bitmap: null,
                    pixelSize,
                    physicalUnit,
                    calibration))
                .ToArray();
        }

        private static bool TryDetectRoi(BitmapSource bitmap, RoiBase roi, ImageAnalysisQualityProfile quality)
        {
            switch (roi)
            {
                case CaliperMeasureRoi dualEdge when ImageAnalysisService.TryDetectLineMeasureEdges(bitmap, dualEdge, out LineMeasureGradientDetectionResult dualResult, quality):
                    RoiDetectionResultMapper.Apply(dualEdge, dualResult);
                    return true;
                case LineCaliperMeasureRoi line when ImageAnalysisService.TryDetectLineCaliperEdges(bitmap, line, out LineCaliperDetectionResult lineResult, quality):
                    RoiDetectionResultMapper.Apply(line, lineResult);
                    return true;
                case CircularCaliperMeasureRoi circular when ImageAnalysisService.TryDetectCircularCaliperEdges(bitmap, circular, out CircularCaliperDetectionResult circularResult, quality):
                    RoiDetectionResultMapper.Apply(circular, circularResult, quality);
                    return true;
                case CaliperMeasureRoi:
                case LineCaliperMeasureRoi:
                case CircularCaliperMeasureRoi:
                    return false;
                default:
                    return true;
            }
        }

        private static Task<BitmapSource> LoadBitmapAsync(string path, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("找不到输入图像。", path);
                }

                using FileStream stream = File.OpenRead(path);
                BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return (BitmapSource)frame;
            }, cancellationToken);
        }

        private static async Task<BatchInputTrace> CaptureInputTraceAsync(int inputIndex, string path, CancellationToken cancellationToken)
        {
            var trace = new BatchInputTrace(inputIndex, path);
            if (!File.Exists(path))
            {
                trace.Status = "InputMissing";
                trace.Error = "找不到输入图像。";
                return trace;
            }

            trace.Exists = true;
            try
            {
                FileInfo info = new(path);
                trace.LengthBytes = info.Length;
                trace.LastWriteTimeUtc = info.LastWriteTimeUtc;
                await using FileStream stream = File.OpenRead(path);
                byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                trace.Sha256 = Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Metadata collection must not hide the actual decode result. The
                // failure is retained alongside the eventual input status.
                trace.MetadataError = ex.Message;
            }

            return trace;
        }

        private sealed class BatchInputTrace
        {
            public BatchInputTrace(int inputIndex, string path)
            {
                InputIndex = inputIndex;
                Path = path;
            }

            public int InputIndex { get; }

            public string Path { get; }

            public bool Exists { get; set; }

            public string Status { get; set; } = "Pending";

            public string? Error { get; set; }

            public string? MetadataError { get; set; }

            public string? Sha256 { get; set; }

            public long? LengthBytes { get; set; }

            public DateTime? LastWriteTimeUtc { get; set; }

            public int? PixelWidth { get; set; }

            public int? PixelHeight { get; set; }

            public int RowCount { get; set; }

            public List<string> RowErrors { get; } = new();

            public Dictionary<string, object?> ToMetadata()
            {
                return new Dictionary<string, object?>
                {
                    ["inputIndex"] = InputIndex,
                    ["path"] = Path,
                    ["exists"] = Exists,
                    ["status"] = Status,
                    ["sha256"] = Sha256,
                    ["lengthBytes"] = LengthBytes,
                    ["lastWriteTimeUtc"] = LastWriteTimeUtc,
                    ["pixelWidth"] = PixelWidth,
                    ["pixelHeight"] = PixelHeight,
                    ["rowCount"] = RowCount,
                    ["error"] = Error,
                    ["metadataError"] = MetadataError,
                    ["rowErrors"] = RowErrors.Count == 0 ? null : RowErrors.ToArray()
                };
            }
        }
    }

    /// <summary>
    /// Summary of a batch export. The legacy processed/failed counts describe fully successful
    /// files versus files with any ROI or input failure; the detailed counters below explain the split.
    /// </summary>
    public sealed record BatchRoiAnalysisExportSummary(
        int RequestedFileCount,
        int ProcessedFileCount,
        int FailedFileCount,
        int ExportedRowCount,
        IReadOnlyList<string> Errors)
    {
        /// <summary>
        /// Number of inputs that were decoded successfully. This includes inputs whose ROI analysis was partially or entirely unsuccessful.
        /// </summary>
        public int DecodedFileCount { get; init; }

        /// <summary>
        /// Number of decoded inputs for which every ROI produced a result without an analysis error.
        /// </summary>
        public int FullySuccessfulFileCount { get; init; }

        /// <summary>
        /// Number of decoded inputs for which at least one ROI succeeded and at least one ROI failed.
        /// </summary>
        public int PartiallySuccessfulFileCount { get; init; }

        /// <summary>
        /// Number of decoded inputs for which every ROI analysis failed.
        /// </summary>
        public int AllRoiFailedFileCount { get; init; }

        /// <summary>
        /// Number of inputs that could not be read or decoded.
        /// </summary>
        public int InputFailedFileCount { get; init; }
    }
}
