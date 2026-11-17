namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    /// <summary>
    /// Aggregated result of a <see cref="Workflows.MultiAgentOrchestrator.MultiAgentOrchestratorWorkflow"/>
    /// run — the sequential lead-agent result plus every fan-out agent's result. Each
    /// <see cref="AgentInvocationResult"/> still carries its own <see cref="AgentInvocationResult.AgentRole"/>,
    /// so callers can identify which agent produced which entry without re-parsing free text.
    /// </summary>
    public class MultiAgentOrchestratorWorkflowOutput
    {
        /// <summary>
        /// Id of the Dapr Workflow instance that produced this result (<c>WorkflowContext.InstanceId</c>).
        /// </summary>
        public string OrchestrationInstanceId { get; set; }

        /// <summary>
        /// Result of the sequential lead-agent step.
        /// </summary>
        public AgentInvocationResult LeadResult { get; set; }

        /// <summary>
        /// Results of every concurrently-invoked fan-out agent, in the same order as
        /// <see cref="MultiAgentOrchestratorWorkflowInput.FanOutAgents"/>.
        /// </summary>
        public List<AgentInvocationResult> FanOutResults { get; set; } = new();

        /// <summary>
        /// True only when the lead agent and every fan-out agent completed successfully.
        /// </summary>
        public bool Success { get; set; }
    }
}
