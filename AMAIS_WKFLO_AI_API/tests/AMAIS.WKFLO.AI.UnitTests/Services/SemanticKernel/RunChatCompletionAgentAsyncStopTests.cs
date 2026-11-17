using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.AgentTokensUsed;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadRunnableAgentChatHistoryBySession;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// US 7502: a turn stopped by the user stores nothing of its own work: its history is saved as it was before the turn
    /// plus one "Stopped" marker (nothing at all for a first turn), while the marker is still sent live and the tokens used
    /// are still recorded. A real error keeps its own handling; a fully streamed answer is saved even when the Stop lands
    /// just before the save; the marker is never sent to the model on later turns.
    /// </summary>
    public class RunChatCompletionAgentAsyncStopTests
    {
        private const string SessionId = "session-stop";
        private static readonly DateTimeOffset Now = new(2026, 10, 19, 10, 0, 5, TimeSpan.Zero);
        private static readonly DateTimeOffset StoppedAt = new(2026, 10, 19, 10, 0, 4, TimeSpan.Zero);

        private readonly Mock<IChatNotificationService> _notificationServiceMock = new();
        private readonly Mock<IWkfloMediator> _mediatorMock = new();
        private readonly Mock<IMessageResolutionWorkflowScheduler> _schedulerMock = new();
        private readonly Mock<IChatStopStateStore> _stopStateStoreMock = new();
        private readonly List<(ChatHistoryUpdatedDomainEvent Event, CancellationToken Token)> _historyEvents = [];
        private readonly List<(AgentTokensUsedDomainEvent Event, CancellationToken Token)> _tokenEvents = [];

        public RunChatCompletionAgentAsyncStopTests()
        {
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<ChatHistoryUpdatedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<ChatHistoryUpdatedDomainEvent, CancellationToken>((e, ct) => _historyEvents.Add((e, ct)))
                .Returns(Task.CompletedTask);
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<AgentTokensUsedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AgentTokensUsedDomainEvent, CancellationToken>((e, ct) => _tokenEvents.Add((e, ct)))
                .Returns(Task.CompletedTask);

            _stopStateStoreMock
                .Setup(x => x.GetStoppedAtAsync("acme", SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StoppedAt);
        }

        private SemanticKernelAgentService BuildService(IChatStopStateStore stopStateStore)
        {
            var dbMock = new Mock<IMasterDataDbContext>();
            dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            dbMock.Setup(x => x.AgentAllowedSpinUpAgents).Returns(new List<AgentAllowedSpinUpAgent>().BuildMockDbSet().Object);

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            return new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(),
                Mock.Of<IWkfloMapper>(),
                Mock.Of<IDatastreamProvider>(),
                Mock.Of<IEisKnowledgeProvider>(),
                _notificationServiceMock.Object,
                Mock.Of<IMcpClientService>(),
                _mediatorMock.Object,
                Options.Create(new ElasticsearchSettings()),
                new TenantContext { TenantName = "acme" },
                new ChatContext(),
                new UserContext { Guid = "user-1" },
                Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(),
                dateTimeProviderMock.Object,
                dbMock.Object,
                Mock.Of<IContactFormPluginLogService>(),
                messageResolutionWorkflowScheduler: _schedulerMock.Object,
                chatStopStateStore: stopStateStore);
        }

        private static async IAsyncEnumerable<StreamingChatMessageContent> Stream(string[] chunks, Action afterChunks, Exception thenThrow,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk);
            }

            afterChunks?.Invoke();
            if (thenThrow != null)
                throw thenThrow;
        }

        private static Kernel BuildKernel(Func<IAsyncEnumerable<StreamingChatMessageContent>> stream, Action<ChatHistory> onRequest = null)
        {
            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            chatCompletionServiceMock
                .Setup(x => x.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()))
                .Returns<ChatHistory, PromptExecutionSettings, Kernel, CancellationToken>((history, _, _, _) =>
                {
                    onRequest?.Invoke(history);
                    return stream();
                });

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);
            return builder.Build();
        }

        private static RunChatData BuildData(Kernel kernel, ChatHistory history = null, IReadOnlyList<ChatMessageContent> detachedMarkers = null) => new()
        {
            Kernel = kernel,
            Agent = new TenantAgent { Id = 42, Name = "Kit", AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" } },
            AgentTagNames = [],
            Prompt = "You are Kit.",
            Message = "Write me a long answer",
            SessionId = SessionId,
            ChatHistoryAgentThread = new ChatHistoryAgentThread(history ?? new ChatHistory()),
            CreatedAt = Now.AddMinutes(-10),
            ChatName = "Test Chat",
            UserMessageId = Guid.NewGuid(),
            DetachedStoppedMarkers = detachedMarkers ?? []
        };

        private static ChatMessageContent Timestamped(AuthorRole role, string text, DateTimeOffset timestamp) =>
            new(role, text, metadata: new Dictionary<string, object>
            {
                [CommonMetadataConsts.Timestamp] = timestamp,
                [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString()
            });

        private static readonly ChatMessageContent EarlierQuestion = Timestamped(AuthorRole.User, "Earlier question", Now.AddMinutes(-6));
        private static readonly ChatMessageContent EarlierAnswer = Timestamped(AuthorRole.Assistant, "Earlier answer", Now.AddMinutes(-5));

        private void SetupLoadedHistory(params ChatMessageContent[] messages) =>
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<LoadRunnableAgentChatHistoryBySessionQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => (Now.AddMinutes(-10), "Test Chat", new ChatHistory(messages)));

        private ChatMessage VerifyOneLiveMarker()
        {
            ChatMessage live = null;
            _notificationServiceMock.Verify(x => x.SendMessageToChat(SessionId, It.Is<ChatMessage>(m => m.Content == StoppedTurnMarker.Text),
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            foreach (var invocation in _notificationServiceMock.Invocations)
            {
                if (invocation.Method.Name == nameof(IChatNotificationService.SendMessageToChat) && invocation.Arguments[1] is ChatMessage m && m.Content == StoppedTurnMarker.Text)
                    live = m;
            }

            return live;
        }

        /// <summary>
        /// US 7502 (user decision): a turn stopped while streaming stores nothing of its own work - not the question, not
        /// the tool calls and results, not the partial text. Its final save is the history as it was before the turn plus
        /// one marker (the Stop's stoppedAt, the same id as the live one), flagged as a stopped turn.
        /// </summary>
        [Fact]
        public async Task StoppedWhileStreaming_SavesThePreTurnHistoryPlusOneMarker()
        {
            SetupLoadedHistory(EarlierQuestion, EarlierAnswer);
            var working = new ChatHistory { EarlierQuestion, EarlierAnswer };
            // What this turn added in memory before the Stop: a tool call and its result.
            working.Add(new ChatMessageContent(AuthorRole.Assistant, [new FunctionCallContent("search", "Docs", "call-1")]));
            working.Add(new ChatMessageContent(AuthorRole.Tool, [new FunctionResultContent("search", "Docs", "call-1", "result")]));
            using var stop = new CancellationTokenSource();
            var kernel = BuildKernel(() => Stream(["Here is ", "the first ", "part"], stop.Cancel, new OperationCanceledException(stop.Token)));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildService(_stopStateStoreMock.Object).RunChatCompletionAgentAsync(BuildData(kernel, working), stop.Token));

            var (saved, saveToken) = Assert.Single(_historyEvents);
            Assert.True(saved.IsStoppedTurn);
            Assert.False(saveToken.CanBeCanceled);
            Assert.Equal(new[] { "Earlier question", "Earlier answer", StoppedTurnMarker.Text }, saved.ChatHistory.Select(m => m.Content));
            Assert.DoesNotContain(saved.ChatHistory, m => m.Items.OfType<FunctionCallContent>().Any() || m.Items.OfType<FunctionResultContent>().Any());

            var marker = Assert.Single(saved.ChatHistory, StoppedTurnMarker.IsMarker);
            Assert.Equal(StoppedAt, StoppedTurnMarker.ReadStoppedAt(marker.Metadata));
            var live = VerifyOneLiveMarker();
            Assert.Equal(live.Id, marker.Metadata[CommonMetadataConsts.MessageId]);
            Assert.True((bool)live.Metadata[CommonMetadataConsts.IsStoppedMarker]);
            _notificationServiceMock.Verify(x => x.SendMessageToChat(It.IsAny<string>(), It.Is<ChatMessage>(m => m.Type == "system"),
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task StoppedTurn_StillRecordsTokenUsage_ButSkipsMessageResolution()
        {
            SetupLoadedHistory(EarlierQuestion, EarlierAnswer);
            using var stop = new CancellationTokenSource();
            var kernel = BuildKernel(() => Stream(["Partial answer"], stop.Cancel, new OperationCanceledException(stop.Token)));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildService(_stopStateStoreMock.Object).RunChatCompletionAgentAsync(BuildData(kernel), stop.Token));

            var (tokensUsed, tokensToken) = Assert.Single(_tokenEvents);
            Assert.Equal(42, tokensUsed.AgentAnswer.AgentId);
            Assert.Equal(string.Empty, tokensUsed.AgentAnswer.Message);
            Assert.False(tokensToken.CanBeCanceled);
            _schedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<MessageResolutionWorkflowInput>()), Times.Never);
        }

        /// <summary>Review fix (thread 97626): a failed usage report no longer skips the marker.</summary>
        [Fact]
        public async Task StoppedTurn_UsageReportFails_StillSendsAndSavesTheMarker()
        {
            SetupLoadedHistory(EarlierQuestion, EarlierAnswer);
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<AgentTokensUsedDomainEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("usage store down"));
            using var stop = new CancellationTokenSource();
            var kernel = BuildKernel(() => Stream(["Partial answer"], stop.Cancel, new OperationCanceledException(stop.Token)));
            var data = BuildData(kernel);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildService(_stopStateStoreMock.Object).RunChatCompletionAgentAsync(data, stop.Token));

            Assert.Equal(StoppedMarkerOutcome.Saved, data.StoppedMarkerOutcome);
            var (saved, _) = Assert.Single(_historyEvents);
            Assert.Single(saved.ChatHistory, StoppedTurnMarker.IsMarker);
            VerifyOneLiveMarker();
        }

        /// <summary>Review fix (thread 97626): a marker that fails (the live send) never costs the usage report or the saved marker.</summary>
        [Fact]
        public async Task StoppedTurn_LiveMarkerFails_StillRecordsUsageAndSavesTheMarker()
        {
            SetupLoadedHistory(EarlierQuestion, EarlierAnswer);
            _notificationServiceMock
                .Setup(x => x.SendMessageToChat(It.IsAny<string>(), It.IsAny<ChatMessage>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("hub down"));
            using var stop = new CancellationTokenSource();
            var kernel = BuildKernel(() => Stream(["Partial answer"], stop.Cancel, new OperationCanceledException(stop.Token)));
            var data = BuildData(kernel);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildService(_stopStateStoreMock.Object).RunChatCompletionAgentAsync(data, stop.Token));

            Assert.Single(_tokenEvents);
            Assert.Equal(StoppedMarkerOutcome.Saved, data.StoppedMarkerOutcome);
            Assert.Single(Assert.Single(_historyEvents).Event.ChatHistory, StoppedTurnMarker.IsMarker);
        }

        /// <summary>Review fix (thread 97626): the marker save itself failing still leaves the usage recorded.</summary>
        [Fact]
        public async Task StoppedTurn_MarkerSaveFails_StillRecordsUsage()
        {
            SetupLoadedHistory(EarlierQuestion, EarlierAnswer);
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<ChatHistoryUpdatedDomainEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("blob store down"));
            using var stop = new CancellationTokenSource();
            var kernel = BuildKernel(() => Stream(["Partial answer"], stop.Cancel, new OperationCanceledException(stop.Token)));
            var data = BuildData(kernel);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildService(_stopStateStoreMock.Object).RunChatCompletionAgentAsync(data, stop.Token));

            Assert.Single(_tokenEvents);
            Assert.Equal(StoppedMarkerOutcome.Failed, data.StoppedMarkerOutcome);
        }

        /// <summary>Review fix (thread 97626): early and queued stops - a failed live send still saves the marker.</summary>
        [Fact]
        public async Task EarlyStop_LiveMarkerFails_StillSavesTheMarker()
        {
            SetupLoadedHistory(EarlierQuestion, EarlierAnswer);
            _notificationServiceMock
                .Setup(x => x.SendMessageToChat(It.IsAny<string>(), It.IsAny<ChatMessage>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("hub down"));

            var marker = await BuildService(_stopStateStoreMock.Object).SaveEarlyStoppedTurnAsync(BuildOptions(), runData: null);

            Assert.Equal(StoppedMarkerOutcome.Saved, marker);
            Assert.Single(_historyEvents);
        }

        /// <summary>US 7502: the first turn of a conversation stopped: nothing saved (no blob, no conversation), marker live only.</summary>
        [Fact]
        public async Task FirstTurnStopped_SavesNothing_ButStillSendsTheMarkerLive()
        {
            SetupLoadedHistory();
            using var stop = new CancellationTokenSource();
            var kernel = BuildKernel(() => Stream(["Partial"], stop.Cancel, new InvalidOperationException("request aborted")));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildService(stopStateStore: null).RunChatCompletionAgentAsync(BuildData(kernel), stop.Token));

            Assert.Empty(_historyEvents);
            Assert.Single(_tokenEvents);
            Assert.Equal(Now, StoppedTurnMarker.ReadStoppedAt(VerifyOneLiveMarker().Metadata));
        }

        [Fact]
        public async Task RealError_StillDiscardsThePartialText_AndSavesNoMarker()
        {
            var kernel = BuildKernel(() => Stream(["Partial before the failure"], afterChunks: null, new InvalidOperationException("provider failure")));

            var answer = await BuildService(_stopStateStoreMock.Object).RunChatCompletionAgentAsync(BuildData(kernel), TestContext.Current.CancellationToken);

            Assert.Equal(string.Empty, answer.Message);
            var (saved, _) = Assert.Single(_historyEvents);
            Assert.DoesNotContain(saved.ChatHistory, m => m.Role == AuthorRole.Assistant);
            Assert.DoesNotContain(saved.ChatHistory, StoppedTurnMarker.IsMarker);
            // US 7959 / Task 7961: a real error is recorded for the turn's choke point instead of a code-less bubble.
            _notificationServiceMock.Verify(x => x.SendMessageToChat(SessionId, It.Is<ChatMessage>(m => m.Type == "system"),
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _notificationServiceMock.Verify(x => x.SendMessageToChat(It.IsAny<string>(), It.Is<ChatMessage>(m => m.Content == StoppedTurnMarker.Text),
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
        /// <summary>
        /// Timing gap: the Stop lands after the whole answer was streamed (the user saw all of it) but before the save.
        /// The answer is still saved, as a normal turn, without a marker.
        /// </summary>
        [Fact]
        public async Task StopLandingAfterStreamingCompleted_StillSavesTheFullAnswer_WithoutAMarker()
        {
            using var stop = new CancellationTokenSource();
            _notificationServiceMock
                .Setup(x => x.SendStreamingTokens(SessionId, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<List<string>>(), AuthorRole.Assistant,
                    ChannelMessageType.Normal, true, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback(stop.Cancel)
                .Returns(Task.CompletedTask);
            var kernel = BuildKernel(() => Stream(["The full ", "answer."], afterChunks: null, thenThrow: null));

            var answer = await BuildService(_stopStateStoreMock.Object).RunChatCompletionAgentAsync(BuildData(kernel), stop.Token);

            Assert.True(stop.IsCancellationRequested);
            Assert.Equal("The full answer.", answer.Message);
            var (saved, saveToken) = Assert.Single(_historyEvents);
            Assert.False(saveToken.CanBeCanceled);
            Assert.Equal("The full answer.", saved.ChatHistory[^1].Content);
            Assert.DoesNotContain(saved.ChatHistory, StoppedTurnMarker.IsMarker);
            Assert.Single(_tokenEvents);
        }
        [Fact]
        public async Task DetachedMarkers_AreNeverSentToTheModel_AndArePutBackWhenTheNextTurnIsSaved()
        {
            var earlierMarker = StoppedTurnMarker.Build("earlier-marker", Now.AddMinutes(-5).AddMilliseconds(1), Now.AddMinutes(-5));
            var history = new ChatHistory
            {
                Timestamped(AuthorRole.User, "Earlier question", Now.AddMinutes(-6)),
                Timestamped(AuthorRole.Assistant, "Earlier partial", Now.AddMinutes(-5))
            };
            ChatHistory sentToModel = null;
            var kernel = BuildKernel(() => Stream(["New answer"], afterChunks: null, thenThrow: null), onRequest: h => sentToModel = new ChatHistory(h));

            await BuildService(_stopStateStoreMock.Object).RunChatCompletionAgentAsync(BuildData(kernel, history, [earlierMarker]), TestContext.Current.CancellationToken);

            Assert.NotNull(sentToModel);
            Assert.DoesNotContain(sentToModel, m => m.Content == StoppedTurnMarker.Text);

            var (saved, _) = Assert.Single(_historyEvents);
            Assert.Equal(new[] { "Earlier question", "Earlier partial", StoppedTurnMarker.Text, "Write me a long answer", "New answer" },
                saved.ChatHistory.Select(m => m.Content));
        }
        [Fact]
        public async Task LoadChatHistoryBySessionIdAsync_DetachesTheMarkersFromTheModelHistory()
        {
            var loaded = new ChatHistory
            {
                Timestamped(AuthorRole.User, "Earlier question", Now.AddMinutes(-6)),
                Timestamped(AuthorRole.Assistant, "Earlier partial", Now.AddMinutes(-5)),
                StoppedTurnMarker.Build("earlier-marker", Now.AddMinutes(-5).AddMilliseconds(1), Now.AddMinutes(-5))
            };
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<LoadRunnableAgentChatHistoryBySessionQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Now.AddMinutes(-10), "Test Chat", loaded));

            var (_, _, thread, detached) = await BuildService(_stopStateStoreMock.Object).LoadChatHistoryBySessionIdAsync(
                new TenantAgent { Id = 42, EnableSTM = true }, SessionId, TestContext.Current.CancellationToken);

            Assert.Equal(new[] { "Earlier question", "Earlier partial" }, thread.ChatHistory.Select(m => m.Content));
            Assert.Equal("earlier-marker", Assert.Single(detached).Metadata[CommonMetadataConsts.MessageId]);
        }

        // ==================== Early Stop (before the answer starts streaming) and Stop while queued ====================

        private static RunTextOptions BuildOptions() => new()
        {
            Agent = new TenantAgent { Id = 42, Name = "Kit", AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" } },
            SessionId = SessionId,
            Message = "Write me a long answer",
            Files = []
        };

        [Fact]
        public async Task EarlyStop_SavesOnlyTheMarker_NeverTheQuestion()
        {
            SetupLoadedHistory(EarlierQuestion, EarlierAnswer);

            var marker = await BuildService(_stopStateStoreMock.Object).SaveEarlyStoppedTurnAsync(BuildOptions(), runData: null);

            Assert.Equal(StoppedMarkerOutcome.Saved, marker);
            var (saved, saveToken) = Assert.Single(_historyEvents);
            Assert.True(saved.IsStoppedTurn);
            Assert.False(saveToken.CanBeCanceled);
            Assert.Equal(new[] { "Earlier question", "Earlier answer", StoppedTurnMarker.Text }, saved.ChatHistory.Select(m => m.Content));
            VerifyOneLiveMarker();
            Assert.Empty(_tokenEvents);
        }

        /// <summary>A Stop during the turn's own vector search: the model run saves nothing, the caller's early save adds the marker once.</summary>
        [Fact]
        public async Task StopDuringVectorSearch_ModelRunSavesNothing_ThenEarlySaveAddsTheMarkerOnce()
        {
            SetupLoadedHistory(EarlierQuestion, EarlierAnswer);
            using var stop = new CancellationTokenSource();
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.SearchRunnableAgentVectorDataByEmail.SearchRunnableAgentVectorDataByEmailQuery>(), It.IsAny<CancellationToken>()))
                .Callback(stop.Cancel)
                .ThrowsAsync(new OperationCanceledException(stop.Token));
            var service = BuildService(_stopStateStoreMock.Object);
            var data = BuildData(BuildKernel(() => Stream(["never"], afterChunks: null, thenThrow: null)), new ChatHistory { EarlierQuestion, EarlierAnswer });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunChatCompletionAgentAsync(data, stop.Token));
            Assert.Empty(_historyEvents);
            Assert.False(data.TurnSaved);

            await service.SaveEarlyStoppedTurnAsync(BuildOptions(), data);
            var repeated = await service.SaveEarlyStoppedTurnAsync(BuildOptions(), data);

            Assert.Equal(StoppedMarkerOutcome.Saved, repeated);
            var (saved, _) = Assert.Single(_historyEvents);
            Assert.Equal(new[] { "Earlier question", "Earlier answer", StoppedTurnMarker.Text }, saved.ChatHistory.Select(m => m.Content));
        }

        /// <summary>A Stop while streaming is saved by the model run itself; the caller's early save then does nothing.</summary>
        [Fact]
        public async Task StopWhileStreaming_IsSavedOnce_EarlySaveDoesNothing()
        {
            SetupLoadedHistory(EarlierQuestion, EarlierAnswer);
            using var stop = new CancellationTokenSource();
            var service = BuildService(_stopStateStoreMock.Object);
            var data = BuildData(BuildKernel(() => Stream(["Partial"], stop.Cancel, new OperationCanceledException(stop.Token))));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunChatCompletionAgentAsync(data, stop.Token));
            var marker = await service.SaveEarlyStoppedTurnAsync(BuildOptions(), data);

            Assert.Equal(StoppedMarkerOutcome.Saved, marker);
            Assert.True(data.TurnSaved);
            Assert.Single(_historyEvents);
        }

        [Fact]
        public async Task StoppedWhileQueued_SavesOnlyTheMarker()
        {
            SetupLoadedHistory(EarlierQuestion, EarlierAnswer);
            var agent = new TenantAgent { Id = 42, Name = "Kit", EnableSTM = true };

            await BuildService(_stopStateStoreMock.Object).SaveTurnStoppedBeforeRunAsync(agent, SessionId, rootIsWidget: null);

            var (saved, _) = Assert.Single(_historyEvents);
            Assert.Same(agent, saved.RunnableAgent);
            Assert.Equal(new[] { "Earlier question", "Earlier answer", StoppedTurnMarker.Text }, saved.ChatHistory.Select(m => m.Content));
            Assert.Equal(StoppedAt, StoppedTurnMarker.ReadStoppedAt(saved.ChatHistory[^1].Metadata));
            VerifyOneLiveMarker();
        }

        [Fact]
        public async Task EarlyStop_NoSavedHistoryYet_ReportsNoHistoryYet()
        {
            SetupLoadedHistory();

            var marker = await BuildService(_stopStateStoreMock.Object).SaveEarlyStoppedTurnAsync(BuildOptions(), runData: null);

            Assert.Equal(StoppedMarkerOutcome.NoHistoryYet, marker);
            Assert.Empty(_historyEvents);
        }

        /// <summary>A first message stopped while queued, or a hand-off agent with no history yet: nothing is saved.</summary>
        [Fact]
        public async Task StoppedWhileQueued_NoSavedHistoryYet_SavesNothing()
        {
            SetupLoadedHistory();

            await BuildService(_stopStateStoreMock.Object).SaveTurnStoppedBeforeRunAsync(new TenantAgent { Id = 7, Name = "Kit", EnableSTM = true }, SessionId, rootIsWidget: null);

            Assert.Empty(_historyEvents);
        }

        // ==================== Marker for the chat agent when hand-offs were dropped ====================

        private static readonly TenantAgent Maisy = new() { Id = 5, Name = "MAISY", EnableSTM = true };

        /// <summary>MAISY had answered, Kit was still queued, Stop: MAISY's history ends with exactly one marker.</summary>
        [Fact]
        public async Task SaveStoppedMarker_AppendsOneMarkerAfterTheLastMessage()
        {
            SetupLoadedHistory(Timestamped(AuthorRole.User, "Ask Kit to write it", Now.AddMinutes(-2)), Timestamped(AuthorRole.Assistant, "Handing off to Kit", Now.AddMinutes(-1)));

            var saved = await BuildService(_stopStateStoreMock.Object).SaveStoppedMarkerAsync(Maisy, SessionId, StoppedAt);

            Assert.Equal(StoppedMarkerOutcome.Saved, saved);
            var (evt, token) = Assert.Single(_historyEvents);
            Assert.False(token.CanBeCanceled);
            Assert.True(evt.IsStoppedTurn);
            Assert.Same(Maisy, evt.RunnableAgent);
            Assert.Equal(new[] { "Ask Kit to write it", "Handing off to Kit", StoppedTurnMarker.Text }, evt.ChatHistory.Select(m => m.Content));
            var marker = Assert.Single(evt.ChatHistory, StoppedTurnMarker.IsMarker);
            Assert.Equal(StoppedAt, StoppedTurnMarker.ReadStoppedAt(marker.Metadata));
        }

        /// <summary>A save that lands after the next message was already saved: the divider stays where the Stop happened.</summary>
        [Fact]
        public async Task SaveStoppedMarker_LateSave_IsStampedWithTheStopAndPlacedBeforeNewerMessages()
        {
            SetupLoadedHistory(Timestamped(AuthorRole.User, "Ask Kit to write it", Now.AddMinutes(-2)), Timestamped(AuthorRole.Assistant, "Handing off to Kit", Now.AddMinutes(-1)),
                Timestamped(AuthorRole.User, "Next question", StoppedAt.AddSeconds(10)));

            var saved = await BuildService(_stopStateStoreMock.Object).SaveStoppedMarkerAsync(Maisy, SessionId, StoppedAt);

            Assert.Equal(StoppedMarkerOutcome.Saved, saved);
            var (evt, _) = Assert.Single(_historyEvents);
            Assert.Equal(new[] { "Ask Kit to write it", "Handing off to Kit", StoppedTurnMarker.Text, "Next question" }, evt.ChatHistory.Select(m => m.Content));
            var marker = Assert.Single(evt.ChatHistory, StoppedTurnMarker.IsMarker);
            Assert.Equal(StoppedAt, AMAIS.WKFLO.Common.AI.Application.Helpers.ChatHistoryMessageExtractor.GetTimestampFromMetadata(marker.Metadata));
        }

        [Fact]
        public async Task SaveStoppedMarker_SameStopAlreadySavedByARunningTurn_AddsNoSecondMarker()
        {
            SetupLoadedHistory(Timestamped(AuthorRole.User, "Question", Now.AddMinutes(-2)), Timestamped(AuthorRole.Assistant, "Partial", Now.AddSeconds(-1)),
                StoppedTurnMarker.Build("running-turn-marker", Now, StoppedAt));

            var saved = await BuildService(_stopStateStoreMock.Object).SaveStoppedMarkerAsync(Maisy, SessionId, StoppedAt);

            Assert.Equal(StoppedMarkerOutcome.AlreadyPresent, saved);
            Assert.Empty(_historyEvents);
        }
        [Fact]
        public async Task SaveStoppedMarker_OnlyAnEarlierStopsMarker_AddsThisStopsMarker()
        {
            SetupLoadedHistory(Timestamped(AuthorRole.User, "Question", Now.AddMinutes(-10)), StoppedTurnMarker.Build("earlier", Now.AddMinutes(-9), Now.AddMinutes(-9)));

            await BuildService(_stopStateStoreMock.Object).SaveStoppedMarkerAsync(Maisy, SessionId, StoppedAt);

            var (evt, _) = Assert.Single(_historyEvents);
            Assert.Equal(2, evt.ChatHistory.Count(StoppedTurnMarker.IsMarker));
        }
        [Fact]
        public async Task SaveStoppedMarker_SaveFails_IsLoggedNotThrown()
        {
            SetupLoadedHistory(Timestamped(AuthorRole.User, "Question", Now.AddMinutes(-2)));
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<ChatHistoryUpdatedDomainEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("blob store down"));

            var saved = await BuildService(_stopStateStoreMock.Object).SaveStoppedMarkerAsync(Maisy, SessionId, StoppedAt);

            Assert.Equal(StoppedMarkerOutcome.Failed, saved);
        }
    }
}
