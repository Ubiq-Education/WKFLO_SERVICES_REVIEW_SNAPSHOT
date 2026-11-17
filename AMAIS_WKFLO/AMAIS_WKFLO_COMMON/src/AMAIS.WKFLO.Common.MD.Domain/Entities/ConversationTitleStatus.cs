namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    /// <summary>
    /// US 7507: lifecycle of a conversation's AI-generated title (see ConversationTitleWorkflowActivity).
    /// Generation runs at most once per conversation: the activity claims the row by moving it from
    /// <see cref="Pending"/> to <see cref="Processing"/> atomically.
    /// </summary>
    public enum ConversationTitleStatus
    {
        /// <summary>Not decided yet: waits for the first completed exchange.</summary>
        Pending = 0,

        /// <summary>Claimed by a title generation run.</summary>
        Processing = 1,

        /// <summary>Conversation.Name holds the AI title.</summary>
        Generated = 2,

        /// <summary>No title is generated for this conversation (not a MAISY conversation, or a test chat).</summary>
        Skipped = 3,

        /// <summary>Generation was attempted and failed; Conversation.Name keeps the first user message.</summary>
        Failed = 4
    }
}
