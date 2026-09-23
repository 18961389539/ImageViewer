using System.Windows;
using System.Windows.Controls;
using ImageViewer.Localization;

namespace ImageViewer.Dialogs
{
    internal sealed class CalibrationDialog : Window
    {
        private readonly TextBox _lengthTextBox;
        private readonly TextBox _unitTextBox;
        private readonly TextBox _k1TextBox;
        private readonly TextBox _k2TextBox;

        public CalibrationDialog(string currentUnit)
        {
            Title = UiText.Get("CalibrationDialogTitle");
            Width = 340;
            Height = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var stackPanel = new StackPanel { Margin = new Thickness(10) };
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

            _k1TextBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 0, 6), Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogK1Label"), Margin = new Thickness(0, 0, 0, 2) });
            stackPanel.Children.Add(_k1TextBox);

            _k2TextBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 0, 6), Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
            stackPanel.Children.Add(new TextBlock { Text = UiText.Get("CalibrationDialogK2Label"), Margin = new Thickness(0, 0, 0, 2) });
            stackPanel.Children.Add(_k2TextBox);

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

            Content = stackPanel;
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

        private void OnOkClick(object? sender, RoutedEventArgs e)
        {
            if (!double.TryParse(_lengthTextBox.Text, out double length) || length <= 0)
            {
                MessageBox.Show(this, UiText.Get("CalibrationDialogInvalidLengthMessage"), UiText.Get("CalibrationDialogWarningTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _ = double.TryParse(_k1TextBox.Text, out double k1);
            _ = double.TryParse(_k2TextBox.Text, out double k2);

            Length = length;
            Unit = string.IsNullOrWhiteSpace(_unitTextBox.Text) ? UiText.Get("CalibrationDefaultUnit") : _unitTextBox.Text.Trim();
            K1 = k1;
            K2 = k2;
            DialogResult = true;
            Close();
        }
    }
}