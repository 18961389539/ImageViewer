using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ImageViewer.Localization;
using ImageViewer.Models;

namespace ImageViewer.Dialogs
{
    internal sealed class CalibrationDialog : Window
    {
        private readonly TextBox _lengthTextBox;
        private readonly TextBox _unitTextBox;
        private readonly TextBox _k1TextBox;
        private readonly TextBox _k2TextBox;
        private readonly TextBox _k3TextBox;
        private readonly TextBox _p1TextBox;
        private readonly TextBox _p2TextBox;
        private readonly TextBox _principalXTextBox;
        private readonly TextBox _principalYTextBox;
        private readonly TextBox _normalizationRadiusTextBox;
        private readonly TextBox _reprojectionErrorTextBox;
        private readonly TextBox _uncertaintyTextBox;
        private readonly TextBox _calibrationIdTextBox;

        public CalibrationDialog(string currentUnit, CameraCalibration? currentCalibration = null)
        {
            Title = UiText.Get("CalibrationDialogTitle");
            Width = 400;
            Height = 660;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stackPanel = new StackPanel { Margin = new Thickness(10) };
            scrollViewer.Content = stackPanel;

            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogPrompt"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogHint"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray });

            _lengthTextBox = new TextBox { Text = "1.0", Margin = new Thickness(0, 0, 0, 8) };
            _unitTextBox = new TextBox { Text = string.IsNullOrWhiteSpace(currentUnit) ? UiText.Get("CalibrationDefaultUnit") : currentUnit };

            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogActualLengthLabel"), Margin = new Thickness(0, 0, 0, 2) });
            stackPanel.Children.Add(_lengthTextBox);
            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogUnitLabel"), Margin = new Thickness(0, 8, 0, 2) });
            stackPanel.Children.Add(_unitTextBox);

            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogDistortionHeader"), Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold });
            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogDistortionHint"), Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray });

            _k1TextBox = AddLabeledField(stackPanel, "CalibrationDialogK1Label", Format(currentCalibration?.K1));
            _k2TextBox = AddLabeledField(stackPanel, "CalibrationDialogK2Label", Format(currentCalibration?.K2));
            _k3TextBox = AddLabeledField(stackPanel, "CalibrationDialogK3Label", Format(currentCalibration?.K3));
            _p1TextBox = AddLabeledField(stackPanel, "CalibrationDialogP1Label", Format(currentCalibration?.TangentialP1, "G6"));
            _p2TextBox = AddLabeledField(stackPanel, "CalibrationDialogP2Label", Format(currentCalibration?.TangentialP2, "G6"));

            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogAccuracyHeader"), Margin = new Thickness(0, 12, 0, 4), FontWeight = FontWeights.SemiBold });
            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogAccuracyHint"), Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray });

            _reprojectionErrorTextBox = AddLabeledField(stackPanel, "CalibrationDialogReprojectionErrorLabel", Format(currentCalibration?.ReprojectionErrorRms, "G6"));
            _uncertaintyTextBox = AddLabeledField(stackPanel, "CalibrationDialogUncertaintyLabel", Format(currentCalibration?.MeasurementUncertaintyPixels, "G6"));
            _calibrationIdTextBox = AddLabeledField(stackPanel, "CalibrationDialogIdLabel", currentCalibration?.CalibrationId ?? string.Empty);

            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogGeometryHeader"), Margin = new Thickness(0, 12, 0, 4), FontWeight = FontWeights.SemiBold });
            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogGeometryHint"), Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray });

            _principalXTextBox = AddLabeledField(stackPanel, "CalibrationDialogPrincipalXLabel", Format(currentCalibration?.PrincipalX, "G8"));
            _principalYTextBox = AddLabeledField(stackPanel, "CalibrationDialogPrincipalYLabel", Format(currentCalibration?.PrincipalY, "G8"));
            _normalizationRadiusTextBox = AddLabeledField(stackPanel, "CalibrationDialogNormalizationRadiusLabel", Format(currentCalibration?.NormalizationRadius, "G8"));

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var okButton = new Button { Content = UiText.Get("DialogButtonOk"), Width = 60, IsDefault = true, Margin = new Thickness(0, 0, 10, 0) };
            okButton.Click += OnOkClick;
            var cancelButton = new Button { Content = UiText.Get("DialogButtonCancel"), Width = 60, IsCancel = true };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stackPanel.Children.Add(buttonPanel);

