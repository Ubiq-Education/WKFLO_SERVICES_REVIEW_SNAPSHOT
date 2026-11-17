using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.AI.Application.Plugins.AttachedDocuments;
using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    public partial class SemanticKernelAgentService
    {
        /// <summary>
        /// Registers <c>search_attached_documents</c> when the chat has indexed documents (in the history, or attached by this turn). The plugin's
        /// scope is fixed here from the server-side contexts; it reads the documents from this agent's own history at call time.
        /// </summary>
        internal void AddAttachedDocumentsSearchTool(Kernel kernel, ChatHistory history, IEnumerable<FileAttachment> thisTurnFiles, string sessionId)
        {
            if (_chatDocumentRetriever == null)
                return;

            // Only documents that can be searched (indexed, with stored chunks): a failed one must not offer the tool.
            var documents = AttachedDocumentMessage.ReadDocuments(history)
                .Concat((thisTurnFiles ?? []).Select(f => f.Document).Where(d => d != null));
            if (ChatDocumentRetriever.SearchableDocuments(documents, _chatDocumentSettings.MaxDocumentsPerTurn).Count == 0)
                return;

            var scope = ChatDocumentScope.For(_tenantContext.TenantName, sessionId, _userContext.Guid);
            kernel.Plugins.AddFromObject(
                new SearchAttachedDocumentsPlugin(_chatDocumentRetriever, () => AttachedDocumentMessage.ReadDocuments(history), scope, _logger),
                ChatDocumentRetriever.SearchPluginName);
        }

        /// <summary>
        /// Bug 8189 / Task 8219: replaces, in place, every legacy document message (the whole text after "Document content:") longer than the
        /// inline limit by the short message of an indexed document. Timestamp, message id and file card metadata are kept. If indexing
        /// fails the short message carries a sampled excerpt instead. Idempotent: a converted message is no longer in the old format.
        /// </summary>
        /// <returns>How many messages were converted.</returns>
        internal async Task<int> ConvertLegacyDocumentsAsync(ChatHistory history, RunnableAgent agent, Kernel kernel, IReadOnlyList<string> agentTagNames, string sessionId,
            CancellationToken cancellationToken)
        {
            if (_chatDocumentIndexer == null)
                return 0;

            var converted = 0;
            for (var i = 0; i < history.Count; i++)
            {
                if (!AttachedDocumentMessage.TryReadInline(history[i], out var item)
                    || item.Text.Length - AttachedDocumentMessage.InlinePrefix.Length <= _chatDocumentSettings.InlineMaxChars)
                    continue;

                var fileName = item.Metadata[CommonMetadataConsts.FileName]?.ToString();
                var fileType = item.Metadata.TryGetValue(CommonMetadataConsts.FileType, out var type) ? type?.ToString() : null;
                var fileSize = item.Metadata.TryGetValue(CommonMetadataConsts.FileSize, out var size) && int.TryParse(size?.ToString(), out var parsedSize) ? parsedSize : 0;
                var scope = ChatDocumentScope.For(_tenantContext.TenantName, sessionId, _userContext.Guid);

                // The prefix stays in the first chunk; copying a text of tens of MB just to drop it is not worth it.
                // Known limitation: the history is only changed in memory here; if the turn fails before it is saved, the next turn
                // converts the message again under a new document id and these chunks stay unreferenced (no scheduled purge).
                AttachedDocumentInfo document;
                try
                {
                    document = await _chatDocumentIndexer.IndexAsync(new ChatDocumentIndexRequest(scope, agent, kernel, agentTagNames, fileName, fileType, fileSize, item.Text), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Converting a legacy document message of session {SessionId} failed; its excerpt is kept instead", sessionId);
                    document = new AttachedDocumentInfo
                    {
                        DocumentId = Guid.NewGuid(), FileName = fileName, FileType = fileType, FileSize = fileSize, CharCount = item.Text.Length,
                        IndexStatus = ChatDocumentIndexStatus.Failed, Excerpt = DocumentExcerptSampler.Build(item.Text, _chatDocumentSettings.InlineMaxChars)
                    };
                }

                history[i] = new ChatMessageContent(history[i].Role,
                    [new TextContent(AttachedDocumentMessage.BuildText(document), metadata: AttachedDocumentMessage.BuildItemMetadata(document))],
                    metadata: history[i].Metadata == null ? null : new Dictionary<string, object>(history[i].Metadata));
                converted++;
            }

            return converted;
        }

        /// <summary>
        /// Bug 8189 / Task 8219: what this turn's instructions say about the chat's large documents: the documents already in the history
        /// plus those attached by this turn, and the passages that match the user's message. Null (and no embedding call) when there is no
        /// indexed document. The result goes into the instructions only, never into the chat history.
        /// </summary>
        /// <param name="history">The history as loaded, before this turn's own messages are added.</param>
        /// <param name="useLastTypedMessage">True on a resume or contact-form replay turn, where <paramref name="message"/> is not what the user typed.</param>
        internal async Task<string> BuildAttachedDocumentContextAsync(ChatHistory history, IEnumerable<AttachedDocumentInfo> thisTurnDocuments, string sessionId,
            string message, bool useLastTypedMessage, Kernel kernel, CancellationToken cancellationToken)
        {
            if (_chatDocumentRetriever == null)
                return null;

            var documents = AttachedDocumentMessage.ReadDocuments(history);
            documents.AddRange(thisTurnDocuments.Where(d => d != null));
            if (documents.Count == 0)
                return null;

            var scope = ChatDocumentScope.For(_tenantContext.TenantName, sessionId, _userContext.Guid);
            var query = ChatDocumentRetriever.ResolveQuery(message, useLastTypedMessage, history);
            var searchToolAvailable = kernel.Plugins.Contains(ChatDocumentRetriever.SearchPluginName);

            return await _chatDocumentRetriever.BuildTurnContextAsync(documents, query, scope, searchToolAvailable, cancellationToken);
        }
    }
}
