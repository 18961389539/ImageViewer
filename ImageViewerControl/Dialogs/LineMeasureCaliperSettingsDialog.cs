using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ImageViewer.Localization;
using ImageViewer.Models;

namespace ImageViewer.Dialogs
{
    /// <summary>
    /// 直线测量卡尺设置对话框
    /// Chinese: 在基类骨架上声明直线测量卡尺的参数字段，并把控件值写回 CaliperMeasureRoi。
    /// English: Declares line-measure caliper fields on the shared base chrome and writes them back to CaliperMeasureRoi.
    /// </summary>
    internal sealed class LineMeasureCaliperSettingsDialog : CaliperSettingsDialogBase
    {
        private readonly CaliperMeasureRoi _model;
        private readonly TextBox _caliperCountTextBox;
        private readonly TextBox _searchRangeTextBox;
        private readonly TextBox _samplingHalfWidthTextBox;
        private readonly TextBox _regionLengthTextBox;
        private readonly TextBox _minimumGradientTextBox;
        private readonly TextBox _minimumValidCalipersTextBox;
        private readonly TextBox _outlierThresholdTextBox;
        private readonly TextBox _minimumEdgeGapTextBox;
        private readonly TextBox _nominalEdgeGapTextBox;
        private readonly TextBox _nominalEdgeGapToleranceTextBox;
        private readonly ComboBox _polarityComboBox;

        public LineMeasureCaliperSettingsDialog(CaliperMeasureRoi model, Action<CaliperMeasureRoi>? previewAction)
            : base(
                "LineMeasureCaliperDialogTitle",
                "LineMeasureCaliperDialogHeading",
                "LineMeasureCaliperDialogDescription",
                360,
                480,
                model,
                preview => previewAction?.Invoke((CaliperMeasureRoi)preview))
        {
            _model = model;

            _caliperCountTextBox = AddField(PrimaryPanel, UiText.Get("CaliperFieldCount"), _model.CaliperCount.ToString(CultureInfo.InvariantCulture));
            _searchRangeTextBox = AddField(PrimaryPanel, UiText.Get("CaliperFieldSearchRange"), _model.CaliperSearchRange.ToString(CultureInfo.InvariantCulture));
            _regionLengthTextBox = AddField(PrimaryPanel, UiText.Get("CaliperFieldRegionLength"), _model.CaliperRegionLength.ToString(CultureInfo.InvariantCulture));

            _samplingHalfWidthTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldSamplingHalfWidth"), _model.CaliperSamplingHalfWidth.ToString(CultureInfo.InvariantCulture));
            _minimumGradientTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldMinimumGradient"), _model.CaliperMinimumGradient.ToString(CultureInfo.InvariantCulture));
            _minimumValidCalipersTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldMinimumValid"), _model.MinimumValidCalipers.ToString(CultureInfo.InvariantCulture));
            _outlierThresholdTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldOutlierThreshold"), _model.CaliperOutlierThreshold.ToString(CultureInfo.InvariantCulture));
            _minimumEdgeGapTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldMinimumEdgeGap"), _model.MinimumEdgeGap.ToString(CultureInfo.InvariantCulture));
            _nominalEdgeGapTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldNominalEdgeGap"), _model.NominalEdgeGap.ToString(CultureInfo.InvariantCulture));
            _nominalEdgeGapToleranceTextBox = AddField(AdvancedPanel, UiText.Get("CaliperFieldNominalEdgeGapTolerance"), _model.NominalEdgeGapTolerance.ToString(CultureInfo.InvariantCulture));

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
            EnableLivePreview(_regionLengthTextBox);
            EnableLivePreview(_minimumGradientTextBox);
            EnableLivePreview(_minimumValidCalipersTextBox);
            EnableLivePreview(_outlierThresholdTextBox);
            EnableLivePreview(_minimumEdgeGapTextBox);
            EnableLivePreview(_nominalEdgeGapTextBox);
            EnableLivePreview(_nominalEdgeGapToleranceTextBox);
            EnableLivePreview(_polarityComboBox);
        }

        public CaliperMeasureRoi? Result => AppliedModel as CaliperMeasureRoi;

        protected override object CloneModelForPreview() => _model.Clone();

        protected override bool TryApplyInputs(out string errorMessage)
        {
            errorMessage = UiText.Get("LineMeasureCaliperDialogInvalidMessage");
            if (!TryParsePositiveInt(_caliperCountTextBox.Text, out int caliperCount, minValue: 3) ||
                !TryParsePositiveInt(_searchRangeTextBox.Text, out int searchRange, minValue: 1) ||
                !TryParseNonNegativeInt(_samplingHalfWidthTextBox.Text, out int samplingHalfWidth) ||
                !TryParseNonNegativeDouble(_regionLengthTextBox.Text, out double regionLength) ||
                !TryParseNonNegativeDouble(_minimumGradientTextBox.Text, out double minimumGradient) ||
                !TryParsePositiveInt(_minimumValidCalipersTextBox.Text, out int minimumValidCalipers, minValue: 2) ||
                !TryParseNonNegativeDouble(_outlierThresholdTextBox.Text, out double outlierThreshold) ||
                !TryParseNonNegativeDouble(_minimumEdgeGapTextBox.Text, out double minimumEdgeGap) ||
                !TryParseNonNegativeDouble(_nominalEdgeGapTextBox.Text, out double nominalEdgeGap) ||
                !TryParseNonNegativeDouble(_nominalEdgeGapToleranceTextBox.Text, out double nominalEdgeGapTolerance) ||
                _polarityComboBox.SelectedItem is not CaliperEdgePolarity polarity)
            {
                return false;
            }

            _model.CaliperCount = caliperCount;
            _model.CaliperSearchRange = searchRange;
            _model.CaliperSamplingHalfWidth = samplingHalfWidth;
            _model.CaliperRegionLength = regionLength;
            _model.CaliperMinimumGradient = minimumGradient;
            _model.MinimumValidCalipers = minimumValidCalipers;
            _model.CaliperOutlierThreshold = outlierThreshold;
            _model.MinimumEdgeGap = minimumEdgeGap;
            _model.NominalEdgeGap = nominalEdgeGap;
            _model.NominalEdgeGapTolerance = nominalEdgeGapTolerance;
            _model.CaliperEdgePolarity = polarity;
            return true;
        }
    }
}