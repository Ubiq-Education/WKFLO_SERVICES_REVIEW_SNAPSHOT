using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ConversationStarted
{
    /// <summary>
    /// Raised exactly once per conversation, from inside
    /// <see cref="ChatHistoryUpdated.UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler.SyncConversationAsync"/>,
    /// immediately after it successfully inserts the FIRST <c>Conversation</c> row for a given
    /// (<see cref="RunnableAgent"/>.Id, <see cref="SessionId"/>) pair, or revives a soft-deleted row for that pair as a
    /// brand-new conversation (Bug 7958: the client kept chatting on the session id of a deleted conversation). It is
    /// therefore once per conversation, NOT once per session: a session whose conversation is deleted and then resumed
    /// raises it again. Unlike the generic
    /// <see cref="ChatHistoryUpdated.ChatHistoryUpdatedDomainEvent"/> that every turn raises, this event firing
    /// at all IS the "this is a genuinely new conversation" signal — by construction, not by inference. A
    /// handler reacting to this event needs no existence check and no dependency on dispatch order relative to
    /// any other handler: <c>SyncConversationAsync</c> is the single place that already knows this with
    /// certainty (its own insert-vs-update branch), so the signal is computed once, at the source, instead of
    /// being re-derived independently downstream.
    /// </summary>
    public class ConversationStartedDomainEvent : IWkfloNotification
    {
        public RunnableAgent RunnableAgent { get; }
        public string SessionId { get; }

        /// <summary>The <c>Conversation.ConversationDate</c> this new conversation was created with (from
        /// <c>ChatHistoryUpdatedDomainEvent.CreatedAt</c>), sent explicitly instead of relying on ingestion
        /// time.</summary>
        public DateTimeOffset? ConversationDate { get; }

        public ConversationStartedDomainEvent(RunnableAgent runnableAgent, string sessionId, DateTimeOffset? conversationDate = null)
        {
            RunnableAgent = runnableAgent;
            SessionId = sessionId;
            ConversationDate = conversationDate;
        }
    }
}
