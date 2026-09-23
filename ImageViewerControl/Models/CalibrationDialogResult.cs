namespace ImageViewer.Models
{
    /// <summary>
    /// 像素/物理尺寸校准对话框的返回结果。
    /// Chinese: Length 为用户输入的实际长度, Unit 为物理单位, K1/K2 为径向畸变系数(0 表示不校正)。
    /// English: Result produced by the calibration dialog; distortion coefficients default to zero.
    /// </summary>
    public readonly record struct CalibrationDialogResult(double Length, string Unit, double K1, double K2);
}