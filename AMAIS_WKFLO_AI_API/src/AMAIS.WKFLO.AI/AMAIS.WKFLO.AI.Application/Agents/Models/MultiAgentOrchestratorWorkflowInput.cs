using AMAIS.WKFLO.Common.Contexts;

namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    /// <summary>
    /// Input to <see cref="Workflows.MultiAgentOrchestrator.MultiAgentOrchestratorWorkflow"/>. Describes
    /// a sequential "lead" agent step followed by a fan-out/fan-in step across two or more agents that
    /// each receive the original message plus the lead agent's answer.
    /// </summary>
    public class MultiAgentOrchestratorWorkflowInput
    {
        /// <summary>
        /// The text/task (plus any file attachments) handed to the lead agent to start the pipeline.
        /// The same <see cref="AgentInvocationInput.Files"/> are also forwarded to every fan-out
        /// agent, not just the lead.
        /// </summary>
        public AgentInvocationInput Input { get; set; }

        /// <summary>
        /// Chat session id forwarded to every agent invocation (see <see cref="AgentInvocationRequest.SessionId"/>).
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// The single agent invoked first, sequentially. Its <see cref="AgentInvocationResult.Content"/>
        /// is folded into every fan-out agent's input message.
        /// </summary>
        public MultiAgentOrchestratorStep LeadAgent { get; set; }

        /// <summary>
        /// Two or more agents invoked concurrently (fan-out) once the lead agent completes. Each runs
        /// exactly once; their results are aggregated (fan-in) into
        /// <see cref="MultiAgentOrchestratorWorkflowOutput.FanOutResults"/>.
        /// </summary>
        public List<MultiAgentOrchestratorStep> FanOutAgents { get; set; } = new();

        /// <summary>
        /// Whether every agent in the pipeline should run its chain-of-thought reasoning pass.
        /// </summary>
        public bool ChainOfThought { get; set; }

        /// <summary>
        /// Ambient contexts from whatever triggers this workflow, forwarded onto every
        /// <see cref="AgentInvocationRequest"/> built for the lead/fan-out steps so
        /// <see cref="Invocation.IAgentInvoker"/> can restore them onto its own scoped context
        /// instances. Read directly
        /// as plain workflow input data (no injected services) since workflow code must stay
        /// deterministic. Unused until this workflow has a real trigger — see the known gap in
        /// <c>docs/multi-agent-orchestration.md</c> Section 4 — but the mechanism is correct and ready.
        /// </summary>
        public TenantContext TenantContext { get; set; }

        public ChatContext ChatContext { get; set; }

        public UserContext UserContext { get; set; }

        public AgentSharableContext AgentSharableContext { get; set; }

        /// <summary>
        /// The lead agent's own widget/non-widget <c>AgentChatSetting</c> context — the lead agent IS
        /// the root of this orchestration run, so this is resolved once (see
        /// <c>AgentChatIconHelper.GetIsWidgetContextAsync</c>) at trigger time
        /// (<c>TriggerMultiAgentOrchestratorCommandHandler</c>) and forwarded onto every
        /// <see cref="AgentInvocationRequest"/> built for the lead AND every fan-out step, so every
        /// fan-out agent resolves its own chat icon in the same context as the lead rather than
        /// independently. Null when the lead agent has no AgentChatSetting row at all.
        /// </summary>
        public bool? RootIsWidget { get; set; }
    }
}
