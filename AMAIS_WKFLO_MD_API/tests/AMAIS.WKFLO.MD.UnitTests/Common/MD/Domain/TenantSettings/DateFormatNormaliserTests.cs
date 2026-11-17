using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;
using System;
using System.Globalization;
using System.Linq;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.TenantSettings
{
    public class DateFormatNormaliserTests
    {
        public static TheoryData<string> AllowedFormats => new(DateFormatNormaliser.AllowedFormats);

        [Fact]
        public void AllowedFormats_IsTheAgreedWhitelist()
        {
            Assert.Equal(
                new[]
                {
                    "dd/MM/yyyy",
                    "MM/dd/yyyy",
                    "yyyy-MM-dd",
                    "dd-MM-yyyy",
                    "MM-dd-yyyy",
                    "MMM dd, yyyy",
                    "MMMM dd, yyyy",
                    "dd MMM, yyyy",
                    "dd MMMM, yyyy",
                    "ddd, MMMM dd, yyyy",
                    "ddd, dd MMMM, yyyy"
                },
                DateFormatNormaliser.AllowedFormats);
        }

        [Theory]
        [MemberData(nameof(AllowedFormats))]
        public void TryNormalise_WhitelistedFormat_IsAcceptedUnchanged(string format)
        {
            var ok = DateFormatNormaliser.TryNormalise(format, out var normalised);

            Assert.True(ok);
            Assert.Equal(format, normalised);
        }

        [Theory]
        [MemberData(nameof(AllowedFormats))]
        public void AllowedFormat_IsAValidDotNetDatePattern(string format)
        {
            var formatted = new DateTime(2026, 3, 9).ToString(format, CultureInfo.InvariantCulture);

            Assert.Contains("2026", formatted);
        }

        [Theory]
        [InlineData(" dd/MM/yyyy ", "dd/MM/yyyy")]
        [InlineData("\tyyyy-MM-dd", "yyyy-MM-dd")]
        public void TryNormalise_SurroundingWhitespace_IsTrimmed(string input, string expected)
        {
            var ok = DateFormatNormaliser.TryNormalise(input, out var normalised);

            Assert.True(ok);
            Assert.Equal(expected, normalised);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("DD/MM/YYYY")]   // tokens are case-sensitive
        [InlineData("dd/mm/yyyy")]   // "mm" is minutes, not month
        [InlineData("yyyy/MM/dd")]   // valid .NET pattern, but not whitelisted
        [InlineData("d")]            // standard format specifier
        [InlineData("dd/MM/yyyy HH:mm")]
        [InlineData("dd.MM.yyyy")]
        public void TryNormalise_NullOrNotWhitelisted_IsRejected(string input)
        {
            var ok = DateFormatNormaliser.TryNormalise(input, out var normalised);

            Assert.False(ok);
            Assert.Null(normalised);
        }

        [Fact]
        public void AllowedFormats_FitTheDateFormatColumn()
        {
            // dbo.TenantSetting.DateFormat is NVARCHAR(32).
            Assert.All(DateFormatNormaliser.AllowedFormats, f => Assert.True(f.Length <= 32, f));
            Assert.Equal(DateFormatNormaliser.AllowedFormats.Count, DateFormatNormaliser.AllowedFormats.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void Default_IsIsoDateAndIsItselfNormalised()
        {
            Assert.Equal("yyyy-MM-dd", TenantSettingDefaults.DateFormat);
            Assert.True(DateFormatNormaliser.TryNormalise(TenantSettingDefaults.DateFormat, out var normalised));
            Assert.Equal(TenantSettingDefaults.DateFormat, normalised);
        }
    }
}
