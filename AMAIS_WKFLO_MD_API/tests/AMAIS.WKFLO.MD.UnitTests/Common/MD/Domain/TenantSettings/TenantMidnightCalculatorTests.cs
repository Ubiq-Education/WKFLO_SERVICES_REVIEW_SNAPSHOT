using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.TenantSettings
{
    public class TenantMidnightCalculatorTests
    {
        private static DateTimeOffset Utc(string value) =>
            DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        private static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static TimeZoneInfo Zone(string ianaId)
        {
            Assert.True(TenantMidnightCalculator.TryResolveTimeZone(ianaId, out var timeZone));
            return timeZone;
        }

        // ==================== TryResolveTimeZone ====================

        [Theory]
        [InlineData("UTC")]
        [InlineData("Europe/London")]
        [InlineData("America/New_York")]
        [InlineData("Asia/Kolkata")]
        [InlineData("Asia/Kathmandu")]
        [InlineData("Pacific/Chatham")]
        public void TryResolveTimeZone_ValidId_ReturnsTrue(string id)
        {
            Assert.True(TenantMidnightCalculator.TryResolveTimeZone(id, out var timeZone));
            Assert.NotNull(timeZone);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Not/AZone")]
        [InlineData("+05:00")]
        public void TryResolveTimeZone_InvalidId_ReturnsFalseAndUtc(string id)
        {
            Assert.False(TenantMidnightCalculator.TryResolveTimeZone(id, out var timeZone));
            Assert.Same(TimeZoneInfo.Utc, timeZone);
        }

        [Fact]
        public void Evaluate_InvalidTimeZoneFallback_BehavesAsUtc()
        {
            TenantMidnightCalculator.TryResolveTimeZone("Not/AZone", out var timeZone);

            var decision = TenantMidnightCalculator.Evaluate(timeZone, Utc("2026-10-22T00:00:00Z"), Date("2026-10-21"));

            Assert.True(decision.IsDue);
            Assert.Equal(Date("2026-10-22"), decision.LocalDate);
            Assert.Equal(Utc("2026-10-22T00:00:00Z"), decision.CutoffUtc);
        }

        // ==================== Due / not due ====================

        [Fact]
        public void Evaluate_Utc_NewDate_IsDueWithMidnightCutoff()
        {
            var decision = TenantMidnightCalculator.Evaluate(TimeZoneInfo.Utc, Utc("2026-10-22T00:30:00Z"), Date("2026-10-21"));

            Assert.True(decision.IsDue);
            Assert.Equal(Date("2026-10-22"), decision.LocalDate);
            Assert.Equal(Utc("2026-10-22T00:00:00Z"), decision.CutoffUtc);
        }

        [Fact]
        public void Evaluate_SameLocalDateAsLastRun_IsNotDue()
        {
            var decision = TenantMidnightCalculator.Evaluate(TimeZoneInfo.Utc, Utc("2026-10-22T23:00:00Z"), Date("2026-10-22"));

            Assert.False(decision.IsDue);
        }

        [Fact]
        public void Evaluate_FirstRun_NoRecord_IsDue()
        {
            var decision = TenantMidnightCalculator.Evaluate(Zone("Europe/London"), Utc("2026-10-22T10:00:00Z"), null);

            Assert.True(decision.IsDue);
            Assert.Equal(Date("2026-10-22"), decision.LocalDate);
            Assert.Equal(Utc("2026-10-21T23:00:00Z"), decision.CutoffUtc);
        }

        [Fact]
        public void Evaluate_CatchUpAfterMultiDayDowntime_IsDueOnceWithTodaysCutoff()
        {
            var timeZone = Zone("Europe/London");
            var decision = TenantMidnightCalculator.Evaluate(timeZone, Utc("2026-10-22T05:00:00Z"), Date("2026-10-18"));

            Assert.True(decision.IsDue);
            Assert.Equal(Date("2026-10-22"), decision.LocalDate);
            Assert.Equal(Utc("2026-10-21T23:00:00Z"), decision.CutoffUtc);

            // Once recorded, the rest of the day is not due again.
            Assert.False(TenantMidnightCalculator.Evaluate(timeZone, Utc("2026-10-22T06:00:00Z"), decision.LocalDate).IsDue);
        }

        // ==================== Time zones ====================

        [Theory]
        // America/New_York (EDT, UTC-4): local midnight of Oct 22 is 04:00Z.
        [InlineData("America/New_York", "2026-10-22T03:00:00Z", false, "2026-10-21", "2026-10-21T04:00:00Z")]
        [InlineData("America/New_York", "2026-10-22T04:00:00Z", true, "2026-10-22", "2026-10-22T04:00:00Z")]
        // Asia/Kolkata (UTC+05:30): local midnight of Oct 22 is 18:30Z on Oct 21; the 18:00Z tick is still Oct 21.
        [InlineData("Asia/Kolkata", "2026-10-21T18:00:00Z", false, "2026-10-21", "2026-10-20T18:30:00Z")]
        [InlineData("Asia/Kolkata", "2026-10-21T19:00:00Z", true, "2026-10-22", "2026-10-21T18:30:00Z")]
        // Asia/Kathmandu (UTC+05:45): local midnight of Oct 22 is 18:15Z on Oct 21.
        [InlineData("Asia/Kathmandu", "2026-10-21T18:00:00Z", false, "2026-10-21", "2026-10-20T18:15:00Z")]
        [InlineData("Asia/Kathmandu", "2026-10-21T19:00:00Z", true, "2026-10-22", "2026-10-21T18:15:00Z")]
        // Pacific/Chatham (UTC+13:45 in its daylight time): local midnight of Oct 22 is 10:15Z on Oct 21.
        [InlineData("Pacific/Chatham", "2026-10-21T10:00:00Z", false, "2026-10-21", "2026-10-20T10:15:00Z")]
        [InlineData("Pacific/Chatham", "2026-10-21T11:00:00Z", true, "2026-10-22", "2026-10-21T10:15:00Z")]
        public void Evaluate_TimeZoneWithOffset_UsesTenantLocalDateAndMidnight(string ianaId, string nowUtc, bool expectedDue, string expectedLocalDate, string expectedCutoff)
        {
            var decision = TenantMidnightCalculator.Evaluate(Zone(ianaId), Utc(nowUtc), Date("2026-10-21"));

            Assert.Equal(expectedDue, decision.IsDue);
            Assert.Equal(Date(expectedLocalDate), decision.LocalDate);
            Assert.Equal(Utc(expectedCutoff), decision.CutoffUtc);
        }

        [Theory]
        // Spring forward (Mar 29, 01:00 GMT -> 02:00 BST): Mar 29 starts at 00:00Z, Mar 30 at 23:00Z on Mar 29.
        [InlineData("2026-03-29", "2026-03-29T00:00:00Z")]
        [InlineData("2026-03-30", "2026-03-29T23:00:00Z")]
        // Fall back (Oct 25, 02:00 BST -> 01:00 GMT): Oct 25 starts at 23:00Z on Oct 24, Oct 26 at 00:00Z.
        [InlineData("2026-10-25", "2026-10-24T23:00:00Z")]
        [InlineData("2026-10-26", "2026-10-26T00:00:00Z")]
        public void StartOfLocalDayUtc_EuropeLondon_AcrossDstTransitions(string localDate, string expectedUtc)
        {
            Assert.Equal(Utc(expectedUtc), TenantMidnightCalculator.StartOfLocalDayUtc(Zone("Europe/London"), Date(localDate)));
        }

        [Theory]
        [InlineData("2026-03-28T20:00:00Z", "2026-03-31T02:00:00Z")]
        [InlineData("2026-10-24T20:00:00Z", "2026-10-27T02:00:00Z")]
        public void HourlyTicks_EuropeLondon_AcrossDstTransitions_RunExactlyOncePerLocalDay(string fromUtc, string toUtc)
        {
            var runs = SimulateHourlyTicks(Zone("Europe/London"), Utc(fromUtc), Utc(toUtc));

            AssertOneRunPerConsecutiveDay(runs);
        }

        // ==================== Midnight in a DST gap / midnight twice ====================

        /// <summary>
        /// A fixed zone whose daylight time starts at 00:00 on the last Sunday of March (00:00 -> 01:00, so that
        /// midnight does not exist) and ends at 01:00 daylight time on the last Sunday of October (back to 00:00, so
        /// that midnight happens twice). Built in code so the test does not depend on the OS time zone database.
        /// </summary>
        private static TimeZoneInfo MidnightTransitionZone()
        {
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 5, DayOfWeek.Sunday));

            return TimeZoneInfo.CreateCustomTimeZone("Test/MidnightTransitions", TimeSpan.Zero, "Midnight transitions", "Standard", "Daylight", [rule]);
        }

        [Fact]
        public void StartOfLocalDayUtc_MidnightSkippedByDstGap_IsFirstValidInstantOfTheDay()
        {
            var timeZone = MidnightTransitionZone();
            Assert.True(timeZone.IsInvalidTime(new DateTime(2026, 3, 29, 0, 0, 0)));

            // 23:59:59 (+00:00) on Mar 28 jumps to 01:00 (+01:00) on Mar 29: the day starts at 00:00Z.
            Assert.Equal(Utc("2026-03-29T00:00:00Z"), TenantMidnightCalculator.StartOfLocalDayUtc(timeZone, Date("2026-03-29")));
        }

        [Fact]
        public void StartOfLocalDayUtc_MidnightHappensTwice_IsFirstOccurrence()
        {
            var timeZone = MidnightTransitionZone();
            Assert.True(timeZone.IsAmbiguousTime(new DateTime(2026, 10, 25, 0, 0, 0)));

            // 00:00 (+01:00) is 23:00Z on Oct 24; the second 00:00 (+00:00) would be 00:00Z.
            Assert.Equal(Utc("2026-10-24T23:00:00Z"), TenantMidnightCalculator.StartOfLocalDayUtc(timeZone, Date("2026-10-25")));
        }

        [Theory]
        [InlineData("2026-03-28T20:00:00Z", "2026-03-31T02:00:00Z")]
        [InlineData("2026-10-24T20:00:00Z", "2026-10-27T02:00:00Z")]
        public void HourlyTicks_MidnightTransitionZone_RunExactlyOncePerLocalDay(string fromUtc, string toUtc)
        {
            var runs = SimulateHourlyTicks(MidnightTransitionZone(), Utc(fromUtc), Utc(toUtc));

            AssertOneRunPerConsecutiveDay(runs);
        }

        // ==================== Helpers ====================

        private static List<TenantMidnightDecision> SimulateHourlyTicks(TimeZoneInfo timeZone, DateTimeOffset fromUtc, DateTimeOffset toUtc)
        {
            var runs = new List<TenantMidnightDecision>();
            DateOnly? last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromUtc, timeZone).DateTime);

            for (var now = fromUtc; now <= toUtc; now = now.AddHours(1))
            {
                var decision = TenantMidnightCalculator.Evaluate(timeZone, now, last);
                if (!decision.IsDue)
                    continue;

                // The cutoff is never in the future and is the start of the day being run.
                Assert.True(decision.CutoffUtc <= now);
                Assert.Equal(decision.LocalDate, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(decision.CutoffUtc, timeZone).DateTime));

                runs.Add(decision);
                last = decision.LocalDate;
            }

            return runs;
        }

        private static void AssertOneRunPerConsecutiveDay(List<TenantMidnightDecision> runs)
        {
            Assert.Equal(3, runs.Count);
            for (var i = 1; i < runs.Count; i++)
                Assert.Equal(runs[i - 1].LocalDate.AddDays(1), runs[i].LocalDate);
        }
    }
}
