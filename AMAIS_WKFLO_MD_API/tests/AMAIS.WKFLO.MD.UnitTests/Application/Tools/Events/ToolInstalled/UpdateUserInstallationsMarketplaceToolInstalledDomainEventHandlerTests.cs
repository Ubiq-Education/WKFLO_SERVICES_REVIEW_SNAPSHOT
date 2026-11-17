using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Events.ToolInstalled;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Events.ToolInstalled
{
    /// <summary>
    /// Bug 7527: same as the agent-install handler - installing a template's tools for a caller with no
    /// resolved WKFLO user must skip the per-user bookkeeping rather than throw.
    /// </summary>
    public class UpdateUserInstallationsMarketplaceToolInstalledDomainEventHandlerTests
    {
        private readonly Mock<IMasterDataDbContext> _db = new();
        private readonly Mock<Microsoft.EntityFrameworkCore.DbSet<UserInstall>> _userInstalls;

        public UpdateUserInstallationsMarketplaceToolInstalledDomainEventHandlerTests()
        {
            _userInstalls = new List<UserInstall>().BuildMockDbSet();
            _db.Setup(x => x.UserInstalls).Returns(_userInstalls.Object);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("admin@school.edu", null)]
        [InlineData("admin@school.edu", "00000000-0000-0000-0000-000000000000")]
        public async Task Handle_WithoutResolvedUser_SkipsBookkeepingInsteadOfThrowing(string email, string guid)
        {
            var handler = new UpdateUserInstallationsMarketplaceToolInstalledDomainEventHandler(_db.Object, new UserContext { Email = email, Guid = guid });

            await handler.Handle(new MarketplaceToolInstalledDomainEvent(3, 1, 1), TestContext.Current.CancellationToken);

            _userInstalls.Verify(x => x.Add(It.IsAny<UserInstall>()), Times.Never);
            _db.Verify(x => x.SaveChangesAsync(It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_WithResolvedUser_RecordsUserInstall()
        {
            var userGuid = Guid.NewGuid();
            var handler = new UpdateUserInstallationsMarketplaceToolInstalledDomainEventHandler(_db.Object, new UserContext { Email = "admin@school.edu", Guid = userGuid.ToString() });

            await handler.Handle(new MarketplaceToolInstalledDomainEvent(3, 1, 1), TestContext.Current.CancellationToken);

            _userInstalls.Verify(x => x.Add(It.Is<UserInstall>(u =>
                u.Email == "admin@school.edu" && u.TypeId == 3 && u.Type == nameof(MarketplaceTool) && u.UserId == userGuid)), Times.Once);
        }

        [Fact]
        public async Task Handle_Uninstall_WithEmailButNoResolvedGuid_StillRemovesUserInstall()
        {
            var existing = new UserInstall { Email = "admin@school.edu", Type = nameof(MarketplaceTool), TypeId = 3 };
            var userInstalls = new List<UserInstall> { existing }.BuildMockDbSet();
            _db.Setup(x => x.UserInstalls).Returns(userInstalls.Object);
            var handler = new UpdateUserInstallationsMarketplaceToolInstalledDomainEventHandler(_db.Object, new UserContext { Email = "admin@school.edu", Guid = null });

            await handler.Handle(new MarketplaceToolInstalledDomainEvent(3, 0, -1), TestContext.Current.CancellationToken);

            userInstalls.Verify(x => x.Remove(existing), Times.Once);
        }
    }
}
