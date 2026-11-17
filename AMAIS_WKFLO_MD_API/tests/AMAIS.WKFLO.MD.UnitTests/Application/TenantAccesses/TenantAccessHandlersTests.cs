using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Commands.AddTenantAccess;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Commands.RemoveTenantAccess;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Models;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Queries.ListTenantAccess;
using AMAIS.WKFLO.MD.UnitTests.Application.Permissions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.MD.UnitTests.Application.Permissions.PermissionsTesting;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantAccesses
{
    public class TenantAccessHandlersTests : IClassFixture<PermissionsTesting>
    {
        private readonly PermissionsTesting _testing;

        public TenantAccessHandlersTests(PermissionsTesting testing)
        {
            _testing = testing;
        }

        private Scenario AsAdmin(params TenantAccess[] accesses) =>
            _testing.CreateScenario(CallerEmail,
                superPowerUsers: new List<SuperPowerUser> { new() { Id = 1, Email = CallerEmail } },
                tenantAccesses: accesses.ToList());

        private Scenario AsRegularUser(params TenantAccess[] accesses) =>
            _testing.CreateScenario("user@example.com",
                superPowerUsers: new List<SuperPowerUser> { new() { Id = 1, Email = CallerEmail } },
                tenantAccesses: accesses.ToList());

        private static void AssertForbidden(Result result)
        {
            var forbidden = Assert.IsType<ForbiddenResult>(result);
            Assert.Equal(ErrorConst.SuperPowerUserRequired, Assert.Single(forbidden.Errors).Code);
        }

        // List

        [Fact]
        public async Task List_NonSuperUser_IsForbidden()
        {
            var s = AsRegularUser();
            var result = await new ListTenantAccessQueryHandler(s.Db.Object, s.User)
                .Handle(new ListTenantAccessQuery(), TestContext.Current.CancellationToken);
            AssertForbidden(result);
        }

        [Fact]
        public async Task List_SuperUser_ReturnsActiveGrantsWithTenantInfo()
        {
            var s = AsAdmin(
                new TenantAccess { Id = 1, TenantId = 2, Email = "b@example.com" },
                new TenantAccess { Id = 2, TenantId = 1, Email = "a@example.com" },
                new TenantAccess { Id = 3, TenantId = 1, Email = "removed@example.com", IsDeleted = true });

            var result = await new ListTenantAccessQueryHandler(s.Db.Object, s.User)
                .Handle(new ListTenantAccessQuery(), TestContext.Current.CancellationToken);

            var data = Assert.IsType<SuccessResult<List<TenantAccessResult>>>(result).Data;
            Assert.Equal(new[] { "a@example.com", "b@example.com" }, data.Select(d => d.Email).ToArray());
            Assert.Equal("tenant-a", data[0].TenantName);
            Assert.Equal(TenantAGuid.ToString(), data[0].TenantGuid);
        }

        // Add

        [Fact]
        public async Task Add_NonSuperUser_IsForbidden()
        {
            var s = AsRegularUser();
            var result = await new AddTenantAccessCommandHandler(s.Db.Object, s.User)
                .Handle(new AddTenantAccessCommand { TenantGuid = TenantAGuid, Email = "x@example.com" }, TestContext.Current.CancellationToken);
            AssertForbidden(result);
            s.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Add_UnknownTenant_IsRejected()
        {
            var s = AsAdmin();
            var result = await new AddTenantAccessCommandHandler(s.Db.Object, s.User)
                .Handle(new AddTenantAccessCommand { TenantGuid = Guid.NewGuid(), Email = "x@example.com" }, TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Equal(ErrorConst.TenantAccessTenantNotFound, Assert.Single(invalid.Errors).Code);
        }

        [Fact]
        public async Task Add_NewGrant_InsertsNormalisedRow()
        {
            var s = AsAdmin();
            var result = await new AddTenantAccessCommandHandler(s.Db.Object, s.User)
                .Handle(new AddTenantAccessCommand { TenantGuid = TenantBGuid, Email = " Person@Example.COM " }, TestContext.Current.CancellationToken);

            var data = Assert.IsType<SuccessResult<TenantAccessResult>>(result).Data;
            Assert.Equal("person@example.com", data.Email);
            Assert.Equal("tenant-b", data.TenantName);
            var added = Assert.IsType<TenantAccess>(Assert.Single(s.Added));
            Assert.Equal(2, added.TenantId);
            Assert.Equal(CallerEmail, added.CreatedBy);
        }

        [Fact]
        public async Task Add_SoftDeletedGrant_Reactivates()
        {
            var existing = new TenantAccess { Id = 7, TenantId = 1, Email = "back@example.com", IsDeleted = true };
            var s = AsAdmin(existing);

            var result = await new AddTenantAccessCommandHandler(s.Db.Object, s.User)
                .Handle(new AddTenantAccessCommand { TenantGuid = TenantAGuid, Email = "Back@example.com" }, TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<TenantAccessResult>>(result);
            Assert.False(existing.IsDeleted);
            Assert.Empty(s.Added);
        }

        [Fact]
        public void AddValidator_RejectsInvalidEmail()
        {
            var validator = new AddTenantAccessCommandValidator();
            Assert.False(validator.Validate(new AddTenantAccessCommand { TenantGuid = TenantAGuid, Email = "nope" }).IsValid);
            Assert.True(validator.Validate(new AddTenantAccessCommand { TenantGuid = TenantAGuid, Email = "ok@example.com" }).IsValid);
        }

        // Remove

        [Fact]
        public async Task Remove_NonSuperUser_IsForbidden()
        {
            var s = AsRegularUser(new TenantAccess { Id = 1, TenantId = 1, Email = "a@example.com" });
            var result = await new RemoveTenantAccessCommandHandler(s.Db.Object, s.User)
                .Handle(new RemoveTenantAccessCommand { Id = 1 }, TestContext.Current.CancellationToken);
            AssertForbidden(result);
        }

        [Fact]
        public async Task Remove_UnknownOrAlreadyRemoved_ReturnsNotFound()
        {
            var s = AsAdmin(new TenantAccess { Id = 1, TenantId = 1, Email = "a@example.com", IsDeleted = true });
            var handler = new RemoveTenantAccessCommandHandler(s.Db.Object, s.User);

            Assert.IsType<NotFoundResult>(await handler.Handle(new RemoveTenantAccessCommand { Id = 1 }, TestContext.Current.CancellationToken));
            Assert.IsType<NotFoundResult>(await handler.Handle(new RemoveTenantAccessCommand { Id = 99 }, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Remove_ActiveGrant_SoftDeletes()
        {
            var grant = new TenantAccess { Id = 1, TenantId = 1, Email = "a@example.com" };
            var s = AsAdmin(grant);

            var result = await new RemoveTenantAccessCommandHandler(s.Db.Object, s.User)
                .Handle(new RemoveTenantAccessCommand { Id = 1 }, TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<TenantAccessResult>>(result);
            Assert.True(grant.IsDeleted);
            s.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
