using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ImageViewer.Services
{
    public enum VolumeSliceOrderSource
    {
        ExplicitPosition,
        InstanceNumber,
        AcquisitionTime,
        NaturalFileName
    }

    /// <summary>
    /// 一张体数据切片及其可用于排序的采集元数据。
    /// </summary>
    public sealed record VolumeSliceDescriptor(
        string Path,
        double? Position = null,
        int? InstanceNumber = null,
        DateTimeOffset? AcquisitionTime = null,
        double? SpacingX = null,
        double? SpacingY = null,
        double? SpacingZ = null);

    public sealed record VolumeSliceOrderingResult(
        IReadOnlyList<VolumeSliceDescriptor> Slices,
        VolumeSliceOrderSource Source,
        bool IsAuthoritative,
        IReadOnlyList<string> Warnings)
    {
        public IReadOnlyList<string> OrderedPaths => Slices.Select(slice => slice.Path).ToArray();
    }

    /// <summary>
    /// 为多文件体数据提供元数据优先的确定性排序。
    /// Chinese: 优先使用空间位置、实例号或采集时间；只有元数据不完整时才回退到文件名，
    /// 并返回警告让宿主要求人工确认。
    /// English: Prefers physical position, instance number, or acquisition time and only falls back
    /// to filename order when metadata is incomplete, returning a warning for operator confirmation.
    /// </summary>
    public static class VolumeSliceOrderingService
    {
        public static VolumeSliceOrderingResult OrderDescriptors(IEnumerable<VolumeSliceDescriptor> descriptors)
        {
            ArgumentNullException.ThrowIfNull(descriptors);

            VolumeSliceDescriptor[] materialized = descriptors.ToArray();
            ValidatePaths(materialized.Select(descriptor => descriptor.Path));
            if (materialized.Length == 0)
            {
                throw new ArgumentException("At least one slice path is required.", nameof(descriptors));
            }

            if (materialized.All(descriptor => descriptor.Position is { } position && double.IsFinite(position)))
            {
                return CreateResult(
                    materialized
                        .OrderBy(descriptor => descriptor.Position!.Value)
                        .ThenBy(descriptor => descriptor.InstanceNumber)
                        .ThenBy(descriptor => Path.GetFileName(descriptor.Path), NaturalFileNameComparer.Instance)
                        .ThenBy(descriptor => descriptor.Path, StringComparer.OrdinalIgnoreCase),
                    VolumeSliceOrderSource.ExplicitPosition,
                    warnings: materialized.GroupBy(descriptor => descriptor.Position!.Value).Any(group => group.Count() > 1)
                        ? ["多个切片具有相同空间位置，已使用实例号和文件名作为并列排序依据。"]
                        : []);
            }

            if (materialized.All(descriptor => descriptor.InstanceNumber.HasValue))
            {
                return CreateResult(
                    materialized
                        .OrderBy(descriptor => descriptor.InstanceNumber!.Value)
                        .ThenBy(descriptor => descriptor.Position)
                        .ThenBy(descriptor => Path.GetFileName(descriptor.Path), NaturalFileNameComparer.Instance)
                        .ThenBy(descriptor => descriptor.Path, StringComparer.OrdinalIgnoreCase),
                    VolumeSliceOrderSource.InstanceNumber,
                    warnings: materialized.GroupBy(descriptor => descriptor.InstanceNumber!.Value).Any(group => group.Count() > 1)
                        ? ["多个切片具有相同实例号，已使用空间位置和文件名作为并列排序依据。"]
                        : []);
            }

            if (materialized.All(descriptor => descriptor.AcquisitionTime.HasValue))
            {
                return CreateResult(
                    materialized
                        .OrderBy(descriptor => descriptor.AcquisitionTime!.Value)
                        .ThenBy(descriptor => descriptor.InstanceNumber)
                        .ThenBy(descriptor => Path.GetFileName(descriptor.Path), NaturalFileNameComparer.Instance)
                        .ThenBy(descriptor => descriptor.Path, StringComparer.OrdinalIgnoreCase),
                    VolumeSliceOrderSource.AcquisitionTime,
                    warnings: []);
            }

            return CreateResult(
                materialized
                    .OrderBy(descriptor => Path.GetFileName(descriptor.Path), NaturalFileNameComparer.Instance)
                    .ThenBy(descriptor => descriptor.Path, StringComparer.OrdinalIgnoreCase),
                VolumeSliceOrderSource.NaturalFileName,
                ["未找到完整的切片空间位置、实例号或采集时间元数据，已回退到文件名自然排序；请确认切片顺序。"]);
        }

        public static IReadOnlyList<string> OrderPaths(IEnumerable<string> paths)
        {
            ArgumentNullException.ThrowIfNull(paths);

            return OrderDescriptors(paths.Select(path => new VolumeSliceDescriptor(path))).OrderedPaths;
        }

        /// <summary>
        /// 从相邻的 .slice.json 或同名 .json 侧车文件读取排序元数据。
        /// 侧车文件支持 zPosition/position、instanceNumber、acquisitionTime 字段。
        /// </summary>
        public static async Task<VolumeSliceOrderingResult> OrderPathsWithMetadataAsync(
            IEnumerable<string> paths,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(paths);
            string[] materialized = paths.ToArray();
            ValidatePaths(materialized);
            if (materialized.Length == 0)
            {
                throw new ArgumentException("At least one slice path is required.", nameof(paths));
            }

            VolumeSliceDescriptor[] descriptors = await Task.WhenAll(
                materialized.Select(path => ReadDescriptorAsync(path, cancellationToken))).ConfigureAwait(false);
            return OrderDescriptors(descriptors);
        }

        public static bool HasOrderChanged(IReadOnlyList<string> original, IReadOnlyList<string> ordered)
        {
            ArgumentNullException.ThrowIfNull(original);
            ArgumentNullException.ThrowIfNull(ordered);

            if (original.Count != ordered.Count)
            {
                return true;
            }

            for (int index = 0; index < original.Count; index++)
            {
                if (!string.Equals(original[index], ordered[index], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static VolumeSliceOrderingResult CreateResult(
            IEnumerable<VolumeSliceDescriptor> ordered,
            VolumeSliceOrderSource source,
            IReadOnlyList<string> warnings)
        {
            return new VolumeSliceOrderingResult(
                ordered.ToArray(),
                source,
                source != VolumeSliceOrderSource.NaturalFileName,
                warnings);
        }

        private static void ValidatePaths(IEnumerable<string> paths)
        {
            string[] materialized = paths.ToArray();
            if (materialized.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Slice paths must not be null or whitespace.", nameof(paths));
            }

            if (materialized.Length != materialized.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                throw new ArgumentException("Slice paths must be unique.", nameof(paths));
            }
        }

        private static async Task<VolumeSliceDescriptor> ReadDescriptorAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string sidecarPath in GetSidecarPaths(path))
            {
                if (!File.Exists(sidecarPath))
                {
                    continue;
                }

                try
                {
                    await using FileStream stream = File.OpenRead(sidecarPath);
                    using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                    JsonElement root = document.RootElement;
                    return new VolumeSliceDescriptor(
                        path,
                        TryGetDouble(root, "zPosition") ?? TryGetDouble(root, "position") ?? TryGetDouble(root, "z"),
                        TryGetInt(root, "instanceNumber") ?? TryGetInt(root, "instance"),
                        TryGetDateTime(root, "acquisitionTime") ?? TryGetDateTime(root, "acquiredAt"),
                        TryGetPositiveDouble(root, "spacingX") ?? TryGetPositiveDouble(root, "pixelSpacingX"),
                        TryGetPositiveDouble(root, "spacingY") ?? TryGetPositiveDouble(root, "pixelSpacingY"),
                        TryGetPositiveDouble(root, "spacingZ") ?? TryGetPositiveDouble(root, "sliceSpacing") ?? TryGetPositiveDouble(root, "pixelSpacingZ"));
                }
                catch (JsonException)
                {
                    // An invalid sidecar is reported through the fallback warning rather than
                    // preventing the remaining images from being inspected.
                }
                catch (IOException)
                {
                    // The image itself can still be opened; retain deterministic fallback ordering.
                }
                catch (UnauthorizedAccessException)
                {
                    // The image itself can still be opened; retain deterministic fallback ordering.
                }
            }

            return new VolumeSliceDescriptor(path);
        }

        private static IEnumerable<string> GetSidecarPaths(string path)
        {
            yield return path + ".slice.json";
            string? directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                yield return Path.Combine(directory, fileName + ".json");
            }
        }

        private static double? TryGetDouble(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number)) return number;
            return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number) ? number : null;
        }

        private static double? TryGetPositiveDouble(JsonElement element, string name)
        {
            double? value = TryGetDouble(element, name);
            return value is { } number && number > 0 ? number : null;
        }

        private static int? TryGetInt(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : null;
        }

        private static DateTimeOffset? TryGetDateTime(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value)) return null;
            return value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp)
                ? timestamp
                : null;
        }

        private sealed class NaturalFileNameComparer : IComparer<string?>
        {
            public static NaturalFileNameComparer Instance { get; } = new();

            public int Compare(string? x, string? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                int xIndex = 0;
                int yIndex = 0;
                while (xIndex < x.Length && yIndex < y.Length)
                {
                    char xChar = x[xIndex];
                    char yChar = y[yIndex];
                    bool xIsDigit = char.IsDigit(xChar);
                    bool yIsDigit = char.IsDigit(yChar);

                    if (xIsDigit && yIsDigit)
                    {
                        int xEnd = ReadDigitRunEnd(x, xIndex);
                        int yEnd = ReadDigitRunEnd(y, yIndex);
                        int xSignificantStart = SkipLeadingZeros(x, xIndex, xEnd);
                        int ySignificantStart = SkipLeadingZeros(y, yIndex, yEnd);
                        int xSignificantLength = xEnd - xSignificantStart;
                        int ySignificantLength = yEnd - ySignificantStart;

                        int result = xSignificantLength.CompareTo(ySignificantLength);
                        if (result != 0) return result;

                        result = string.CompareOrdinal(
                            x,
                            xSignificantStart,
                            y,
                            ySignificantStart,
                            xSignificantLength);
                        if (result != 0) return result;

                        // 数值相同（例如 2 与 002）时，让较短的写法先出现，结果仍然稳定。
                        result = (xEnd - xIndex).CompareTo(yEnd - yIndex);
                        if (result != 0) return result;

                        xIndex = xEnd;
                        yIndex = yEnd;
                        continue;
                    }

                    int characterResult = char.ToUpperInvariant(xChar).CompareTo(char.ToUpperInvariant(yChar));
                    if (characterResult != 0) return characterResult;

                    xIndex++;
                    yIndex++;
                }

                return x.Length.CompareTo(y.Length);
            }

            private static int ReadDigitRunEnd(string value, int start)
            {
                int index = start;
                while (index < value.Length && char.IsDigit(value[index]))
                {
                    index++;
                }

                return index;
            }

            private static int SkipLeadingZeros(string value, int start, int end)
            {
                int index = start;
                while (index < end - 1 && value[index] == '0')
                {
                    index++;
                }

                return index;
            }
        }
    }
}
