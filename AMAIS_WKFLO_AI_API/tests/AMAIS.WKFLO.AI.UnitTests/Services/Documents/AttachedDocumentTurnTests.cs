using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
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
using AMAIS.WKFLO.Common.Models.Documents;
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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Services.Documents
{
    /// <summary>
    /// Bug 8189 / Task 8219: in a turn, a long document is indexed and the history gets a short message in its place; a short
    /// one stays inline exactly as before.
    /// </summary>
    public class AttachedDocumentTurnTests
    {
        private readonly Mock<IChatDocumentIndexer> _indexer = new();
        private readonly Mock<IWkfloMediator> _mediator = new();
        private readonly List<ChatHistoryUpdatedDomainEvent> _historyEvents = [];

        public AttachedDocumentTurnTests()
        {
            _mediator.Setup(x => x.Publish(It.IsAny<ChatHistoryUpdatedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<ChatHistoryUpdatedDomainEvent, CancellationToken>((e, _) => _historyEvents.Add(e))
                .Returns(Task.CompletedTask);
        }

        private SemanticKernelAgentService BuildService(bool withIndexer = true, IChatDocumentRetriever retriever = null)
        {
            var dbMock = new Mock<IMasterDataDbContext>();
            dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            dbMock.Setup(x => x.AgentAllowedSpinUpAgents).Returns(new List<AgentAllowedSpinUpAgent>().BuildMockDbSet().Object);

            return new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(), Mock.Of<IWkfloMapper>(), Mock.Of<IDatastreamProvider>(), Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(), Mock.Of<IMcpClientService>(), _mediator.Object, Options.Create(new ElasticsearchSettings()),
                new TenantContext { TenantName = "acme" }, new ChatContext(), new UserContext { Guid = "user-1" }, Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(), Mock.Of<IDateTimeProvider>(d => d.DateTimeOffsetUtcNow() == DateTimeOffset.UtcNow), dbMock.Object,
                Mock.Of<IContactFormPluginLogService>(),
                chatDocumentIndexer: withIndexer ? _indexer.Object : null,
                chatDocumentSettings: Options.Create(new ChatDocumentSettings()),
                chatDocumentRetriever: retriever);
        }

        private static string DataUri(string text) => $"data:text/plain;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(text))}";

        private static AttachedDocumentInfo Info(string status = ChatDocumentIndexStatus.Ready) => new()
        {
            DocumentId = Guid.NewGuid(),
            FileName = "report.pdf",
            FileType = "application/pdf",
            FileSize = 11_534_336,
            CharCount = 2_400_000,
            ChunkCount = 1_600,
            IndexedChunkCount = status == ChatDocumentIndexStatus.Partial ? 1_000 : 1_600,
            IndexStatus = status,
            PageCount = 1_180,
            Summary = "A long annual report."
        };

        [Fact]
        public async Task ALongDocument_IsIndexedOnce_AndItsFileDataIsReleased()
        {
            var info = Info();
            ChatDocumentIndexRequest seen = null;
            _indexer.Setup(i => i.IndexAsync(It.IsAny<ChatDocumentIndexRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ChatDocumentIndexRequest, CancellationToken>((r, _) => seen = r)
                .ReturnsAsync(info);
            var longText = new string('a', 60_000);
            var file = new FileAttachment { FileName = "notes.txt", FileSize = 60_000, FileData = DataUri(longText) };

            await BuildService().ProcessFilesAsync([file], "session-docs", DocumentTestSupport.Agent(), new Kernel(), [], TestContext.Current.CancellationToken);

            Assert.Same(info, file.Document);
            Assert.Null(file.FileData);
            Assert.Null(file.ExtractedText);
            Assert.Equal("text/plain", file.FileType);
            Assert.Equal(longText, seen.Text);
            Assert.Equal(new ChatDocumentScope("acme", "session-docs", "user-1"), seen.Scope);
            _indexer.Verify(i => i.IndexAsync(It.IsAny<ChatDocumentIndexRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AShortDocument_StaysInline_WithoutIndexing()
        {
            var file = new FileAttachment { FileName = "notes.txt", FileSize = 10, FileData = DataUri(new string('a', 50_000)) };

            await BuildService().ProcessFilesAsync([file], "session-docs", DocumentTestSupport.Agent(), new Kernel(), [], TestContext.Current.CancellationToken);

            Assert.Null(file.Document);
            Assert.Null(file.FileData);
            Assert.Equal(50_000, file.ExtractedText.Length);
            _indexer.Verify(i => i.IndexAsync(It.IsAny<ChatDocumentIndexRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task WithoutAnIndexer_ALongDocumentStaysInline()
        {
            var file = new FileAttachment { FileName = "notes.txt", FileSize = 10, FileData = DataUri(new string('a', 60_000)) };

            await BuildService(withIndexer: false).ProcessFilesAsync([file], "session-docs", DocumentTestSupport.Agent(), new Kernel(), [], TestContext.Current.CancellationToken);

            Assert.Null(file.Document);
            Assert.Equal(60_000, file.ExtractedText.Length);
        }

        [Fact]
        public async Task AForwardedDocument_GetsItsOwnCopy_OfTheSourceDocument()
        {
            var source = Info();
            var copy = Info();
            _indexer.Setup(i => i.CopyAsync(It.Is<ChatDocumentScope>(s => s.TenantName == "acme" && s.SessionId == "session-docs" && s.UserId == "user-1"),
                    It.Is<AttachedDocumentInfo>(s => s.DocumentId == source.DocumentId), 77, It.IsAny<CancellationToken>()))
                .ReturnsAsync(copy);
            var file = new FileAttachment { FileName = "report.pdf", SourceDocumentId = source.DocumentId, SourceDocument = source };

            await BuildService().ProcessFilesAsync([file], "session-docs", DocumentTestSupport.Agent(77), new Kernel(), [], TestContext.Current.CancellationToken);

            Assert.Same(copy, file.Document);
        }

        private RunChatData BuildData(params FileAttachment[] files)
        {
            var chat = new Mock<IChatCompletionService>();
            chat.Setup(x => x.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()))
                .Returns(() => new[] { new StreamingChatMessageContent(AuthorRole.Assistant, "Answer.") }.ToAsyncEnumerable());
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chat.Object);

            return new RunChatData
            {
                Kernel = builder.Build(),
                Agent = DocumentTestSupport.Agent(),
                AgentTagNames = [],
                Prompt = "You are Kit.",
                Message = "What is in the report?",
                SessionId = "session-docs",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test",
                UserMessageId = Guid.NewGuid(),
                UserMessageTimestamp = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
                Files = [.. files]
            };
        }

        [Fact]
        public async Task AnIndexedDocument_GetsAShortMessage_WithTheCardMetadata_AndNoDocumentText()
        {
            var info = Info();
            var data = BuildData(new FileAttachment { FileName = "report.pdf", FileSize = info.FileSize, Document = info, FileType = "application/pdf" });

            await BuildService().RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            var saved = Assert.Single(_historyEvents).ChatHistory;
            var document = Assert.Single(saved, m => m.Items.OfType<TextContent>().Any(t => t.Metadata?.ContainsKey(CommonMetadataConsts.DocumentId) == true));
            Assert.StartsWith("Attached document: 'report.pdf' (application/pdf, 11.0 MB, ~2.4M characters, 1,180 pages).", document.Content);
            Assert.Contains("Summary: A long annual report.", document.Content);
            Assert.DoesNotContain("Document content:", document.Content);

            // The metadata the file card, forwarding and the memory handlers read, and the question's timestamp.
            var item = document.Items.OfType<TextContent>().Single();
            Assert.Equal("report.pdf", item.Metadata[CommonMetadataConsts.FileName]);
            Assert.Equal("application/pdf", item.Metadata[CommonMetadataConsts.FileType]);
            Assert.Equal(info.FileSize, item.Metadata[CommonMetadataConsts.FileSize]);
            Assert.Equal(info.DocumentId.ToString("D"), item.Metadata[CommonMetadataConsts.DocumentId]);
            Assert.Equal(ChatDocumentIndexStatus.Ready, item.Metadata[CommonMetadataConsts.DocumentIndexStatus]);
            Assert.Equal(data.UserMessageTimestamp, document.Metadata[CommonMetadataConsts.Timestamp]);
            Assert.True(Guid.TryParse((string)document.Metadata[CommonMetadataConsts.MessageId], out _));
        }

        [Fact]
        public async Task AShortDocument_IsStillAddedInlineWithTheDocumentContentPrefix()
        {
            var data = BuildData(new FileAttachment { FileName = "notes.txt", FileSize = 10, ExtractedText = "Some notes", FileType = "text/plain" });

            await BuildService().RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            var saved = Assert.Single(_historyEvents).ChatHistory;
            var document = Assert.Single(saved, m => m.Content?.StartsWith("Document content:\n") == true);
            Assert.Equal("Document content:\nSome notes", document.Content);
            Assert.Equal("notes.txt", document.Items.OfType<TextContent>().Single().Metadata[CommonMetadataConsts.FileName]);
        }

        [Fact]
        public async Task ThePassagesGoIntoThisTurnsInstructionsOnly_NeverIntoTheSavedHistory()
        {
            var existing = Info();
            var retriever = new Mock<IChatDocumentRetriever>();
            IReadOnlyList<AttachedDocumentInfo> asked = null;
            string query = null;
            retriever.Setup(r => r.BuildTurnContextAsync(It.IsAny<IReadOnlyList<AttachedDocumentInfo>>(), It.IsAny<string>(), It.IsAny<ChatDocumentScope>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<AttachedDocumentInfo>, string, ChatDocumentScope, bool, CancellationToken>((d, q, _, _, _) => { asked = d; query = q; })
                .ReturnsAsync("PASSAGE-ABOUT-PRICING");

            var sent = new List<ChatMessageContent>();
            var chat = new Mock<IChatCompletionService>();
            chat.Setup(x => x.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()))
                .Returns((ChatHistory h, PromptExecutionSettings _, Kernel _, CancellationToken _) =>
                {
                    sent.AddRange(h);
                    return new[] { new StreamingChatMessageContent(AuthorRole.Assistant, "Answer.") }.ToAsyncEnumerable();
                });
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chat.Object);

            var data = BuildData();
            data.Kernel = builder.Build();
            data.Message = "What does it say about pricing?";
            data.ChatHistoryAgentThread.ChatHistory.Add(new ChatMessageContent(AuthorRole.User,
                [new TextContent(AttachedDocumentMessage.BuildText(existing), metadata: AttachedDocumentMessage.BuildItemMetadata(existing))]));

            await BuildService(retriever: retriever.Object).RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.Equal(existing.DocumentId, Assert.Single(asked).DocumentId);
            Assert.Equal("What does it say about pricing?", query);
            Assert.Contains(sent, m => m.Role == AuthorRole.System && m.Content.Contains("PASSAGE-ABOUT-PRICING"));
            var saved = Assert.Single(_historyEvents).ChatHistory;
            Assert.DoesNotContain(saved, m => m.Content?.Contains("PASSAGE-ABOUT-PRICING") == true);
            Assert.DoesNotContain(saved, m => m.Role == AuthorRole.System);
        }

        [Fact]
        public void TheShortMessageRoundTripsThroughTheSavedJson()
        {
            var info = Info(ChatDocumentIndexStatus.Partial);
            var message = new ChatMessageContent(AuthorRole.User, [new TextContent(AttachedDocumentMessage.BuildText(info), metadata: AttachedDocumentMessage.BuildItemMetadata(info))]);

            // The chat blob turns metadata values into JSON elements: round trip through JSON like it does.
            var json = JsonSerializer.Serialize(message);
            var loaded = JsonSerializer.Deserialize<ChatMessageContent>(json);

            Assert.True(AttachedDocumentMessage.TryRead(loaded, out var read));
            Assert.Equal(info.DocumentId, read.DocumentId);
            Assert.Equal("report.pdf", read.FileName);
            Assert.Equal(1_600, read.ChunkCount);
            Assert.Equal(1_000, read.IndexedChunkCount);
            Assert.Equal(ChatDocumentIndexStatus.Partial, read.IndexStatus);
            Assert.Equal("A long annual report.", read.Summary);
            Assert.Contains("Only 1,000 of its 1,600 sections are searchable", AttachedDocumentMessage.BuildText(info));
        }

        [Fact]
        public void AFailedDocument_CarriesItsExcerpt_AndNoSummaryIsReadBack()
        {
            var info = Info(ChatDocumentIndexStatus.Failed);
            info.Excerpt = "[... ~50% into the document ...]\nsome text";

            var text = AttachedDocumentMessage.BuildText(info);
            var message = new ChatMessageContent(AuthorRole.User, [new TextContent(text, metadata: AttachedDocumentMessage.BuildItemMetadata(info))]);

            Assert.Contains("could not be indexed", text);
            Assert.EndsWith("some text", text);
            Assert.True(AttachedDocumentMessage.TryRead(message, out var read));
            Assert.Null(read.Summary);
        }
    }
}
