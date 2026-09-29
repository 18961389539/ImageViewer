using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageViewer.Controls;
using ImageViewer.Services;
using ImageViewerDemo.Localization;
using ImageViewer.Models;

namespace ImageViewerDemo;

public partial class MainWindow : Window, IDisposable
{
    private CancellationTokenSource? _openCancellation;
    private bool _isOpeningData;
    private bool _allowClose;
    private bool _closePromptActive;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnMainWindowLoaded;
    }

    /// <summary>
    /// 启动时自动加载随程序分发的单通道灰度测试图。
    /// Chinese: 便于直接演示测量与标注功能；文件缺失或加载失败时静默跳过，不阻塞主窗口。
    /// English: Loads the bundled single-channel gray test image on startup for immediate measurement demos.
    /// </summary>
    private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources", "TestImage.png");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            if (!TryBeginOpen(DemoText.Get("ImageLoadingStatus"), isIndeterminate: true))
            {
                return;
            }

            BitmapSource bitmap = await LoadBitmapAsync(path, _openCancellation!.Token);
            Viewer.ImageSource = bitmap;
            Viewer.MarkDocumentClean();
        }
        catch (OperationCanceledException)
        {
            SetOpenStatus(DemoText.Get("OpenCancelledStatus"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, DemoText.Get("OpenImageErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndOpen();
        }
    }


    private async void OnOpenImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = DemoText.Get("OpenImageDialogTitle"),
            Filter = DemoText.Get("OpenImageDialogFilter")
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await OpenImageAsync(dialog.FileName);
    }

    private async Task OpenImageAsync(string path)
    {
        if (!TryBeginOpen(DemoText.Get("ImageLoadingStatus"), isIndeterminate: true))
        {
            return;
        }

        try
        {
            BitmapSource bitmap = await LoadBitmapAsync(path, _openCancellation!.Token);
            _openCancellation.Token.ThrowIfCancellationRequested();
            Viewer.Volume = null;
            Viewer.ImageSource = bitmap;
            Viewer.DisplayMode = AdaptiveDisplayMode.Auto;
            ClearOpenSummary();
        }
        catch (OperationCanceledException)
        {
            SetOpenStatus(DemoText.Get("OpenCancelledStatus"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, DemoText.Get("OpenImageErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndOpen();
        }
    }

    private async void OnOpenVolumeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = DemoText.Get("OpenVolumeDialogTitle"),
            Filter = DemoText.Get("OpenVolumeDialogFilter"),
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (dialog.FileNames.Length < 2)
        {
            MessageBox.Show(this, DemoText.Get("OpenVolumeNeedMultiple"), DemoText.Get("OpenVolumeErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryBeginOpen(DemoText.Get("VolumeInspectingStatus"), isIndeterminate: true))
        {
            return;
        }

        try
        {
            CancellationToken cancellationToken = _openCancellation!.Token;
            string[] selectedPaths = dialog.FileNames;
            VolumeSliceOrderingResult ordering = await VolumeSliceOrderingService.OrderPathsWithMetadataAsync(selectedPaths, cancellationToken);
            IReadOnlyList<string> orderedPaths = ordering.OrderedPaths;
            List<string> importWarnings = ordering.Warnings.ToList();
            (double Value, string Source) spacingX = ResolveSpacing(ordering.Slices.Select(slice => slice.SpacingX).ToArray(), 1.0, "X", importWarnings);
            (double Value, string Source) spacingY = ResolveSpacing(ordering.Slices.Select(slice => slice.SpacingY).ToArray(), 1.0, "Y", importWarnings);
            (double Value, string Source) spacingZ = ResolveSpacing(ordering.Slices.Select(slice => slice.SpacingZ).ToArray(), 1.0, "Z", importWarnings);
            string orderSource = GetOrderSource(ordering.Source);
            bool orderNeedsConfirmation = !ordering.IsAuthoritative ||
                importWarnings.Count > 0 ||
                VolumeSliceOrderingService.HasOrderChanged(selectedPaths, orderedPaths);
            if (orderNeedsConfirmation)
            {
                MessageBoxResult orderConfirmation = MessageBox.Show(
                    this,
                    DemoText.Format(
                        "OpenVolumeOrderChangedMessage",
                        orderedPaths.Count,
                        orderSource,
                        Path.GetFileName(orderedPaths[0]),
                        Path.GetFileName(orderedPaths[Math.Min(1, orderedPaths.Count - 1)]),
                        Path.GetFileName(orderedPaths[Math.Max(0, orderedPaths.Count - 2)]),
                        Path.GetFileName(orderedPaths[^1]),
                        importWarnings.Count == 0 ? string.Empty : $"\n{string.Join(Environment.NewLine, importWarnings)}"),
                    DemoText.Get("OpenVolumeOrderChangedTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (orderConfirmation != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            openProgressBar.IsIndeterminate = false;
            openProgressBar.Value = 0;
            BitmapSource[] slices = new BitmapSource[orderedPaths.Count];
            int? expectedWidth = null;
            int? expectedHeight = null;
            for (int index = 0; index < orderedPaths.Count; index++)
            {
                int sliceNumber = index + 1;
                SetOpenStatus(DemoText.Format("VolumeLoadingProgress", sliceNumber, orderedPaths.Count));
                slices[index] = await LoadBitmapAsync(orderedPaths[index], cancellationToken);
                expectedWidth ??= slices[index].PixelWidth;
                expectedHeight ??= slices[index].PixelHeight;
                if (slices[index].PixelWidth != expectedWidth || slices[index].PixelHeight != expectedHeight)
                {
                    throw new InvalidOperationException(DemoText.Format(
                        "OpenVolumeDimensionMismatch",
                        sliceNumber,
                        slices[index].PixelWidth,
                        slices[index].PixelHeight,
                        expectedWidth,
                        expectedHeight));
                }
                openProgressBar.Value = sliceNumber * 100d / orderedPaths.Count;
            }

            long estimatedBytes = EstimateVolumeBytes(slices, bytesPerPixel: 4);
            if (estimatedBytes > 768L * 1024 * 1024)
            {
                MessageBoxResult memoryConfirmation = MessageBox.Show(
                    this,
                    DemoText.Format("OpenVolumeMemoryWarning", estimatedBytes / (1024d * 1024d)),
                    DemoText.Get("OpenVolumeErrorTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (memoryConfirmation != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var orderMetadata = new VolumeSliceOrderMetadata(
                ordering.Source.ToString(),
                ordering.IsAuthoritative,
                ordering.Slices.Select(slice => new VolumeSliceProvenance(
                    slice.Path,
                    slice.Position,
                    slice.InstanceNumber,
                    slice.AcquisitionTime)));
            cancellationToken.ThrowIfCancellationRequested();
            SetOpenStatus(DemoText.Get("VolumeAssemblingStatus"));
            VolumeData volume = await Task.Run(
                () => new VolumeData(slices, spacingX.Value, spacingY.Value, spacingZ.Value, orderMetadata),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Viewer.Volume = volume;
            Viewer.ImageSource = volume.GetAxialSlice(0);
            Viewer.DisplayMode = AdaptiveDisplayMode.Auto;
            string summary = DemoText.Format(
                "OpenVolumeLoaded",
                slices.Length,
                expectedWidth ?? 0,
                expectedHeight ?? 0,
                spacingX.Value,
                spacingY.Value,
                spacingZ.Value);
            summary += Environment.NewLine + DemoText.Format(
                "OpenVolumeSourceSummary",
                orderSource,
                spacingX.Source,
                spacingY.Source,
                spacingZ.Source);
            if (importWarnings.Count > 0)
            {
                summary += Environment.NewLine + DemoText.Format(
                    "OpenVolumeWarningSummary",
                    string.Join(Environment.NewLine, importWarnings));
            }

            SetOpenSummary(summary);
        }
        catch (OperationCanceledException)
        {
            SetOpenStatus(DemoText.Get("OpenCancelledStatus"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, DemoText.Get("OpenVolumeErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndOpen();
        }
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !Viewer.IsDirty)
        {
            return;
        }

        e.Cancel = true;
        if (_closePromptActive)
        {
            return;
        }

        _closePromptActive = true;
        try
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                DemoText.Get("UnsavedCloseMessage"),
                DemoText.Get("UnsavedCloseTitle"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);

            if (result == MessageBoxResult.Cancel)
            {
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                await Viewer.SaveSessionAsync();
                if (Viewer.IsDirty)
                {
                    MessageBox.Show(this, DemoText.Get("UnsavedCloseSaveFailed"), DemoText.Get("UnsavedCloseTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _allowClose = true;
            // Closing is a synchronous, cancelable WPF event. Save may yield
            // back to the dispatcher, so calling Close() here re-enters the
            // window while its original Closing event is still active.
            // Queue the final close until that event has returned.
            _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.ApplicationIdle);
        }
        finally
        {
            _closePromptActive = false;
        }
    }

    private void OnCancelOpenClick(object sender, RoutedEventArgs e)
    {
        _openCancellation?.Cancel();
        cancelOpenButton.IsEnabled = false;
    }

    private bool TryBeginOpen(string status, bool isIndeterminate)
    {
        if (_isOpeningData)
        {
            return false;
        }

        _isOpeningData = true;
        _openCancellation = new CancellationTokenSource();
        openImageButton.IsEnabled = false;
        openVolumeButton.IsEnabled = false;
        cancelOpenButton.IsEnabled = true;
        cancelOpenButton.Visibility = Visibility.Visible;
        openProgressPanel.Visibility = Visibility.Visible;
        openProgressBar.IsIndeterminate = isIndeterminate;
        openProgressBar.Value = 0;
        SetOpenStatus(status);
        return true;
    }

    private void SetOpenStatus(string status)
    {
        openStatusText.Text = status;
    }

    private void SetOpenSummary(string summary)
    {
        openSummaryText.Text = summary;
        openSummaryText.Visibility = Visibility.Visible;
    }

    private void ClearOpenSummary()
    {
        openSummaryText.Text = string.Empty;
        openSummaryText.Visibility = Visibility.Collapsed;
    }

    private void EndOpen()
    {
        _openCancellation?.Dispose();
        _openCancellation = null;
        _isOpeningData = false;
        openImageButton.IsEnabled = true;
        openVolumeButton.IsEnabled = true;
        cancelOpenButton.IsEnabled = true;
        cancelOpenButton.Visibility = Visibility.Collapsed;
        openProgressPanel.Visibility = Visibility.Collapsed;
    }

    private static async Task<BitmapSource> LoadBitmapAsync(string filePath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            BitmapSource bitmap = LoadBitmap(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        }, cancellationToken);
    }

    private static (double Value, string Source) ResolveSpacing(
        IReadOnlyList<double?> values,
        double fallback,
        string axis,
        ICollection<string> warnings)
    {
        double[] valid = values
            .Where(value => value is { } number && double.IsFinite(number) && number > 0)
            .Select(value => value!.Value)
            .ToArray();
        if (valid.Length == 0)
        {
            warnings.Add(DemoText.Format("OpenVolumeSpacingWarning", axis));
            return (fallback, "默认回退值 1.0 mm");
        }

        double selected = valid[0];
        bool isConsistent = valid.Length == values.Count && valid.All(value => Math.Abs(value - selected) <= 1e-9);
        if (!isConsistent)
        {
            warnings.Add(DemoText.Format("OpenVolumeSpacingWarning", axis));
        }

        return (selected, isConsistent ? "侧车元数据" : "侧车元数据首个有效值");
    }

    private static string GetOrderSource(VolumeSliceOrderSource source)
    {
        return source switch
        {
            VolumeSliceOrderSource.ExplicitPosition => "空间位置元数据",
            VolumeSliceOrderSource.InstanceNumber => "实例号元数据",
            VolumeSliceOrderSource.AcquisitionTime => "采集时间元数据",
            _ => "文件名自然排序（缺少完整元数据）"
        };
    }

    private static long EstimateVolumeBytes(IReadOnlyList<BitmapSource> slices, int bytesPerPixel)
    {
        if (slices.Count == 0)
        {
            return 0;
        }

        return checked((long)slices[0].PixelWidth * slices[0].PixelHeight * slices.Count * bytesPerPixel);
    }

    private static BitmapSource LoadBitmap(string filePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        _openCancellation?.Cancel();
        _openCancellation?.Dispose();
        _openCancellation = null;
        GC.SuppressFinalize(this);
    }
}
