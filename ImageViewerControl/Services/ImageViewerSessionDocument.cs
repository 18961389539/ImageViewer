using System;
using System.Text.Json.Serialization;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    internal sealed class ImageViewerSessionDocument
    {
        /// <summary>
        /// 会话文档结构版本。
        /// Chinese: 描述会话"信封"结构；缺失或非正数视为早期文件，按当前版本宽容处理。
        /// English: Describes the session envelope layout. Missing or non-positive values are treated as early files.
        /// </summary>
        public int Version { get; set; }

        public string? SessionName { get; set; }
        public DateTimeOffset SavedAtUtc { get; set; }
        public string? ImagePath { get; set; }
        [JsonConverter(typeof(RoiDocumentJsonConverter))]
        public RoiDocument? RoiDocument { get; set; }

        /// <summary>
        /// 历史字段名兼容入口，只读不写。
        /// Chinese: 改造前的会话文件把 ROI 载荷放在 "RoiDocumentJson" 下（转义字符串）。读取时转交给
        /// RoiDocument；写出时该属性恒为 null，配合 WhenWritingNull 保证不再出现在新文件里。
        /// English: Legacy field name, read-only. Pre-change session files stored the ROI payload under
        /// "RoiDocumentJson" as an escaped string. Reads forward to RoiDocument; the getter always returns null
        /// so WhenWritingNull keeps it out of newly written files.
        /// </summary>
        [JsonPropertyName("RoiDocumentJson")]
        [JsonConverter(typeof(RoiDocumentJsonConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RoiDocument? LegacyRoiDocument
        {
            get => null;
            set => RoiDocument ??= value;
        }

        public double Scale { get; set; } = 1.0;
        public double TranslateX { get; set; }
        public double TranslateY { get; set; }
        public CameraCalibration? Calibration { get; set; }
    }
}