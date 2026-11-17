using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.AI.Providers;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models
{
    public class RunTextOptions
    {
        public string Message { get; set; }
        public List<FileAttachment> Files { get; set; }
        public string SessionId { get; set; }
        public AIBaseProvider Provider { get; set; }
        public RunnableAgent Agent { get; set; }
        public List<string> AgentTagNames { get; set; }
        public bool ChainOfThought { get; set; }

        /// <summary>
        /// Set only when <see cref="Agent"/> is a subagent invoked via a spin-up/orchestration
        /// hand-off — the root agent's own widget/non-widget <c>AgentChatSetting</c> context, threaded
        /// through so this agent's chat icon resolves consistently with the root's (see
        /// <c>AgentChatIconHelper.GetChatbotIconUrlAsync</c>). Null for a root agent's own direct
        /// invocation (no hand-off involved) — that path is unaffected.
        /// </summary>
        public bool? RootIsWidget { get; set; }

        /// <summary>
        /// Whether the agent is tagged with the "Maisy" feature-selection tag (see
        /// <c>AgentTagHelper.IsMaisyAgentAsync</c>). Threaded down into
        /// <see cref="Models.RunChatData.IsMaisyAgent"/> so the LLM <c>Instructions</c> builder can
        /// qualify the tool-disclosure guidance: Maisy agents keep answering meta/introspection
        /// questions about their tools with the full tool list, while non-Maisy agents deflect them
        /// (Task #7032).
        /// </summary>
        public bool IsMaisyAgent { get; set; }

        /// <summary>
        /// True for wkflo_ui's ContactForm "replay" turn — a follow-up call with the literal original
        /// question text, sent once the visitor completes the ContactForm tool's registration form, to
        /// obtain the real answer the agent couldn't give pre-registration. The model still runs and
        /// produces a fresh answer, but this turn must not be recorded as a second, independent visitor
        /// message: <see cref="Models.RunTextOptions"/>'s consumers must skip re-classifying the topic
        /// (see <c>SemanticKernelAgentService.RunChatClassificationAsync</c>'s own guard) and must not add
        /// a duplicate <c>AuthorRole.User</c> ChatHistory entry for the already-recorded question (see
        /// <c>SemanticKernelAgentService.RunChatCompletionAgentAsync</c>). Default <c>false</c> for every
        /// ordinary turn.
        /// </summary>
        public bool IsContactFormReplay { get; set; }

        /// <summary>
        /// Only meaningful alongside <see cref="IsContactFormReplay"/> — the MessageId of the ORIGINAL
        /// question the ContactForm tool deferred, recorded at the moment the form was first requested
        /// (see <c>IContactFormReplayStateService.RecordDeferredQuestionAsync</c>) and resolved by the
        /// replay command handler from the now-confirmed pending-replay state. When present, this replay
        /// turn reuses it as <see cref="Models.RunChatData.UserMessageId"/> instead of generating a fresh
        /// one, so <c>MessageResolutionWorkflow</c> can correlate the real final answer back to the SAME
        /// <c>MessageTopicClassification</c> row the original question was classified under. Null falls
        /// back to a freshly-generated id (the pre-existing behavior, which silently loses message-level
        /// resolution tracking for this question — a known, accepted gap only when the deferred-question
        /// record couldn't be resolved, e.g. it expired).
        /// </summary>
        public Guid? OriginalMessageId { get; set; }

        /// <summary>
        /// WI 7575: true only for an interactive text chat where the person chatting can answer a tool approval
        /// request (tenant test chat, public widget). False (the default) for every run where nobody could
        /// answer - sub-agent/spin-up runs, orchestrations, synchronous/background runs - and there an "Ask"
        /// permission behaves as Block.
        /// </summary>
        public bool ApprovalsAvailable { get; set; }

        /// <summary>
        /// Bug 7722: set when this run is a sub-agent hand-off from an interactive chat - the id of that chat's agent
        /// (the root). Stored on approval requests so the root chat can decide them; a sub-agent run never passes
        /// approvals on to its own hand-offs.
        /// </summary>
        public int? RootAgentId { get; set; }

        /// <summary>
        /// WI 7575: set only for the resume turn after a tool approval decision. The turn runs (or refuses) the
        /// stored call first, then continues the conversation without a new user message - like the ContactForm
        /// replay, the question was already recorded by the turn that asked for approval.
        /// </summary>
        public Domain.Agents.ToolApproval.ToolApprovalRecord ToolApprovalResume { get; set; }
    }
}
