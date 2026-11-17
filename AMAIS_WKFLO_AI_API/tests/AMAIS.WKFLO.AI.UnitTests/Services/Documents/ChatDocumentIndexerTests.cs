using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.AgentTokensUsed;
using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Documents;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AMAIS.WKFLO.AI.UnitTests.Services.Documents
{
    public class ChatDocumentIndexerTests
    {
        private static readonly ChatDocumentScope Scope = new("acme", "session-1", "user-1");

        private readonly InMemoryChatDocumentStore _store = new();
        private readonly KeywordEmbeddingGenerator _embeddings = new();
        private readonly Mock<IWkfloMediator> _mediator = new();
        private readonly List<AgentAnswer> _usage = [];
        private readonly ChatDocumentSettings _settings = new();

        public ChatDocumentIndexerTests()
        {
            _mediator.Setup(m => m.Publish(It.IsAny<AgentTokensUsedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AgentTokensUsedDomainEvent, CancellationToken>((e, _) => _usage.Add(e.AgentAnswer))
                .Returns(Task.CompletedTask);
        }

        private ChatDocumentIndexer BuildIndexer() => new(_store, _embeddings, _mediator.Object, Mock.Of<IDateTimeProvider>(d => d.DateTimeOffsetUtcNow() == DateTimeOffset.UtcNow),
            Options.Create(_settings), Mock.Of<ILogger<ChatDocumentIndexer>>());

        private static string Text(int paragraphs)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < paragraphs; i++)
                sb.Append($"Paragraph {i} about {KeywordEmbeddingGenerator.Topics[i % 6]} ").Append('x', 650).Append("\n\n");
            return sb.ToString();
        }

        private ChatDocumentIndexRequest Request(string text, Microsoft.SemanticKernel.Kernel kernel = null) =>
            new(Scope, DocumentTestSupport.Agent(), kernel ?? DocumentTestSupport.KernelReplying("A report about many topics."), ["Support"], "report.pdf", "application/pdf", 11_000_000, text);

        [Fact]
        public async Task AllChunksAreStoredInBatches_InCoverageOrder_WithTheScope_AndTheDocumentIsReady()
        {
            var text = Text(300);

            var info = await BuildIndexer().IndexAsync(Request(text), TestContext.Current.CancellationToken);

            Assert.Equal(ChatDocumentIndexStatus.Ready, info.IndexStatus);
            Assert.Equal(info.ChunkCount, info.IndexedChunkCount);
            Assert.True(info.ChunkCount > 100);
            Assert.Equal(info.ChunkCount, _store.ChunkCount(info.DocumentId));
            Assert.Equal(text.Length, info.CharCount);
            Assert.All(_store.UpsertBatchSizes, size => Assert.InRange(size, 1, 64));
            Assert.All(_store.Calls, call => Assert.Equal(Scope, call.Scope));

            // Coverage order: the very first chunk of the first batch is 0, and the second chunk is half way through the document.
            Assert.Contains(0, _store.FirstChunkOfEachUpsert);
            var all = await _store.GetChunksAsync(Scope, info.DocumentId, Enumerable.Range(0, info.ChunkCount).ToList(), TestContext.Current.CancellationToken);
            Assert.Equal(info.ChunkCount, all.Count);
            Assert.Equal("report.pdf", all[0].FileName);
        }

        [Fact]
        public async Task TheSummaryIsBuiltFromTheAgentsModel_AndItsTokensAreReportedAsUsage()
        {
            var prompts = new List<string>();
            var kernel = DocumentTestSupport.KernelReplying("A report about many topics.", 5_000, 300, prompts: prompts);

            var info = await BuildIndexer().IndexAsync(Request(Text(100), kernel), TestContext.Current.CancellationToken);

            Assert.Equal("A report about many topics.", info.Summary);
            var usage = Assert.Single(_usage);
            Assert.Equal(5_000, usage.TokenUsage.InputTokenCount);
            Assert.Equal(300, usage.TokenUsage.OutputTokenCount);
            Assert.Equal(42, usage.AgentId);
            Assert.Equal("Support", usage.AgentType);
            Assert.Equal("gpt-4o", usage.Model);
            Assert.Contains("report.pdf", Assert.Single(prompts));
        }

        [Fact]
        public async Task TheSummaryIsBuiltFromAnExcerptNotTheWholeText()
        {
            var prompts = new List<string>();
            var kernel = DocumentTestSupport.KernelReplying("Summary.", prompts: prompts);
            var text = Text(1_200);
            Assert.True(text.Length > _settings.ExcerptChars * 3 / 2);

            await BuildIndexer().IndexAsync(Request(text, kernel), TestContext.Current.CancellationToken);

            var prompt = Assert.Single(prompts);
            Assert.True(prompt.Length < _settings.ExcerptChars + 5_000);
            Assert.Contains("% into the document", prompt);
        }

        [Fact]
        public async Task WhenTheSummaryFails_ThereIsNoSummary_ButTheDocumentIsStillIndexed()
        {
            var kernel = DocumentTestSupport.KernelReplying("x", fail: new InvalidOperationException("model down"));

            var info = await BuildIndexer().IndexAsync(Request(Text(100), kernel), TestContext.Current.CancellationToken);

            Assert.Null(info.Summary);
            Assert.Equal(ChatDocumentIndexStatus.Ready, info.IndexStatus);
            Assert.Empty(_usage);
        }

        [Fact]
        public async Task TheChunkCapMakesTheDocumentPartial_AndTheStoredChunksAreSpreadOverTheWholeText()
        {
            _settings.MaxIndexedChunksPerDocument = 100;

            var info = await BuildIndexer().IndexAsync(Request(Text(400)), TestContext.Current.CancellationToken);

            Assert.Equal(ChatDocumentIndexStatus.Partial, info.IndexStatus);
            Assert.Equal(100, info.IndexedChunkCount);
            Assert.True(info.ChunkCount > 100);
            Assert.Equal(100, _store.ChunkCount(info.DocumentId));

            var stored = (await _store.GetChunksAsync(Scope, info.DocumentId, Enumerable.Range(0, info.ChunkCount).ToList(), TestContext.Current.CancellationToken))
                .Select(p => p.ChunkIndex).ToList();
            Assert.True(stored.Min() < 10);
            Assert.True(stored.Max() > info.ChunkCount - 15);
        }

        [Fact]
        public async Task TheTimeBudgetMakesTheDocumentPartial_AndKeepsWhatWasStored()
        {
            _settings.IndexingTimeBudgetSeconds = 1;
            _settings.EmbeddingParallelism = 1;
            _embeddings.OnCall = async (call, token) =>
            {
                if (call > 1)
                    await Task.Delay(Timeout.Infinite, token);
            };

            var info = await BuildIndexer().IndexAsync(Request(Text(300)), TestContext.Current.CancellationToken);

            Assert.Equal(ChatDocumentIndexStatus.Partial, info.IndexStatus);
            Assert.Equal(64, info.IndexedChunkCount);
            Assert.Equal(64, _store.ChunkCount(info.DocumentId));
        }

        [Fact]
        public async Task WhitespaceOnlyText_GivesAFailedDocument_InsteadOfAnException()
        {
            var info = await BuildIndexer().IndexAsync(Request(new string(' ', 60_000)), TestContext.Current.CancellationToken);

            Assert.Equal(ChatDocumentIndexStatus.Failed, info.IndexStatus);
            Assert.Equal(0, info.ChunkCount);
            Assert.Null(info.PageCount);
            Assert.Equal(0, _store.TotalChunks);
        }

        [Fact]
        public async Task WhenNothingCanBeStored_TheDocumentFails_KeepsAnExcerpt_AndLeavesNothingBehind()
        {
            _store.FailUpserts = new InvalidOperationException("database down");

            var info = await BuildIndexer().IndexAsync(Request(Text(300)), TestContext.Current.CancellationToken);

            Assert.Equal(ChatDocumentIndexStatus.Failed, info.IndexStatus);
            Assert.Equal(0, info.IndexedChunkCount);
            Assert.False(string.IsNullOrEmpty(info.Excerpt));
            Assert.Equal(0, _store.TotalChunks);
        }

        [Fact]
        public async Task WhenIndexingFails_TheExcerptIsCappedAtTheInlineLimit_AndThereIsNoSummary()
        {
            _store.FailUpserts = new InvalidOperationException("database down");
            var text = Text(250);
            Assert.True(text.Length > _settings.InlineMaxChars * 2);

            var info = await BuildIndexer().IndexAsync(Request(text), TestContext.Current.CancellationToken);

            Assert.Equal(ChatDocumentIndexStatus.Failed, info.IndexStatus);
            Assert.True(info.Excerpt.Length <= _settings.InlineMaxChars);
            Assert.Null(info.Summary);
            Assert.True(AttachedDocumentMessage.BuildText(info).Length <= _settings.InlineMaxChars + 500);
        }

        [Fact]
        public async Task WhenTheUserStops_TheDocumentsChunksAreDeleted_AndTheCancellationIsRethrown()
        {
            using var stop = new CancellationTokenSource();
            _embeddings.OnCall = async (call, token) =>
            {
                if (call == 3)
                {
                    stop.Cancel();
                    await Task.Delay(Timeout.Infinite, token);
                }
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildIndexer().IndexAsync(Request(Text(400)), stop.Token));

            Assert.Equal(0, _store.TotalChunks);
            Assert.Contains(_store.Calls, c => c.Method == nameof(IChatDocumentStore.DeleteDocumentAsync) && c.Scope == Scope);
        }
    }
}
