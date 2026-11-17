using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.TenantSettings
{
    public class TimeZoneIdNormaliserTests
    {
        [Theory]
        [InlineData("UTC")]
        [InlineData("utc")]
        [InlineData("  Utc  ")]
        public void TryNormalise_Utc_AlwaysNormalisesToExactlyUtc(string input)
        {
            var ok = TimeZoneIdNormaliser.TryNormalise(input, out var normalised);

            Assert.True(ok);
            Assert.Equal("UTC", normalised);
        }

        [Theory]
        [InlineData("Europe/London", "Europe/London")]
        [InlineData("  America/New_York  ", "America/New_York")]
        [InlineData("Asia/Kolkata", "Asia/Kolkata")]
        [InlineData("GMT Standard Time", "Europe/London")]
        [InlineData("Eastern Standard Time", "America/New_York")]
        public void TryNormalise_ValidIanaOrWindowsId_NormalisesToIana(string input, string expected)
        {
            var ok = TimeZoneIdNormaliser.TryNormalise(input, out var normalised);

            Assert.True(ok);
            Assert.Equal(expected, normalised);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Not/AZone")]
        [InlineData("Mars Standard Time")]
        [InlineData("+01:00")]
        [InlineData("-05:00")]
        [InlineData("+1")]
        [InlineData("UTC+01:00")]
        [InlineData("UTC+1")]
        [InlineData("GMT-3")]
        [InlineData("UTC+12")]
        [InlineData("UTC-02")]
        [InlineData("Etc/GMT+5")]
        public void TryNormalise_NullInvalidIdOrRawOffset_IsRejected(string input)
        {
            var ok = TimeZoneIdNormaliser.TryNormalise(input, out var normalised);

            Assert.False(ok);
            Assert.Null(normalised);
        }

        [Fact]
        public void Default_IsUtcAndIsItselfNormalised()
        {
            Assert.Equal("UTC", TenantSettingDefaults.TimeZone);
            Assert.True(TimeZoneIdNormaliser.TryNormalise(TenantSettingDefaults.TimeZone, out var normalised));
            Assert.Equal(TenantSettingDefaults.TimeZone, normalised);
        }
    }
}
