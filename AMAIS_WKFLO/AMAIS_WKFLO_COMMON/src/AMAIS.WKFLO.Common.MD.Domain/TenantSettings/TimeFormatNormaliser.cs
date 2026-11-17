namespace AMAIS.WKFLO.Common.MD.Domain.TenantSettings
{
    /// <summary>
    /// Validates a time format and normalises it to <c>12h</c> or <c>24h</c>. Input is case-insensitive and
    /// trimmed; the stored value is always lowercase.
    /// </summary>
    public static class TimeFormatNormaliser
    {
        public const string TwelveHour = "12h";
        public const string TwentyFourHour = "24h";

        public static bool TryNormalise(string rawValue, out string timeFormat)
        {
            timeFormat = null;

            if (rawValue is null)
                return false;

            var candidate = rawValue.Trim().ToLowerInvariant();
            if (candidate is not (TwelveHour or TwentyFourHour))
                return false;

            timeFormat = candidate;
            return true;
        }
    }
}
