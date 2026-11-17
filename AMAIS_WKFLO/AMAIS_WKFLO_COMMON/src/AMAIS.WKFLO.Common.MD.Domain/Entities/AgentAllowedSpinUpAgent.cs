namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    /// <summary>
    /// Explicit allowlist entry: <see cref="Agent"/> is permitted to notify
    /// <see cref="AllowedSpinUpAgent"/> via <c>AgentEventPublisherPlugin.PublishAgentEventAsync</c>.
    /// Composite key <c>(AgentId, AllowedSpinUpAgentId)</c>, hard-deleted on unassign — no soft-delete,
    /// matching <see cref="EntityTag"/>'s junction-row pattern. Both sides reference the same
    /// <c>Agent</c> table (TPH), so the application layer — not a DB constraint — enforces both are
    /// <c>RunnableAgent</c>s.
    /// </summary>
    public class AgentAllowedSpinUpAgent
    {
        public int AgentId { get; set; }
        public Agent Agent { get; set; }

        public int AllowedSpinUpAgentId { get; set; }
        public Agent AllowedSpinUpAgent { get; set; }
    }
}
