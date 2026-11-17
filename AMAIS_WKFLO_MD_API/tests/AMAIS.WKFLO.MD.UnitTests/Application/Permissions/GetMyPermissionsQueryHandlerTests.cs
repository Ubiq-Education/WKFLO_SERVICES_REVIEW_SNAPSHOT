using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Permissions;
using AMAIS.WKFLO.MD.Application.Permissions.Models;
using AMAIS.WKFLO.MD.Application.Permissions.Queries.GetMyPermissions;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AMAIS.WKFLO.MD.UnitTests.Application.Permissions.PermissionsTesting;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Permissions
{
    public class GetMyPermissionsQueryHandlerTests : IClassFixture<PermissionsTesting>
    {
        private readonly PermissionsTesting _testing;

        public GetMyPermissionsQueryHandlerTests(PermissionsTesting testing)
        {
            _testing = testing;
        }

        private static async Task<MyPermissionsResult> Run(Scenario s)
        {
            var handler = new GetMyPermissionsQueryHandler(s.Db.Object, s.User);
            var result = await handler.Handle(new GetMyPermissionsQuery(), TestContext.Current.CancellationToken);
            return Assert.IsType<SuccessResult<MyPermissionsResult>>(result).Data;
        }

        [Theory]
        [InlineData("admin@example.com")]
        [InlineData("  Admin@Example.COM ")]
        public async Task Handle_ActiveSuperPowerUser_ReturnsSuperWithNoTenantList(string callerEmail)
        {
            var s = _testing.CreateScenario(callerEmail,
                superPowerUsers: new List<SuperPowerUser> { new() { Id = 1, Email = CallerEmail } },
                tenantAccesses: new List<TenantAccess> { new() { Id = 1, TenantId = 1, Email = CallerEmail } });

            var data = await Run(s);

            Assert.True(data.IsSuperPowerUser);
            Assert.Empty(data.AccessibleTenantGuids);
        }

        [Fact]
        public async Task Handle_SoftDeletedSuperPowerUser_IsNotSuper()
        {
            var s = _testing.CreateScenario(CallerEmail,
                superPowerUsers: new List<SuperPowerUser> { new() { Id = 1, Email = CallerEmail, IsDeleted = true } });

            var data = await Run(s);

            Assert.False(data.IsSuperPowerUser);
        }

        [Fact]
        public async Task Handle_RegularUser_ReturnsOnlyActiveGrantedTenants()
        {
            var s = _testing.CreateScenario("User@Example.com",
                tenantAccesses: new List<TenantAccess>
                {
                    new() { Id = 1, TenantId = 1, Email = "user@example.com" },
                    new() { Id = 2, TenantId = 2, Email = "user@example.com", IsDeleted = true },
                    new() { Id = 3, TenantId = 2, Email = "someone.else@example.com" }
                });

            var data = await Run(s);

            Assert.False(data.IsSuperPowerUser);
            Assert.Equal(new[] { TenantAGuid.ToString() }, data.AccessibleTenantGuids);
        }

        [Fact]
        public async Task Handle_NoEmail_ReturnsEmptyPermissions()
        {
            var s = _testing.CreateScenario(string.Empty,
                superPowerUsers: new List<SuperPowerUser> { new() { Id = 1, Email = string.Empty } });

            var data = await Run(s);

            Assert.False(data.IsSuperPowerUser);
            Assert.Empty(data.AccessibleTenantGuids);
        }

        [Theory]
        [InlineData("  Mixed@Example.COM ", "mixed@example.com")]
        [InlineData(null, "")]
        [InlineData("   ", "")]
        public void NormaliseEmail_TrimsAndLowerCases(string input, string expected)
        {
            Assert.Equal(expected, PermissionHelper.NormaliseEmail(input));
        }
    }
}
