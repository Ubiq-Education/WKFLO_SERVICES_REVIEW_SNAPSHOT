using AMAIS.WKFLO.AI.UnitTests.Services.Documents;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.AI.Application.Plugins.AttachedDocuments;
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
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.AttachedDocuments
{
    /// <summary>Bug 8189 / Task 8219: the document search function the model can call.</summary>
    public class SearchAttachedDocumentsPluginTests
    {
        private static readonly ChatDocumentScope Scope = new("acme", "session-1", "user-1");

        private static AttachedDocumentInfo Document(string name = "report.pdf") => new()
        {
            DocumentId = Guid.NewGuid(), FileName = name, FileType = "application/pdf", FileSize = 100, CharCount = 100_000, ChunkCount = 60,
            IndexedChunkCount = 60, IndexStatus = ChatDocumentIndexStatus.Ready
        };

        private static ChatMessageContent DocumentMessage(AttachedDocumentInfo info) =>
            new(AuthorRole.User, [new TextContent(AttachedDocumentMessage.BuildText(info), metadata: AttachedDocumentMessage.BuildItemMetadata(info))]);

        private static SemanticKernelAgentService BuildService(IChatDocumentRetriever retriever)
        {
            var db = new Mock<IMasterDataDbContext>();
            return new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(), Mock.Of<IWkfloMapper>(), Mock.Of<IDatastreamProvider>(), Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(), Mock.Of<IMcpClientService>(), Mock.Of<IWkfloMediator>(), Options.Create(new ElasticsearchSettings()),
                new TenantContext { TenantName = "acme" }, new ChatContext(), new UserContext { Guid = "user-1" }, Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(), Mock.Of<IDateTimeProvider>(), db.Object, Mock.Of<IContactFormPluginLogService>(),
                chatDocumentRetriever: retriever);
        }

        [Fact]
        public async Task TheFunction_TakesOnlyAQueryAndAnOptionalFileName_TheScopeComesFromTheServer()
        {
            var documents = new List<AttachedDocumentInfo> { Document() };
            ChatDocumentScope seenScope = null;
            var retriever = new Mock<IChatDocumentRetriever>();
            retriever.Setup(r => r.SearchAsync(It.IsAny<IReadOnlyList<AttachedDocumentInfo>>(), "pricing", "report.pdf", It.IsAny<ChatDocumentScope>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<AttachedDocumentInfo>, string, string, ChatDocumentScope, CancellationToken>((_, _, _, s, _) => seenScope = s)
                .ReturnsAsync("[report.pdf]\nPrices.");
            var plugin = new SearchAttachedDocumentsPlugin(retriever.Object, () => documents, Scope, Mock.Of<ILogger>());
            var kernel = new Kernel();
            kernel.Plugins.AddFromObject(plugin, ChatDocumentRetriever.SearchPluginName);

            var function = kernel.Plugins[ChatDocumentRetriever.SearchPluginName][ChatDocumentRetriever.SearchFunctionName];
            var result = await kernel.InvokeAsync(function, new KernelArguments { ["query"] = "pricing", ["fileName"] = "report.pdf" }, TestContext.Current.CancellationToken);

            Assert.Equal(["query", "fileName"], function.Metadata.Parameters.Select(p => p.Name).ToArray());
            Assert.Equal("[report.pdf]\nPrices.", result.ToString());
            Assert.Equal(Scope, seenScope);
        }

        [Fact]
        public async Task ASearchFailure_IsReportedToTheModelWithoutDetails()
        {
            var retriever = new Mock<IChatDocumentRetriever>();
            retriever.Setup(r => r.SearchAsync(It.IsAny<IReadOnlyList<AttachedDocumentInfo>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ChatDocumentScope>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("password=secret"));
            var plugin = new SearchAttachedDocumentsPlugin(retriever.Object, () => [Document()], Scope, Mock.Of<ILogger>());

            var result = await plugin.SearchAsync("x", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("The attached documents cannot be searched right now.", result);
        }

        [Fact]
        public void TheFunctionIsRegisteredOnlyWhenTheChatHasIndexedDocuments()
        {
            var service = BuildService(Mock.Of<IChatDocumentRetriever>());

            var without = new Kernel();
            service.AddAttachedDocumentsSearchTool(without, new ChatHistory(), null, "session-1");

            var withHistory = new Kernel();
            service.AddAttachedDocumentsSearchTool(withHistory, new ChatHistory { DocumentMessage(Document()) }, null, "session-1");

            var withThisTurn = new Kernel();
            service.AddAttachedDocumentsSearchTool(withThisTurn, new ChatHistory(), [new FileAttachment { Document = Document() }], "session-1");

            var noRetriever = new Kernel();
            BuildService(null).AddAttachedDocumentsSearchTool(noRetriever, new ChatHistory { DocumentMessage(Document()) }, null, "session-1");

            Assert.False(without.Plugins.Contains(ChatDocumentRetriever.SearchPluginName));
            Assert.True(withHistory.Plugins.Contains(ChatDocumentRetriever.SearchPluginName));
            Assert.True(withThisTurn.Plugins.Contains(ChatDocumentRetriever.SearchPluginName));
            Assert.False(noRetriever.Plugins.Contains(ChatDocumentRetriever.SearchPluginName));
        }

        [Fact]
        public void TheFunctionIsNotRegistered_ForDocumentsThatCannotBeSearched()
        {
            var service = BuildService(Mock.Of<IChatDocumentRetriever>());
            var failed = Document();
            failed.IndexStatus = ChatDocumentIndexStatus.Failed;
            var noChunks = Document();
            noChunks.IndexedChunkCount = 0;

            var inHistory = new Kernel();
            service.AddAttachedDocumentsSearchTool(inHistory, new ChatHistory { DocumentMessage(failed), DocumentMessage(noChunks) }, null, "session-1");

            var thisTurn = new Kernel();
            service.AddAttachedDocumentsSearchTool(thisTurn, new ChatHistory(), [new FileAttachment { Document = failed }, new FileAttachment { Document = noChunks }], "session-1");

            Assert.False(inHistory.Plugins.Contains(ChatDocumentRetriever.SearchPluginName));
            Assert.False(thisTurn.Plugins.Contains(ChatDocumentRetriever.SearchPluginName));
        }

        /// <summary>
        /// What the connectors persist differs: OpenAI-style ones keep the plugin and function names; a connector wrapped as IChatClient
        /// (Anthropic) keeps a result with neither, only the call id, and the call carries the combined name.
        /// </summary>
        [Theory]
        [InlineData("AttachedDocuments", "search_attached_documents", "AttachedDocuments", "search_attached_documents")]
        [InlineData(null, "AttachedDocuments-search_attached_documents", null, null)]
        [InlineData(null, "AttachedDocuments_search_attached_documents", null, null)]
        [InlineData(null, "AttachedDocuments.search_attached_documents", null, null)]
        [InlineData("AttachedDocuments", "search_attached_documents", null, null)]
        [InlineData(null, "search_attached_documents", null, "AttachedDocuments-search_attached_documents")]
        [InlineData(null, "search_attached_documents", null, "attacheddocuments_SEARCH_attached_documents")]
        public void TheSavedHistory_OmitsThePassages_ForEveryFormAConnectorPersists(string callPlugin, string callFunction, string resultPlugin, string resultFunction)
        {
            var call = new FunctionCallContent(callFunction, callPlugin, "call-9", new KernelArguments { ["query"] = "pricing" });
            var result = new FunctionResultContent(resultFunction, resultPlugin, "call-9", "[report.pdf]\nThe full passages");
            var history = new ChatHistory();
            history.AddUserMessage("question");
            history.Add(new ChatMessageContent(AuthorRole.Assistant, [call]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [result]));

            var saved = SemanticKernelAgentService.BuildPersistableChatHistory(history);

            Assert.Same(call, saved[1].Items.OfType<FunctionCallContent>().Single());
            Assert.Equal("[document passages omitted]", saved[2].Items.OfType<FunctionResultContent>().Single().Result);
            Assert.Equal("call-9", saved[2].Items.OfType<FunctionResultContent>().Single().CallId);
        }

        [Fact]
        public void TheSavedHistory_KeepsTheResultsOfOtherFunctions_EvenWithAnIdThatLooksSimilar()
        {
            var search = new FunctionCallContent("AttachedDocuments-search_attached_documents", null, "call-1");
            var other = new FunctionCallContent("get_page", "plugin_1_Integration_Amais_Page", "call-2");
            var history = new ChatHistory();
            history.Add(new ChatMessageContent(AuthorRole.Assistant, [search, other]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [new FunctionResultContent(callId: "call-2", result: "page data")]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [new FunctionResultContent(callId: "call-1", result: "passages")]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [new FunctionResultContent("search_attached_documents", "other_plugin", "call-3", "not ours")]));

            var saved = SemanticKernelAgentService.BuildPersistableChatHistory(history);

            Assert.Equal("page data", saved[1].Items.OfType<FunctionResultContent>().Single().Result);
            Assert.Equal("[document passages omitted]", saved[2].Items.OfType<FunctionResultContent>().Single().Result);
            Assert.Equal("not ours", saved[3].Items.OfType<FunctionResultContent>().Single().Result);
        }

        [Fact]
        public void TheSavedHistory_KeepsTheCall_ButNotThePassages()
        {
            var call = new FunctionCallContent("search_attached_documents", ChatDocumentRetriever.SearchPluginName, "call-1", new KernelArguments { ["query"] = "pricing" });
            var search = new FunctionResultContent("search_attached_documents", ChatDocumentRetriever.SearchPluginName, "call-1", "[report.pdf]\nThe full passages");
            var other = new FunctionResultContent("get_page", "plugin_1_Integration_Amais_Page", "call-2", "page data");
            var history = new ChatHistory();
            history.AddUserMessage("question");
            history.Add(new ChatMessageContent(AuthorRole.Assistant, [call]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [search]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [other]));

            var saved = SemanticKernelAgentService.BuildPersistableChatHistory(history);

            Assert.Equal(4, saved.Count);
            Assert.Same(call, saved[1].Items.OfType<FunctionCallContent>().Single());
            var replaced = saved[2].Items.OfType<FunctionResultContent>().Single();
            Assert.Equal("[document passages omitted]", replaced.Result);
            Assert.Equal("call-1", replaced.CallId);
            Assert.Equal("search_attached_documents", replaced.FunctionName);
            Assert.Equal("page data", saved[3].Items.OfType<FunctionResultContent>().Single().Result);

            // The live history, still used by this turn, keeps the passages.
            Assert.Equal("[report.pdf]\nThe full passages", history[2].Items.OfType<FunctionResultContent>().Single().Result);
        }
    }
}
