namespace AMAIS.WKFLO.Common.MD.Domain.TenantSettings
{
    /// <summary>
    /// Validates a date format against a fixed whitelist of .NET custom date pattern strings (e.g.
    /// <c>dd/MM/yyyy</c>). Matching is exact and case-sensitive after trimming, because the pattern tokens
    /// are case-sensitive (<c>MM</c> is month, <c>mm</c> is minutes). Clients render each whitelisted
    /// pattern themselves, so arbitrary patterns are rejected rather than passed through.
    /// </summary>
    public static class DateFormatNormaliser
    {
        public const string IsoDate = "yyyy-MM-dd";

        /// <summary>Every accepted date format, in display order.</summary>
        public static readonly IReadOnlyList<string> AllowedFormats =
        [
            "dd/MM/yyyy",
            "MM/dd/yyyy",
            IsoDate,
            "dd-MM-yyyy",
            "MM-dd-yyyy",
            "MMM dd, yyyy",
            "MMMM dd, yyyy",
            "dd MMM, yyyy",
            "dd MMMM, yyyy",
            "ddd, MMMM dd, yyyy",
            "ddd, dd MMMM, yyyy"
        ];

        private static readonly HashSet<string> AllowedFormatSet = new(AllowedFormats, StringComparer.Ordinal);

        public static bool TryNormalise(string rawValue, out string dateFormat)
        {
            dateFormat = null;

            if (rawValue is null)
                return false;

            var candidate = rawValue.Trim();
            if (!AllowedFormatSet.Contains(candidate))
                return false;

            dateFormat = candidate;
            return true;
        }
    }
}
