using AMAIS.WKFLO.Common.Contexts;

namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    /// <summary>
    /// Payload passed to <see cref="Invocation.IAgentInvoker.InvokeAsync"/> by the orchestrator
    /// workflow activity or pub/sub subscriber.
    /// </summary>
    public class AgentInvocationRequest
    {
        /// <summary>
        /// Id of the runnable agent to run — a <c>TenantAgent</c> or <c>PublishedTenantAgent</c> (see
        /// <c>IRunnableAISyncService.RunRunnableAgentByIdAsync</c>).
        /// </summary>
        public int AgentId { get; set; }

        /// <summary>
        /// Free-form role label assigned by the orchestrator (e.g. "researcher", "critic").
        /// Not persisted on the Agent entity — it only has meaning within the orchestration run.
        /// </summary>
        public string AgentRole { get; set; }

        /// <summary>
        /// The text/prompt (plus any file attachments) this agent should respond to — e.g. the task
        /// handed to it by the orchestrator, or the previous agent's output for a pipeline step.
        /// </summary>
        public AgentInvocationInput Input { get; set; }

        /// <summary>
        /// Chat session id. Conversation history continues to load/save through the existing
        /// blob-based <c>IChatStoreService</c> path keyed by this value — the actor never touches
        /// chat history directly.
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Id of the parent Dapr Workflow orchestration instance driving this run. Used for
        /// correlation/logging/tracing.
        /// </summary>
        public string OrchestrationInstanceId { get; set; }

        /// <summary>
        /// Whether the agent should run its chain-of-thought reasoning pass before answering.
        /// </summary>
        public bool ChainOfThought { get; set; }

        /// <summary>
        /// Ambient contexts from wherever this request originated (workflow input or the pub/sub
        /// queue message), restored onto <see cref="Invocation.IAgentInvoker"/>'s own scoped context
        /// instances before it dispatches to MediatR — mirroring <c>BaseWorkflowActivity.LoadContext</c>,
        /// which solves the identical problem for Dapr Workflow activities. Without this, every
        /// invocation would run with a blank <see cref="TenantContext"/>: a Workflow Activity or an
        /// ASP.NET Core request gets its own fresh DI scope, but not the ambient values from whatever
        /// originally triggered it.
        /// </summary>
        public TenantContext TenantContext { get; set; }

        public ChatContext ChatContext { get; set; }

        public UserContext UserContext { get; set; }

        public AgentSharableContext AgentSharableContext { get; set; }

        /// <summary>
        /// Whether the root/main agent that triggered this hand-off is being viewed via its widget vs.
        /// non-widget <c>AgentChatSetting</c> config — restored here alongside the ambient contexts
        /// above (see <c>AgentEventMessage.RootIsWidget</c> / <c>MultiAgentOrchestratorWorkflowInput.RootIsWidget</c>)
        /// so <see cref="Invocation.IAgentInvoker"/> can forward it into <c>InvokeAgentCommand</c>, and
        /// this agent's own chat icon resolves in the same context as the root rather than
        /// independently. Null when the root has no AgentChatSetting row at all.
        /// </summary>
        public bool? RootIsWidget { get; set; }

        /// <summary>
        /// Bug 7722: see <c>PublishAgentEventDomainEvent.ApprovalRootAgentId</c>. Left null by the multi-agent
        /// orchestrator (no person can answer approval requests there).
        /// </summary>
        public int? ApprovalRootAgentId { get; set; }

        /// <summary>
        /// US 7501: when the orchestration that asked for this run started. A Stop cancels the session's runs requested
        /// before it, so a step that only starts after the Stop is cancelled too. Null: the activity start is used.
        /// </summary>
        public DateTimeOffset? RequestedAt { get; set; }
    }
}
