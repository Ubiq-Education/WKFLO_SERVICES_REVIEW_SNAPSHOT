using AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval;
using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.StopChatSession
{
    /// <summary>
    /// US 7501 (review fix): a Stop is scoped by (tenant, session). A session id is chosen by the client, so a Stop from
    /// tenant A must never touch tenant B's turns, stop state or approvals, and a session already owned by another chat
    /// is rejected.
    /// </summary>
    public class StopChatSessionTenantIsolationTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<IChatStopStateStore> _stateStore = new();
        private readonly Mock<IChatStopBroadcaster> _broadcaster = new();
        private readonly ChatTurnRegistry _registry = new();
        private readonly Mock<IToolApprovalStateService> _approvals = new();
        private readonly Mock<IChatNotificationService> _notifications = new();

        private StopChatSessionProcessor Processor(string tenantName)
        {
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            _approvals.Setup(x => x.ExpireOpenForSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ToolApprovalRecord>());
            var gate = new ToolApprovalSessionGate(_approvals.Object, Mock.Of<IToolApprovalNotifier>(), dateTimeProvider, NullLogger<ToolApprovalSessionGate>.Instance);
            return new StopChatSessionProcessor(_stateStore.Object, _broadcaster.Object, _registry, gate, Mock.Of<IContactFormReplayStateService>(),
                _notifications.Object, dateTimeProvider, NullLogger<StopChatSessionProcessor>.Instance, new TenantContext { TenantName = tenantName },
                Mock.Of<IHandOffQueueClearScheduler>());
        }

        [Fact]
        public async Task StopFromTenantA_NeverTouchesTenantBsTurnStateOrApprovals_OnTheSameSessionId()
        {
            using var tenantATurn = _registry.Register("tenant-a", "session-1", Now.AddSeconds(-10));
            using var tenantBTurn = _registry.Register("tenant-b", "session-1", Now.AddSeconds(-10));

            await Processor("tenant-a").StopAsync("session-1", 5, null);

            Assert.True(tenantATurn.StopRequested);
            Assert.False(tenantBTurn.StopRequested);
            _stateStore.Verify(x => x.SetStoppedAsync("tenant-a", "session-1", Now, It.IsAny<CancellationToken>()), Times.Once);
            _stateStore.Verify(x => x.SetStoppedAsync("tenant-b", It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
            _broadcaster.Verify(x => x.PublishAsync(new ChatStopSignal("tenant-a", "session-1", Now), It.IsAny<CancellationToken>()), Times.Once);
            _approvals.Verify(x => x.ExpireOpenForSessionAsync("tenant-a", "session-1", Now, It.IsAny<CancellationToken>()), Times.Once);
            _approvals.Verify(x => x.ExpireOpenForSessionAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void BroadcastFromTenantA_NeverCancelsTenantBsTurn()
        {
            using var tenantATurn = _registry.Register("Tenant-A", "session-1", Now.AddSeconds(-10));
            using var tenantBTurn = _registry.Register("tenant-b", "session-1", Now.AddSeconds(-10));

            var cancelled = new ChatStopBroadcastHandler(_registry, NullLogger<ChatStopBroadcastHandler>.Instance)
                .Handle(new ChatStopSignal("tenant-a", "session-1", Now));

            Assert.Equal(1, cancelled);
            Assert.True(tenantATurn.StopRequested);
            Assert.False(tenantBTurn.StopRequested);
        }

        [Fact]
        public void BroadcastWithoutTenant_IsIgnored()
        {
            using var turn = _registry.Register("tenant-a", "session-1", Now.AddSeconds(-10));

            var cancelled = new ChatStopBroadcastHandler(_registry, NullLogger<ChatStopBroadcastHandler>.Instance)
                .Handle("""{"sessionId":"session-1","stoppedAt":"2026-10-19T12:00:00+00:00"}""");

            Assert.Equal(0, cancelled);
            Assert.False(turn.StopRequested);
        }

        [Fact]
        public void StateKey_IsScopedByTenantAndSession()
        {
            Assert.Equal("chat-stop-tenant-a:session-1", ChatStopConsts.BuildStateKey(" Tenant-A ", "session-1"));
            Assert.NotEqual(ChatStopConsts.BuildStateKey("tenant-a", "session-1"), ChatStopConsts.BuildStateKey("tenant-b", "session-1"));
        }

        /// <summary>Review fix: a failed state write must not stop the rest of the Stop.</summary>
        [Fact]
        public async Task StateWriteFails_StillBroadcastsCancelsTellsTheChatAndExpiresApprovals()
        {
            _stateStore.Setup(x => x.SetStoppedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("state store down"));
            using var turn = _registry.Register("tenant-a", "session-1", Now.AddSeconds(-10));

            var result = await Processor("tenant-a").StopAsync("session-1", 5, null);

            Assert.True(result.ResultType == ResultType.Ok);
            _broadcaster.Verify(x => x.PublishAsync(new ChatStopSignal("tenant-a", "session-1", Now), It.IsAny<CancellationToken>()), Times.Once);
            Assert.True(turn.StopRequested);
            _notifications.Verify(x => x.SendTypingIndicator("session-1", false, null, null, null), Times.Once);
            _notifications.Verify(x => x.SendConversationStateUpdate("session-1", "idle", null), Times.Once);
            _notifications.Verify(x => x.SendConversationStoppedAsync("session-1", Now), Times.Once);
            _approvals.Verify(x => x.ExpireOpenForSessionAsync("tenant-a", "session-1", Now, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>Review fix: once validated the Stop runs to the end, never with the request's token.</summary>
        [Fact]
        public async Task Stop_DoesNotUseTheRequestToken()
        {
            using var aborted = new CancellationTokenSource();
            aborted.Cancel();

            var result = await new StopTenantChatSessionCommandHandler(Processor("tenant-a"))
                .Handle(new StopTenantChatSessionCommand { Id = 5, SessionId = "session-1" }, aborted.Token);

            Assert.True(result.ResultType == ResultType.Ok);
            _stateStore.Verify(x => x.SetStoppedAsync("tenant-a", "session-1", Now, It.Is<CancellationToken>(t => !t.CanBeCanceled)), Times.Once);
            _approvals.Verify(x => x.ExpireOpenForSessionAsync("tenant-a", "session-1", Now, It.Is<CancellationToken>(t => !t.CanBeCanceled)), Times.Once);
        }

        // ==================== Session ownership (defence in depth) ====================

        private static Mock<IMasterDataDbContext> Db(IEnumerable<Conversation> conversations, IEnumerable<AgentAllowedSpinUpAgent> allowed = null)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Conversations).Returns(new List<Conversation>(conversations).BuildMockDbSet().Object);
            db.Setup(x => x.AgentAllowedSpinUpAgents).Returns(new List<AgentAllowedSpinUpAgent>(allowed ?? []).BuildMockDbSet().Object);
            return db;
        }

        private static Conversation Row(string tenant, int agentId, string userId = null) =>
            new() { SessionId = "session-1", AgentId = agentId, UserId = userId, Tenant = new Tenant { Name = tenant } };

        private static Mock<IChatSessionOwnerStore> Owner(ChatSessionOwner owner)
        {
            var store = new Mock<IChatSessionOwnerStore>();
            store.Setup(x => x.GetAsync("tenant-a", "session-1", It.IsAny<CancellationToken>())).ReturnsAsync(owner);
            return store;
        }

        private static Task<bool> FirstTurnOwnership(Mock<IMasterDataDbContext> db, Mock<IChatSessionOwnerStore> store, int? hostAgentId, string callerGuid) =>
            StopChatSessionOwnership.IsSessionOfThisChatAsync(db.Object, "tenant-a", hostAgentId, callerGuid, "session-1", TestContext.Current.CancellationToken, store?.Object);

        /// <summary>Review fix (thread 97604): no conversation row yet, so the owner the first message recorded decides.</summary>
        [Theory]
        [InlineData(5, "user-1", true)]   // this chat, this user
        [InlineData(5, "USER-1", true)]   // user ids compare case-insensitively
        [InlineData(5, "user-2", false)]  // another user of the tenant
        [InlineData(5, null, false)]      // an anonymous caller on a known user's session
        [InlineData(7, "user-1", false)]  // another agent of the tenant
        public async Task Ownership_NoConversationYet_OwnerOfAKnownUser_OnlyThisChatAndUser(int callerAgentId, string callerGuid, bool allowed)
        {
            var result = await FirstTurnOwnership(Db([]), Owner(new ChatSessionOwner { AgentId = 5, UserId = "user-1" }), callerAgentId, callerGuid);

            Assert.Equal(allowed, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [InlineData("user-9")]
        public async Task Ownership_NoConversationYet_AnonymousOwner_AnyCallerOfThisChat(string callerGuid)
        {
            Assert.True(await FirstTurnOwnership(Db([]), Owner(new ChatSessionOwner { AgentId = 5, UserId = Guid.Empty.ToString() }), 5, callerGuid));
        }

        [Fact]
        public async Task Ownership_NoConversationYet_NoRecordedOwner_IsRejected()
        {
            Assert.False(await FirstTurnOwnership(Db([]), Owner(null), 5, "user-1"));
            Assert.False(await FirstTurnOwnership(Db([]), store: null, 5, "user-1"));
        }

        [Fact]
        public async Task Ownership_NoConversationYet_OwnerIsAHandOffTargetOfThisChat_IsAllowed()
        {
            var db = Db([], [HandOff(5, 9)]);

            Assert.True(await FirstTurnOwnership(db, Owner(new ChatSessionOwner { AgentId = 9, UserId = "user-1" }), 5, "user-1"));
        }

        [Fact]
        public async Task Ownership_NoConversationYet_OwnerRecordedByShareId_MatchesTheShareAgent()
        {
            var db = Db([]);
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { AgentId = 5, ShareId = "share-1", Agent = new TenantAgent { Id = 5, Tenant = new Tenant { Name = "tenant-a" } } }
            }.BuildMockDbSet().Object);
            var owner = Owner(new ChatSessionOwner { ShareId = "share-1" });

            Assert.True(await FirstTurnOwnership(db, owner, 5, null));
            Assert.False(await FirstTurnOwnership(db, owner, 7, null));
        }

        /// <summary>The state store is down: no mismatch can be shown, so the Stop stays allowed (tenant-scoped, as before).</summary>
        [Fact]
        public async Task Ownership_NoConversationYet_OwnerCannotBeRead_IsAllowed()
        {
            var store = new Mock<IChatSessionOwnerStore>();
            store.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis down"));

            Assert.True(await FirstTurnOwnership(Db([]), store, 5, "user-1"));
        }

        [Fact]
        public async Task Ownership_SessionOfThisChat_IsAllowed()
        {
            var db = Db([Row("tenant-a", 5, "user-1"), Row("tenant-a", 9)]);

            Assert.True(await StopChatSessionOwnership.IsSessionOfThisChatAsync(db.Object, "TENANT-A", 5, "user-1", "session-1", TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("tenant-b", 5, "user-1")] // another tenant
        [InlineData("tenant-a", 7, "user-1")] // another agent, not one this chat hands off to
        [InlineData("tenant-a", 5, "user-2")] // another user
        public async Task Ownership_SessionOwnedByAnotherChat_IsRejected(string tenant, int agentId, string userId)
        {
            var db = Db([Row(tenant, agentId, userId)]);

            Assert.False(await StopChatSessionOwnership.IsSessionOfThisChatAsync(db.Object, "tenant-a", 5, "user-1", "session-1", TestContext.Current.CancellationToken));
        }

        private static AgentAllowedSpinUpAgent HandOff(int from, int to, string tenant = "tenant-a") =>
            new() { AgentId = from, AllowedSpinUpAgentId = to, AllowedSpinUpAgent = new TenantAgent { Id = to, Tenant = new Tenant { Name = tenant } } };

        [Fact]
        public async Task Ownership_OnlyAHandOffAgentSavedSoFar_IsAllowed()
        {
            var db = Db([Row("tenant-a", 9)], [HandOff(5, 9)]);

            Assert.True(await StopChatSessionOwnership.IsSessionOfThisChatAsync(db.Object, "tenant-a", 5, "user-1", "session-1", TestContext.Current.CancellationToken));
        }

        /// <summary>Review fix: nested hand-offs (host -> B -> C) that saved before the host are still the chat's agents.</summary>
        [Fact]
        public async Task Ownership_NestedHandOffAgentsSavedBeforeTheHost_IsAllowed()
        {
            var db = Db([Row("tenant-a", 8), Row("tenant-a", 9)], [HandOff(5, 8), HandOff(8, 9)]);

            Assert.True(await StopChatSessionOwnership.IsSessionOfThisChatAsync(db.Object, "tenant-a", 5, "user-1", "session-1", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Ownership_AgentNotReachableFromTheChat_IsRejected()
        {
            var db = Db([Row("tenant-a", 8), Row("tenant-a", 9)], [HandOff(5, 8), HandOff(7, 9)]);

            Assert.False(await StopChatSessionOwnership.IsSessionOfThisChatAsync(db.Object, "tenant-a", 5, "user-1", "session-1", TestContext.Current.CancellationToken));
        }

        /// <summary>
        /// Review fix: anonymous callers get Guid.Empty (unauthenticated, see UserContextResolver) or no id at all (a
        /// token without an email). Neither may stop a session that belongs to a known user.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        public async Task Ownership_AnonymousCaller_OnASessionOfAKnownUser_IsRejected(string callerGuid)
        {
            var db = Db([Row("tenant-a", 5, "user-1")]);

            Assert.False(await StopChatSessionOwnership.IsSessionOfThisChatAsync(db.Object, "tenant-a", 5, callerGuid, "session-1", TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-000000000000")]
        [InlineData("", "00000000-0000-0000-0000-000000000000")]
        public async Task Ownership_AnonymousCaller_OnAnAnonymousSession_IsAllowed(string callerGuid, string rowUserId)
        {
            var db = Db([Row("tenant-a", 5, rowUserId)]);

            Assert.True(await StopChatSessionOwnership.IsSessionOfThisChatAsync(db.Object, "tenant-a", 5, callerGuid, "session-1", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Ownership_AnonymousCaller_OnlyHandOffAgentsOfAKnownUserSaved_IsRejected()
        {
            var db = Db([Row("tenant-a", 9, "user-1")], [HandOff(5, 9)]);

            Assert.False(await StopChatSessionOwnership.IsSessionOfThisChatAsync(db.Object, "tenant-a", 5, Guid.Empty.ToString(), "session-1", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Validator_SessionOwnedByAnotherAgent_ReturnsTheOwnershipError()
        {
            var db = Db([Row("tenant-a", 7)]);
            db.Setup(x => x.Tenants).Returns(new List<Tenant> { new() { Id = 1, Name = "tenant-a" } }.BuildMockDbSet().Object);
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent> { new() { Id = 5, Tenant = new Tenant { Name = "tenant-a" } } }.BuildMockDbSet().Object);
            var validator = new StopTenantChatSessionCommandValidator(db.Object, new TenantContext { TenantName = "tenant-a" }, new UserContext(),
                Owner(null).Object, NullLogger<StopTenantChatSessionCommandValidator>.Instance);

            var result = await validator.ValidateAsync(new StopTenantChatSessionCommand { Id = 5, SessionId = "session-1" }, TestContext.Current.CancellationToken);

            var error = Assert.Single(result.Errors);
            Assert.Equal("WKFLOAI1001", error.ErrorMessage);
        }

        /// <summary>Review fix (thread 97604): another user of the tenant who knows the session id cannot stop its first turn.</summary>
        [Fact]
        public async Task Validator_FirstTurnOfAnotherUser_ReturnsTheOwnershipError()
        {
            var db = Db([]);
            db.Setup(x => x.Tenants).Returns(new List<Tenant> { new() { Id = 1, Name = "tenant-a" } }.BuildMockDbSet().Object);
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent> { new() { Id = 5, Tenant = new Tenant { Name = "tenant-a" } } }.BuildMockDbSet().Object);
            var validator = new StopTenantChatSessionCommandValidator(db.Object, new TenantContext { TenantName = "tenant-a" }, new UserContext { Guid = "user-2" },
                Owner(new ChatSessionOwner { AgentId = 5, UserId = "user-1" }).Object, NullLogger<StopTenantChatSessionCommandValidator>.Instance);

            var result = await validator.ValidateAsync(new StopTenantChatSessionCommand { Id = 5, SessionId = "session-1" }, TestContext.Current.CancellationToken);

            Assert.Equal("WKFLOAI1001", Assert.Single(result.Errors).ErrorMessage);
        }
    }
}
