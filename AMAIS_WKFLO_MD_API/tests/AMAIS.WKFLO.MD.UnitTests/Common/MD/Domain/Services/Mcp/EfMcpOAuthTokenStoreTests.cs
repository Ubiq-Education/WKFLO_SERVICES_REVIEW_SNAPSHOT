using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp
{
    public class EfMcpOAuthTokenStoreTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        private static readonly Guid ConnectionId = Guid.Parse("88888888-8888-8888-8888-888888888888");

        private static EfMcpOAuthTokenStore BuildStore(IMasterDataDbContext db)
        {
            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            return new EfMcpOAuthTokenStore(db, Mock.Of<IMcpTokenProtector>(), dateTimeProvider.Object);
        }

        [Fact]
        public async Task RecordConnectionResultAsync_RowExists_Succeeded_UpdatesFieldsAndClearsError()
        {
            var token = new ToolOAuthToken
            {
                Id = 1,
                McpOAuthConnectionId = ConnectionId,
                AccessToken = "encrypted",
                ConnectedOnUtc = Now.AddDays(-1),
                TokenObtainedAtUtc = Now.AddDays(-1),
                LastCheckSucceeded = false,
                LastError = "stale error from a previous failed attempt"
            };
            var tokens = new List<ToolOAuthToken> { token }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.ToolOAuthTokens).Returns(tokens.Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var store = BuildStore(db.Object);

            await store.RecordConnectionResultAsync(ConnectionId, succeeded: true, error: null, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(Now, token.LastCheckedOnUtc);
            Assert.True(token.LastCheckSucceeded);
            Assert.Null(token.LastError);
            db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RecordConnectionResultAsync_RowExists_Failed_RecordsError()
        {
            var token = new ToolOAuthToken
            {
                Id = 1,
                McpOAuthConnectionId = ConnectionId,
                AccessToken = "encrypted",
                ConnectedOnUtc = Now.AddDays(-1),
                TokenObtainedAtUtc = Now.AddDays(-1)
            };
            var tokens = new List<ToolOAuthToken> { token }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.ToolOAuthTokens).Returns(tokens.Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var store = BuildStore(db.Object);

            const string error = "401 { \"error\": { \"code\": \"AUTH\", \"message\": \"No active subscription for this account.\" } }";
            await store.RecordConnectionResultAsync(ConnectionId, succeeded: false, error: error, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(Now, token.LastCheckedOnUtc);
            Assert.False(token.LastCheckSucceeded);
            Assert.Equal(error, token.LastError);
            db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RecordConnectionResultAsync_RowMissing_IsNoOp()
        {
            var tokens = new List<ToolOAuthToken>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.ToolOAuthTokens).Returns(tokens.Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var store = BuildStore(db.Object);

            await store.RecordConnectionResultAsync(Guid.Parse("99999999-9999-9999-9999-999999999999"), succeeded: true, error: null, cancellationToken: TestContext.Current.CancellationToken);

            db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        // --- Per-user (McpOAuthScope.PerUser, e.g. Canva) resolution -------------------------------------
        // Shared-vs-per-user is a fixed, mutually exclusive choice per tool: a connection has either exactly
        // one row with UserId == null (shared, e.g. Humantic) or one row per distinct user (per-user) — never
        // both. These tests exercise that every store operation filters on (connectionId, userId) together,
        // which is what the two filtered unique indexes on ToolOAuthToken rely on being respected in practice.

        private static readonly Guid UserA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid UserB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        [Fact]
        public async Task GetAsync_PerUser_ReturnsOnlyMatchingUsersRow()
        {
            var rowA = new ToolOAuthToken
            {
                Id = 1,
                McpOAuthConnectionId = ConnectionId,
                UserId = UserA,
                AccessToken = "encrypted-a",
                ConnectedOnUtc = Now,
                TokenObtainedAtUtc = Now
            };
            var rowB = new ToolOAuthToken
            {
                Id = 2,
                McpOAuthConnectionId = ConnectionId,
                UserId = UserB,
                AccessToken = "encrypted-b",
                ConnectedOnUtc = Now,
                TokenObtainedAtUtc = Now
            };
            var tokens = new List<ToolOAuthToken> { rowA, rowB }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.ToolOAuthTokens).Returns(tokens.Object);

            var store = BuildStore(db.Object);

            var resultForA = await store.GetAsync(ConnectionId, UserA, TestContext.Current.CancellationToken);
            var resultForNoUser = await store.GetAsync(ConnectionId, null, TestContext.Current.CancellationToken);

            Assert.NotNull(resultForA);
            Assert.Equal(UserA, rowA.UserId);
            // No shared (UserId == null) row exists for this connection — only per-user rows — so the
            // shared-mode lookup finds nothing, even though the connection id matches.
            Assert.Null(resultForNoUser);
        }

        [Fact]
        public async Task SaveAsync_PerUser_CreatesRowTaggedWithThatUser()
        {
            var tokens = new List<ToolOAuthToken>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.ToolOAuthTokens).Returns(tokens.Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var store = BuildStore(db.Object);

            await store.SaveAsync(ConnectionId, new McpOAuthTokenData
            {
                AccessToken = "token-for-a",
                ObtainedAt = Now
            }, UserA, TestContext.Current.CancellationToken);

            db.Verify(x => x.ToolOAuthTokens.AddAsync(
                It.Is<ToolOAuthToken>(e => e.McpOAuthConnectionId == ConnectionId && e.UserId == UserA),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_PerUser_OnlyDeletesTheMatchingUsersRow()
        {
            var rowA = new ToolOAuthToken { Id = 1, McpOAuthConnectionId = ConnectionId, UserId = UserA, AccessToken = "a" };
            var rowB = new ToolOAuthToken { Id = 2, McpOAuthConnectionId = ConnectionId, UserId = UserB, AccessToken = "b" };
            var tokens = new List<ToolOAuthToken> { rowA, rowB }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.ToolOAuthTokens).Returns(tokens.Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var store = BuildStore(db.Object);

            await store.DeleteAsync(ConnectionId, UserA, TestContext.Current.CancellationToken);

            db.Verify(x => x.ToolOAuthTokens.Remove(rowA), Times.Once);
            db.Verify(x => x.ToolOAuthTokens.Remove(rowB), Times.Never);
        }

        [Fact]
        public async Task ExistsAsync_PerUser_DoesNotMatchSharedRowOrOtherUsersRow()
        {
            var sharedRow = new ToolOAuthToken { Id = 1, McpOAuthConnectionId = ConnectionId, UserId = null, AccessToken = "shared" };
            var tokens = new List<ToolOAuthToken> { sharedRow }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.ToolOAuthTokens).Returns(tokens.Object);

            var store = BuildStore(db.Object);

            Assert.True(await store.ExistsAsync(ConnectionId, null, TestContext.Current.CancellationToken));
            Assert.False(await store.ExistsAsync(ConnectionId, UserA, TestContext.Current.CancellationToken));
        }
    }
}
