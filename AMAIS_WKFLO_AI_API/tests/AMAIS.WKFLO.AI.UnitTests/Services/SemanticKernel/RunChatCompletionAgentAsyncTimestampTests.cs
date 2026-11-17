using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
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
    /// US 7974: every streaming update of an answer carries the same server timestamp, and that timestamp is the one
    /// persisted with the answer, so a bubble shows the same time live and after a reload. The question and the
    /// "thinking" message keep the timestamps the run was given for them.
    /// </summary>
    public class RunChatCompletionAgentAsyncTimestampTests
    {
        private const string SessionId = "session-timestamps";
        private static readonly DateTimeOffset Start = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

        private readonly Mock<IChatNotificationService> _notificationServiceMock = new();
        private readonly Mock<IWkfloMediator> _mediatorMock = new();
        private readonly List<ChatHistoryUpdatedDomainEvent> _historyEvents = [];
        private DateTimeOffset _clock = Start;

        public RunChatCompletionAgentAsyncTimestampTests()
        {
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<ChatHistoryUpdatedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<ChatHistoryUpdatedDomainEvent, CancellationToken>((e, _) => _historyEvents.Add(e))
                .Returns(Task.CompletedTask);
        }

        private SemanticKernelAgentService BuildService()
        {
            var speechProviderMock = new Mock<IAzureSpeechProvider>();
            speechProviderMock
                .Setup(x => x.SynthesizeSpeechAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([1, 2, 3]);

            var dbMock = new Mock<IMasterDataDbContext>();
            dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            dbMock.Setup(x => x.AgentAllowedSpinUpAgents).Returns(new List<AgentAllowedSpinUpAgent>().BuildMockDbSet().Object);

            // A clock that moves on every read, so a timestamp read twice would not match.
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(() => _clock = _clock.AddSeconds(1));

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
                speechProviderMock.Object,
                dateTimeProviderMock.Object,
                dbMock.Object,
                Mock.Of<IContactFormPluginLogService>());
        }

        private static async IAsyncEnumerable<StreamingChatMessageContent> Stream(string[] chunks, IReadOnlyDictionary<string, object> chunkMetadata,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk, metadata: chunkMetadata);
            }
        }

        private static Kernel BuildKernel(params string[] chunks) => BuildKernel(null, chunks);

        /// <param name="chunkMetadata">Metadata carried by every streamed chunk, e.g. a provider's own Timestamp.</param>
        private static Kernel BuildKernel(IReadOnlyDictionary<string, object> chunkMetadata, params string[] chunks)
        {
            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            chatCompletionServiceMock
                .Setup(x => x.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()))
                .Returns(() => Stream(chunks, chunkMetadata));

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);
            return builder.Build();
        }

        private static RunChatData BuildData(Kernel kernel) => new()
        {
            Kernel = kernel,
            Agent = new TenantAgent { Id = 42, Name = "Kit", AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" } },
            AgentTagNames = [],
            Prompt = "You are Kit.",
            Message = "What time is it?",
            SessionId = SessionId,
            ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
            CreatedAt = Start,
            ChatName = "Test Chat",
            UserMessageId = Guid.NewGuid()
        };

        private List<(string MessageId, DateTimeOffset Timestamp, bool IsComplete)> StreamedUpdates() =>
            _notificationServiceMock.Invocations
                .Where(i => i.Method.Name == nameof(IChatNotificationService.SendStreamingTokens))
                .Select(i => ((string)i.Arguments[1], (DateTimeOffset)i.Arguments[2], (bool)i.Arguments[6]))
                .ToList();

        private static DateTimeOffset PersistedTimestamp(ChatMessageContent message) => (DateTimeOffset)message.Metadata[CommonMetadataConsts.Timestamp];

        [Fact]
        public async Task EveryStreamingUpdateOfTheAnswer_CarriesTheTimestampPersistedWithTheAnswer()
        {
            // 12 chunks: two partial updates (every 5 tokens) plus the final one.
            var kernel = BuildKernel("One ", "two ", "three ", "four ", "five ", "six ", "seven ", "eight ", "nine ", "ten ", "eleven ", "twelve.");

            var answer = await BuildService().RunChatCompletionAgentAsync(BuildData(kernel), TestContext.Current.CancellationToken);

            var updates = StreamedUpdates();
            Assert.Equal(3, updates.Count);
            Assert.True(updates[^1].IsComplete);
            Assert.All(updates, u => Assert.Equal(answer.MessageId, u.MessageId));
            var streamedTimestamp = Assert.Single(updates.Select(u => u.Timestamp).Distinct());

            var saved = Assert.Single(_historyEvents).ChatHistory[^1];
            Assert.Equal(AuthorRole.Assistant, saved.Role);
            Assert.Equal(answer.MessageId, saved.Metadata[CommonMetadataConsts.MessageId]);
            Assert.Equal(streamedTimestamp, PersistedTimestamp(saved));
            Assert.Equal(TimeSpan.Zero, PersistedTimestamp(saved).Offset);
        }

        private static readonly IReadOnlyDictionary<string, object> StaleChunkMetadata = new Dictionary<string, object>
        {
            [CommonMetadataConsts.Timestamp] = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            [CommonMetadataConsts.MessageId] = "provider-message-id"
        };

        [Fact]
        public async Task ChunksCarryingAProviderTimestamp_ReplyIsPersistedWithTheStreamedTimestampAndMessageId()
        {
            var kernel = BuildKernel(StaleChunkMetadata, "One ", "two ", "three ", "four ", "five ", "six.");

            var answer = await BuildService().RunChatCompletionAgentAsync(BuildData(kernel), TestContext.Current.CancellationToken);

            var streamedTimestamp = Assert.Single(StreamedUpdates().Select(u => u.Timestamp).Distinct());
            var saved = Assert.Single(_historyEvents).ChatHistory[^1];
            Assert.Equal(streamedTimestamp, PersistedTimestamp(saved));
            Assert.Equal(answer.MessageId, saved.Metadata[CommonMetadataConsts.MessageId]);
        }

        [Fact]
        public async Task VoiceChunksCarryingAProviderTimestamp_ReplyIsPersistedWithTheStreamedTimestampAndMessageId()
        {
            var kernel = BuildKernel(StaleChunkMetadata, "One ", "two ", "three ", "four ", "five ", "six.");
            var data = new RunVoiceChatData
            {
                Kernel = kernel,
                Agent = new TenantAgent { Id = 42, Name = "Kit", AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" } },
                AgentTagNames = [],
                Prompt = "You are Kit.",
                Message = "What time is it?",
                SessionId = SessionId,
                ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
                CreatedAt = Start,
                ChatName = "Test Chat",
                Voice = "en-US-JennyNeural",
                Language = "en-US",
                UserMessageId = Guid.NewGuid()
            };

            var answer = await BuildService().RunVoiceChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            var streamedTimestamp = Assert.Single(StreamedUpdates().Select(u => u.Timestamp).Distinct());
            var saved = Assert.Single(_historyEvents).ChatHistory[^1];
            Assert.Equal(AuthorRole.Assistant, saved.Role);
            Assert.Equal(streamedTimestamp, PersistedTimestamp(saved));
            Assert.Equal(answer.MessageId, saved.Metadata[CommonMetadataConsts.MessageId]);
        }

        // StampStreamedReply is shared by the text and voice runs. Semantic Kernel builds the reply from the streamed
        // text alone, so a reply already carrying a Timestamp is set up directly.

        [Fact]
        public void StampStreamedReply_ReplyAlreadyCarryingATimestamp_OverwritesTimestampAndMessageId()
        {
            var streamedAt = Start.AddSeconds(5);
            var history = new ChatHistory();
            history.AddUserMessage("What time is it?");
            history.Add(new ChatMessageContent(AuthorRole.Assistant, "partial", metadata: new Dictionary<string, object>
            {
                [CommonMetadataConsts.Timestamp] = Start.AddSeconds(9),
                [CommonMetadataConsts.MessageId] = "provider-message-id",
                ["provider-key"] = "kept"
            }));

            SemanticKernelAgentService.StampStreamedReply(history, historyCountBeforeStreaming: 1, "Full answer.", "msg-1", streamedAt);

            var reply = history[^1];
            Assert.Equal(2, history.Count);
            Assert.Equal("Full answer.", reply.Content);
            Assert.Equal(streamedAt, reply.Metadata[CommonMetadataConsts.Timestamp]);
            Assert.Equal("msg-1", reply.Metadata[CommonMetadataConsts.MessageId]);
            Assert.Equal("kept", reply.Metadata["provider-key"]);
        }

        [Fact]
        public void StampStreamedReply_StreamAddedNothing_LeavesTheThinkingMessageAddedBeforeItUntouched()
        {
            var thinkingAt = Start.AddSeconds(2);
            var history = new ChatHistory();
            history.AddUserMessage("What time is it?");
            history.Add(new ChatMessageContent(AuthorRole.Assistant, "1. Thinking.", metadata: new Dictionary<string, object>
            {
                [CommonMetadataConsts.Timestamp] = thinkingAt
            }));

            SemanticKernelAgentService.StampStreamedReply(history, historyCountBeforeStreaming: 2, string.Empty, "msg-1", Start.AddSeconds(5));

            Assert.Equal("1. Thinking.", history[^1].Content);
            Assert.Equal(thinkingAt, history[^1].Metadata[CommonMetadataConsts.Timestamp]);
            Assert.False(history[^1].Metadata.ContainsKey(CommonMetadataConsts.MessageId));
        }

        [Fact]
        public async Task ShortAnswer_SentOnlyByTheFinalUpdate_StillMatchesThePersistedTimestamp()
        {
            var kernel = BuildKernel("Hi!");

            await BuildService().RunChatCompletionAgentAsync(BuildData(kernel), TestContext.Current.CancellationToken);

            var update = Assert.Single(StreamedUpdates());
            Assert.Equal(update.Timestamp, PersistedTimestamp(Assert.Single(_historyEvents).ChatHistory[^1]));
        }

        [Fact]
        public async Task QuestionAndThinkingMessage_ArePersistedWithTheTimestampsTheRunWasGiven()
        {
            var questionAt = Start.AddMilliseconds(100);
            var thinkingAt = Start.AddMilliseconds(200);
            var data = BuildData(BuildKernel("Answer."));
            data.UserMessageTimestamp = questionAt;
            data.AssitantReasoning = "1. The user wants the time.";
            data.AssitantReasoningTimestamp = thinkingAt;

            await BuildService().RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            var saved = Assert.Single(_historyEvents).ChatHistory;
            var question = Assert.Single(saved, m => m.Role == AuthorRole.User);
            var thinking = Assert.Single(saved, m => m.Content == data.AssitantReasoning);
            var answer = saved[^1];
            Assert.Equal(questionAt, PersistedTimestamp(question));
            Assert.Equal(thinkingAt, PersistedTimestamp(thinking));

            // The order the history endpoints sort by: question, thinking, answer.
            Assert.True(PersistedTimestamp(question) < PersistedTimestamp(thinking));
            Assert.True(PersistedTimestamp(thinking) < PersistedTimestamp(answer));
        }

        [Fact]
        public async Task QuestionAttachments_ShareTheQuestionTimestamp_AndKeepTheirPlaceBeforeIt()
        {
            const string onePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
            var questionAt = Start.AddMilliseconds(100);
            var data = BuildData(BuildKernel("Answer."));
            data.Files =
            [
                new FileAttachment { FileName = "photo.png", FileData = $"data:image/png;base64,{onePixelPng}", FileSize = 68, BlobUrl = "https://blob.test/chat-images/photo.png" },
                new FileAttachment { FileName = "notes.txt", FileData = $"data:text/plain;base64,{Convert.ToBase64String("Some notes"u8.ToArray())}", FileSize = 10 }
            ];
            data.UserMessageTimestamp = questionAt;
            data.AssitantReasoning = "1. The user shared a photo and notes.";
            data.AssitantReasoningTimestamp = Start.AddMilliseconds(200);

            await BuildService().RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            var saved = Assert.Single(_historyEvents).ChatHistory;
            var image = Assert.Single(saved, m => m.Items.OfType<ImageContent>().Any());
            var document = Assert.Single(saved, m => m.Content?.StartsWith("Document content:") == true);
            var question = Assert.Single(saved, m => m.Content == data.Message);
            var thinking = Assert.Single(saved, m => m.Content == data.AssitantReasoning);
            Assert.Equal(questionAt, PersistedTimestamp(image));
            Assert.Equal(questionAt, PersistedTimestamp(document));
            Assert.Equal(questionAt, PersistedTimestamp(question));

            // The history endpoints sort by timestamp; OrderBy is stable, so equal timestamps keep insertion order.
            var sorted = saved.OrderBy(PersistedTimestamp).ToList();
            Assert.Equal(new[] { image, document, question, thinking, saved[^1] }, sorted);
        }
    }
}
