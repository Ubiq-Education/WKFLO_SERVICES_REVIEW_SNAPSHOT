namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    /// <summary>
    /// One entry in the list returned by <c>GetAvailableSpinUpAgentsByTenantAgentIdQueryHandler</c> and
    /// <c>GetAvailableSpinUpAgentsByShareIdQueryHandler</c> — the discoverability data a UI needs to
    /// build an <c>@name</c> target picker for <c>publish_agent_event</c>, including the agent's chat
    /// icon so the picker can render a real per-agent icon instead of a generic placeholder.
    /// </summary>
    public class AgentSpinUpTargetResult
    {
        /// <summary>
        /// The agent's display name, as used for the <c>@name</c> target in <c>publish_agent_event</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The agent's configured chat icon URL, resolved via <see cref="AMAIS.WKFLO.Common.AI.Application.Helpers.AgentChatIconHelper.GetChatbotIconUrlAsync"/>.
        /// Null when the agent has no <c>AgentChatSetting</c> row with a configured icon — callers should
        /// fall back to a generic placeholder in that case.
        /// </summary>
        public string IconUrl { get; set; }
    }
}
