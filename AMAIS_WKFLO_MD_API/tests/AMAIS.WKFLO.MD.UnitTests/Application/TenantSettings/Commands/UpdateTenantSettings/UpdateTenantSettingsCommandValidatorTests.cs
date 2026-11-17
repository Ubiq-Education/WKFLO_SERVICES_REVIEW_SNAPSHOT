using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.TenantSettings.Commands.UpdateTenantSettings;
using Moq;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantSettings.Commands.UpdateTenantSettings
{
    public class UpdateTenantSettingsCommandValidatorTests
    {
        private readonly UpdateTenantSettingsCommandValidator _validator =
            new(new Mock<IMasterDataDbContext>().Object, new TenantContext { TenantName = "test-tenant" });

        [Theory]
        [InlineData(null, null)]
        [InlineData("Europe/London", null)]
        [InlineData(null, "12h")]
        [InlineData("GMT Standard Time", "24H")]
        [InlineData("UTC", "24h")]
        public void Validate_ValidOrOmittedValues_IsValid(string timeZone, string timeFormat)
        {
            var result = _validator.Validate(new UpdateTenantSettingsCommand { TimeZone = timeZone, TimeFormat = timeFormat });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Not/AZone")]
        [InlineData("+01:00")]
        [InlineData("UTC+1")]
        public void Validate_InvalidTimeZone_ReturnsTimeZoneError(string timeZone)
        {
            var result = _validator.Validate(new UpdateTenantSettingsCommand { TimeZone = timeZone });

            Assert.False(result.IsValid);
            var error = Assert.Single(result.Errors);
            Assert.Equal(nameof(UpdateTenantSettingsCommand.TimeZone), error.PropertyName);
            Assert.Equal(ErrorConst.TenantSettingTimeZoneInvalid, error.ErrorMessage);
        }

        [Theory]
        [InlineData("")]
        [InlineData("24")]
        [InlineData("am/pm")]
        public void Validate_InvalidTimeFormat_ReturnsTimeFormatError(string timeFormat)
        {
            var result = _validator.Validate(new UpdateTenantSettingsCommand { TimeFormat = timeFormat });

            Assert.False(result.IsValid);
            var error = Assert.Single(result.Errors);
            Assert.Equal(nameof(UpdateTenantSettingsCommand.TimeFormat), error.PropertyName);
            Assert.Equal(ErrorConst.TenantSettingTimeFormatInvalid, error.ErrorMessage);
        }

        [Theory]
        [InlineData("dd/MM/yyyy")]
        [InlineData("MM/dd/yyyy")]
        [InlineData("yyyy-MM-dd")]
        [InlineData("ddd, dd MMMM, yyyy")]
        [InlineData(" MMM dd, yyyy ")]
        public void Validate_WhitelistedDateFormat_IsValid(string dateFormat)
        {
            var result = _validator.Validate(new UpdateTenantSettingsCommand { DateFormat = dateFormat });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData("")]
        [InlineData("dd/mm/yyyy")]
        [InlineData("DD/MM/YYYY")]
        [InlineData("yyyy/MM/dd")]
        [InlineData("dd.MM.yyyy")]
        public void Validate_InvalidDateFormat_ReturnsDateFormatError(string dateFormat)
        {
            var result = _validator.Validate(new UpdateTenantSettingsCommand { DateFormat = dateFormat });

            Assert.False(result.IsValid);
            var error = Assert.Single(result.Errors);
            Assert.Equal(nameof(UpdateTenantSettingsCommand.DateFormat), error.PropertyName);
            Assert.Equal(ErrorConst.TenantSettingDateFormatInvalid, error.ErrorMessage);
        }
    }
}
