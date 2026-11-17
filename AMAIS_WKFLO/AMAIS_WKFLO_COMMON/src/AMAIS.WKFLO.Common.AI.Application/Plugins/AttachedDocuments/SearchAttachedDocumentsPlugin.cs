using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.Models.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.AttachedDocuments
{
    /// <summary>
    /// Bug 8189 / Task 8219: lets the model look for more of a long attached document than the passages given with the message. Registered
    /// only when the chat has indexed documents. The tenant, session and user are fixed by the server when the plugin is created and the
    /// documents are the ones in this agent's own history: the model chooses only the query and, optionally, a file name. Its output is
    /// capped, and its results are replaced by a placeholder before the history is saved (see
    /// <c>SemanticKernelAgentService.BuildPersistableChatHistory</c>), so the passages live in the turn that asked for them.
    /// </summary>
    public sealed class SearchAttachedDocumentsPlugin
    {
        /// <summary>What the saved history keeps in place of the passages.</summary>
        public const string OmittedResult = "[document passages omitted]";

        private readonly IChatDocumentRetriever _retriever;
        private readonly Func<IReadOnlyList<AttachedDocumentInfo>> _documents;
        private readonly ChatDocumentScope _scope;
        private readonly ILogger _logger;

        public SearchAttachedDocumentsPlugin(IChatDocumentRetriever retriever, Func<IReadOnlyList<AttachedDocumentInfo>> documents, ChatDocumentScope scope, ILogger logger)
        {
            _retriever = retriever;
            _documents = documents;
            _scope = scope;
            _logger = logger;
        }

        /// <summary>
        /// The messages as they are saved: every result of this function is replaced by <see cref="OmittedResult"/> (the call itself is kept,
        /// so the model knows it already searched). Messages without such a result are returned as they are; the live history is not changed.
        /// </summary>
        public static IEnumerable<ChatMessageContent> OmitPassages(IEnumerable<ChatMessageContent> messages)
        {
            var all = messages as IReadOnlyList<ChatMessageContent> ?? messages.ToList();

            // What a result carries depends on the connector: the OpenAI-style connectors set PluginName and FunctionName, but a connector
            // wrapped as IChatClient (Anthropic) hands back a result with neither, only the id of the call. The calls name the function
            // (plain, or combined with the plugin name), so a result is also recognised by the id of a call to this function.
            var searchCallIds = all
                .SelectMany(m => m.Items.OfType<FunctionCallContent>())
                .Where(c => IsSearchFunction(c.PluginName, c.FunctionName) && !string.IsNullOrEmpty(c.Id))
                .Select(c => c.Id)
                .ToHashSet(StringComparer.Ordinal);

            bool IsSearchResult(FunctionResultContent result) =>
                IsSearchFunction(result.PluginName, result.FunctionName)
                || (!string.IsNullOrEmpty(result.CallId) && searchCallIds.Contains(result.CallId));

            foreach (var message in all)
            {
                if (!message.Items.OfType<FunctionResultContent>().Any(IsSearchResult))
                {
                    yield return message;
                    continue;
                }

                var items = new ChatMessageContentItemCollection();
                foreach (var item in message.Items)
                {
                    items.Add(item is FunctionResultContent result && IsSearchResult(result)
                        ? new FunctionResultContent(result.FunctionName, result.PluginName, result.CallId, OmittedResult)
                        : item);
                }

                yield return new ChatMessageContent(message.Role, items, message.ModelId, message.InnerContent, message.Encoding, message.Metadata);
            }
        }

        /// <summary>
        /// Whether a call or result names this function: plugin and function separately, or the function name alone or combined with the
        /// plugin name by a separator the connectors use ('-', '_', '.', ':' or '/').
        /// </summary>
        internal static bool IsSearchFunction(string pluginName, string functionName)
        {
            if (string.IsNullOrEmpty(functionName))
                return false;

            const string plugin = ChatDocumentRetriever.SearchPluginName;
            const string function = ChatDocumentRetriever.SearchFunctionName;

            if (string.Equals(functionName, function, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrEmpty(pluginName) || string.Equals(pluginName, plugin, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(pluginName) && !string.Equals(pluginName, plugin, StringComparison.OrdinalIgnoreCase))
                return false;

            return functionName.Length == plugin.Length + 1 + function.Length
                && functionName.StartsWith(plugin, StringComparison.OrdinalIgnoreCase)
                && functionName.EndsWith(function, StringComparison.OrdinalIgnoreCase)
                && "-_.:/".Contains(functionName[plugin.Length]);
        }

        [KernelFunction(ChatDocumentRetriever.SearchFunctionName)]
        [Description("Searches the long documents the user attached to this conversation and returns the passages that best match the query. Use it when the passages already provided do not answer the question, or to look for a specific detail, name or section.")]
        public async Task<string> SearchAsync(
            [Description("What to look for, as a short question or a few key words")] string query,
            [Description("Optional. The exact file name of one attached document to search; omit to search all of them")] string fileName = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await _retriever.SearchAsync(_documents(), query, fileName, _scope, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "search_attached_documents failed");
                return "The attached documents cannot be searched right now.";
            }
        }
    }
}
