namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    /// <summary>
    /// US 7507: one row of the MAISY conversations list.
    /// </summary>
    public class PublicAgentConversationResult
    {
        public string SessionId { get; set; }

        /// <summary>
        /// The MAISY Conversation row's Name: the first user message, replaced by an AI-generated title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// The first user message (max 300 chars). Null when there is none; the UI then shows "New conversation".
        /// </summary>
        public string PreviewLine { get; set; }

        /// <summary>
        /// The latest activity across every agent that took part in the session.
        /// </summary>
        public DateTimeOffset LastActivityAt { get; set; }

        /// <summary>
        /// MAISY first, then the other agents in the order they joined the session.
        /// </summary>
        public List<PublicAgentConversationAgentResult> Agents { get; set; } = new();
    }

    public class PublicAgentConversationAgentResult
    {
        public int AgentId { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// The agent's Description, cut to a tooltip-friendly length.
        /// </summary>
        public string Role { get; set; }

        public string AvatarUrl { get; set; }
    }
}
