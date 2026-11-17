using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.TenantSettings.Models;
using AMAIS.WKFLO.MD.Application.TenantSettings.Queries.GetTenantSettings;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AMAIS.WKFLO.MD.UnitTests.Application.TenantSettings.Queries.GetTenantSettings.GetTenantSettingsQueryHandlerTesting;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantSettings.Queries.GetTenantSettings
{
    public class GetTenantSettingsQueryHandlerTests : IClassFixture<GetTenantSettingsQueryHandlerTesting>
    {
        private readonly GetTenantSettingsQueryHandlerTesting _testing;

        public GetTenantSettingsQueryHandlerTests(GetTenantSettingsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_NoSettingsSaved_ReturnsDefaults()
        {
            var handler = _testing.CreateHandler(new List<TenantSetting>());

            var result = await handler.Handle(new GetTenantSettingsQuery(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("UTC", success.Data.TimeZone);
            Assert.Equal("24h", success.Data.TimeFormat);
            Assert.Equal("yyyy-MM-dd", success.Data.DateFormat);
            Assert.Null(success.Data.LastUpdatedOn);
        }

        [Fact]
        public async Task Handle_SettingsSaved_ReturnsStoredValuesAndLastUpdate()
        {
            var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var updated = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
            var handler = _testing.CreateHandler(new List<TenantSetting>
            {
                new() { Id = 1, TenantId = TenantId, TimeZone = "Europe/London", TimeFormat = "12h", DateFormat = "dd/MM/yyyy", CreatedOn = created, LastUpdatedOn = updated }
            });

            var result = await handler.Handle(new GetTenantSettingsQuery(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("Europe/London", success.Data.TimeZone);
            Assert.Equal("12h", success.Data.TimeFormat);
            Assert.Equal("dd/MM/yyyy", success.Data.DateFormat);
            Assert.Equal(updated, success.Data.LastUpdatedOn);
        }

        [Fact]
        public async Task Handle_SettingsCreatedButNeverUpdated_ReturnsCreatedOnAsLastUpdate()
        {
            var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var handler = _testing.CreateHandler(new List<TenantSetting>
            {
                new() { Id = 1, TenantId = TenantId, TimeZone = "UTC", TimeFormat = "12h", CreatedOn = created }
            });

            var result = await handler.Handle(new GetTenantSettingsQuery(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("UTC", success.Data.TimeZone);
            Assert.Equal("12h", success.Data.TimeFormat);
            Assert.Equal(created, success.Data.LastUpdatedOn);
        }

        [Fact]
        public async Task Handle_RowWithoutExplicitDateFormat_ReturnsDefaultDateFormat()
        {
            // Mirrors a row saved before the DateFormat column existed: the column default and the entity's
            // initialiser are both TenantSettingDefaults.DateFormat.
            var handler = _testing.CreateHandler(new List<TenantSetting>
            {
                new() { Id = 1, TenantId = TenantId, TimeZone = "Europe/London", TimeFormat = "12h" }
            });

            var result = await handler.Handle(new GetTenantSettingsQuery(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("yyyy-MM-dd", success.Data.DateFormat);
        }

        [Fact]
        public async Task Handle_IgnoresSoftDeletedRowAndOtherTenantsRow()
        {
            var handler = _testing.CreateHandler(new List<TenantSetting>
            {
                new() { Id = 1, TenantId = TenantId, TimeZone = "Europe/London", TimeFormat = "12h", IsDeleted = true },
                new() { Id = 2, TenantId = OtherTenantId, TimeZone = "Asia/Tokyo", TimeFormat = "12h" }
            });

            var result = await handler.Handle(new GetTenantSettingsQuery(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("UTC", success.Data.TimeZone);
            Assert.Equal("24h", success.Data.TimeFormat);
            Assert.Null(success.Data.LastUpdatedOn);
        }

        [Theory]
        [InlineData("unknown-tenant")]
        [InlineData(null)]
        public async Task Handle_TenantNotFound_ReturnsNotFound(string tenantName)
        {
            var handler = _testing.CreateHandler(new List<TenantSetting>(), tenantName);

            var result = await handler.Handle(new GetTenantSettingsQuery(), TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.NotFound, result.ResultType);
        }
    }
}
