using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ImageViewer.Models;

namespace ImageViewer.Core.Analysis
{
    /// <summary>
    /// 报告里单个测量项。
    /// Chinese: 把"测什么、测到多少、允许多少、判成什么"四件事放在一条记录里，
    /// 避免报告读者在多个表格间来回对照。
    /// English: One measurable item — what, measured, allowed, verdict — in a single record.
    /// </summary>
    public sealed record MeasurementReportItem(
        string Label,
        string Quantity,
        string Unit,
        double? MeasuredValue,
        double? NominalValue,
        double? TolerancePlus,
        double? ToleranceMinus,
        string DetectionStatus,
        string SpecificationStatus)
    {
        /// <summary>测得值是否可用。</summary>
        public bool HasMeasuredValue => MeasuredValue.HasValue;

        /// <summary>是否给出了合格/不合格判定（而不是"不可评估/未配置"）。</summary>
        public bool HasVerdict =>
            string.Equals(SpecificationStatus, "Passed", StringComparison.Ordinal) ||
            string.Equals(SpecificationStatus, "Failed", StringComparison.Ordinal);
    }

    /// <summary>
    /// 一份可交付的测量报告（结构化，渲染交给调用方）。
    /// Chinese: 报告头部刻意包含"标定身份 + 前置检查结论"，因为一份没有可追溯标定的
    /// 尺寸报告在计量上是无效的——数字再漂亮也无法复现。
    /// English: A deliverable measurement report. The header carries calibration identity and the
    /// readiness verdict because a dimensional report without traceable calibration is metrologically void.
    /// </summary>
    public sealed record MeasurementReport(
        string Title,
        string? SourceName,
        string? SourceSha256,
        PixelSizeDescriptor Scale,
        CameraCalibration? Calibration,
        MeasurementWorkflowReadiness Readiness,
        IReadOnlyList<MeasurementReportItem> Items,
        string? Unit,
        DateTimeOffset GeneratedAtUtc)
    {
        /// <summary>总测量项数。</summary>
        public int ItemCount => Items.Count;

        /// <summary>有明确判定且通过的项数。</summary>
        public int PassedCount => Items.Count(item => string.Equals(item.SpecificationStatus, "Passed", StringComparison.Ordinal));

        /// <summary>有明确判定且未通过的项数。</summary>
        public int FailedCount => Items.Count(item => string.Equals(item.SpecificationStatus, "Failed", StringComparison.Ordinal));

        /// <summary>无法评估的项数（缺测得值 / 缺公差 / 检测失败 / 标定非法）。</summary>
        public int NotEvaluableCount => Items.Count(item => !item.HasVerdict);

        /// <summary>整体结论：三项以上的判定才谈得上"整体"，否则只报计数。</summary>
        public string OverallVerdict =>
            Readiness.IsBlocked ? "Blocked"
            : FailedCount > 0 ? "Failed"
            : PassedCount > 0 ? "Passed"
            : "NoVerdict";

        /// <summary>是否有任何一项不合格。</summary>
        public bool HasFailure => FailedCount > 0;
    }

    /// <summary>
    /// 比例标定的描述（报告头部用）。
    /// Chinese: 单独一个类型是为了让"未标定"成为显式状态，而不是用 pixelSize=1 + unit="px" 冒充。
    /// English: Makes "uncalibrated" an explicit state instead of masquerading as 1 px = 1 px.
    /// </summary>
    public sealed record PixelSizeDescriptor(bool IsCalibrated, double PixelSize, string Unit)
    {
        /// <summary>描述文本，例如 <c>1 px = 0.050000 mm</c> 或未标定提示。</summary>
        public string Describe() => IsCalibrated
            ? string.Create(CultureInfo.InvariantCulture, $"1 px = {PixelSize:G6} {Unit}")
            : "未标定（像素单位）";
    }

