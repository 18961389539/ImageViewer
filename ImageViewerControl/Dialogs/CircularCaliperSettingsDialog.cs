using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ImageViewer.Localization;
using ImageViewer.Models;

namespace ImageViewer.Dialogs
{
    /// <summary>
    /// 圆卡尺设置对话框
    /// Chinese: 在基类骨架上声明圆卡尺的单边检测参数字段，并把控件值写回 CircularCaliperMeasureRoi。
    /// English: Declares single-edge caliper fields on the shared base chrome and writes them back to CircularCaliperMeasureRoi.
    /// </summary>
    internal sealed class CircularCaliperSettingsDialog : CaliperSettingsDialogBase
    {
        private readonly CircularCaliperMeasureRoi _model;
        private readonly TextBox _caliperCountTextBox;
        private readonly TextBox _searchRangeTextBox;
        private readonly TextBox _samplingHalfWidthTextBox;
        private readonly TextBox _minimumGradientTextBox;
        private readonly TextBox _minimumValidCalipersTextBox;
        private readonly TextBox _outlierThresholdTextBox;
        private readonly TextBox _edgeSelectionTextBox;
        private readonly ComboBox _polarityComboBox;

        public CircularCaliperSettingsDialog(CircularCaliperMeasureRoi model, Action<CircularCaliperMeasureRoi>? previewAction)
            : base(
                "CircularCaliperDialogTitle",
                "CircularCaliperDialogHeading",
                "CircularCaliperDialogDescription",
                360,
                450,
                model,
                preview => previewAction?.Invoke((CircularCaliperMeasureRoi)preview))
        {
            _model = model;

            _caliperCountTextBox = AddField(PrimaryPanel, UiText.Get("CaliperFieldCount"), _model.CaliperCount.ToString(CultureInfo.InvariantCulture));
            _searchRangeTextBox = AddField(PrimaryPanel, UiText.Get("CaliperFieldSearchRange"), _model.CaliperSearchRange.ToString(CultureInfo.InvariantCulture));

            _samplingHalfWidthTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldSamplingHalfWidth"), _model.CaliperSamplingHalfWidth.ToString(CultureInfo.InvariantCulture));
            _minimumGradientTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldMinimumGradient"), _model.CaliperMinimumGradient.ToString(CultureInfo.InvariantCulture));
            _minimumValidCalipersTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldMinimumValid"), _model.MinimumValidCalipers.ToString(CultureInfo.InvariantCulture));
            _outlierThresholdTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldOutlierThreshold"), _model.CaliperOutlierThreshold.ToString(CultureInfo.InvariantCulture));
            _edgeSelectionTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldEdgeSelection"), _model.EdgeSelection.ToString(CultureInfo.InvariantCulture));

            AdvancedPanel.Children.Add(new TextBlock { Text = UiText.Get("CaliperFieldPolarity"), Margin = new Thickness(0, 8, 0, 2) });
            _polarityComboBox = new ComboBox
            {
                ItemsSource = Enum.GetValues<CaliperEdgePolarity>(),
                SelectedItem = _model.CaliperEdgePolarity,
                Margin = new Thickness(0, 0, 0, 4)
            };
            AdvancedPanel.Children.Add(_polarityComboBox);

            EnableLivePreview(_caliperCountTextBox);
            EnableLivePreview(_searchRangeTextBox);
            EnableLivePreview(_samplingHalfWidthTextBox);
            EnableLivePreview(_minimumGradientTextBox);
            EnableLivePreview(_minimumValidCalipersTextBox);
            EnableLivePreview(_outlierThresholdTextBox);
            EnableLivePreview(_edgeSelectionTextBox);
            EnableLivePreview(_polarityComboBox);
        }

        public CircularCaliperMeasureRoi? Result => AppliedModel as CircularCaliperMeasureRoi;

        protected override object CloneModelForPreview() => _model.Clone();

        protected override bool TryApplyInputs(out string errorMessage)
        {
            errorMessage = UiText.Get("CircularCaliperDialogInvalidMessage");
            if (!TryParsePositiveInt(_caliperCountTextBox.Text, out int caliperCount, minValue: 6) ||
                !TryParsePositiveInt(_searchRangeTextBox.Text, out int searchRange, minValue: 1) ||
                !TryParseNonNegativeInt(_samplingHalfWidthTextBox.Text, out int samplingHalfWidth) ||
                !TryParseNonNegativeDouble(_minimumGradientTextBox.Text, out double minimumGradient) ||
                !TryParsePositiveInt(_minimumValidCalipersTextBox.Text, out int minimumValidCalipers, minValue: 3) ||
                !TryParseNonNegativeDouble(_outlierThresholdTextBox.Text, out double outlierThreshold) ||
                !TryParsePositiveInt(_edgeSelectionTextBox.Text, out int edgeSelection, minValue: 1) ||
                _polarityComboBox.SelectedItem is not CaliperEdgePolarity polarity)
            {
                return false;
            }

            _model.CaliperCount = caliperCount;
            _model.CaliperSearchRange = searchRange;
            _model.CaliperSamplingHalfWidth = samplingHalfWidth;
            _model.CaliperMinimumGradient = minimumGradient;
            _model.MinimumValidCalipers = minimumValidCalipers;
            _model.CaliperOutlierThreshold = outlierThreshold;
            _model.EdgeSelection = Math.Clamp(edgeSelection, 1, 8);
            _model.CaliperEdgePolarity = polarity;
            return true;
        }
    }
}