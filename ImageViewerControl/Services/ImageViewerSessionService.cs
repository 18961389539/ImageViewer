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

        public void SaveToFile(string filePath, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            EnsureDirectory(filePath);
            File.WriteAllText(filePath, SerializeSession(Path.GetFileNameWithoutExtension(filePath), snapshot, pluginRegistry));
        }

        public Task SaveToFileAsync(string filePath, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            EnsureDirectory(filePath);
            return File.WriteAllTextAsync(filePath, SerializeSession(Path.GetFileNameWithoutExtension(filePath), snapshot, pluginRegistry), cancellationToken);
        }

        public string SerializeSession(string? sessionName, ImageViewerPersistenceSnapshot snapshot, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            var session = new ImageViewerSessionDocument
            {
                Version = CurrentSessionVersion,
                SessionName = sessionName,
                SavedAtUtc = DateTimeOffset.UtcNow,
                ImagePath = snapshot.ImagePath,
                RoiDocument = RoiPersistenceService.CreateDocument(snapshot.Rois, snapshot.PixelSize, snapshot.PhysicalUnit, pluginRegistry, snapshot.UnresolvedRois),
                Scale = snapshot.Scale,
                TranslateX = snapshot.TranslateX,
                TranslateY = snapshot.TranslateY,
                Calibration = snapshot.Calibration
            };

            return JsonSerializer.Serialize(session, ImageViewerJsonSerializationContext.Default.ImageViewerSessionDocument);
        }

        public ImageViewerSessionData LoadFromFile(string filePath, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            return LoadFromJson(File.ReadAllText(filePath), Path.GetDirectoryName(Path.GetFullPath(filePath)), pluginRegistry);
        }

        public async Task<ImageViewerSessionData> LoadFromFileAsync(string filePath, RoiPluginRegistry? pluginRegistry = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(pluginRegistry);

            return LoadFromJson(await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false), Path.GetDirectoryName(Path.GetFullPath(filePath)), pluginRegistry);
        }

        public ImageViewerSessionData LoadFromJson(string sessionJson, string? sessionBaseDirectory = null, RoiPluginRegistry? pluginRegistry = null)
        {
            ArgumentNullException.ThrowIfNull(pluginRegistry);
            var session = JsonSerializer.Deserialize(sessionJson, ImageViewerJsonSerializationContext.Default.ImageViewerSessionDocument)
                ?? new ImageViewerSessionDocument();
            ValidateSessionVersion(session.Version);
            var roiData = RoiPersistenceService.CreateRois(session.RoiDocument ?? new RoiDocument(), pluginRegistry);
            return new ImageViewerSessionData(session.SessionName, session.SavedAtUtc, ResolveImagePath(session.ImagePath, sessionBaseDirectory), roiData.Rois, roiData.PixelSize, roiData.PhysicalUnit, session.Scale, session.TranslateX, session.TranslateY, session.Calibration)
            {
                UnresolvedRois = roiData.UnresolvedItems
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

        private static void EnsureDirectory(string filePath)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
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
    }
}
