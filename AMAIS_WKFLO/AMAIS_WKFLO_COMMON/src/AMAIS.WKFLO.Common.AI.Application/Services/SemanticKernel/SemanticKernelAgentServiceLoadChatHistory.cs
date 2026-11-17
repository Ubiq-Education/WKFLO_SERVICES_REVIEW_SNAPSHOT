using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadRunnableAgentChatHistoryBySession;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadSiblingAgentContextBySession;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    public partial class SemanticKernelAgentService
    {
        /// <summary>
        /// Asynchronously loads the chat history for a specified agent and session identifier.
        /// </summary>
        /// <param name="runnableAgent">The agent for which to load the chat history. Cannot be null.</param>
        /// <param name="sessionId">The unique identifier of the chat session to retrieve. Cannot be null or empty.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A tuple containing the creation timestamp of the chat session, the chat name, a thread representing the
        /// chat history, and the "Stopped" markers taken out of that history (US 7502).</returns>
        internal async Task<(DateTimeOffset, string, ChatHistoryAgentThread, IReadOnlyList<ChatMessageContent>)> LoadChatHistoryBySessionIdAsync(RunnableAgent runnableAgent,
            string sessionId, CancellationToken cancellationToken, Kernel kernel = null, IReadOnlyList<string> agentTagNames = null)
        {
            var (createdAt, chatName, chatHistory) = await _mediator.Send(new LoadRunnableAgentChatHistoryBySessionQuery()
            {
                Agent = runnableAgent,
                SessionId = sessionId
            }, cancellationToken);

            // Bug 8189 / Task 8219: a long document saved in the old format (its whole text in the history) is indexed now and its message
            // replaced in place. Only when the caller gives the kernel the summary needs.
            if (kernel != null)
                await ConvertLegacyDocumentsAsync(chatHistory, runnableAgent, kernel, agentTagNames, sessionId, cancellationToken);

            // US 7502: the "Stopped" dividers are for people only - never sent to the model. Taken out here and put back
            // by BuildPersistableChatHistory before the history is saved again.
            var detachedStoppedMarkers = StoppedTurnMarker.Detach(chatHistory);

            // US 7078: ephemeral sibling-agent context injection, gated on RunnableAgent.EnableSpinUpAgents
            // (see LoadSiblingAgentContextBySessionQueryHandler for the full gate/scope logic — it
            // re-checks this same flag and never issues a DB query when it's false). Short-circuited
            // here too, before even sending the query, so the vast majority of agents (no spin-up rights)
            // skip this step entirely with zero extra indirection. Always pulls a child's full
            // injectable history for the session — no newer-than-last-turn watermark — so the root
            // agent sees everything a child has produced, not just what's new since its own last turn.
            var siblingContext = runnableAgent.EnableSpinUpAgents
                ? await _mediator.Send(new LoadSiblingAgentContextBySessionQuery
                {
                    Agent = runnableAgent,
                    SessionId = sessionId
                }, cancellationToken)
                : [];

            // chatHistory is the exact object SaveChatAsync will persist later (via
            // ChatHistoryUpdatedDomainEvent), so injected sibling content must never be added to it
            // directly — MergeSiblingContextByTimestamp only clones when there's actually something to
            // merge in. Messages carrying CommonMetadataConsts.IsEphemeralAgentContext are stripped back
            // out of the working copy right before that event is published (see
            // SemanticKernelAgentServiceRun/RunVoice's RunChatCompletionAgentAsync/RunVoiceChatCompletionAgentAsync).
            var historyForThisTurn = MergeSiblingContextByTimestamp(chatHistory, siblingContext);

            var chatHistoryAgentThread = new ChatHistoryAgentThread(historyForThisTurn);

            return (createdAt, chatName, chatHistoryAgentThread, detachedStoppedMarkers);
        }

        // Merges by timestamp rather than appending siblingContext as a trailing block: siblingContext
        // can span the child's entire session (no watermark), so a plain append would put messages the
        // child produced long before the root's most recent turn after everything the root has already
        // said — an out-of-order transcript.
        //
        // A pinned AuthorRole.System message (chatHistory's index-0 convention — see
        // SemanticKernelExtensions/RemoveMidConversationSystemMessages) MUST stay first no matter what:
        // Anthropic rejects a 'system' message anywhere else in the array. A plain timestamp sort gives
        // no such guarantee — if any sibling message happened to carry an earlier (or missing, which
        // sorts first) timestamp, it would push the system message out of position 0. OrderBy's Role
        // key forces every System-role message to the front regardless of timestamp; ThenBy orders
        // everything else (and breaks ties among any System-role messages, though the pinned-message
        // convention means there's normally only one) by actual timestamp. Both are stable, so messages
        // sharing a key (or missing a timestamp — shouldn't happen in practice, every real message sets
        // one) keep their original relative order.
        //
        // Internal (not private) so this pure merge step is directly unit-testable without exercising
        // the full LoadChatHistoryBySessionIdAsync pipeline. Returns chatHistory itself, unchanged and
        // uncloned, when there's no sibling content — true for the vast majority of agents (no spin-up
        // rights) on every single turn, so the common path avoids an unnecessary full-history copy.
        internal static ChatHistory MergeSiblingContextByTimestamp(ChatHistory chatHistory, IReadOnlyList<ChatMessageContent> siblingContext) =>
            siblingContext.Count == 0
                ? chatHistory
                : new ChatHistory(chatHistory.Concat(siblingContext)
                    .OrderBy(m => m.Role == AuthorRole.System ? 0 : 1)
                    .ThenBy(GetMessageTimestamp));

        // Metadata survives a blob JSON round-trip as System.Text.Json.JsonElement rather than its
        // original CLR type — same tolerant-cast pattern used by
        // LoadSiblingAgentContextBySessionQueryHandler.GetMessageTimestamp and
        // TenantAgentChatMessageResult.GetTimestampFromMetadata.
        private static DateTimeOffset? GetMessageTimestamp(ChatMessageContent message)
        {
            if (message.Metadata == null || !message.Metadata.TryGetValue(CommonMetadataConsts.Timestamp, out var value) || value == null)
                return null;

            return value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(dt),
                System.Text.Json.JsonElement element when DateTimeOffset.TryParse(element.ToString(), out var parsed) => parsed,
                string s when DateTimeOffset.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }
    }
}
