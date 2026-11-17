using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Mediator;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.PublishAgentEvent
{
    /// <summary>
    /// Published by <c>AgentEventPublisherPlugin</c> (story #6480) when an agent with
    /// <c>RunnableAgent.EnableSpinUpAgents</c> set decides to hand a message off to one or more other
    /// agents, asynchronously, without waiting for a reply — one instance of this event per target
    /// agent, even when the plugin's caller passed several targets in a single call. Bridges
    /// <c>Common.AI.Application</c> to the AI API's own <c>PublishAgentEventCommand</c> — the AI API's
    /// <c>PublishAgentEventDomainEventHandler</c> picks this up and sends the real command via
    /// <c>IWkfloMediator.Send</c>. Mirrors the existing <c>AgentTokensUsedDomainEvent</c> bridge
    /// pattern for the same reason: Common cannot reference AI.Application's command types.
    /// </summary>
    public class PublishAgentEventDomainEvent : IWkfloNotification
    {
        /// <summary>
        /// Id of the target agent that should react to this event, as supplied by the calling agent's
        /// own LLM at call time.
        /// </summary>
        public int AgentId { get; }

        /// <summary>
        /// Free-form role label for the target agent (e.g. "refunds", "escalation"), forwarded as-is.
        /// </summary>
        public string AgentRole { get; }

        /// <summary>
        /// The message the target agent should react to.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Chat session id the calling agent is running under — the target agent's reaction is posted
        /// into the same session via the existing blob-based chat history path.
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// Free-form category describing why this event was published (e.g. "refund-requested").
        /// </summary>
        public string EventType { get; }

        /// <summary>
        /// Attachments resolved by <c>AgentEventPublisherPlugin</c> from the calling conversation's chat
        /// history (by file name/mime type), forwarded as-is onto the target agent's own invocation via
        /// <c>PublishAgentEventCommand.Input.Files</c>. Null/empty when no files were requested or none
        /// resolved.
        /// </summary>
        public List<FileAttachment> Files { get; }

        /// <summary>
        /// The calling agent's own widget/non-widget <c>AgentChatSetting</c> context, resolved once by
        /// <c>AgentEventPublisherPlugin</c> at the point this event is published (i.e. while still
        /// running inside the calling agent's own turn) — see
        /// <c>AgentChatIconHelper.GetIsWidgetContextAsync</c>. Threaded through so the target agent
        /// resolves its own chat icon in the same context as the caller, rather than independently.
        /// Null when the calling agent has no AgentChatSetting row at all.
        /// </summary>
        public bool? RootIsWidget { get; }

        /// <summary>
        /// Bug 7722: set only when the hand-off comes from an interactive chat turn in which the person chatting can
        /// answer tool approval requests - the id of that chat's agent (the root). The target agent then runs with
        /// approvals available, so its "Ask" tools ask in the same chat instead of acting as Block. Null for every
        /// other invocation (orchestrator, background, voice, a hand-off made by a sub-agent).
        /// </summary>
        public int? ApprovalRootAgentId { get; }

        /// <summary>
        /// US 7501: when the requesting turn started (<c>ChatContext.TurnStartedAt</c>). The hand-off is queued with this
        /// time, so a Stop of the requesting turn also drops it, even when the Stop lands while it is being published.
        /// Null when the request does not come from a registered turn (then the publish time is used).
        /// </summary>
        public DateTimeOffset? RequestedByTurnStartedAt { get; }

        public PublishAgentEventDomainEvent(int agentId, string agentRole, string text, string sessionId, string eventType, List<FileAttachment> files = null, bool? rootIsWidget = null, int? approvalRootAgentId = null,
            DateTimeOffset? requestedByTurnStartedAt = null)
        {
            RequestedByTurnStartedAt = requestedByTurnStartedAt;
            ApprovalRootAgentId = approvalRootAgentId;
            AgentId = agentId;
            AgentRole = agentRole;
            Text = text;
            SessionId = sessionId;
            EventType = eventType;
            Files = files;
            RootIsWidget = rootIsWidget;
        }
    }
}
