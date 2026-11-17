using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.WebOrigins.Models;
using AMAIS.WKFLO.MD.Application.WebOrigins.Queries.ListTenantWebOrigins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static AMAIS.WKFLO.MD.UnitTests.Application.WebOrigins.Queries.ListTenantWebOrigins.ListTenantWebOriginsQueryHandlerTesting;

namespace AMAIS.WKFLO.MD.UnitTests.Application.WebOrigins.Queries.ListTenantWebOrigins
{
    public class ListTenantWebOriginsQueryHandlerTests : IClassFixture<ListTenantWebOriginsQueryHandlerTesting>
    {
        private readonly ListTenantWebOriginsQueryHandlerTesting _testing;

        public ListTenantWebOriginsQueryHandlerTests(ListTenantWebOriginsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        private async Task<Result> Run(
            string callerEmail = CallerEmail,
            string tenantName = TenantName,
            List<SuperPowerUser> superPowerUsers = null,
            List<WebOrigin> origins = null)
        {
            var handler = _testing.CreateHandler(callerEmail, tenantName, superPowerUsers ?? Admins(), TenantRows(), origins ?? new());
            return await handler.Handle(new ListTenantWebOriginsQuery(), TestContext.Current.CancellationToken);
        }

        private static List<TenantWebOriginListResult> Data(Result result) =>
            Assert.IsType<SuccessResult<List<TenantWebOriginListResult>>>(result).Data;

        private static void AssertForbidden(Result result)
        {
            var forbidden = Assert.IsType<ForbiddenResult>(result);
            Assert.Equal(ErrorConst.SuperPowerUserRequired, Assert.Single(forbidden.Errors).Code);
        }

        [Fact]
        public async Task NonSuperPowerUser_IsForbidden()
        {
            var result = await Run(callerEmail: "user@example.com", origins: new() { Origin(1, 1, "SignalR", "https://a.example.com") });
            AssertForbidden(result);
        }

        [Fact]
        public async Task SoftDeletedSuperPowerUser_IsForbidden()
        {
            var result = await Run(superPowerUsers: new() { new() { Id = 1, Email = CallerEmail, IsDeleted = true } });
            AssertForbidden(result);
        }

        [Fact]
        public async Task UnknownTenant_IsNotFound()
        {
            var result = await Run(tenantName: "missing");
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task ReturnsOnlyCurrentTenantRows()
        {
            var data = Data(await Run(origins: new()
            {
                Origin(1, 1, "SignalR", "https://a.example.com"),
                Origin(2, 2, "SignalR", "https://b.example.com")
            }));

            Assert.Equal("https://a.example.com", Assert.Single(data).Name);
        }

        [Fact]
        public async Task IncludesDeletedAndInactiveRowsWithFlags()
        {
            var data = Data(await Run(origins: new()
            {
                Origin(1, 1, "SignalR", "https://active.example.com"),
                Origin(2, 1, "SignalR", "https://inactive.example.com", isActive: false),
                Origin(3, 1, "SignalR", "https://deleted.example.com", isDeleted: true)
            }));

            Assert.Equal(3, data.Count);
            var active = data.Single(d => d.Id == 1);
            Assert.True(active.IsActive);
            Assert.False(active.IsDeleted);
            var inactive = data.Single(d => d.Id == 2);
            Assert.False(inactive.IsActive);
            Assert.False(inactive.IsDeleted);
            Assert.True(data.Single(d => d.Id == 3).IsDeleted);
        }

        [Fact]
        public async Task OrdersByTypeThenName()
        {
            var data = Data(await Run(origins: new()
            {
                Origin(1, 1, "SignalR", "https://z.example.com"),
                Origin(2, 1, "Other", "https://m.example.com"),
                Origin(3, 1, "SignalR", "https://a.example.com")
            }));

            Assert.Equal(new[] { 2, 3, 1 }, data.Select(d => d.Id));
        }

        [Fact]
        public async Task MapsTypeAndTimestamps()
        {
            var created = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var updated = created.AddDays(3);
            var data = Data(await Run(origins: new()
            {
                Origin(1, 1, "SignalR", "https://a.example.com", createdOn: created, updatedOn: updated),
                Origin(2, 1, "SignalR", "https://b.example.com", createdOn: created)
            }));

            var first = data.Single(d => d.Id == 1);
            Assert.Equal("SignalR", first.WebOriginType);
            Assert.Equal(created, first.CreatedOn);
            Assert.Equal(updated, first.LastUpdatedOn);
            Assert.Null(data.Single(d => d.Id == 2).LastUpdatedOn);
        }
    }
}
