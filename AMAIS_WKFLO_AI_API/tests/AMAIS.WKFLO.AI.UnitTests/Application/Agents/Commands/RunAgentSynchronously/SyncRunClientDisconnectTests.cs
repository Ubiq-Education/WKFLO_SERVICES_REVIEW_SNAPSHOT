using AMAIS.WKFLO.AI.Application.Agents.Commands.RunPublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Commands.RunTenantAgentById;
using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.RunAgentSynchronously
{
    /// <summary>
    /// US 7501: the synchronous run endpoints are stoppable like a queued turn (registered as a chat turn, one stop-state
    /// read at the start, the turn's Stop token), but a client disconnect (the request's token) never reaches the run,
    /// which then finishes and saves as before.
    /// </summary>
    public class SyncRunClientDisconnectTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly ChatTurnRegistry _registry = new();
        private readonly Mock<IChatStopStateStore> _stopState = new();
        private readonly Mock<IChatNotificationService> _notifications = new();
        private readonly Mock<ITenantAISyncService> _tenantService = new();
        private readonly Mock<IPublicAISyncService> _publicService = new();
        private readonly Mock<IChatSessionOwnerStore> _ownerStore = new();

        private StoppableSyncRun StoppableRun() => new(_registry, new ChatStopCheckpoint(_stopState.Object, _registry, NullLogger<ChatStopCheckpoint>.Instance),
            _notifications.Object, Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now), new TenantContext { TenantName = "tenant-a" },
            new ChatContext(), new UserContext { Guid = "user-1" }, _ownerStore.Object, NullLogger<StoppableSyncRun>.Instance);

        private RunTenantAgentByIdCommandHandler TenantHandler() => new(_tenantService.Object, WkfloMapperExtensions.CreateMapper(), StoppableRun());

        private RunPublicAgentByShareIdCommandHandler PublicHandler() => new(_publicService.Object, WkfloMapperExtensions.CreateMapper(), StoppableRun());

        private static RunTenantAgentByIdCommand TenantCommand() => new() { Id = 7, SessionId = "session-1", Input = new RunTenantAgentInputCommand { Text = "hello" } };

        private static RunPublicAgentByShareIdCommand PublicCommand() => new() { ShareId = "share-1", SessionId = "session-1", Input = new RunPublicAgentInputCommand { Text = "hello" } };

        [Fact]
        public async Task RunTenantAgentById_ClientDisconnected_RunGetsTheStopTokenNotTheRequestToken()
        {
            using var disconnected = new CancellationTokenSource();
            disconnected.Cancel();
            CancellationToken passed = disconnected.Token;
            _tenantService
                .Setup(x => x.RunTenantAgentByIdAsync(7, "hello", It.IsAny<List<FileAttachment>>(), "session-1", true, false, null, It.IsAny<CancellationToken>()))
                .Callback<int, string, List<FileAttachment>, string, bool, bool, Guid?, CancellationToken>((_, _, _, _, _, _, _, ct) => passed = ct)
                .ReturnsAsync(new TenantAgentData { Message = "answer" });

            var result = await TenantHandler().Handle(TenantCommand(), disconnected.Token);

            Assert.False(passed.IsCancellationRequested);
            Assert.True(passed.CanBeCanceled);
            var data = Assert.IsType<SuccessResult<TenantAgentDataResult>>(result).Data;
            Assert.Equal("answer", data.Message);
            Assert.False(data.Stopped);
        }

        [Fact]
        public async Task RunPublicAgentByShareId_ClientDisconnected_RunGetsTheStopTokenNotTheRequestToken()
        {
            using var disconnected = new CancellationTokenSource();
            disconnected.Cancel();
            CancellationToken passed = disconnected.Token;
            _publicService
                .Setup(x => x.RunPublicAgentByShareIdAsync("share-1", "hello", It.IsAny<List<FileAttachment>>(), "session-1", false, null, It.IsAny<CancellationToken>()))
                .Callback<string, string, List<FileAttachment>, string, bool, Guid?, CancellationToken>((_, _, _, _, _, _, ct) => passed = ct)
                .ReturnsAsync(new PublicAgentData { Message = "answer" });

            var result = await PublicHandler().Handle(PublicCommand(), disconnected.Token);

            Assert.False(passed.IsCancellationRequested);
            Assert.True(passed.CanBeCanceled);
            Assert.False(Assert.IsType<SuccessResult<PublicAgentDataResult>>(result).Data.Stopped);
        }

        [Fact]
        public async Task RunTenantAgentById_StoppedWhileRunning_ReturnsAStoppedResultAndSendsIdle()
        {
            _tenantService
                .Setup(x => x.RunTenantAgentByIdAsync(7, "hello", It.IsAny<List<FileAttachment>>(), "session-1", true, false, null, It.IsAny<CancellationToken>()))
                .Returns<int, string, List<FileAttachment>, string, bool, bool, Guid?, CancellationToken>(async (_, _, _, _, _, _, _, ct) =>
                {
                    // The Stop (broadcast or the Stop endpoint on this replica) arrives while the agent runs.
                    _registry.CancelTurnsStartedBefore("tenant-a", "session-1", Now.AddSeconds(1));
                    await Task.Delay(Timeout.Infinite, ct);
                    return null;
                });

            var result = await TenantHandler().Handle(TenantCommand(), TestContext.Current.CancellationToken);

            var data = Assert.IsType<SuccessResult<TenantAgentDataResult>>(result).Data;
            Assert.True(data.Stopped);
            Assert.Null(data.Message);
            _notifications.Verify(x => x.SendConversationStateUpdate("session-1", "idle", null), Times.Once);
        }

        [Fact]
        public async Task RunPublicAgentByShareId_StoppedWhileRunning_EvenWhenTheRunSwallowsTheCancellation_ReturnsAStoppedResult()
        {
            _publicService
                .Setup(x => x.RunPublicAgentByShareIdAsync("share-1", "hello", It.IsAny<List<FileAttachment>>(), "session-1", false, null, It.IsAny<CancellationToken>()))
                .Returns<string, string, List<FileAttachment>, string, bool, Guid?, CancellationToken>((_, _, _, _, _, _, _) =>
                {
                    _registry.CancelTurnsStartedBefore("tenant-a", "session-1", Now.AddSeconds(1));
                    return Task.FromResult<PublicAgentData>(null);
                });

            var result = await PublicHandler().Handle(PublicCommand(), TestContext.Current.CancellationToken);

            Assert.True(Assert.IsType<SuccessResult<PublicAgentDataResult>>(result).Data.Stopped);
        }

        /// <summary>Review fix: the run finished (and saved) its answer just before the Stop was seen: the answer is returned.</summary>
        [Fact]
        public async Task RunTenantAgentById_AnswerFinishedBeforeTheStopWasSeen_ReturnsTheAnswer()
        {
            _tenantService
                .Setup(x => x.RunTenantAgentByIdAsync(7, "hello", It.IsAny<List<FileAttachment>>(), "session-1", true, false, null, It.IsAny<CancellationToken>()))
                .Returns<int, string, List<FileAttachment>, string, bool, bool, Guid?, CancellationToken>((_, _, _, _, _, _, _, _) =>
                {
                    _registry.CancelTurnsStartedBefore("tenant-a", "session-1", Now.AddSeconds(1));
                    return Task.FromResult(new TenantAgentData { Message = "answer" });
                });

            var result = await TenantHandler().Handle(TenantCommand(), TestContext.Current.CancellationToken);

            var data = Assert.IsType<SuccessResult<TenantAgentDataResult>>(result).Data;
            Assert.False(data.Stopped);
            Assert.Equal("answer", data.Message);
            _notifications.Verify(x => x.SendConversationStateUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Never);
        }

        /// <summary>Review fix: the idle notification is best effort; a SignalR failure never turns the stopped run into an error.</summary>
        [Fact]
        public async Task RunTenantAgentById_Stopped_NotificationFails_StillReturnsTheStoppedResult()
        {
            _stopState.Setup(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>())).ReturnsAsync(Now.AddSeconds(1));
            _notifications.Setup(x => x.SendTypingIndicator(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("hub down"));

            var result = await TenantHandler().Handle(TenantCommand(), TestContext.Current.CancellationToken);

            Assert.True(Assert.IsType<SuccessResult<TenantAgentDataResult>>(result).Data.Stopped);
        }

        [Fact]
        public async Task RunTenantAgentById_StoppedBeforeItStarted_NeverRunsTheAgent()
        {
            // A Stop this replica's broadcast missed: the stop-state read at the start catches it.
            _stopState.Setup(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>())).ReturnsAsync(Now.AddSeconds(1));

            var result = await TenantHandler().Handle(TenantCommand(), TestContext.Current.CancellationToken);

            Assert.True(Assert.IsType<SuccessResult<TenantAgentDataResult>>(result).Data.Stopped);
            _tenantService.Verify(x => x.RunTenantAgentByIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<FileAttachment>>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SyncRuns_RecordTheSessionOwner()
        {
            await TenantHandler().Handle(TenantCommand(), TestContext.Current.CancellationToken);
            await PublicHandler().Handle(PublicCommand(), TestContext.Current.CancellationToken);

            _ownerStore.Verify(x => x.RecordAsync("tenant-a", "session-1", It.Is<ChatSessionOwner>(o => o.AgentId == 7 && o.UserId == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
            _ownerStore.Verify(x => x.RecordAsync("tenant-a", "session-1", It.Is<ChatSessionOwner>(o => o.ShareId == "share-1" && o.UserId == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunTenantAgentById_TurnIsUnregisteredWhenTheRunEnds()
        {
            _tenantService
                .Setup(x => x.RunTenantAgentByIdAsync(7, "hello", It.IsAny<List<FileAttachment>>(), "session-1", true, false, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TenantAgentData { Message = "answer" });

            await TenantHandler().Handle(TenantCommand(), TestContext.Current.CancellationToken);

            Assert.Equal(0, _registry.CancelTurnsStartedBefore("tenant-a", "session-1", Now.AddHours(1)));
        }
    }
}