            Content = scrollViewer;
            Loaded += (_, _) =>
            {
                _lengthTextBox.Focus();
                _lengthTextBox.SelectAll();
            };
        }

        public double Length { get; private set; }

        public string Unit { get; private set; } = "mm";

        public double K1 { get; private set; }

        public double K2 { get; private set; }

        /// <summary>
        /// 对话框采集到的完整畸变模型；所有系数为 0 时为 null（表示不校正）。
        /// Chinese: 只有真正配置了任一畸变项或精度元数据时才提交 CameraCalibration，避免生成空标定把状态栏点亮。
        /// </summary>
        public CameraCalibration? Distortion { get; private set; }

        private static TextBox AddLabeledField(Panel panel, string labelKey, string initialText)
        {
            panel.Children.Add(new TextBlock { Text = UiText.Get(labelKey), Margin = new Thickness(0, 0, 0, 2) });
            var textBox = new TextBox { Text = initialText, Margin = new Thickness(0, 0, 0, 6), Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(textBox);
            return textBox;
        }

        private static string Format(double? value, string format = "F6")
        {
            return value.HasValue ? value.Value.ToString(format, CultureInfo.CurrentCulture) : "0";
        }

        private static double ParseOrDefault(TextBox textBox, double fallback = 0)
        {
            return double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
                ? value
                : fallback;
        }

        private void OnOkClick(object? sender, RoutedEventArgs e)
        {
            if (!TryCommit())
            {
                MessageBox.Show(this, UiText.Get("CalibrationDialogInvalidLengthMessage"), UiText.Get("CalibrationDialogWarningTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 从输入框读取并校验，成功则写入 <see cref="Length"/>/<see cref="Unit"/>/<see cref="Distortion"/> 并返回 true。
        /// Chinese: 与 <c>DialogResult</c>/<c>Close</c> 解耦后可在无窗口环境下直测解析与校验逻辑；
        /// OnOkClick 只负责弹错误提示和关闭对话框。
        /// </summary>
        internal bool TryCommit()
        {
            if (!double.TryParse(_lengthTextBox.Text, out double length) || length <= 0)
            {
                return false;
            }

            Length = length;
            Unit = string.IsNullOrWhiteSpace(_unitTextBox.Text) ? UiText.Get("CalibrationDefaultUnit") : _unitTextBox.Text.Trim();

            double k1 = ParseOrDefault(_k1TextBox);
            double k2 = ParseOrDefault(_k2TextBox);
            double k3 = ParseOrDefault(_k3TextBox);
            double p1 = ParseOrDefault(_p1TextBox);
            double p2 = ParseOrDefault(_p2TextBox);
            double reprojectionError = ParseOrDefault(_reprojectionErrorTextBox);
            double uncertainty = ParseOrDefault(_uncertaintyTextBox);
            string? calibrationId = string.IsNullOrWhiteSpace(_calibrationIdTextBox.Text) ? null : _calibrationIdTextBox.Text.Trim();
            double principalX = ParseOrDefault(_principalXTextBox);
            double principalY = ParseOrDefault(_principalYTextBox);
            double normalizationRadius = ParseOrDefault(_normalizationRadiusTextBox);

            K1 = k1;
            K2 = k2;
            Distortion = CreateCalibration(k1, k2, k3, p1, p2, principalX, principalY, normalizationRadius, reprojectionError, uncertainty, calibrationId);
            return true;
        }

        /// <summary>
        /// 组装畸变模型。主点 / 归一化半径留空（或非法）时按图像几何推断，交由调用方在拿到图像尺寸后补齐。
        /// Chinese: NormalizationRadius 默认 1.0 与 CameraCalibration 的默认值保持一致，避免"未配置"被误判为"零半径"。
        /// </summary>
        private static CameraCalibration? CreateCalibration(
            double k1,
            double k2,
            double k3,
            double p1,
            double p2,
            double principalX,
            double principalY,
            double normalizationRadius,
            double reprojectionError,
            double uncertainty,
            string? calibrationId)
        {
            bool hasDistortion = Math.Abs(k1) > 1e-12 || Math.Abs(k2) > 1e-12 || Math.Abs(k3) > 1e-12
                || Math.Abs(p1) > 1e-12 || Math.Abs(p2) > 1e-12;
            bool hasAccuracyMetadata = reprojectionError > 0 || uncertainty > 0 || !string.IsNullOrWhiteSpace(calibrationId);
            if (!hasDistortion && !hasAccuracyMetadata)
            {
                return null;
            }

            return new CameraCalibration
            {
                K1 = k1,
                K2 = k2,
                K3 = k3,
                TangentialP1 = p1,
                TangentialP2 = p2,
                PrincipalX = principalX,
                PrincipalY = principalY,
                NormalizationRadius = normalizationRadius > 0 ? normalizationRadius : 1.0,
                ReprojectionErrorRms = reprojectionError,
                MeasurementUncertaintyPixels = uncertainty,
                CalibrationId = calibrationId,
                CalibratedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }
}
