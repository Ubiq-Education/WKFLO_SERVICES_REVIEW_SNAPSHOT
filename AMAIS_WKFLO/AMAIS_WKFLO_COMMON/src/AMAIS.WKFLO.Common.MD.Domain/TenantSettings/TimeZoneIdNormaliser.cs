using System.Text.RegularExpressions;

namespace AMAIS.WKFLO.Common.MD.Domain.TenantSettings
{
    /// <summary>
    /// Validates a time zone id and normalises it to an IANA id (e.g. <c>Europe/London</c>).
    /// Accepts IANA ids (returned with their canonical casing) and Windows ids (converted to IANA, e.g.
    /// <c>GMT Standard Time</c> → <c>Europe/London</c>). Rejects unknown ids and raw/fixed UTC offsets
    /// (<c>+01:00</c>, <c>UTC+1</c>, Windows <c>UTC+12</c>, IANA <c>Etc/GMT-5</c>): offsets do not follow
    /// daylight saving, so storing one would silently show the wrong time for half the year.
    /// </summary>
    public static partial class TimeZoneIdNormaliser
    {
        public const string Utc = "UTC";

        public static bool TryNormalise(string rawValue, out string ianaId)
        {
            ianaId = null;

            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            var candidate = rawValue.Trim();

            // "UTC" is both an IANA id and a Windows id, so the system lookup below resolves it differently
            // per OS (it can come back as "Etc/UTC" after Windows→IANA conversion). Pin it so the stored and
            // returned value is always exactly "UTC" — also the TenantSettingDefaults.TimeZone default.
            if (string.Equals(candidate, Utc, StringComparison.OrdinalIgnoreCase))
            {
                ianaId = Utc;
                return true;
            }

            if (OffsetPattern().IsMatch(candidate))
                return false;

            if (!TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out var timeZone))
                return false;

            if (timeZone.HasIanaId)
            {
                ianaId = timeZone.Id;
                return true;
            }

            // A Windows id. Convert using the id the runtime resolved (canonical casing) rather than the
            // raw input, since the Windows→IANA mapping lookup is case-sensitive.
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone.Id, out var converted))
            {
                ianaId = converted;
                return true;
            }

            return false;
        }

        // "+01:00", "-5", "UTC+1", "GMT-03:30", "UTC+12" (Windows), "Etc/GMT+5" (IANA fixed offset).
        [GeneratedRegex(@"^(?:(?:Etc/)?(?:UTC|GMT|UT))?\s*[+-]\s*\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex OffsetPattern();
    }
}
