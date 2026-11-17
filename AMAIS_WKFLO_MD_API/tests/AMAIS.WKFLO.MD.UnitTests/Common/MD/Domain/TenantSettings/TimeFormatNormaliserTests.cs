using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.TenantSettings
{
    public class TimeFormatNormaliserTests
    {
        [Theory]
        [InlineData("12h", "12h")]
        [InlineData("24h", "24h")]
        [InlineData("12H", "12h")]
        [InlineData(" 24H ", "24h")]
        public void TryNormalise_ValidValue_NormalisesToLowercase(string input, string expected)
        {
            var ok = TimeFormatNormaliser.TryNormalise(input, out var normalised);

            Assert.True(ok);
            Assert.Equal(expected, normalised);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("12")]
        [InlineData("24")]
        [InlineData("am/pm")]
        [InlineData("12-hour")]
        public void TryNormalise_NullOrInvalidValue_IsRejected(string input)
        {
            var ok = TimeFormatNormaliser.TryNormalise(input, out var normalised);

            Assert.False(ok);
            Assert.Null(normalised);
        }

        [Fact]
        public void Default_Is24hAndIsItselfNormalised()
        {
            Assert.Equal("24h", TenantSettingDefaults.TimeFormat);
            Assert.True(TimeFormatNormaliser.TryNormalise(TenantSettingDefaults.TimeFormat, out var normalised));
            Assert.Equal(TenantSettingDefaults.TimeFormat, normalised);
        }
    }
}
