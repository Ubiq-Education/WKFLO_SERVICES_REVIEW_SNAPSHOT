using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Commands.AddSuperPowerUser;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Commands.RemoveSuperPowerUser;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Models;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Queries.ListSuperPowerUsers;
using AMAIS.WKFLO.MD.UnitTests.Application.Permissions;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.MD.UnitTests.Application.Permissions.PermissionsTesting;

namespace AMAIS.WKFLO.MD.UnitTests.Application.SuperPowerUsers
{
    public class SuperPowerUserHandlersTests : IClassFixture<PermissionsTesting>
    {
        private readonly PermissionsTesting _testing;

        public SuperPowerUserHandlersTests(PermissionsTesting testing)
        {
            _testing = testing;
        }

        private Scenario AsAdmin(params SuperPowerUser[] others)
        {
            var rows = new List<SuperPowerUser> { new() { Id = 1, Email = CallerEmail } };
            rows.AddRange(others);
            return _testing.CreateScenario(CallerEmail, superPowerUsers: rows);
        }

        private Scenario AsRegularUser() =>
            _testing.CreateScenario("user@example.com",
                superPowerUsers: new List<SuperPowerUser> { new() { Id = 1, Email = CallerEmail } });

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
            var result = await new ListSuperPowerUsersQueryHandler(s.Db.Object, s.User)
                .Handle(new ListSuperPowerUsersQuery(), TestContext.Current.CancellationToken);
            AssertForbidden(result);
        }

        [Fact]
        public async Task List_SuperUser_ReturnsActiveRowsOnly()
        {
            var s = AsAdmin(new SuperPowerUser { Id = 2, Email = "gone@example.com", IsDeleted = true },
                            new SuperPowerUser { Id = 3, Email = "second@example.com", CreatedBy = CallerEmail });
            var result = await new ListSuperPowerUsersQueryHandler(s.Db.Object, s.User)
                .Handle(new ListSuperPowerUsersQuery(), TestContext.Current.CancellationToken);

            var data = Assert.IsType<SuccessResult<List<SuperPowerUserResult>>>(result).Data;
            Assert.Equal(new[] { CallerEmail, "second@example.com" }, data.Select(d => d.Email).ToArray());
        }

        // Add

        [Fact]
        public async Task Add_NonSuperUser_IsForbiddenAndNothingSaved()
        {
            var s = AsRegularUser();
            var result = await new AddSuperPowerUserCommandHandler(s.Db.Object, s.User)
                .Handle(new AddSuperPowerUserCommand { Email = "new@example.com" }, TestContext.Current.CancellationToken);

            AssertForbidden(result);
            s.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Add_NewEmail_InsertsNormalisedRowWithCreatedBy()
        {
            var s = AsAdmin();
            var result = await new AddSuperPowerUserCommandHandler(s.Db.Object, s.User)
                .Handle(new AddSuperPowerUserCommand { Email = "  New.Person@Example.COM " }, TestContext.Current.CancellationToken);

            var data = Assert.IsType<SuccessResult<SuperPowerUserResult>>(result).Data;
            Assert.Equal("new.person@example.com", data.Email);
            Assert.Equal(CallerEmail, data.CreatedBy);
            var added = Assert.IsType<SuperPowerUser>(Assert.Single(s.Added));
            Assert.Equal("new.person@example.com", added.Email);
            s.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Add_SoftDeletedEmail_ReactivatesExistingRow()
        {
            var deleted = new SuperPowerUser { Id = 5, Email = "back@example.com", IsDeleted = true, CreatedBy = null };
            var s = AsAdmin(deleted);

            var result = await new AddSuperPowerUserCommandHandler(s.Db.Object, s.User)
                .Handle(new AddSuperPowerUserCommand { Email = "BACK@example.com" }, TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<SuperPowerUserResult>>(result);
            Assert.False(deleted.IsDeleted);
            Assert.Equal(CallerEmail, deleted.CreatedBy);
            Assert.Empty(s.Added);
        }

        [Fact]
        public async Task Add_AlreadyActiveEmail_IsIdempotent()
        {
            var s = AsAdmin(new SuperPowerUser { Id = 5, Email = "dup@example.com" });
            var result = await new AddSuperPowerUserCommandHandler(s.Db.Object, s.User)
                .Handle(new AddSuperPowerUserCommand { Email = "dup@example.com" }, TestContext.Current.CancellationToken);

            Assert.Equal(5, Assert.IsType<SuccessResult<SuperPowerUserResult>>(result).Data.Id);
            Assert.Empty(s.Added);
            s.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("not-an-email", false)]
        [InlineData("user@example.com", true)]
        public void AddValidator_ChecksEmailFormat(string email, bool valid)
        {
            var result = new AddSuperPowerUserCommandValidator().Validate(new AddSuperPowerUserCommand { Email = email });
            Assert.Equal(valid, result.IsValid);
        }

        // Remove

        [Fact]
        public async Task Remove_NonSuperUser_IsForbidden()
        {
            var s = AsRegularUser();
            var result = await new RemoveSuperPowerUserCommandHandler(s.Db.Object, s.User)
                .Handle(new RemoveSuperPowerUserCommand { Id = 1 }, TestContext.Current.CancellationToken);
            AssertForbidden(result);
        }

        [Fact]
        public async Task Remove_Unknown_ReturnsNotFound()
        {
            var s = AsAdmin();
            var result = await new RemoveSuperPowerUserCommandHandler(s.Db.Object, s.User)
                .Handle(new RemoveSuperPowerUserCommand { Id = 99 }, TestContext.Current.CancellationToken);
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Remove_Self_IsRejected()
        {
            var s = AsAdmin(new SuperPowerUser { Id = 2, Email = "other@example.com" });
            var result = await new RemoveSuperPowerUserCommandHandler(s.Db.Object, s.User)
                .Handle(new RemoveSuperPowerUserCommand { Id = 1 }, TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Equal(ErrorConst.CannotRemoveOwnSuperPowerUser, Assert.Single(invalid.Errors).Code);
            Assert.False(s.SuperPowerUsers[0].IsDeleted);
        }

        [Fact]
        public async Task Remove_OtherUser_SoftDeletes()
        {
            var other = new SuperPowerUser { Id = 2, Email = "other@example.com" };
            var s = AsAdmin(other);
            var result = await new RemoveSuperPowerUserCommandHandler(s.Db.Object, s.User)
                .Handle(new RemoveSuperPowerUserCommand { Id = 2 }, TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<SuperPowerUserResult>>(result);
            Assert.True(other.IsDeleted);
            s.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
