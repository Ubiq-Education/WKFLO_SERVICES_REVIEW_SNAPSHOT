using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Helpers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.Helpers
{
    public class DxpAgentIdentityResolverTests
    {
        [Fact]
        public void ResolveIds_TenantAgent_ReturnsOwnIdAndPublishedId()
        {
            var agent = new TenantAgent { Id = 5, PublishedTenantAgentId = 9 };

            var (tenantAgentId, publishedTenantAgentId) = DxpAgentIdentityResolver.ResolveIds(agent);

            Assert.Equal(5, tenantAgentId);
            Assert.Equal(9, publishedTenantAgentId);
        }

        [Fact]
        public void ResolveIds_TenantAgent_NeverPublished_PublishedIdIsNull()
        {
            var agent = new TenantAgent { Id = 5, PublishedTenantAgentId = null };

            var (tenantAgentId, publishedTenantAgentId) = DxpAgentIdentityResolver.ResolveIds(agent);

            Assert.Equal(5, tenantAgentId);
            Assert.Null(publishedTenantAgentId);
        }

        [Fact]
        public void ResolveIds_PublishedTenantAgent_ReturnsCreatorIdAndOwnId()
        {
            var agent = new PublishedTenantAgent { Id = 9, CreatedByTenantAgentId = 5 };

            var (tenantAgentId, publishedTenantAgentId) = DxpAgentIdentityResolver.ResolveIds(agent);

            Assert.Equal(5, tenantAgentId);
            Assert.Equal(9, publishedTenantAgentId);
        }

        [Fact]
        public void ResolveIds_PublishedTenantAgent_NoKnownCreator_TenantAgentIdIsNull()
        {
            var agent = new PublishedTenantAgent { Id = 9, CreatedByTenantAgentId = null };

            var (tenantAgentId, publishedTenantAgentId) = DxpAgentIdentityResolver.ResolveIds(agent);

            Assert.Null(tenantAgentId);
            Assert.Equal(9, publishedTenantAgentId);
        }

        [Fact]
        public void ResolveIds_UnrecognizedAgentType_ReturnsBothNull()
        {
            var agent = new MarketplaceAgent { Id = 1 };

            var (tenantAgentId, publishedTenantAgentId) = DxpAgentIdentityResolver.ResolveIds(agent);

            Assert.Null(tenantAgentId);
            Assert.Null(publishedTenantAgentId);
        }

        [Fact]
        public async Task ResolveIdsAsync_MatchesTenantAgent_ReturnsItsIds()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent> { new() { Id = 5, PublishedTenantAgentId = 9 } }.BuildMockDbSet().Object);
            db.Setup(x => x.PublishedTenantAgents).Returns(new List<PublishedTenantAgent>().BuildMockDbSet().Object);

            var (tenantAgentId, publishedTenantAgentId) = await DxpAgentIdentityResolver.ResolveIdsAsync(db.Object, 5, TestContext.Current.CancellationToken);

            Assert.Equal(5, tenantAgentId);
            Assert.Equal(9, publishedTenantAgentId);
        }

        [Fact]
        public async Task ResolveIdsAsync_MatchesPublishedTenantAgent_ReturnsItsIds()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent>().BuildMockDbSet().Object);
            db.Setup(x => x.PublishedTenantAgents).Returns(new List<PublishedTenantAgent> { new() { Id = 9, CreatedByTenantAgentId = 5 } }.BuildMockDbSet().Object);

            var (tenantAgentId, publishedTenantAgentId) = await DxpAgentIdentityResolver.ResolveIdsAsync(db.Object, 9, TestContext.Current.CancellationToken);

            Assert.Equal(5, tenantAgentId);
            Assert.Equal(9, publishedTenantAgentId);
        }

        [Fact]
        public async Task ResolveIdsAsync_NoMatch_ReturnsBothNull()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent>().BuildMockDbSet().Object);
            db.Setup(x => x.PublishedTenantAgents).Returns(new List<PublishedTenantAgent>().BuildMockDbSet().Object);

            var (tenantAgentId, publishedTenantAgentId) = await DxpAgentIdentityResolver.ResolveIdsAsync(db.Object, 999, TestContext.Current.CancellationToken);

            Assert.Null(tenantAgentId);
            Assert.Null(publishedTenantAgentId);
        }

        [Fact]
        public async Task ResolveIdsFromShareIdAsync_EmptyShareId_ReturnsBothNullWithoutQuerying()
        {
            var db = new Mock<IMasterDataDbContext>();

            var (tenantAgentId, publishedTenantAgentId) = await DxpAgentIdentityResolver.ResolveIdsFromShareIdAsync(db.Object, string.Empty, TestContext.Current.CancellationToken);

            Assert.Null(tenantAgentId);
            Assert.Null(publishedTenantAgentId);
            db.VerifyGet(x => x.AgentChatSettings, Moq.Times.Never);
        }

        [Fact]
        public async Task ResolveIdsFromShareIdAsync_MatchesTenantAgentChatSetting_ReturnsBothIds()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { AgentId = 5, ShareId = "share-abc", IsDeleted = false }
            }.BuildMockDbSet().Object);
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent> { new() { Id = 5, PublishedTenantAgentId = 9 } }.BuildMockDbSet().Object);
            db.Setup(x => x.PublishedTenantAgents).Returns(new List<PublishedTenantAgent>().BuildMockDbSet().Object);

            var (tenantAgentId, publishedTenantAgentId) = await DxpAgentIdentityResolver.ResolveIdsFromShareIdAsync(db.Object, "share-abc", TestContext.Current.CancellationToken);

            Assert.Equal(5, tenantAgentId);
            Assert.Equal(9, publishedTenantAgentId);
        }

        [Fact]
        public async Task ResolveIdsFromShareIdAsync_DeletedChatSetting_ReturnsBothNull()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { AgentId = 5, ShareId = "share-abc", IsDeleted = true }
            }.BuildMockDbSet().Object);

            var (tenantAgentId, publishedTenantAgentId) = await DxpAgentIdentityResolver.ResolveIdsFromShareIdAsync(db.Object, "share-abc", TestContext.Current.CancellationToken);

            Assert.Null(tenantAgentId);
            Assert.Null(publishedTenantAgentId);
        }

        [Fact]
        public async Task ResolveIdsFromShareIdAsync_NoMatch_ReturnsBothNull()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);

            var (tenantAgentId, publishedTenantAgentId) = await DxpAgentIdentityResolver.ResolveIdsFromShareIdAsync(db.Object, "unknown-share-id", TestContext.Current.CancellationToken);

            Assert.Null(tenantAgentId);
            Assert.Null(publishedTenantAgentId);
        }

        [Fact]
        public async Task ResolveShareIdAsync_BothIdsNull_ReturnsNullWithoutQuerying()
        {
            var db = new Mock<IMasterDataDbContext>();

            var shareId = await DxpAgentIdentityResolver.ResolveShareIdAsync(db.Object, null, null, TestContext.Current.CancellationToken);

            Assert.Null(shareId);
            db.VerifyGet(x => x.AgentChatSettings, Moq.Times.Never);
        }

        [Fact]
        public async Task ResolveShareIdAsync_MatchesByTenantAgentId_ReturnsShareId()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { AgentId = 5, ShareId = "share-abc", IsDeleted = false }
            }.BuildMockDbSet().Object);

            var shareId = await DxpAgentIdentityResolver.ResolveShareIdAsync(db.Object, 5, null, TestContext.Current.CancellationToken);

            Assert.Equal("share-abc", shareId);
        }

        [Fact]
        public async Task ResolveShareIdAsync_MatchesByPublishedTenantAgentId_ReturnsShareId()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { AgentId = 9, ShareId = "share-def", IsDeleted = false }
            }.BuildMockDbSet().Object);

            var shareId = await DxpAgentIdentityResolver.ResolveShareIdAsync(db.Object, null, 9, TestContext.Current.CancellationToken);

            Assert.Equal("share-def", shareId);
        }

        [Fact]
        public async Task ResolveShareIdAsync_DeletedChatSetting_IsIgnored()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { AgentId = 5, ShareId = "share-abc", IsDeleted = true }
            }.BuildMockDbSet().Object);

            var shareId = await DxpAgentIdentityResolver.ResolveShareIdAsync(db.Object, 5, null, TestContext.Current.CancellationToken);

            Assert.Null(shareId);
        }
    }
}
