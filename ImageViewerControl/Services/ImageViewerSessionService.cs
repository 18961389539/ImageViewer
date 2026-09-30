using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageViewer.Abstractions;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Services
{
    public sealed class ImageViewerSessionService : IImageViewerSessionService
    {
        // 版本 1 = ROI 载荷以转义字符串内嵌；版本 2 = ROI 载荷为嵌套对象。
        // Version 1 = ROI payload embedded as an escaped string; version 2 = ROI payload is a nested object.
        private const int CurrentSessionVersion = 2;

        public void SaveToFile(string filePath, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry pluginRegistry)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            ImageViewerAtomicFile.WriteAllText(
                filePath,
                SerializeSession(Path.GetFileNameWithoutExtension(filePath), snapshot, pluginRegistry));
        }

        /// <summary>
        /// 异步保存会话。
        /// Chinese: 序列化本身也在后台线程执行——快照里的 ROI 载荷已在 UI 线程构建成脱离 UI 的纯数据图，
        /// 因此这里可以安全 offload，避免在 UI 线程上把整份标注文档转成 JSON。
        /// English: Serialization runs off the UI thread too. The snapshot already carries a detached ROI payload
        /// built on the UI thread, so offloading here cannot race the editor.
        /// </summary>
        public async Task SaveToFileAsync(string filePath, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry pluginRegistry, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            string sessionName = Path.GetFileNameWithoutExtension(filePath);
            string sessionJson = await Task.Run(
                () => SerializeSession(sessionName, snapshot, pluginRegistry),
                cancellationToken).ConfigureAwait(false);

            await ImageViewerAtomicFile.WriteAllTextAsync(
                filePath,
                sessionJson,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 把快照映射成会话文档并序列化。
        /// Chinese: 只做字段拷贝，不再构造 ROI 文档、也不再访问 <see cref="RoiBase"/> 或插件注册表；
        /// <paramref name="pluginRegistry"/> 保留用于 API 兼容与空值校验（ROI→DTO 的解析已在 CaptureSnapshot 完成）。
        /// English: Pure field mapping; it no longer builds the ROI document or touches <see cref="RoiBase"/>.
        /// The registry parameter is kept for API compatibility and null checking only.
        /// </summary>
        public string SerializeSession(string? sessionName, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry pluginRegistry)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            var session = new ImageViewerSessionDocument
            {
                Version = CurrentSessionVersion,
                SessionName = sessionName,
                SavedAtUtc = DateTimeOffset.UtcNow,
                ImagePath = snapshot.ImagePath,
                RoiDocument = snapshot.RoiDocument,
                Scale = snapshot.Scale,
                TranslateX = snapshot.TranslateX,
                TranslateY = snapshot.TranslateY,
                Calibration = snapshot.Calibration
            };

            return JsonSerializer.Serialize(session, ImageViewer.Core.Persistence.ImageViewerCoreJsonSerializationContext.Default.ImageViewerSessionDocument);
        }

        public ImageViewerSessionData LoadFromFile(string filePath, RoiPluginRegistry pluginRegistry)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            return LoadFromJson(File.ReadAllText(filePath), pluginRegistry, Path.GetDirectoryName(Path.GetFullPath(filePath)));
        }

        public async Task<ImageViewerSessionData> LoadFromFileAsync(string filePath, RoiPluginRegistry pluginRegistry, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            return LoadFromJson(await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false), pluginRegistry, Path.GetDirectoryName(Path.GetFullPath(filePath)));
        }

        public ImageViewerSessionData LoadFromJson(string sessionJson, RoiPluginRegistry pluginRegistry, string? sessionBaseDirectory = null)
        {
            ArgumentNullException.ThrowIfNull(pluginRegistry);
            var session = JsonSerializer.Deserialize(sessionJson, ImageViewer.Core.Persistence.ImageViewerCoreJsonSerializationContext.Default.ImageViewerSessionDocument)
                ?? new ImageViewerSessionDocument();
            ValidateSessionVersion(session.Version);
            var roiData = RoiPersistenceService.CreateRois(session.RoiDocument ?? new RoiDocument(), pluginRegistry);
            ImageAnalysisQualityProfile qualityProfile = roiData.QualityProfile ?? ImageAnalysisQualityProfile.Default;
            qualityProfile.Validate();
            return new ImageViewerSessionData(session.SessionName, session.SavedAtUtc, ResolveImagePath(session.ImagePath, sessionBaseDirectory), roiData.Rois, roiData.PixelSize, roiData.PhysicalUnit, session.Scale, session.TranslateX, session.TranslateY, session.Calibration)
            {
                UnresolvedRois = roiData.UnresolvedItems,
                QualityProfile = qualityProfile
            };
        }

        /// <summary>
        /// 校验会话文档版本。
        /// Chinese: 版本号高于当前支持版本时拒绝加载，避免用旧代码误读新格式；缺失或非正数视为早期文件，宽容接受。
        /// English: Rejects session files written by a newer format so old code never misreads a new layout.
        /// Missing or non-positive versions are treated as early files and accepted leniently.
        /// </summary>
        private static void ValidateSessionVersion(int version)
        {
            if (version > CurrentSessionVersion)
            {
                throw new NotSupportedException(
                    $"The session document version {version} is newer than the supported version {CurrentSessionVersion}.");
            }
        }

        private static string? ResolveImagePath(string? imagePath, string? sessionBaseDirectory)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(sessionBaseDirectory) || Path.IsPathRooted(imagePath))
            {
                return imagePath;
            }

            return Path.GetFullPath(Path.Combine(sessionBaseDirectory, imagePath));
        }

    }

    public sealed record ImageViewerSessionData(
        string? SessionName,
        DateTimeOffset SavedAtUtc,
        string? ImagePath,
        IReadOnlyList<RoiBase> Rois,
        double PixelSize,
        string PhysicalUnit,
        double Scale,
        double TranslateX,
        double TranslateY,
        CameraCalibration? Calibration)
    {
        /// <summary>
        /// 加载时未能解析的 ROI 载荷。
        /// Chinese: 缺插件 / 类型键变更时不为空；随快照流回保存路径，避免数据丢失。
        /// English: ROI payloads that no registered plugin could resolve on load; carried back into saves.
        /// </summary>
        public IReadOnlyList<RoiPersistenceData> UnresolvedRois { get; init; } = [];

        public ImageAnalysisQualityProfile QualityProfile { get; init; } = ImageAnalysisQualityProfile.Default;
    }
}