    /// <summary>
    /// 测量报告渲染器：把结构化报告渲染成 Markdown 文本。
    /// Chinese: 选 Markdown 不是审美选择——它是最容易被 Excel/网页/工单系统二次消费的纯文本格式，
    /// 且不需要引入 PDF 依赖（当前项目没有任何 PDF 库）。落盘时与 CSV 同目录、同基名，凑成一套交付物。
    /// English: Markdown because it is consumable by spreadsheets, wikis and ticket systems without adding
    /// a PDF dependency (the project references no PDF library).
    /// </summary>
    public static class MeasurementReportRenderer
    {
        /// <summary>渲染为 Markdown。</summary>
        public static string RenderMarkdown(MeasurementReport report)
        {
            ArgumentNullException.ThrowIfNull(report);

            var builder = new StringBuilder();
            builder.Append("# ").AppendLine(report.Title);
            builder.AppendLine();
            builder.Append("生成时间: ").AppendLine(report.GeneratedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(report.SourceName))
            {
                builder.Append("图像来源: ").Append(report.SourceName);
                if (!string.IsNullOrWhiteSpace(report.SourceSha256))
                {
                    builder.Append("（SHA256 ").Append(Shorten(report.SourceSha256)).Append('）');
                }

                builder.AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine("## 结论");
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"- 整体判定: **{DescribeVerdict(report.OverallVerdict)}**");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- 测量项: {report.ItemCount}（通过 {report.PassedCount} / 不合格 {report.FailedCount} / 不可评估 {report.NotEvaluableCount}）");
            builder.Append("- 标定比例: ").AppendLine(report.Scale.Describe());
            builder.Append("- 标定身份: ").AppendLine(DescribeCalibrationIdentity(report.Calibration));
            builder.Append("- 测量能力: ")
                .AppendLine(report.Readiness.CanProduceSpecificationVerdict
                    ? "可给出尺寸判定"
                    : report.Readiness.CanProducePhysicalResults
                        ? "只能给出物理测量值，无法给出尺寸判定"
                        : "无法给出物理单位的测量结果");

            AppendPrecisionSection(builder, report);

            builder.AppendLine();
            builder.AppendLine("## 前置检查");
            builder.AppendLine();
            if (report.Readiness.Checks.Count == 0)
            {
                builder.AppendLine("- 全部通过（无标定/配置缺口）");
            }
            else
            {
                foreach (MeasurementReadinessCheck check in report.Readiness.Checks)
                {
                    builder.Append("- [").Append(DescribeLevel(check.Level)).Append("] ").AppendLine(check.Message);
                }
            }

            builder.AppendLine();
            builder.AppendLine("## 测量明细");
            builder.AppendLine();
            builder.AppendLine("| 项目 | 量 | 测得值 | 标称值 | 公差 | 检测 | 判定 |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (MeasurementReportItem item in report.Items)
            {
                builder.Append("| ").Append(Escape(item.Label))
                    .Append(" | ").Append(Escape(item.Quantity))
                    .Append(" | ").Append(FormatValue(item.MeasuredValue, item.Unit, "G6"))
                    .Append(" | ").Append(FormatValue(item.NominalValue, item.Unit, "G6"))
                    .Append(" | ").Append(FormatTolerance(item))
                    .Append(" | ").Append(Escape(item.DetectionStatus))
                    .Append(" | ").Append(DescribeVerdict(item.SpecificationStatus))
                    .AppendLine(" |");
            }

            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine("本报告由 ImageViewer 自动生成；判定基于 2D 图像量测，不等同于三坐标测量机的基准公差结论。");
            return builder.ToString();
        }

        private static void AppendPrecisionSection(StringBuilder builder, MeasurementReport report)
        {
            CameraCalibration? calibration = report.Calibration;
            double? uncertaintyPixels = calibration is { IsValid: true } && calibration.MeasurementUncertaintyPixels > 0
                ? calibration.MeasurementUncertaintyPixels
                : null;
            double? reprojectionRms = calibration is { } c && c.ReprojectionErrorRms > 0 ? c.ReprojectionErrorRms : null;
            if (!uncertaintyPixels.HasValue && !reprojectionRms.HasValue)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("### 标定能力");
            builder.AppendLine();
            if (reprojectionRms.HasValue)
            {
                builder.Append("- 重投影 RMS: ").AppendLine(reprojectionRms.Value.ToString("G4", CultureInfo.InvariantCulture) + " px");
            }

            if (uncertaintyPixels.HasValue)
            {
                builder.Append("- 测量不确定度: ").Append(uncertaintyPixels.Value.ToString("G4", CultureInfo.InvariantCulture)).Append(" px");
                if (report.Scale.IsCalibrated)
                {
                    builder.Append("（≈ ").Append((uncertaintyPixels.Value * report.Scale.PixelSize).ToString("G4", CultureInfo.InvariantCulture)).Append(' ').Append(report.Scale.Unit).Append(", 1σ）");
                }

                builder.AppendLine();
            }

            builder.AppendLine("- 单次测量的综合不确定度 = 重复性(未评估) ⊕ 标定不确定度；未做重复测量时以上述标定不确定度为准。");
        }

        private static string DescribeCalibrationIdentity(CameraCalibration? calibration)
        {
            if (calibration is not { IsEnabled: true })
            {
                return "无（仅比例标定或未标定）";
            }

            if (!calibration.IsValid)
            {
                return "非法（已按恒等处理，尺寸结果不可信）";
            }

            string id = string.IsNullOrWhiteSpace(calibration.CalibrationId) ? "（无标识）" : calibration.CalibrationId!;
            string when = calibration.CalibratedAtUtc is { } timestamp
                ? timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
                : "（无时间戳）";
            string model = calibration.HasExtendedDistortionModel ? "扩展畸变模型" : "径向畸变 (K1/K2)";
            return $"{id} · {when} · {model}";
        }

        private static string DescribeVerdict(string verdict) => verdict switch
        {
            "Passed" => "通过",
            "Failed" => "不合格",
            "Blocked" => "已阻断",
            "NoVerdict" => "无可判定项",
            "NotEvaluable" => "不可评估",
            "NotConfigured" => "未配置公差",
            "NotApplicable" => "不适用",
            "Review" => "需复核",
            _ => verdict
        };

        private static string DescribeLevel(MeasurementReadinessLevel level) => level switch
        {
            MeasurementReadinessLevel.Passed => "通过",
            MeasurementReadinessLevel.Warning => "警告",
            _ => "阻断"
        };

        private static string FormatValue(double? value, string? unit, string format)
        {
            if (value is not { } number || !double.IsFinite(number))
            {
                return "—";
            }

            string text = number.ToString(format, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(unit) || unit == "px" ? text : text + " " + unit;
        }

        private static string FormatTolerance(MeasurementReportItem item)
        {
            if (item.NominalValue is not { } nominal || !double.IsFinite(nominal))
            {
                return "—";
            }

            double plus = item.TolerancePlus is { } p && double.IsFinite(p) ? p : 0;
            double minus = item.ToleranceMinus is { } m && double.IsFinite(m) ? m : 0;
            if (plus == 0 && minus == 0)
            {
                return "—";
            }

            return string.Create(CultureInfo.InvariantCulture, $"+{plus:G6} / -{minus:G6}");
        }

        private static string Escape(string? value) =>
            (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal);

        private static string Shorten(string hash) => hash.Length <= 12 ? hash : hash[..12];
    }

    /// <summary>
    /// 由"逐项结果 + 标定 + 环境"组装一份 <see cref="MeasurementReport"/>。
    /// Chinese: 组装逻辑独立于渲染，便于测试只断言结构（不依赖 Markdown 具体排版）。
    /// English: Assembly is separated from rendering so tests can assert structure, not layout.
    /// </summary>
    public static class MeasurementReportBuilder
    {
        /// <summary>构造报告。</summary>
        public static MeasurementReport Build(
            IEnumerable<MeasurementReportItem> items,
            double pixelSize,
            string? physicalUnit,
            CameraCalibration? calibration,
            string? sourceName = null,
            string? sourceSha256 = null,
            bool hasKnownReference = false,
            string? title = null,
            DateTimeOffset? generatedAtUtc = null)
        {
            ArgumentNullException.ThrowIfNull(items);

            List<MeasurementReportItem> itemList = items.ToList();
            bool scaleCalibrated = double.IsFinite(pixelSize) && pixelSize > 0;
            string unit = string.IsNullOrWhiteSpace(physicalUnit) ? "px" : physicalUnit!;

            int toleranced = itemList.Count(item => item.NominalValue.HasValue);
            int unmeasuredToleranced = itemList.Count(item => item.NominalValue.HasValue && !item.MeasuredValue.HasValue);

            MeasurementWorkflowReadiness readiness = MeasurementWorkflowReadinessEvaluator.Evaluate(
                pixelSize,
                physicalUnit,
                calibration,
                itemList.Count,
                toleranced,
                unmeasuredToleranced,
                hasKnownReference);

            return new MeasurementReport(
                ResolveTitle(title, sourceName),
                sourceName,
                sourceSha256,
                new PixelSizeDescriptor(scaleCalibrated, scaleCalibrated ? pixelSize : 1.0, scaleCalibrated ? unit : "px"),
                calibration,
                readiness,
                itemList,
                unit,
                generatedAtUtc ?? DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// 标题解析：显式标题优先，否则用图像名，再否则用通用标题。
        /// Chinese: 把来源名写进标题是为了让一批报告躺在一个目录里也能一眼分辨哪份对应哪张图。
        /// English: Prefers an explicit title, then the source image name.
        /// </summary>
        private static string ResolveTitle(string? title, string? sourceName)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title!;
            }

            return string.IsNullOrWhiteSpace(sourceName) ? "测量报告" : $"测量报告 – {sourceName}";
        }
    }
}
