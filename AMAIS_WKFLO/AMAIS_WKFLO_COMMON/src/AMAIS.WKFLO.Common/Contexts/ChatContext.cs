using AMAIS.WKFLO.Common.Mappings;
using Mapster;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AMAIS.WKFLO.Common.Contexts
{
    public class ChatContext : IMap
    {
        public string SessionId { get; set; }

        public string BasePath { get; set; }

        public string PagePath { get; set; }

        /// <summary>
        /// CMS ID of the page the user is on (<see cref="Models.CommonConsts.PageId"/> header), when the host sends
        /// it. Identifies the same page as <see cref="PagePath"/>; used to look the page up in DXP by ID.
        /// </summary>
        public string PageId { get; set; }

        /// <summary>
        /// The school's Windows timezone ID (e.g. "Eastern Standard Time"), forwarded via
        /// <see cref="Models.CommonConsts.TimeZone"/> so time-context extraction judges "past vs. upcoming"
        /// using the school's own local calendar rather than naive UTC. <c>null</c>/empty means the caller
        /// didn't send the header - resolved to UTC by callers (fail-open).
        /// </summary>
        public string TimeZone { get; set; }

        public string SubjectId { get; set; }

        public string FeatureId { get; set; }

        public string FeatureType { get; set; }

        public string FeatureName { get; set; }

        /// <summary>
        /// The short opaque AMAIS endpoint codes (e.g. "CRP", "WLC" — never a literal endpoint class
        /// name, deliberately) that this request's PetraCMS instance exposes, forwarded via
        /// <see cref="Models.CommonConsts.AmaisAvailableEndpoints"/>. <c>null</c>/empty means the
        /// caller didn't send the header (an AMAIS build predating this mechanism) — treated as
        /// fail-open (every AMAIS skill/plugin available) by <see cref="AI.Application.Plugins.Amais.AmaisPluginCatalog"/>.
        /// </summary>
        public IReadOnlyList<string> AmaisAvailableEndpoints { get; set; }

        /// <summary>
        /// True when the current request/queue message carries the <c>amais-wkflo-is-test-chat</c> header —
        /// set by wkflo_ui when the widget is embedded from a "test chat" surface (e.g. the CMS Test Agent
        /// tab). Flows through <c>WkfloQueueMessage&lt;T&gt;</c> to background processing the same way
        /// <see cref="SubjectId"/> does, so it's available wherever the conversation is first synced.
        /// </summary>
        public bool IsTestChat { get; set; }

        public List<ChatMessageContent> AssistantPostMessagesToBeProcessed { get; set; } = new List<ChatMessageContent>();
        public ChatHistory CurrentChatHistory { get; set; }

        /// <summary>
        /// Set by <c>ContactFormPlugin.RequestContactFormAsync</c> when it actually displays a NEW contact
        /// form this turn (never set on its "already collected, answer directly" short-circuit path, since
        /// a real answer follows in that same turn instead). Read by
        /// <c>SemanticKernelAgentServiceRun.RunChatCompletionAgentAsync</c> to skip scheduling
        /// <c>MessageResolutionWorkflow</c> for this turn — the assistant's response in that case is just
        /// the registration-required filler text, not a real answer to the user's question. Judging THAT
        /// text as "not resolved" would report the wrong <c>Response</c> to DXP and permanently mark the
        /// question unresolved even once the real answer arrives later via the ContactForm replay turn.
        /// </summary>
        public bool ContactFormRequestedThisTurn { get; set; }

        /// <summary>
        /// Bug 8007: set by <c>AgentEventPublisherPlugin.PublishAgentEventAsync</c> as soon as the model invokes
        /// <c>publish_agent_event</c> this turn (whether or not the publish then succeeds). Read by the text run to
        /// detect a reply that announced a hand-off without making the call. Never restored across a scope boundary.
        /// </summary>
        public bool AgentEventPublishCalledThisTurn { get; set; }

        /// <summary>
        /// WI 7575: true only while running an interactive text chat turn whose user can answer a tool approval
        /// request (set by the message command handlers, and by the approval resume). Never restored across a
        /// scope boundary (see <see cref="Mapping(TypeAdapterConfig)"/>), so a sub-agent or background run started
        /// from such a turn always starts with false - there, an "Ask" permission behaves as Block.
        /// </summary>
        public bool ToolApprovalsAvailable { get; set; }

        /// <summary>
        /// WI 7575: set by the approval filter when this turn stopped to wait for a tool approval. The turn's
        /// assistant reply is then only the "needs approval" notice, not a real answer (so message-level
        /// resolution judgment is skipped, same as for <see cref="ContactFormRequestedThisTurn"/>).
        /// </summary>
        public bool ToolApprovalRequestedThisTurn { get; set; }

        /// <summary>
        /// Bug 7722: in a sub-agent run started by an agent event, that event's type - the <c>action</c> of its
        /// sub-agent activity updates. Kept on a tool approval so its resume/expiry updates carry the same action.
        /// </summary>
        public string SubagentActivityAction { get; set; }

        /// <summary>
        /// US 7501: when the chat turn running in this scope started, as registered for the Stop (its message's queue
        /// time). Set by whatever registers the turn. A hand-off this turn requests is queued with this time, not the
        /// time it is published, so a Stop of this turn also drops it; a hand-off turn passes the same time on to its
        /// own hand-offs. Null outside a registered turn.
        /// </summary>
        public DateTimeOffset? TurnStartedAt { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<ChatContext, ChatContext>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            // AssistantPostMessagesToBeProcessed/CurrentChatHistory hold live Semantic Kernel types
            // (ChatMessageContent/ChatHistory, whose Items are polymorphic KernelContent). Mapster's
            // default same-type mapping tries to deep-clone them property-by-property and fails to
            // compile against KernelContent's abstract/constructor-only shape. Neither property needs
            // restoring across a scope boundary (AgentInvoker/BaseWorkflowActivity.LoadContext): both are
            // populated fresh downstream by SemanticKernelAgentServiceRun{,Voice} during the actual
            // model call, never read from an inbound request/message ChatContext.
            config.NewConfig<ChatContext, ChatContext>()
                .Ignore(dest => dest.AssistantPostMessagesToBeProcessed)
                .Ignore(dest => dest.CurrentChatHistory)
                .Ignore(dest => dest.ToolApprovalsAvailable)
                .Ignore(dest => dest.ToolApprovalRequestedThisTurn)
                .Ignore(dest => dest.AgentEventPublishCalledThisTurn);
        }
    }
}
