using System.Windows.Media.Imaging;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    internal sealed record RoiAnalysisBatchCsvRow(
        string SourcePath,
        string Status,
        string? Error,
        RoiBase Roi,
        BitmapSource? Bitmap,
        double PixelSize,
        string? PhysicalUnit,
        CameraCalibration? Calibration);
}
