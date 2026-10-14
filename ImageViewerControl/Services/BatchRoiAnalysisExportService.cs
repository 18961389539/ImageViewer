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
using ImageViewer.Core.Analysis;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    /// <summary>进度通知。CompletedFileCount 只统计已经落入结果摘要的输入文件。</summary>
    public sealed record BatchRoiAnalysisExportProgress(
        int RequestedFileCount,
        int CompletedFileCount,
        string CurrentPath,
        string Phase,
        int ExportedRowCount);

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
            IProgress<BatchRoiAnalysisExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return ExportAsync(
                outputPath,
                imagePaths,
                roiTemplates,
                pixelSize,
                physicalUnit,
                calibration,
                pluginRegistry,
                renderSettings,
                qualityProfile: null,
                progress: progress,
                cancellationToken: cancellationToken);
        }

        public static Task<BatchRoiAnalysisExportSummary> ExportAsync(
            string outputPath,
            IEnumerable<string> imagePaths,
            IEnumerable<RoiBase> roiTemplates,
            double pixelSize,
            string? physicalUnit,
            RepeatabilityStudyOptions repeatabilityOptions,
            CameraCalibration? calibration = null,
            RoiPluginRegistry? pluginRegistry = null,
            IReadOnlyDictionary<string, string>? renderSettings = null,
            IProgress<BatchRoiAnalysisExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return ExportAsync(outputPath, imagePaths, roiTemplates, pixelSize, physicalUnit, calibration,
                pluginRegistry, renderSettings, qualityProfile: null, repeatabilityOptions: repeatabilityOptions,
                progress: progress, cancellationToken: cancellationToken);
        }

        public static Task<BatchRoiAnalysisExportSummary> ExportAsync(
            string outputPath,
            IEnumerable<string> imagePaths,
            IEnumerable<RoiBase> roiTemplates,
            double pixelSize,
            string? physicalUnit,
            CameraCalibration? calibration,
            RoiPluginRegistry? pluginRegistry,
            IReadOnlyDictionary<string, string>? renderSettings,
            ImageAnalysisQualityProfile? qualityProfile,
            IProgress<BatchRoiAnalysisExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return ExportAsync(outputPath, imagePaths, roiTemplates, pixelSize, physicalUnit, calibration,
                pluginRegistry, renderSettings, qualityProfile, repeatabilityOptions: new RepeatabilityStudyOptions(),
                progress: progress, cancellationToken: cancellationToken);
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
            RepeatabilityStudyOptions repeatabilityOptions,
            IProgress<BatchRoiAnalysisExportProgress>? progress = null,
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
            bool canceled = false;

            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                string sourcePath = sources[sourceIndex];
                bool bitmapLoaded = false;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new BatchRoiAnalysisExportProgress(sources.Length, sourceIndex, sourcePath, "Preparing", rows.Count));
                    BatchInputTrace inputTrace = await CaptureInputTraceAsync(sourceIndex, sourcePath, cancellationToken).ConfigureAwait(false);
                    inputTraces.Add(inputTrace);
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
                        () => AnalyzeSource(sourceIndex, sourcePath, bitmap, templates, pixelSize, physicalUnit, calibration, quality, cancellationToken),
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
                    progress?.Report(new BatchRoiAnalysisExportProgress(sources.Length, sourceIndex + 1, sourcePath, "Completed", rows.Count));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }
                catch (Exception ex)
                {
                    BatchInputTrace? inputTrace = inputTraces.LastOrDefault(trace => trace.InputIndex == sourceIndex);
                    if (inputTrace is null)
                    {
                        inputTrace = new BatchInputTrace(sourceIndex, sourcePath);
                        inputTraces.Add(inputTrace);
                    }
                    string status = bitmapLoaded ? "AnalysisFailed" : File.Exists(sourcePath) ? "DecodeFailed" : "InputMissing";
                    string error = ex.Message;
                    inputTrace.Status = status;
                    inputTrace.Error = error;
                    inputTrace.RowCount = templates.Length;
                    rows.AddRange(CreateFailureRows(sourceIndex, sourcePath, status, error, templates, pixelSize, physicalUnit, calibration));
                    errors.Add($"{Path.GetFileName(sourcePath)}: {error}");
                    if (bitmapLoaded)
                    {
                        allRoiFailedFileCount++;
                    }
                    else
                    {
                        inputFailedFileCount++;
                    }
                    progress?.Report(new BatchRoiAnalysisExportProgress(sources.Length, sourceIndex + 1, sourcePath, "Completed", rows.Count));
                }
            }

            string csv = RoiAnalysisExportService.BuildBatchCsv(rows);
            // Persist completed work even when the user cancels. The summary is useful
            // for resuming or auditing a long batch and must not be blocked by the
            // canceled token that triggered the partial export.
            string metadataPath = Path.ChangeExtension(outputPath, ".metadata.json");
            string summaryPath = Path.ChangeExtension(outputPath, ".summary.json");
            // 重复性研究随批次一并产出：多图批次正是重复性的唯一真实来源（单图只有 1 个样本）。
            string repeatabilityPath = RoiAnalysisExportService.GetRepeatabilityPath(outputPath);
            string batchId = Guid.NewGuid().ToString("N");
            RepeatabilityStudy repeatabilityStudy = RoiAnalysisExportService.BuildRepeatabilityStudy(
                rows, physicalUnit, repeatabilityOptions, inputTraces.Select(trace => trace.InputIndex));
            string repeatabilityMarkdown = RepeatabilityStudyRenderer.RenderMarkdown(repeatabilityStudy);
            string csvHash = ImageViewerExportArtifactSet.ComputeSha256Utf8Text(csv);
            string repeatabilityHash = ImageViewerExportArtifactSet.ComputeSha256Utf8Text(repeatabilityMarkdown);
            var metadata = new Dictionary<string, object?>
            {
                ["exportFormatVersion"] = 2,
                ["batch"] = true,
                ["batchId"] = batchId,
                ["status"] = canceled ? "Canceled" : "Completed",
                ["canceled"] = canceled,
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
                ["repeatability"] = new Dictionary<string, object?>
                {
                    ["path"] = repeatabilityPath,
                    ["workpieceContext"] = repeatabilityStudy.Context.Context.ToString(),
                    ["workpieceId"] = repeatabilityStudy.Context.WorkpieceId,
                    ["sourceCount"] = repeatabilityStudy.SourceCount,
                    ["conclusiveItemCount"] = repeatabilityStudy.ConclusiveItemCount,
                    ["inconclusiveItemCount"] = repeatabilityStudy.InconclusiveItemCount,
                    ["toleranceConsumedItemCount"] = repeatabilityStudy.ToleranceConsumedCount,
                    ["hasConclusiveResult"] = repeatabilityStudy.HasConclusiveResult,
                    ["items"] = repeatabilityStudy.Items.Select(item => new Dictionary<string, object?>
                    {
                        ["label"] = item.Label,
                        ["measurementItemKey"] = item.MeasurementItemKey,
                        ["unit"] = item.Unit,
                        ["quantity"] = item.Quantity,
                        ["sampleCount"] = item.Statistics.SampleCount,
                        ["mean"] = item.Statistics.Mean,
                        ["standardDeviation"] = item.Statistics.StandardDeviation,
                        ["expandedUncertainty"] = item.Statistics.ExpandedUncertainty,
                        ["range"] = item.Statistics.Range,
                        ["toleranceBand"] = item.ToleranceBand,
                        ["shareOfTolerancePercent"] = item.RepeatabilityShareOfTolerancePercent
                    }).ToList()
                },
                ["artifacts"] = new Dictionary<string, object?>
                {
                    ["csvSha256"] = csvHash,
                    ["repeatabilitySha256"] = repeatabilityHash,
                    ["summarySha256"] = null
                },
                ["roiTypes"] = templates
                    .GroupBy(roi => roi.RoiTypeName)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                ["renderSettings"] = renderSettings,
                ["hasRoiPersistenceRegistry"] = pluginRegistry != null
            };
            string metadataJson = JsonSerializer.Serialize(metadata, MetadataJsonOptions);
            BatchRoiAnalysisExportSummary summary = new(
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
                InputFailedFileCount = inputFailedFileCount,
                IsCanceled = canceled,
                MetadataPath = metadataPath,
                SummaryPath = summaryPath,
                RepeatabilityPath = repeatabilityPath,
                RepeatabilitySourceCount = repeatabilityStudy.SourceCount,
                RepeatabilityConclusiveItemCount = repeatabilityStudy.ConclusiveItemCount,
                RepeatabilityToleranceConsumedItemCount = repeatabilityStudy.ToleranceConsumedCount,
                BatchId = batchId,
                CsvSha256 = csvHash,
                RepeatabilitySha256 = repeatabilityHash
            };
            string summaryJson = JsonSerializer.Serialize(summary, MetadataJsonOptions);
            metadata["artifacts"] = new Dictionary<string, object?>
            {
                ["csvSha256"] = csvHash,
                ["repeatabilitySha256"] = repeatabilityHash,
                ["summarySha256"] = ImageViewerExportArtifactSet.ComputeSha256Utf8Text(summaryJson)
            };
            metadataJson = JsonSerializer.Serialize(metadata, MetadataJsonOptions);
            await ImageViewerExportArtifactSet.WriteAllTextAsync(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [outputPath] = csv,
                    [repeatabilityPath] = repeatabilityMarkdown,
                    [metadataPath] = metadataJson,
                    [summaryPath] = summaryJson
                },
                CancellationToken.None).ConfigureAwait(false);
            progress?.Report(new BatchRoiAnalysisExportProgress(sources.Length, inputTraces.Count, string.Empty, canceled ? "Canceled" : "Exported", rows.Count));
            if (canceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return summary;
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
            int sourceIndex,
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

                rows[i] = new RoiAnalysisBatchCsvRow(sourcePath, status, error, roi, bitmap, pixelSize, physicalUnit, calibration, sourceIndex, $"roi:{i}");
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
            int sourceIndex,
            string sourcePath,
            string status,
            string error,
            IReadOnlyList<RoiBase> templates,
            double pixelSize,
            string? physicalUnit,
            CameraCalibration? calibration)
        {
            return templates
                .Select((template, index) => new RoiAnalysisBatchCsvRow(
                    sourcePath,
                    status,
                    error,
                    template.Clone(),
                    Bitmap: null,
                    pixelSize,
                    physicalUnit,
                    calibration,
                    sourceIndex,
                    $"roi:{index}"))
                .ToArray();
        }

        private static bool TryDetectRoi(BitmapSource bitmap, RoiBase roi, ImageAnalysisQualityProfile quality)
        {
            switch (roi)
            {
                case CaliperMeasureRoi dualEdge when ImageAnalysisService.TryDetectLineMeasureEdgesCore(bitmap, dualEdge, out ImageViewer.Core.Analysis.LineMeasureGradientDetectionResult dualResult, quality):
                    RoiDetectionResultMapper.Apply(dualEdge, dualResult);
                    return true;
                case LineCaliperMeasureRoi line when ImageAnalysisService.TryDetectLineCaliperEdgesCore(bitmap, line, out ImageViewer.Core.Analysis.LineCaliperDetectionResult lineResult, quality):
                    RoiDetectionResultMapper.Apply(line, lineResult);
                    return true;
                case CircularCaliperMeasureRoi circular when ImageAnalysisService.TryDetectCircularCaliperEdgesCore(bitmap, circular, out ImageViewer.Core.Analysis.CircularCaliperDetectionResult circularResult, quality):
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

        /// <summary>用户在导出完成前取消了批次；已完成输入仍会保存到输出与摘要文件。</summary>
        public bool IsCanceled { get; init; }

        public string? MetadataPath { get; init; }

        public string? SummaryPath { get; init; }

        /// <summary>重复性研究报告路径（<c>*.repeatability.md</c>）。</summary>
        public string? RepeatabilityPath { get; init; }

        /// <summary>参与重复性研究的图像数。</summary>
        public int RepeatabilitySourceCount { get; init; }

        /// <summary>样本数足够（≥2）因而能给出重复性结论的项数。</summary>
        public int RepeatabilityConclusiveItemCount { get; init; }

        /// <summary>重复性已占满公差带（≥100%）的项数；&gt;0 表示该测量方案的可判定性存在问题。</summary>
        public int RepeatabilityToleranceConsumedItemCount { get; init; }

        /// <summary>Links the CSV, repeatability report, summary, and metadata from one export.</summary>
        public string? BatchId { get; init; }

        /// <summary>SHA-256 of the exact UTF-8-with-BOM CSV bytes.</summary>
        public string? CsvSha256 { get; init; }

        /// <summary>SHA-256 of the exact UTF-8-with-BOM repeatability report bytes.</summary>
        public string? RepeatabilitySha256 { get; init; }
    }
}
