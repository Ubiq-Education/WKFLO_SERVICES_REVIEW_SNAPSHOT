namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    /// <summary>
    /// One agent role participating in a <see cref="MultiAgentOrchestratorWorkflowInput"/> pipeline —
    /// identifies which <c>TenantAgent</c> to run. <see cref="AgentRole"/> must be unique across a
    /// single orchestration's steps: it's the label callers use to tell fan-out results apart in
    /// <see cref="MultiAgentOrchestratorWorkflowOutput"/>.
    /// </summary>
    public class MultiAgentOrchestratorStep
    {
        /// <summary>
        /// Id of the <c>TenantAgent</c> to run for this step.
        /// </summary>
        public int AgentId { get; set; }

        /// <summary>
        /// Free-form role label for this step (e.g. "researcher", "critic", "summarizer").
        /// </summary>
        public string AgentRole { get; set; }
    }
}
