using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.TenantSettings.Commands.UpdateTenantSettings;
using AMAIS.WKFLO.MD.Application.TenantSettings.Models;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.MD.UnitTests.Application.TenantSettings.Commands.UpdateTenantSettings.UpdateTenantSettingsCommandHandlerTesting;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantSettings.Commands.UpdateTenantSettings
{
    public class UpdateTenantSettingsCommandHandlerTests : IClassFixture<UpdateTenantSettingsCommandHandlerTesting>
    {
        private readonly UpdateTenantSettingsCommandHandlerTesting _testing;

        public UpdateTenantSettingsCommandHandlerTests(UpdateTenantSettingsCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("Europe/London", "12h", "Europe/London", "12h")]
        [InlineData("GMT Standard Time", "24H", "Europe/London", "24h")]
        [InlineData("Eastern Standard Time", " 12H ", "America/New_York", "12h")]
        public async Task Handle_NoExistingRow_InsertsOneRowWithNormalisedValuesInOneSave(
            string timeZone, string timeFormat, string expectedTimeZone, string expectedTimeFormat)
        {
            var scenario = _testing.CreateScenario(new List<TenantSetting>());

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { TimeZone = timeZone, TimeFormat = timeFormat },
                TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal(expectedTimeZone, success.Data.TimeZone);
            Assert.Equal(expectedTimeFormat, success.Data.TimeFormat);

            var added = Assert.Single(scenario.Added);
            Assert.Equal(TenantId, added.TenantId);
            Assert.Equal(expectedTimeZone, added.TimeZone);
            Assert.Equal(expectedTimeFormat, added.TimeFormat);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_NoExistingRowAndOnlyOneValue_InsertsRowWithDefaultForTheOther()
        {
            var scenario = _testing.CreateScenario(new List<TenantSetting>());

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { TimeFormat = "12h" },
                TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("UTC", success.Data.TimeZone);
            Assert.Equal("12h", success.Data.TimeFormat);

            var added = Assert.Single(scenario.Added);
            Assert.Equal("UTC", added.TimeZone);
            Assert.Equal("12h", added.TimeFormat);
        }

        [Fact]
        public async Task Handle_ExistingRow_UpdatesInPlaceWithoutInserting()
        {
            var setting = new TenantSetting { Id = 1, TenantId = TenantId, TimeZone = "UTC", TimeFormat = "24h" };
            var scenario = _testing.CreateScenario(new List<TenantSetting> { setting });

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { TimeZone = "Asia/Kolkata", TimeFormat = "12h" },
                TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("Asia/Kolkata", success.Data.TimeZone);
            Assert.Equal("12h", success.Data.TimeFormat);
            Assert.Equal("Asia/Kolkata", setting.TimeZone);
            Assert.Equal("12h", setting.TimeFormat);
            Assert.Empty(scenario.Added);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_NullProperty_LeavesThatSettingUnchanged()
        {
            var setting = new TenantSetting { Id = 1, TenantId = TenantId, TimeZone = "Europe/London", TimeFormat = "24h" };
            var scenario = _testing.CreateScenario(new List<TenantSetting> { setting });

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { TimeZone = null, TimeFormat = "12h" },
                TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("Europe/London", success.Data.TimeZone);
            Assert.Equal("12h", success.Data.TimeFormat);
            Assert.Equal("Europe/London", setting.TimeZone);
            Assert.Empty(scenario.Added);
        }

        [Fact]
        public async Task Handle_AllPropertiesNull_ReturnsCurrentSettingsWithoutSaving()
        {
            var updated = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
            var scenario = _testing.CreateScenario(new List<TenantSetting>
            {
                new() { Id = 1, TenantId = TenantId, TimeZone = "Europe/Paris", TimeFormat = "12h", LastUpdatedOn = updated }
            });

            var result = await scenario.Handler.Handle(new UpdateTenantSettingsCommand(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("Europe/Paris", success.Data.TimeZone);
            Assert.Equal("12h", success.Data.TimeFormat);
            Assert.Equal(updated, success.Data.LastUpdatedOn);
            Assert.Empty(scenario.Added);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_AllPropertiesNullAndNoRow_ReturnsDefaultsWithoutSaving()
        {
            var scenario = _testing.CreateScenario(new List<TenantSetting>());

            var result = await scenario.Handler.Handle(new UpdateTenantSettingsCommand(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("UTC", success.Data.TimeZone);
            Assert.Equal("24h", success.Data.TimeFormat);
            Assert.Equal("yyyy-MM-dd", success.Data.DateFormat);
            Assert.Null(success.Data.LastUpdatedOn);
            Assert.Empty(scenario.Added);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_SoftDeletedRow_IsRevivedRatherThanReinserted_WithDefaultsForUnchangedSettings()
        {
            var deleted = new TenantSetting { Id = 1, TenantId = TenantId, TimeZone = "Asia/Tokyo", TimeFormat = "12h", IsDeleted = true };
            var scenario = _testing.CreateScenario(new List<TenantSetting> { deleted });

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { TimeZone = "Europe/Paris" },
                TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("Europe/Paris", success.Data.TimeZone);
            Assert.Equal("24h", success.Data.TimeFormat);
            Assert.False(deleted.IsDeleted);
            Assert.Equal("Europe/Paris", deleted.TimeZone);
            Assert.Equal("24h", deleted.TimeFormat);
            Assert.Empty(scenario.Added);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_AllPropertiesNullAndSoftDeletedRow_ReturnsDefaultsWithoutReviving()
        {
            var deleted = new TenantSetting { Id = 1, TenantId = TenantId, TimeZone = "Asia/Tokyo", TimeFormat = "12h", IsDeleted = true };
            var scenario = _testing.CreateScenario(new List<TenantSetting> { deleted });

            var result = await scenario.Handler.Handle(new UpdateTenantSettingsCommand(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("UTC", success.Data.TimeZone);
            Assert.Equal("24h", success.Data.TimeFormat);
            Assert.Null(success.Data.LastUpdatedOn);
            Assert.True(deleted.IsDeleted);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_NoExistingRowAndOnlyDateFormat_InsertsRowWithDateFormatAndDefaultsForTheRest()
        {
            var scenario = _testing.CreateScenario(new List<TenantSetting>());

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { DateFormat = " dd/MM/yyyy " },
                TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("dd/MM/yyyy", success.Data.DateFormat);
            Assert.Equal("UTC", success.Data.TimeZone);
            Assert.Equal("24h", success.Data.TimeFormat);

            var added = Assert.Single(scenario.Added);
            Assert.Equal("dd/MM/yyyy", added.DateFormat);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_ExistingRow_UpdatesDateFormatInPlace()
        {
            var setting = new TenantSetting { Id = 1, TenantId = TenantId, TimeZone = "Europe/London", TimeFormat = "12h", DateFormat = "yyyy-MM-dd" };
            var scenario = _testing.CreateScenario(new List<TenantSetting> { setting });

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { DateFormat = "MMMM dd, yyyy" },
                TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("MMMM dd, yyyy", success.Data.DateFormat);
            Assert.Equal("Europe/London", success.Data.TimeZone);
            Assert.Equal("12h", success.Data.TimeFormat);
            Assert.Equal("MMMM dd, yyyy", setting.DateFormat);
            Assert.Empty(scenario.Added);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_RequestWithoutDateFormat_KeepsStoredDateFormat()
        {
            // Backward compatibility: clients that predate DateFormat send only TimeZone/TimeFormat.
            var setting = new TenantSetting { Id = 1, TenantId = TenantId, TimeZone = "UTC", TimeFormat = "24h", DateFormat = "dd MMM, yyyy" };
            var scenario = _testing.CreateScenario(new List<TenantSetting> { setting });

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { TimeZone = "Europe/Paris", TimeFormat = "12h" },
                TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("dd MMM, yyyy", success.Data.DateFormat);
            Assert.Equal("dd MMM, yyyy", setting.DateFormat);
            Assert.Equal("Europe/Paris", setting.TimeZone);
        }

        [Fact]
        public async Task Handle_SoftDeletedRow_RevivedWithDefaultDateFormatWhenNotProvided()
        {
            var deleted = new TenantSetting { Id = 1, TenantId = TenantId, TimeZone = "Asia/Tokyo", TimeFormat = "12h", DateFormat = "MM/dd/yyyy", IsDeleted = true };
            var scenario = _testing.CreateScenario(new List<TenantSetting> { deleted });

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { TimeFormat = "12h" },
                TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("yyyy-MM-dd", success.Data.DateFormat);
            Assert.False(deleted.IsDeleted);
            Assert.Equal("yyyy-MM-dd", deleted.DateFormat);
        }

        [Theory]
        [InlineData("+01:00", null, "TimeZone")]
        [InlineData("Not/AZone", null, "TimeZone")]
        [InlineData(null, "12-hour", "TimeFormat")]
        public async Task Handle_InvalidValue_ReturnsInvalidWithoutSaving(string timeZone, string timeFormat, string expectedProperty)
        {
            var scenario = _testing.CreateScenario(new List<TenantSetting>());

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { TimeZone = timeZone, TimeFormat = timeFormat },
                TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Property == expectedProperty);
            Assert.Empty(scenario.Added);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("dd/mm/yyyy")]
        [InlineData("yyyy/MM/dd")]
        public async Task Handle_InvalidDateFormat_ReturnsInvalidWithoutSaving(string dateFormat)
        {
            var scenario = _testing.CreateScenario(new List<TenantSetting>());

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { TimeZone = "Europe/London", DateFormat = dateFormat },
                TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Property == "DateFormat");
            Assert.Empty(scenario.Added);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_TenantNotFound_ReturnsNotFound()
        {
            var scenario = _testing.CreateScenario(new List<TenantSetting>(), "unknown-tenant");

            var result = await scenario.Handler.Handle(
                new UpdateTenantSettingsCommand { TimeFormat = "12h" },
                TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.NotFound, result.ResultType);
            scenario.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
