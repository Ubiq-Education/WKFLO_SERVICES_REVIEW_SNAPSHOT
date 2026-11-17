namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    /// <summary>
    /// Structured result returned by <see cref="Invocation.IAgentInvoker.InvokeAsync"/>. Designed so a
    /// fan-in step (multi-agent orchestrator, task #6484) can aggregate several agents'
    /// results by <see cref="AgentRole"/> without re-parsing free text.
    /// </summary>
    public class AgentInvocationResult
    {
        /// <summary>
        /// Echoes <see cref="AgentInvocationRequest.AgentRole"/> so callers can identify which
        /// agent produced this result when aggregating multiple results.
        /// </summary>
        public string AgentRole { get; set; }

        /// <summary>
        /// Id of the <c>TenantAgent</c> that was invoked.
        /// </summary>
        public int AgentId { get; set; }

        /// <summary>
        /// Whether the invocation completed with an answer. <c>false</c> when the agent could not
        /// be found/run or the underlying Semantic Kernel call failed.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The agent's final text answer. Null when <see cref="Success"/> is <c>false</c>.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Error description for the logs when <see cref="Success"/> is <c>false</c>. Never sent to the chat: the chat
        /// only gets <see cref="ErrorCode"/>.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// US 7959 / Task 7963: chat runtime code (<c>ChatErrorCodes</c>, WKFLOAI11xx) of a failed invocation - what the
        /// chat is told about it. Null on success and for a run stopped by the person.
        /// </summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// Bug 7722: the run stopped to wait for the person to allow or deny one of its tool calls; <see cref="Content"/>
        /// is then only the "needs approval" notice and the run continues later, after the decision.
        /// </summary>
        public bool AwaitingApproval { get; set; }

        /// <summary>
        /// US 7501: the person stopped the session while this agent was running. Not a failure: the run is finished
        /// and must not be retried. <see cref="Success"/> is <c>false</c>.
        /// </summary>
        public bool Cancelled { get; set; }

        /// <summary>
        /// UTC timestamp when the invocation finished processing the request.
        /// </summary>
        public DateTimeOffset CompletedAtUtc { get; set; }

        /// <summary>
        /// Extensible bag for orchestration-relevant metadata (e.g. session id, orchestration
        /// instance id). Kept as a string dictionary so it round-trips cleanly through Dapr Workflow
        /// serialization without extra converters.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
