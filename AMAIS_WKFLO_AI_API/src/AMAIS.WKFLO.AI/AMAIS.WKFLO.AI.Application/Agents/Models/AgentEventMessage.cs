namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    /// <summary>
    /// Minimal payload for a cross-agent async notification published on
    /// <c>AiWorkerTopicConsts.AgentEventMessageTopic</c> (story #6480 / task #6485).
    /// Delivered outside the main orchestrator workflow path: the subscriber
    /// (<c>WebAPI.EndPoints.Dapr.AgentEventSubscriber</c>) invokes
    /// <see cref="Invocation.IAgentInvoker"/> directly — no <c>WorkflowContext</c> or activity
    /// wrapper — since pub/sub handlers aren't subject to Dapr Workflow's replay-determinism constraint.
    /// This payload is never published directly as the CloudEvent body — it's wrapped in a
    /// <c>WkfloQueueMessage&lt;AgentEventMessage&gt;</c>, saved into <c>statestore-ai</c>, and only a
    /// short correlation key travels through <c>pubsub-ai</c>, matching
    /// <c>TenantAgentChatMessage</c>/<c>PublicAgentChatMessage</c>'s strategy — the message broker
    /// has a payload size limit, and this DTO's <see cref="Input"/> text can be arbitrarily long.
    /// </summary>
    public class AgentEventMessage
    {
        /// <summary>
        /// Id of the <c>TenantAgent</c> that should react to this event.
        /// </summary>
        public int AgentId { get; set; }

        /// <summary>
        /// Free-form role label for the reacting agent (e.g. "notifier", "watcher"). Forwarded
        /// as-is to <see cref="Invocation.IAgentInvoker.InvokeAsync"/>.
        /// </summary>
        public string AgentRole { get; set; }

        /// <summary>
        /// The text/prompt (plus any file attachments) the reacting agent should respond to.
        /// </summary>
        public AgentInvocationInput Input { get; set; }

        /// <summary>
        /// Chat session id used to load/save conversation history via the existing blob-based
        /// <c>IChatStoreService</c> path.
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Free-form category describing why this event was published (e.g. "task-completed",
        /// "escalation"). Not interpreted by the subscriber today — carried through for logging
        /// and for future routing/filtering.
        /// </summary>
        public string EventType { get; set; }

        /// <summary>
        /// The calling agent's own widget/non-widget context, resolved by
        /// <c>AgentEventPublisherPlugin</c> while still running inside the calling agent's own turn —
        /// see <c>PublishAgentEventDomainEvent.RootIsWidget</c>. Forwarded by
        /// <c>AgentEventWorkflowActivity</c> into <see cref="Invocation.IAgentInvoker"/> so every
        /// subagent invoked by this hand-off resolves its own chat icon in the same widget/non-widget
        /// context as the root, instead of independently guessing. Null when the root has no
        /// AgentChatSetting row to resolve a context from.
        /// </summary>
        public bool? RootIsWidget { get; set; }

        /// <summary>Bug 7722: see <c>PublishAgentEventDomainEvent.ApprovalRootAgentId</c>.</summary>
        public int? ApprovalRootAgentId { get; set; }
    }
}
