using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.StopChatSession
{
    /// <summary>
    /// US 7502: a Stop that only dropped queued hand-offs saves ONE marker in the chat agent's (host's) history, as the
    /// session owner, in its own scope, and never throws.
    /// </summary>
    public class DroppedHandOffMarkerSaverTests
    {
        private const string TenantName = "tenant-a";
        private static readonly DateTimeOffset StoppedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<ISemanticKernelAgentService> _agentService = new();
        private readonly Mock<ILogger<DroppedHandOffMarkerSaver>> _logger = new();
        private string _savedAsUser;
        private string _savedInTenant;

        private DroppedHandOffMarkerSaver BuildSaver(IEnumerable<Conversation> conversations, IEnumerable<AgentAllowedSpinUpAgent> handOffs = null)
        {
            var tenant = new Tenant { Name = TenantName };
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Conversations).Returns(new List<Conversation>(conversations).BuildMockDbSet().Object);
            db.Setup(x => x.AgentAllowedSpinUpAgents).Returns(new List<AgentAllowedSpinUpAgent>(handOffs ?? []).BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            db.Setup(x => x.Agents).Returns(new List<Agent>
            {
                new TenantAgent { Id = 5, Name = "MAISY", Tenant = tenant },
                new TenantAgent { Id = 7, Name = "Kit", Tenant = tenant }
            }.BuildMockDbSet().Object);

            UserContext scopeUser = null;
            TenantContext scopeTenant = null;
            var services = new ServiceCollection();
            services.AddScoped(_ => db.Object);
            services.AddScoped(_ => scopeTenant = new TenantContext());
            services.AddScoped(_ => scopeUser = new UserContext());
            services.AddScoped<ChatContext>();
            services.AddScoped(_ => _agentService.Object);
            _agentService
                .Setup(x => x.SaveStoppedMarkerAsync(It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()))
                .Callback(() =>
                {
                    _savedAsUser = scopeUser?.Guid;
                    _savedInTenant = scopeTenant?.TenantName;
                })
                .ReturnsAsync(StoppedMarkerOutcome.Saved);

            return new DroppedHandOffMarkerSaver(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _logger.Object);
        }

        private static Conversation Row(int agentId, string userId, int minutesAgo) =>
            new() { SessionId = "session-1", AgentId = agentId, UserId = userId, ConversationDate = StoppedAt.AddMinutes(-minutesAgo), Tenant = new Tenant { Name = TenantName } };

        private static AgentAllowedSpinUpAgent HandOff(int from, int to) =>
            new() { AgentId = from, AllowedSpinUpAgentId = to, AllowedSpinUpAgent = new TenantAgent { Id = to, Tenant = new Tenant { Name = TenantName } } };

        /// <summary>Actor dequeue check: only the dropped agent (Kit) is known; MAISY, who hands off to Kit, gets the marker.</summary>
        [Fact]
        public async Task DroppedHandOff_SavesTheMarkerInTheChatAgentsHistory_AsTheSessionOwner()
        {
            var saver = BuildSaver([Row(5, "user-1", 5)], [HandOff(5, 7)]);

            await saver.SaveAsync(new DroppedHandOffStop(TenantName, "session-1", StoppedAt) { DroppedAgentId = 7 });

            _agentService.Verify(x => x.SaveStoppedMarkerAsync(It.Is<RunnableAgent>(a => a.Id == 5), "session-1", StoppedAt), Times.Once);
            Assert.Equal("user-1", _savedAsUser);
            Assert.Equal(TenantName, _savedInTenant);
        }

        /// <summary>Review fix: MAISY -> Kit -> Vera, Vera dropped: the marker goes to MAISY (the root), never to Kit.</summary>
        [Fact]
        public async Task NestedHandOff_SavesTheMarkerInTheRootAgent_NotTheIntermediateOne()
        {
            // Kit (7) saved first here, so a "first agent that reaches Vera" rule would wrongly pick him.
            var saver = BuildSaver([Row(7, "user-1", 6), Row(5, "user-1", 5)], [HandOff(5, 7), HandOff(7, 9)]);

            await saver.SaveAsync(new DroppedHandOffStop(TenantName, "session-1", StoppedAt) { DroppedAgentId = 9 });

            _agentService.Verify(x => x.SaveStoppedMarkerAsync(It.Is<RunnableAgent>(a => a.Id == 5), "session-1", StoppedAt), Times.Once);
            _agentService.Verify(x => x.SaveStoppedMarkerAsync(It.Is<RunnableAgent>(a => a.Id == 7), It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
        }

        /// <summary>MAISY and Kit may hand off to each other; Kit dropped: MAISY is still the root and gets the marker.</summary>
        [Fact]
        public async Task TwoWayHandOff_DroppedAgentReachingTheHostBack_StillSavesInTheHost()
        {
            var saver = BuildSaver([Row(5, "user-1", 5), Row(7, "user-1", 4)], [HandOff(5, 7), HandOff(7, 5)]);

            await saver.SaveAsync(new DroppedHandOffStop(TenantName, "session-1", StoppedAt) { DroppedAgentId = 7 });

            _agentService.Verify(x => x.SaveStoppedMarkerAsync(It.Is<RunnableAgent>(a => a.Id == 5), "session-1", StoppedAt), Times.Once);
            _agentService.Verify(x => x.SaveStoppedMarkerAsync(It.Is<RunnableAgent>(a => a.Id == 7), It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
        }

        [Fact]
        public async Task NoRootOfTheHandOffGraph_SavesNothing()
        {
            // MAISY and Kit hand off to each other: neither is a root, so the chat agent is unknown.
            var saver = BuildSaver([Row(5, "user-1", 5), Row(7, "user-1", 4)], [HandOff(5, 7), HandOff(7, 5), HandOff(7, 9)]);

            await saver.SaveAsync(new DroppedHandOffStop(TenantName, "session-1", StoppedAt) { DroppedAgentId = 9 });

            _agentService.Verify(x => x.SaveStoppedMarkerAsync(It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
        }

        [Fact]
        public async Task KnownChatAgent_SavesTheMarkerThere()
        {
            var saver = BuildSaver([Row(5, "user-1", 5), Row(7, "user-1", 4)]);

            await saver.SaveAsync(new DroppedHandOffStop(TenantName, "session-1", StoppedAt) { HostAgentId = 5 });

            _agentService.Verify(x => x.SaveStoppedMarkerAsync(It.Is<RunnableAgent>(a => a.Id == 5), "session-1", StoppedAt), Times.Once);
        }

        [Fact]
        public async Task ChatAgentHasNoSavedHistoryYet_SavesNothing()
        {
            // Its first turn is still running and saves its own history (and marker).
            var saver = BuildSaver([Row(7, "user-1", 1)]);

            await saver.SaveAsync(new DroppedHandOffStop(TenantName, "session-1", StoppedAt) { HostAgentId = 5 });

            _agentService.Verify(x => x.SaveStoppedMarkerAsync(It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
        }

        [Fact]
        public async Task SaveFails_IsLoggedNotThrown()
        {
            var saver = BuildSaver([Row(5, "user-1", 5)]);
            _agentService
                .Setup(x => x.SaveStoppedMarkerAsync(It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()))
                .ThrowsAsync(new InvalidOperationException("blob store down"));

            var exception = await Record.ExceptionAsync(() => saver.SaveAsync(new DroppedHandOffStop(TenantName, "session-1", StoppedAt) { HostAgentId = 5 }));

            Assert.Null(exception);
            _logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }
    }
}
