using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageViewerDemo.Localization;

namespace ImageViewerDemo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
            Viewer.ImageSource = LoadBitmap(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, DemoText.Get("OpenImageErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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