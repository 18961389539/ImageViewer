using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ImageViewer.Localization;
using ImageViewer.Models;

namespace ImageViewer.Dialogs
{
    /// <summary>
    /// 直线卡尺设置对话框
    /// Chinese: 在基类骨架上声明直线卡尺的单边检测参数字段，并把控件值写回 LineCaliperMeasureRoi。
    /// English: Declares single-edge caliper fields on the shared base chrome and writes them back to LineCaliperMeasureRoi.
    /// </summary>
    internal sealed class LineCaliperSettingsDialog : CaliperSettingsDialogBase
    {
        private readonly LineCaliperMeasureRoi _model;
        private readonly TextBox _caliperCountTextBox;
        private readonly TextBox _searchRangeTextBox;
        private readonly TextBox _samplingHalfWidthTextBox;
        private readonly TextBox _edgeSigmaTextBox;
        private readonly TextBox _minimumGradientTextBox;
        private readonly TextBox _minimumValidCalipersTextBox;
        private readonly TextBox _outlierThresholdTextBox;
        private readonly TextBox _edgeSelectionTextBox;
        private readonly ComboBox _polarityComboBox;

        public LineCaliperSettingsDialog(LineCaliperMeasureRoi model, Action<LineCaliperMeasureRoi>? previewAction)
            : base(
                "LineCaliperDialogTitle",
                "LineCaliperDialogHeading",
                "LineCaliperDialogDescription",
                360,
                450,
                model,
                preview => previewAction?.Invoke((LineCaliperMeasureRoi)preview))
        {
            _model = model;

            _caliperCountTextBox = AddField(PrimaryPanel, UiText.Get("CaliperFieldCount"), _model.CaliperCount.ToString(CultureInfo.InvariantCulture));
            _searchRangeTextBox = AddField(PrimaryPanel, UiText.Get("CaliperFieldSearchRange"), _model.CaliperSearchRange.ToString(CultureInfo.InvariantCulture));

            _samplingHalfWidthTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldSamplingHalfWidth"), _model.CaliperSamplingHalfWidth.ToString(CultureInfo.InvariantCulture));
            _edgeSigmaTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldEdgeSigma"), _model.CaliperEdgeSigma.ToString(CultureInfo.InvariantCulture));
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
            EnableLivePreview(_edgeSigmaTextBox);
            EnableLivePreview(_minimumGradientTextBox);
            EnableLivePreview(_minimumValidCalipersTextBox);
            EnableLivePreview(_outlierThresholdTextBox);
            EnableLivePreview(_edgeSelectionTextBox);
            EnableLivePreview(_polarityComboBox);
        }

        public LineCaliperMeasureRoi? Result => AppliedModel as LineCaliperMeasureRoi;

        protected override object CloneModelForPreview() => _model.Clone();

        protected override bool TryApplyInputs(out string errorMessage)
        {
            errorMessage = UiText.Get("LineCaliperDialogInvalidMessage");
            if (!TryParsePositiveInt(_caliperCountTextBox.Text, out int caliperCount, minValue: 6) ||
                !TryParsePositiveInt(_searchRangeTextBox.Text, out int searchRange, minValue: 1) ||
                !TryParseNonNegativeInt(_samplingHalfWidthTextBox.Text, out int samplingHalfWidth) ||
                !TryParseNonNegativeDouble(_edgeSigmaTextBox.Text, out double edgeSigma) || edgeSigma < 0.5 || edgeSigma > 5.0 ||
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
            _model.CaliperEdgeSigma = edgeSigma;
            _model.CaliperMinimumGradient = minimumGradient;
            _model.MinimumValidCalipers = minimumValidCalipers;
            _model.CaliperOutlierThreshold = outlierThreshold;
            _model.EdgeSelection = Math.Clamp(edgeSelection, 1, 8);
            _model.CaliperEdgePolarity = polarity;
            return true;
        }
    }
}
