using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ImageViewer.Controls
{
    /// <summary>
    /// Chinese: 记录"由文件加载出来的位图实例"对应的路径。EXIF 旋正会把 BitmapImage 换成 TransformedBitmap，
    /// 此时 UriSource 不再可用；用弱引用表按实例关联路径，外部直接设置 ImageSource 时不受影响。
    /// English: Associates a loaded bitmap instance with its source path; EXIF correction replaces BitmapImage with
    /// TransformedBitmap, so UriSource alone is no longer sufficient. Weak keys keep host-supplied sources unaffected.
    /// </summary>
    internal static class ImageViewerImageSourceRegistry
    {
        private static readonly ConditionalWeakTable<ImageSource, PathHolder> Paths = new();

        internal static void Register(ImageSource source, string filePath, FileStream? cacheLease = null, string? sha256 = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            Paths.Remove(source);
            Paths.Add(source, new PathHolder(filePath, cacheLease, sha256));
        }

        internal static bool TryGetPath(ImageSource? source, out string filePath)
        {
            if (source is not null && Paths.TryGetValue(source, out PathHolder? holder))
            {
                filePath = holder.Path;
                return true;
            }

            filePath = string.Empty;
            return false;
        }

        internal static string? GetSha256(ImageSource? source) =>
            source != null && Paths.TryGetValue(source, out PathHolder? holder) ? holder.Sha256 : null;

        private sealed class PathHolder
        {
            public PathHolder(string path, FileStream? cacheLease, string? sha256)
            {
                Path = path;
                CacheLease = cacheLease;
                Sha256 = sha256;
            }

            public string Path { get; }

            public string? Sha256 { get; }

            // Retain the cross-process lease for exactly as long as the bitmap is reachable.
            public FileStream? CacheLease { get; }
        }
    }
}
