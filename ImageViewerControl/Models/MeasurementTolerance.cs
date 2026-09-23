namespace ImageViewer.Models
{
    /// <summary>
    /// 测量公差的判定参数。
    /// Chinese: 标称值 Nominal + 上公差/下公差；下偏差为负（允许范围 = [Nominal - ToleranceMinus, Nominal + TolerancePlus]）。
    /// English: Nominal value with plus/minus tolerances for pass-fail judgement of measurements.
    /// </summary>
    public sealed class MeasurementTolerance
    {
        public double? Nominal { get; set; }

        public double? TolerancePlus { get; set; }

        public double? ToleranceMinus { get; set; }

        public bool IsEnabled => Nominal.HasValue;

        public bool IsWithinTolerance(double measured)
        {
            if (!IsEnabled)
            {
                return true;
            }

            double upper = Nominal!.Value + (TolerancePlus ?? 0);
            double lower = Nominal.Value - (ToleranceMinus ?? 0);
            return measured >= lower && measured <= upper;
        }
    }
}