namespace AMAIS.WKFLO.Common.MD.Domain.TenantSettings
{
    /// <summary>
    /// The outcome of <see cref="TenantMidnightCalculator.Evaluate"/>.
    /// </summary>
    /// <param name="IsDue">True when the tenant's local date is later than the local date it was last processed on.</param>
    /// <param name="LocalDate">The tenant's current local date.</param>
    /// <param name="CutoffUtc">The UTC instant the tenant's current local day started (its most recent local midnight).</param>
    public readonly record struct TenantMidnightDecision(bool IsDue, DateOnly LocalDate, DateTimeOffset CutoffUtc);

    /// <summary>
    /// US 7974 / Task 7980: pure, clock-free logic for "run once per local day, at the tenant's midnight".
    /// A tenant is due when its local date is later than the local date it last ran on. Comparing dates (rather than
    /// looking for 00:00 on the clock) makes it DST-safe: a day whose midnight is skipped by a spring-forward gap, and a
    /// fall-back day whose midnight happens twice, both run exactly once; after downtime the next call catches up; a
    /// tenant with no previous run is due straight away.
    /// </summary>
    public static class TenantMidnightCalculator
    {
        // A day is 24h; no real time zone gap is that long, so this only guards against an endless loop.
        private const int MaxGapMinutes = 24 * 60;

        /// <summary>
        /// Resolves a stored tenant time zone id (IANA, or anything <see cref="TimeZoneIdNormaliser"/> accepts).
        /// Missing or unresolvable ids give <see cref="TimeZoneInfo.Utc"/> and false, so the caller can log a warning.
        /// </summary>
        public static bool TryResolveTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
        {
            timeZone = TimeZoneInfo.Utc;

            if (!TimeZoneIdNormaliser.TryNormalise(timeZoneId, out var ianaId))
                return false;

            if (ianaId == TimeZoneIdNormaliser.Utc)
                return true;

            if (!TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var found))
                return false;

            timeZone = found;
            return true;
        }

        /// <summary>
        /// Whether a tenant in <paramref name="timeZone"/> is due at <paramref name="nowUtc"/>, given the local date it
        /// last ran on (null = never), plus its current local date and the UTC instant that local day started.
        /// </summary>
        public static TenantMidnightDecision Evaluate(TimeZoneInfo timeZone, DateTimeOffset nowUtc, DateOnly? lastLocalDate)
        {
            ArgumentNullException.ThrowIfNull(timeZone);

            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime);
            var isDue = lastLocalDate is null || localDate > lastLocalDate.Value;

            return new TenantMidnightDecision(isDue, localDate, StartOfLocalDayUtc(timeZone, localDate));
        }

        /// <summary>
        /// The UTC instant <paramref name="localDate"/> starts in <paramref name="timeZone"/>: its local midnight, or
        /// the first valid local instant of that day when a DST gap skips midnight; when midnight happens twice (a
        /// fall-back at midnight), the first occurrence.
        /// </summary>
        public static DateTimeOffset StartOfLocalDayUtc(TimeZoneInfo timeZone, DateOnly localDate)
        {
            ArgumentNullException.ThrowIfNull(timeZone);

            var midnight = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

            if (timeZone.IsAmbiguousTime(midnight))
            {
                // The larger offset is the earlier of the two instants.
                var offset = timeZone.GetAmbiguousTimeOffsets(midnight).Max();
                return new DateTimeOffset(midnight, offset).ToUniversalTime();
            }

            var local = midnight;
            for (var i = 0; i < MaxGapMinutes && timeZone.IsInvalidTime(local); i++)
                local = local.AddMinutes(1);

            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
        }
    }
}
