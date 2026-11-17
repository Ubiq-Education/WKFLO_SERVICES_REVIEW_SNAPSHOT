using AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.AI;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Common.AI.Application.Services.AI
{
    /// <summary>
    /// US 7959 / Task 7961: the single-agent sync services record why a turn produced no answer (missing or unavailable
    /// agent, no usable provider, an empty answer) for the turn's workflow activity to report; nothing is sent as an answer
    /// text and the failing agent's thinking bubble stops.
    /// </summary>
    public class AISyncServicesChatErrorTests
    {
        private const string TenantName = "tenant-a";
        private const string SessionId = "session-1";

        private readonly ChatNotificationRecorder _chat = new();
        private readonly ChatTurnFailureState _failureState = new();
        private readonly ChatContext _chatContext = new() { SessionId = SessionId };
        private readonly Mock<ISemanticKernelAgentService> _semanticKernel = new();

        private static TenantAgent Agent(string provider = "openai", bool isActive = true) => new()
        {
            Id = 5,
            Name = "Support Bot",
            IsActive = isActive,
            Tenant = new Tenant { Id = 1, Name = TenantName },
            AIModel = new AIModel { Id = 3, Provider = provider, Model = "gpt-4o", Secret = new Secret { ApiKey = "key" } }
        };

        private RunnableAISyncService Runnable(params Agent[] agents)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(new List<Agent>(agents).BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            db.Setup(x => x.EntityTags).Returns(new List<EntityTag>().BuildMockDbSet().Object);
            return new RunnableAISyncService(db.Object, WkfloMapperExtensions.CreateMapper(), _semanticKernel.Object, _chat.Object,
                Mock.Of<IDateTimeProvider>(), new TenantContext { TenantName = TenantName }, _failureState, _chatContext);
        }

        private void Answer(string message) =>
            _semanticKernel.Setup(x => x.RunTextAsync(It.IsAny<RunTextOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentAnswer { MessageId = Guid.NewGuid().ToString(), Message = message });

        [Fact]
        public async Task NoProvider_SingleAgentText_ReturnsNoAnswer_RecordsConfigInvalid_AndStopsTheBubble()
        {
            var result = await Runnable().RunRunnableAgentAsync(Agent(provider: "unknown"), "hi", null, SessionId, chainOfThought: false,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(ChatErrorCodes.ChatModelConfigInvalid, _failureState.Failure?.Code);
            Assert.Equal(5, _failureState.Failure.AgentId);
            Assert.Equal([true, false], _chat.TypingOf(5));
            _chat.AssertNoLeak("No provider found!");
        }

        [Fact]
        public async Task NoProvider_Voice_ReturnsNoAnswer_RecordsConfigInvalid()
        {
            var result = await Runnable().RunRunnableAgentVoiceAsync(Agent(provider: "unknown"), "hi", SessionId, "branch-1", chainOfThought: false,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(ChatErrorCodes.ChatModelConfigInvalid, _failureState.Failure?.Code);
            Assert.Equal([true, false], _chat.TypingOf(5));
        }

        /// <summary>Task 7963: the hand-off / orchestrator path records it too, instead of answering "No provider found!".</summary>
        [Fact]
        public async Task NoProvider_RunById_ReturnsNoAnswer_RecordsConfigInvalid_AndStopsTheBubble()
        {
            var agent = Agent(provider: "unknown");

            var result = await Runnable(agent).RunRunnableAgentByIdAsync(agent.Id, "hi", null, SessionId, chainOfThought: false,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(ChatErrorCodes.ChatModelConfigInvalid, _failureState.Failure?.Code);
            Assert.Equal([true, false], _chat.TypingOf(5));
            _chat.AssertNoLeak("No provider found!");
        }

        /// <summary>Task 7963: the tool approval resume path records it too.</summary>
        [Fact]
        public async Task NoProvider_ResumeAfterToolApproval_ReturnsNoAnswer_RecordsConfigInvalid()
        {
            var agent = Agent(provider: "unknown");
            var record = new AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.ToolApprovalRecord
            {
                ApprovalId = "approval-1", TenantName = TenantName, SessionId = SessionId, AgentId = agent.Id, Status = "Approved"
            };

            var result = await Runnable(agent).ResumeAfterToolApprovalAsync(record, TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(ChatErrorCodes.ChatModelConfigInvalid, _failureState.Failure?.Code);
            _chat.AssertNoLeak("No provider found!");
        }

        /// <summary>Task 7963: an empty answer of a hand-off / orchestrator run is recorded as well.</summary>
        [Fact]
        public async Task EmptyAnswer_RunById_RecordsEmptyResponse()
        {
            Answer(string.Empty);
            var agent = Agent();

            await Runnable(agent).RunRunnableAgentByIdAsync(agent.Id, "hi", null, SessionId, chainOfThought: false,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ChatErrorCodes.ChatModelEmptyResponse, _failureState.Failure?.Code);
        }

        [Fact]
        public async Task EmptyAnswer_WithNothingPending_RecordsEmptyResponse()
        {
            Answer(string.Empty);

            var result = await Runnable().RunRunnableAgentAsync(Agent(), "hi", null, SessionId, chainOfThought: false,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(string.Empty, result);
            Assert.Equal(ChatErrorCodes.ChatModelEmptyResponse, _failureState.Failure?.Code);
            Assert.Equal(5, _failureState.Failure.AgentId);
        }

        [Fact]
        public async Task EmptyAnswer_WhileAToolApprovalIsPending_IsNoError()
        {
            Answer(string.Empty);
            _chatContext.ToolApprovalRequestedThisTurn = true;

            await Runnable().RunRunnableAgentAsync(Agent(), "hi", null, SessionId, chainOfThought: false,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(_failureState.HasFailure);
        }

        [Fact]
        public async Task EmptyAnswer_AfterSomethingElseReachedTheChat_IsNoError()
        {
            Answer(string.Empty);
            _failureState.MarkAnswerDelivered(); // e.g. a tool card (post message) was sent

            await Runnable().RunRunnableAgentAsync(Agent(), "hi", null, SessionId, chainOfThought: false,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(_failureState.HasFailure);
        }

        [Fact]
        public async Task EmptyAnswer_AfterARecordedFailure_KeepsThatFailure()
        {
            Answer(string.Empty);
            _semanticKernel.Setup(x => x.RunTextAsync(It.IsAny<RunTextOptions>(), It.IsAny<CancellationToken>()))
                .Callback(() => _failureState.Record(ChatErrorCodes.ChatModelRateLimited, agentId: 5))
                .ReturnsAsync(new AgentAnswer { MessageId = Guid.NewGuid().ToString(), Message = string.Empty });

            await Runnable().RunRunnableAgentAsync(Agent(), "hi", null, SessionId, chainOfThought: false,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, _failureState.Failure?.Code);
        }

        [Fact]
        public async Task Answer_IsDelivered_AndNothingRecorded()
        {
            Answer("Here you go.");

            var result = await Runnable().RunRunnableAgentAsync(Agent(), "hi", null, SessionId, chainOfThought: false,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("Here you go.", result);
            Assert.True(_failureState.AnswerDelivered);
            Assert.False(_failureState.HasFailure);
        }

        [Fact]
        public async Task TenantAgentNotFound_RecordsAgentUnavailable()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent>().BuildMockDbSet().Object);
            var service = new TenantAISyncService(db.Object, _chat.Object, Mock.Of<IRunnableAISyncService>(), Mock.Of<IDateTimeProvider>(),
                new TenantContext { TenantName = TenantName }, _failureState);

            var result = await service.RunTenantAgentByIdAsync(77, "hi", null, SessionId, chainOfThought: false, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, _failureState.Failure?.Code);
            Assert.Equal(77, _failureState.Failure.AgentId);
        }

        [Fact]
        public async Task TenantVoiceAgentNotFound_RecordsAgentUnavailable()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent>().BuildMockDbSet().Object);
            var service = new TenantAISyncService(db.Object, _chat.Object, Mock.Of<IRunnableAISyncService>(), Mock.Of<IDateTimeProvider>(),
                new TenantContext { TenantName = TenantName }, _failureState);

            var result = await service.RunVoiceTenantAgentByIdAsync(77, "hi", SessionId, "branch-1", chainOfThought: false, TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, _failureState.Failure?.Code);
        }

        [Fact]
        public async Task PublicAgentSwitchedOff_RecordsAgentUnavailable_WithoutRunningIt()
        {
            var agent = Agent(isActive: false);
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { ShareId = "share-1", Agent = agent, AgentId = agent.Id }
            }.BuildMockDbSet().Object);
            var runnable = new Mock<IRunnableAISyncService>();
            var service = new PublicAISyncService(db.Object, _chat.Object, runnable.Object, new TenantContext { TenantName = TenantName }, Mock.Of<IDateTimeProvider>(), _failureState);

            var result = await service.RunPublicAgentByShareIdAsync("share-1", "hi", null, SessionId, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, _failureState.Failure?.Code);
            Assert.Equal(agent.Id, _failureState.Failure.AgentId);
            runnable.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task PublicShareNotFound_RecordsAgentUnavailable()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            var service = new PublicAISyncService(db.Object, _chat.Object, Mock.Of<IRunnableAISyncService>(), new TenantContext { TenantName = TenantName }, Mock.Of<IDateTimeProvider>(), _failureState);

            var result = await service.RunVoicePublicAgentByIdAsync("missing", "hi", SessionId, "branch-1", chainOfThought: false, TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, _failureState.Failure?.Code);
            Assert.Null(_failureState.Failure.AgentId);
        }
    }
}
