using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Events.AgentInstalled;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Events.AgentInstalled
{
    /// <summary>
    /// Bug 7527: an install by a caller with no resolved WKFLO user (app-only token without the
    /// amais-wkflo-user-email header) used to fail the whole install on Guid.Parse(null).
    /// </summary>
    public class UpdateUserInstallationsMarketplaceAgentInstalledDomainEventHandlerTests
    {
        private readonly Mock<IMasterDataDbContext> _db = new();
        private readonly Mock<Microsoft.EntityFrameworkCore.DbSet<UserInstall>> _userInstalls;

        public UpdateUserInstallationsMarketplaceAgentInstalledDomainEventHandlerTests()
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
            var handler = new UpdateUserInstallationsMarketplaceAgentInstalledDomainEventHandler(_db.Object, new UserContext { Email = email, Guid = guid });

            await handler.Handle(new MarketplaceAgentInstalledDomainEvent(7, 1, 1), TestContext.Current.CancellationToken);

            _userInstalls.Verify(x => x.Add(It.IsAny<UserInstall>()), Times.Never);
            _db.Verify(x => x.SaveChangesAsync(It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_WithResolvedUser_RecordsUserInstall()
        {
            var userGuid = Guid.NewGuid();
            var handler = new UpdateUserInstallationsMarketplaceAgentInstalledDomainEventHandler(_db.Object, new UserContext { Email = "admin@school.edu", Guid = userGuid.ToString() });

            await handler.Handle(new MarketplaceAgentInstalledDomainEvent(7, 1, 1), TestContext.Current.CancellationToken);

            _userInstalls.Verify(x => x.Add(It.Is<UserInstall>(u =>
                u.Email == "admin@school.edu" && u.TypeId == 7 && u.Type == nameof(MarketplaceAgent) && u.UserId == userGuid)), Times.Once);
        }

        [Fact]
        public async Task Handle_Uninstall_WithEmailButNoResolvedGuid_StillRemovesUserInstall()
        {
            var existing = new UserInstall { Email = "admin@school.edu", Type = nameof(MarketplaceAgent), TypeId = 7 };
            var userInstalls = new List<UserInstall> { existing }.BuildMockDbSet();
            _db.Setup(x => x.UserInstalls).Returns(userInstalls.Object);
            var handler = new UpdateUserInstallationsMarketplaceAgentInstalledDomainEventHandler(_db.Object, new UserContext { Email = "admin@school.edu", Guid = null });

            await handler.Handle(new MarketplaceAgentInstalledDomainEvent(7, 0, -1), TestContext.Current.CancellationToken);

            userInstalls.Verify(x => x.Remove(existing), Times.Once);
        }
    }
}
