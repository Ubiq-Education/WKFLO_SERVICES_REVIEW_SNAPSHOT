using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Models
{
    /// <summary>US 7974: the chat's date/time display settings fall back to the tenant setting defaults.</summary>
    public class TenantDisplaySettingsResultTests
    {
        [Fact]
        public void Create_WithoutASettingsRow_ReturnsTheDefaults()
        {
            var result = TenantDisplaySettingsResult.Create(null);

            Assert.Equal("UTC", result.TimeZone);
            Assert.Equal("24h", result.TimeFormat);
            Assert.Equal("yyyy-MM-dd", result.DateFormat);
        }

        [Fact]
        public void Create_WithASavedRow_ReturnsItsValues()
        {
            var result = TenantDisplaySettingsResult.Create(new TenantSetting { TimeZone = "Australia/Sydney", TimeFormat = "12h", DateFormat = "dd MMM, yyyy" });

            Assert.Equal("Australia/Sydney", result.TimeZone);
            Assert.Equal("12h", result.TimeFormat);
            Assert.Equal("dd MMM, yyyy", result.DateFormat);
        }

        [Fact]
        public void Create_WithASoftDeletedRow_ReturnsTheDefaults()
        {
            var result = TenantDisplaySettingsResult.Create(new TenantSetting { TimeZone = "Australia/Sydney", TimeFormat = "12h", DateFormat = "dd MMM, yyyy", IsDeleted = true });

            Assert.Equal("UTC", result.TimeZone);
            Assert.Equal("24h", result.TimeFormat);
            Assert.Equal("yyyy-MM-dd", result.DateFormat);
        }

        [Fact]
        public void Create_WithABlankValue_ReturnsTheDefaultForThatSettingOnly()
        {
            var result = TenantDisplaySettingsResult.Create(new TenantSetting { TimeZone = "Australia/Sydney", TimeFormat = "12h", DateFormat = " " });

            Assert.Equal("Australia/Sydney", result.TimeZone);
            Assert.Equal("12h", result.TimeFormat);
            Assert.Equal("yyyy-MM-dd", result.DateFormat);
        }
    }
}
