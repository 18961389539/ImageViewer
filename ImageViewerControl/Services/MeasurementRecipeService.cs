using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageViewer.Core.Analysis;
using ImageViewer.Core.Geometry;
using ImageViewer.Core.Persistence;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    /// <summary>
    /// 测量配方的读写。
    /// Chinese: 配方 = 名义工件坐标系 + 基准点 + 标定 + 测量项（ROI 模板 + 公差），是可交付、可复现的
    /// 测量方案。保存/加载都走 <see cref="ImageViewerAtomicFile"/>，避免半截文件；加载时对
    /// 未知 ROI 类型走与 ROI 文档一致的 unresolved 语义，缺插件不会导致数据被抹掉。
    /// English: Reads and writes measurement recipes through the project's atomic file helper; unknown ROI types
    /// degrade to unresolved payloads exactly like the ROI document so a missing plugin never erases data.
    /// </summary>
    public static class MeasurementRecipeService
    {
        /// <summary>
        /// 由当前编辑状态构造配方。
        /// Chinese: ROI 模板保存的是**图像坐标**下的几何（ROI 文档本来的语义），
        /// 而"随工件移动"由运行时用 <see cref="WorkpieceCoordinateSystem"/> 施加变换实现——
        /// 刻意不在保存时把 ROI 折算到工件坐标，否则改一次坐标系就要重写所有模板，且丢掉了原始像素几何。
        /// English: Templates are stored in image coordinates; the workpiece transform is applied at run time.
        /// Baking it in at save time would invalidate every template whenever the datum changes.
        /// </summary>
        public static MeasurementRecipeDocument Create(
            string? name,
            string? description,
            IEnumerable<RoiBase> rois,
            double pixelSize,
            string? physicalUnit,
            RoiPluginRegistry pluginRegistry,
            WorkpieceCoordinateSystem? coordinateSystem = null,
            IEnumerable<RecipeDatumPoint>? datumPoints = null,
            CameraCalibration? calibration = null,
            ImageAnalysisQualityProfile? qualityProfile = null)
        {
            ArgumentNullException.ThrowIfNull(rois);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            List<RoiBase> roiList = rois.ToList();
            RoiDocument templateDocument = RoiPersistenceService.CreateDocument(
                roiList,
                pixelSize,
                physicalUnit,
                pluginRegistry,
                qualityProfile: qualityProfile);

            // CreateDocument 的输出顺序与输入一致，因此可以按下标配对把几何载荷搬进配方项。
            // 数量不一致说明上游 API 契约变了，宁可抛错也不要错位配对（错位=量错工件）。
            if (templateDocument.Items.Count != roiList.Count)
            {
                throw new InvalidOperationException(
                    $"ROI 模板数量({templateDocument.Items.Count})与输入 ROI 数量({roiList.Count})不一致，无法建立配方项。");
            }

            return new MeasurementRecipeDocument
            {
                Version = MeasurementRecipeDocument.CurrentVersion,
                Name = name,
                Description = description,
                SavedAtUtc = DateTimeOffset.UtcNow,
                CoordinateSystem = coordinateSystem,
                DatumPoints = datumPoints?.ToList() ?? new List<RecipeDatumPoint>(),
                PixelSize = pixelSize,
                PhysicalUnit = physicalUnit,
                Calibration = calibration,
                Items = roiList.Select((roi, index) => new MeasurementRecipeItem
                {
                    Key = $"{roi.RoiTypeName}#{index}",
                    DisplayName = string.IsNullOrWhiteSpace(roi.Label) ? null : roi.Label,
                    RoiTypeKey = roi.RoiTypeName,
                    Tolerance = roi.Tolerance?.Clone(),
                    IsEnabled = roi.IsVisible,
                    // 公差只在 Tolerance 一处存放，这里把模板里的副本清掉，避免"改一处、另一处还是旧值"。
                    Template = StripTolerance(templateDocument.Items[index])
                }).ToList()
            };
        }

        /// <summary>
        /// 清掉模板里冗余的公差副本。
        /// Chinese: <see cref="RoiPersistenceData.Common"/> 的 Tolerance 与本项的
        /// <see cref="MeasurementRecipeItem.Tolerance"/> 是同一份数据的两个出口；两者都在时无法判断谁生效，
        /// 因此配方统一以项上的 Tolerance 为准。
        /// </summary>
        private static RoiPersistenceData StripTolerance(RoiPersistenceData template)
        {
            template.Common.Tolerance = null;
            return template;
        }

        /// <summary>序列化配方（source-generated context，保证 AOT/裁剪友好）。</summary>
        public static string Serialize(MeasurementRecipeDocument recipe)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            return JsonSerializer.Serialize(recipe, ImageViewerCoreJsonSerializationContext.Default.MeasurementRecipeDocument);
        }

        /// <summary>反序列化配方；结构非法时抛 <see cref="JsonException"/> 而不是静默返回空配方。</summary>
        public static MeasurementRecipeDocument Deserialize(string json)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(json);
            MeasurementRecipeDocument? recipe = JsonSerializer.Deserialize(
                json,
                ImageViewerCoreJsonSerializationContext.Default.MeasurementRecipeDocument);
            if (recipe is null)
            {
                throw new JsonException("配方文件内容为空或不是有效的配方 JSON。");
            }

            return recipe;
        }

        /// <summary>保存到文件（原子写）。</summary>
        public static void SaveToFile(string filePath, MeasurementRecipeDocument recipe)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(recipe);
            ImageViewerAtomicFile.WriteAllText(filePath, Serialize(recipe));
        }

        /// <summary>
        /// 异步保存。
        /// Chinese: 与 ROI/会话保存同约定——对象图构造在调用线程，序列化与写盘放后台，
        /// 避免在 UI 线程上序列化整份配方。
        /// </summary>
        public static async Task SaveToFileAsync(string filePath, MeasurementRecipeDocument recipe, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(recipe);
            string json = await Task.Run(() => Serialize(recipe), cancellationToken).ConfigureAwait(false);
            await ImageViewerAtomicFile.WriteAllTextAsync(filePath, json, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>从文件加载配方。</summary>
        public static MeasurementRecipeDocument LoadFromFile(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            return Deserialize(File.ReadAllText(filePath));
        }

        /// <summary>
        /// 把配方还原成 ROI 文档对象，交给统一的 ROI 反序列化路径。
        /// Chinese: 刻意复用 <see cref="RoiPersistenceService"/> 的文档管线而不是自己 new ROI：
        /// 版本校验、未知类型 → unresolved、公差应用这些语义只有一份，走同一条路才不会出现
        /// "加载会话能工作、加载配方不行"的分叉。
        /// </summary>
        internal static RoiDocument BuildRoiDocument(MeasurementRecipeDocument recipe)
        {
            ArgumentNullException.ThrowIfNull(recipe);

            var document = new RoiDocument
            {
                Version = 1,
                PixelSize = recipe.HasValidScale ? recipe.PixelSize : null,
                PhysicalUnit = string.IsNullOrWhiteSpace(recipe.PhysicalUnit) ? "px" : recipe.PhysicalUnit
            };

            foreach (MeasurementRecipeItem item in recipe.Items)
            {
                if (item.Template is null)
                {
                    // 没有几何的项无法还原成 ROI——宁可跳过也不要造一个空壳 ROI 出来。
                    continue;
                }

                // 公差以项上的为准回填进载荷（保存时被刻意清掉，避免两处出口）。
                item.Template.Common.Tolerance = item.Tolerance?.Clone();
                document.Items.Add(item.Template);
            }

            return document;
        }

        /// <summary>
        /// Builds a detached, aligned ROI document without mutating the saved recipe.
        /// </summary>
        public static RoiDocument BuildAlignedRoiDocument(
            MeasurementRecipeDocument recipe,
            PointD currentDatum1,
            PointD currentDatum2)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            return MeasurementRecipeAlignmentService.BuildAlignedDocument(
                recipe, currentDatum1, currentDatum2);
        }

        /// <summary>
        /// 判断配方能否在当前环境下用于判定，产出与"标定检查"一致的结论。
        /// Chinese: 配方只是"计划"，能否判定仍取决于标定/公差配置，因此这里复用同一个
        /// <see cref="MeasurementWorkflowReadinessEvaluator"/>，避免"载入配方就以为可以出报告"。
        /// </summary>
        public static MeasurementWorkflowReadiness EvaluateReadiness(MeasurementRecipeDocument recipe)
        {
            ArgumentNullException.ThrowIfNull(recipe);

            int enabledItems = recipe.EnabledItemCount;
            int tolerancedItems = recipe.Items.Count(item => item.IsEnabled && item.Tolerance is { IsEnabled: true });
            int unmeasuredItems = tolerancedItems;
            return MeasurementWorkflowReadinessEvaluator.Evaluate(
                recipe.PixelSize,
                recipe.PhysicalUnit,
                recipe.Calibration,
                enabledItems,
                tolerancedItems,
                unmeasuredItems,
                hasKnownReference: false);
        }
    }
}
