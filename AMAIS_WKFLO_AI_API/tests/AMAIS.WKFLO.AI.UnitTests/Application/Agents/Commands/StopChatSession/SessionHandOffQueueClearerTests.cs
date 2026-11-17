using AMAIS.WKFLO.AI.Application.Agents.Actors;
using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.StopChatSession
{
    /// <summary>US 7501 (approved change 3): Stop drops the hand-offs queued in every agent actor of the session.</summary>
    public class SessionHandOffQueueClearerTests
    {
        private const string TenantName = "tenant-a";
        private static readonly DateTimeOffset StoppedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<IMasterDataDbContext> _db = new();
        private readonly Mock<IActorProxyFactory> _actorProxyFactory = new();
        private readonly Dictionary<string, Mock<IAgentActor>> _actors = [];
        private readonly SessionHandOffQueueClearer _clearer;

        public SessionHandOffQueueClearerTests()
        {
            var tenant = new Tenant { Name = TenantName };
            var otherTenant = new Tenant { Name = "tenant-b" };

            // Agent 1 (the chat's agent) may hand off to 2; 2 to 3; 3 back to 1 (a cycle). Agent 4 already answered in
            // the session. 9 belongs to another tenant's session with the same id.
            _db.Setup(x => x.Conversations).Returns(new List<Conversation>
            {
                new() { SessionId = "session-1", AgentId = 4, Tenant = tenant },
                new() { SessionId = "session-1", AgentId = 4, Tenant = tenant },
                new() { SessionId = "session-2", AgentId = 8, Tenant = tenant },
                new() { SessionId = "session-1", AgentId = 9, Tenant = otherTenant }
            }.BuildMockDbSet().Object);
            // Review fix: a hand-off target of another tenant (11) is never cleared.
            _db.Setup(x => x.AgentAllowedSpinUpAgents).Returns(new List<AgentAllowedSpinUpAgent>
            {
                new() { AgentId = 1, AllowedSpinUpAgentId = 2, AllowedSpinUpAgent = new TenantAgent { Id = 2, Tenant = tenant } },
                new() { AgentId = 2, AllowedSpinUpAgentId = 3, AllowedSpinUpAgent = new TenantAgent { Id = 3, Tenant = tenant } },
                new() { AgentId = 3, AllowedSpinUpAgentId = 1, AllowedSpinUpAgent = new TenantAgent { Id = 1, Tenant = tenant } },
                new() { AgentId = 8, AllowedSpinUpAgentId = 10, AllowedSpinUpAgent = new TenantAgent { Id = 10, Tenant = tenant } },
                new() { AgentId = 1, AllowedSpinUpAgentId = 11, AllowedSpinUpAgent = new TenantAgent { Id = 11, Tenant = otherTenant } }
            }.BuildMockDbSet().Object);
            _db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { AgentId = 12, ShareId = "share-1", Agent = new TenantAgent { Id = 12, Tenant = otherTenant } },
                new() { AgentId = 1, ShareId = "share-1", Agent = new TenantAgent { Id = 1, Tenant = tenant } }
            }.BuildMockDbSet().Object);

            _actorProxyFactory
                .Setup(x => x.CreateActorProxy<IAgentActor>(It.IsAny<ActorId>(), nameof(AgentActor), null))
                .Returns<ActorId, string, ActorProxyOptions>((id, _, _) =>
                {
                    var actor = new Mock<IAgentActor>();
                    _actors[id.GetId()] = actor;
                    return actor.Object;
                });

            _clearer = new SessionHandOffQueueClearer(_db.Object, new TenantContext { TenantName = TenantName }, _actorProxyFactory.Object,
                NullLogger<SessionHandOffQueueClearer>.Instance, _markerSaver.Object);
        }

        private readonly Mock<IDroppedHandOffMarkerSaver> _markerSaver = new();

        private void ActorsDrop(Dictionary<string, int> droppedByActor) =>
            _actorProxyFactory
                .Setup(x => x.CreateActorProxy<IAgentActor>(It.IsAny<ActorId>(), nameof(AgentActor), null))
                .Returns<ActorId, string, ActorProxyOptions>((id, _, _) =>
                {
                    var actor = new Mock<IAgentActor>();
                    actor.Setup(x => x.ClearQueueAsync(It.IsAny<DateTimeOffset>()))
                        .ReturnsAsync(droppedByActor.TryGetValue(id.GetId(), out var count) ? count : 0);
                    _actors[id.GetId()] = actor;
                    return actor.Object;
                });

        /// <summary>
        /// US 7502: hand-offs dropped by the Stop (here two, in two agents) save ONE marker for the chat agent, with the
        /// Stop's stoppedAt.
        /// </summary>
        [Fact]
        public async Task ClearAsync_HandOffsDropped_SavesOneMarkerForTheChatAgent()
        {
            ActorsDrop(new() { ["session-1:2"] = 1, ["session-1:3"] = 1 });

            await _clearer.ClearAsync("session-1", 1, null, StoppedAt, TestContext.Current.CancellationToken);

            _markerSaver.Verify(x => x.SaveAsync(It.Is<DroppedHandOffStop>(s =>
                s.TenantName == TenantName && s.SessionId == "session-1" && s.StoppedAt == StoppedAt && s.HostAgentId == 1)), Times.Once);
        }

        [Fact]
        public async Task ClearAsync_NothingDropped_SavesNoMarker()
        {
            ActorsDrop([]);

            await _clearer.ClearAsync("session-1", 1, null, StoppedAt, TestContext.Current.CancellationToken);

            _markerSaver.Verify(x => x.SaveAsync(It.IsAny<DroppedHandOffStop>()), Times.Never);
        }

        [Theory]
        [InlineData(1, null)]
        [InlineData(null, "share-1")]
        public async Task ClearAsync_ClearsTheChatAgentItsHandOffTargetsAndTheSessionParticipants(int? hostAgentId, string hostShareId)
        {
            await _clearer.ClearAsync("session-1", hostAgentId, hostShareId, StoppedAt, TestContext.Current.CancellationToken);

            Assert.Equal(["session-1:1", "session-1:2", "session-1:3", "session-1:4"], _actors.Keys.OrderBy(k => k));
            Assert.All(_actors.Values, actor => actor.Verify(x => x.ClearQueueAsync(StoppedAt), Times.Once));
        }

        [Fact]
        public async Task ClearAsync_OneActorFails_StillClearsTheOthers()
        {
            _actorProxyFactory
                .Setup(x => x.CreateActorProxy<IAgentActor>(It.IsAny<ActorId>(), nameof(AgentActor), null))
                .Returns<ActorId, string, ActorProxyOptions>((id, _, _) =>
                {
                    var actor = new Mock<IAgentActor>();
                    if (id.GetId() == "session-1:2")
                        actor.Setup(x => x.ClearQueueAsync(It.IsAny<DateTimeOffset>())).ThrowsAsync(new InvalidOperationException("sidecar down"));
                    _actors[id.GetId()] = actor;
                    return actor.Object;
                });

            await _clearer.ClearAsync("session-1", 1, null, StoppedAt, TestContext.Current.CancellationToken);

            Assert.Equal(4, _actors.Count);
            _actors["session-1:3"].Verify(x => x.ClearQueueAsync(StoppedAt), Times.Once);
        }

        [Fact]
        public async Task ResolveSessionAgentIdsAsync_UnknownHost_OnlyTheParticipantsAndTheirTargets()
        {
            var agentIds = await _clearer.ResolveSessionAgentIdsAsync("session-2", null, "no-such-share", TestContext.Current.CancellationToken);

            Assert.Equal([8, 10], agentIds.OrderBy(id => id));
        }
    }
}
