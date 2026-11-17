using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Documents;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Services.Documents
{
    /// <summary>
    /// Bug 8189 / Task 8219: a chat saved before the fix holds a long document as one message with its whole text. When such a chat
    /// is loaded the message is indexed and replaced in place by the short one, so the history stops failing.
    /// </summary>
    public class LegacyDocumentConversionTests
    {
        private static readonly DateTimeOffset At = new(2026, 10, 20, 8, 30, 0, TimeSpan.Zero);

        private readonly Mock<IChatDocumentIndexer> _indexer = new();

        private SemanticKernelAgentService BuildService(bool withIndexer = true)
        {
            var db = new Mock<IMasterDataDbContext>();
            return new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(), Mock.Of<IWkfloMapper>(), Mock.Of<IDatastreamProvider>(), Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(), Mock.Of<IMcpClientService>(), Mock.Of<IWkfloMediator>(), Options.Create(new ElasticsearchSettings()),
                new TenantContext { TenantName = "acme" }, new ChatContext(), new UserContext { Guid = "user-1" }, Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(), Mock.Of<IDateTimeProvider>(), db.Object, Mock.Of<IContactFormPluginLogService>(),
                chatDocumentIndexer: withIndexer ? _indexer.Object : null,
                chatDocumentSettings: Options.Create(new ChatDocumentSettings()));
        }

        private static ChatMessageContent LegacyDocument(int chars, string fileName = "old.pdf", string messageId = "11111111-1111-1111-1111-111111111111") =>
            new(AuthorRole.User,
                [new TextContent("Document content:\n" + new string('a', chars), metadata: new Dictionary<string, object>
                {
                    [CommonMetadataConsts.FileName] = fileName,
                    [CommonMetadataConsts.FileType] = "application/pdf",
                    [CommonMetadataConsts.FileSize] = 2_000_000
                })],
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.Timestamp] = At, [CommonMetadataConsts.MessageId] = messageId });

        private static AttachedDocumentInfo Indexed() => new()
        {
            DocumentId = Guid.NewGuid(), FileName = "old.pdf", FileType = "application/pdf", FileSize = 2_000_000, CharCount = 60_000, ChunkCount = 40,
            IndexedChunkCount = 40, IndexStatus = ChatDocumentIndexStatus.Ready, Summary = "An old report."
        };

        [Fact]
        public async Task ALegacyDocumentOverTheLimit_IsIndexedAndReplacedInPlace_KeepingTimestampMessageIdAndCardMetadata()
        {
            var info = Indexed();
            ChatDocumentIndexRequest seen = null;
            _indexer.Setup(i => i.IndexAsync(It.IsAny<ChatDocumentIndexRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ChatDocumentIndexRequest, CancellationToken>((r, _) => seen = r)
                .ReturnsAsync(info);
            var history = new ChatHistory();
            history.AddUserMessage("hello");
            history.Add(LegacyDocument(60_000));
            history.AddAssistantMessage("hi");

            var converted = await BuildService().ConvertLegacyDocumentsAsync(history, DocumentTestSupport.Agent(), new Kernel(), [], "session-1", TestContext.Current.CancellationToken);

            Assert.Equal(1, converted);
            Assert.Equal(3, history.Count);
            var message = history[1];
            Assert.StartsWith("Attached document: 'old.pdf'", message.Content);
            Assert.Equal(At, message.Metadata[CommonMetadataConsts.Timestamp]);
            Assert.Equal("11111111-1111-1111-1111-111111111111", message.Metadata[CommonMetadataConsts.MessageId]);
            var item = message.Items.OfType<TextContent>().Single();
            Assert.Equal("old.pdf", item.Metadata[CommonMetadataConsts.FileName]);
            Assert.Equal(info.DocumentId.ToString("D"), item.Metadata[CommonMetadataConsts.DocumentId]);
            Assert.Equal(new ChatDocumentScope("acme", "session-1", "user-1"), seen.Scope);
            Assert.Equal("old.pdf", seen.FileName);
            Assert.Equal(2_000_000, seen.FileSize);
        }

        [Fact]
        public async Task TheConversionIsIdempotent_AConvertedMessageIsNotConvertedAgain()
        {
            _indexer.Setup(i => i.IndexAsync(It.IsAny<ChatDocumentIndexRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Indexed());
            var history = new ChatHistory { LegacyDocument(60_000) };
            var service = BuildService();

            var first = await service.ConvertLegacyDocumentsAsync(history, DocumentTestSupport.Agent(), new Kernel(), [], "session-1", TestContext.Current.CancellationToken);

            // Saved and loaded again: the metadata comes back as JSON elements.
            var reloaded = JsonSerializer.Deserialize<ChatHistory>(JsonSerializer.Serialize(history));
            var second = await service.ConvertLegacyDocumentsAsync(reloaded, DocumentTestSupport.Agent(), new Kernel(), [], "session-1", TestContext.Current.CancellationToken);

            Assert.Equal(1, first);
            Assert.Equal(0, second);
            _indexer.Verify(i => i.IndexAsync(It.IsAny<ChatDocumentIndexRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AShortLegacyDocument_IsLeftAsItIs()
        {
            var history = new ChatHistory { LegacyDocument(50_000) };

            var converted = await BuildService().ConvertLegacyDocumentsAsync(history, DocumentTestSupport.Agent(), new Kernel(), [], "session-1", TestContext.Current.CancellationToken);

            Assert.Equal(0, converted);
            Assert.StartsWith("Document content:\n", history[0].Content);
            _indexer.Verify(i => i.IndexAsync(It.IsAny<ChatDocumentIndexRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task WhenIndexingFails_TheShortMessageCarriesTheSampledExcerpt()
        {
            _indexer.Setup(i => i.IndexAsync(It.IsAny<ChatDocumentIndexRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
            var history = new ChatHistory { LegacyDocument(300_000) };

            var converted = await BuildService().ConvertLegacyDocumentsAsync(history, DocumentTestSupport.Agent(), new Kernel(), [], "session-1", TestContext.Current.CancellationToken);

            Assert.Equal(1, converted);
            var message = history[0];
            Assert.Contains("could not be indexed", message.Content);
            Assert.Contains("% into the document", message.Content);
            Assert.InRange(message.Content.Length, 30_000, 51_000);
            Assert.Equal(ChatDocumentIndexStatus.Failed, message.Items.OfType<TextContent>().Single().Metadata[CommonMetadataConsts.DocumentIndexStatus]);
        }

        [Fact]
        public async Task WithoutAnIndexer_NothingIsConverted()
        {
            var history = new ChatHistory { LegacyDocument(60_000) };

            var converted = await BuildService(withIndexer: false).ConvertLegacyDocumentsAsync(history, DocumentTestSupport.Agent(), new Kernel(), [], "session-1", TestContext.Current.CancellationToken);

            Assert.Equal(0, converted);
        }
    }
}
