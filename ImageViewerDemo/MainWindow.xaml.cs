using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageViewer.Controls;
using ImageViewer.Services;
using ImageViewerDemo.Localization;
using ImageViewer.Models;

namespace ImageViewerDemo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LoadStartupImage();
    }

    /// <summary>
    /// 启动时自动加载随程序分发的单通道灰度测试图。
    /// Chinese: 便于直接演示测量与标注功能；文件缺失或加载失败时静默跳过，不阻塞主窗口。
    /// English: Loads the bundled single-channel gray test image on startup for immediate measurement demos.
    /// </summary>
    private void LoadStartupImage()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources", "TestImage.png");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            Viewer.ImageSource = LoadBitmap(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, DemoText.Get("OpenImageErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private void OnOpenImageClick(object sender, RoutedEventArgs e)
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

        try
        {
            Viewer.Volume = null;
            Viewer.ImageSource = LoadBitmap(dialog.FileName);
            Viewer.DisplayMode = AdaptiveDisplayMode.Auto;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, DemoText.Get("OpenImageErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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

        try
        {
            string[] selectedPaths = dialog.FileNames;
            VolumeSliceOrderingResult ordering = await VolumeSliceOrderingService.OrderPathsWithMetadataAsync(selectedPaths);
            IReadOnlyList<string> orderedPaths = ordering.OrderedPaths;
            bool orderNeedsConfirmation = !ordering.IsAuthoritative ||
                ordering.Warnings.Count > 0 ||
                VolumeSliceOrderingService.HasOrderChanged(selectedPaths, orderedPaths);
            if (orderNeedsConfirmation)
            {
                string orderSource = ordering.Source switch
                {
                    VolumeSliceOrderSource.ExplicitPosition => "空间位置元数据",
                    VolumeSliceOrderSource.InstanceNumber => "实例号元数据",
                    VolumeSliceOrderSource.AcquisitionTime => "采集时间元数据",
                    _ => "文件名自然排序（缺少完整元数据）"
                };
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
                        ordering.Warnings.Count == 0 ? string.Empty : $"\n{string.Join(Environment.NewLine, ordering.Warnings)}"),
                    DemoText.Get("OpenVolumeOrderChangedTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (orderConfirmation != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            BitmapSource[] slices = await Task.Run(() => orderedPaths.Select(LoadBitmap).ToArray());
            var orderMetadata = new VolumeSliceOrderMetadata(
                ordering.Source.ToString(),
                ordering.IsAuthoritative,
                ordering.Slices.Select(slice => new VolumeSliceProvenance(
                    slice.Path,
                    slice.Position,
                    slice.InstanceNumber,
                    slice.AcquisitionTime)));
            var volume = new VolumeData(slices, orderMetadata: orderMetadata);
            Viewer.Volume = volume;
            Viewer.ImageSource = volume.GetAxialSlice(0);
            Viewer.DisplayMode = AdaptiveDisplayMode.Auto;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, DemoText.Get("OpenVolumeErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
}
