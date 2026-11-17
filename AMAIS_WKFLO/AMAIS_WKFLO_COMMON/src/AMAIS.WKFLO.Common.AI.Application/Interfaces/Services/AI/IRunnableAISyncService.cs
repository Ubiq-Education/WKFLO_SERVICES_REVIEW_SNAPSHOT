using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.AI;

namespace AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI
{
    /// <summary>
    /// Runs any <c>RunnableAgent</c> (both <c>TenantAgent</c> and <c>PublishedTenantAgent</c>) by its
    /// tenant-scoped id — used by the multi-agent orchestration pipeline (story #6480), which needs to
    /// invoke either agent subtype interchangeably. Unlike <see cref="ITenantAISyncService"/> (scoped
    /// to <c>TenantAgent</c> only, used by the original single-agent chat flow), this widens the
    /// lookup to the <c>RunnableAgent</c> base type.
    /// </summary>
    public interface IRunnableAISyncService
    {
        /// <summary>
        /// <paramref name="rootIsWidget"/>: set only when this invocation is a subagent invoked via a
        /// spin-up/orchestration hand-off (see <c>InvokeAgentCommand.RootIsWidget</c>) — the root
        /// agent's own widget/non-widget <c>AgentChatSetting</c> context, threaded through so this
        /// agent's chat icon resolves consistently with the root's rather than independently. Null for
        /// every other (root, direct) invocation path — unaffected.
        /// </summary>
        Task<RunnableAgentData> RunRunnableAgentByIdAsync(int agentId, string input, List<FileAttachment> files, string sessionId, bool chainOfThought, bool? rootIsWidget = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Bug 7722: same as the overload above, for a hand-off from an interactive chat. <paramref name="approvalRootAgentId"/>
        /// is the id of that chat's agent; the run then has tool approvals available. Null: no approvals (Ask acts as Block).
        /// </summary>
        Task<RunnableAgentData> RunRunnableAgentByIdAsync(int agentId, string input, List<FileAttachment> files, string sessionId, bool chainOfThought, bool? rootIsWidget, int? approvalRootAgentId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs an already-resolved <see cref="RunnableAgent"/> — the shared execution core (typing
        /// indicator, tag lookup, provider resolution, Semantic Kernel invocation, response
        /// notification) reused by <see cref="ITenantAISyncService.RunTenantAgentByIdAsync"/> and
        /// <c>IPublicAISyncService.RunPublicAgentByShareIdAsync</c>, which each do their own distinct
        /// lookup (by tenant-scoped id vs. by share id) before calling this. Returns the agent's
        /// response text, or <c>null</c> if the model produced no answer or no AI provider is configured
        /// (US 7959: that is recorded as a chat error; <paramref name="noProviderMessage"/> is no longer
        /// returned as an answer and is kept only for compatibility) — callers wrap a non-null result into their own
        /// result DTO and pass through a <c>null</c> as-is, exactly matching each service's original
        /// behavior before this was extracted. <paramref name="rootIsWidget"/>: see
        /// <see cref="RunRunnableAgentByIdAsync"/>. <paramref name="isContactFormReplay"/>: forwarded from
        /// <c>MessagePublicAgentByShareIdCommand.IsContactFormReplay</c> down into
        /// <c>RunTextOptions.IsContactFormReplay</c> — see there for what honoring it actually does.
        /// <paramref name="originalMessageId"/>: only meaningful alongside
        /// <paramref name="isContactFormReplay"/> — forwarded into
        /// <c>RunTextOptions.OriginalMessageId</c>, see there for why.
        /// </summary>
        /// <summary>
        /// WI 7575: the resume turn after a tool approval decision - looks the agent up from the stored record
        /// (its tenant included), runs or refuses the stored call and continues the conversation, streaming and
        /// notifying exactly like an ordinary turn.
        /// </summary>
        /// <summary>
        /// WI 7575: whether the stored call of <paramref name="record"/> may still run - the agent exists, is active and,
        /// for a shared chat, the share still exists and still points at that agent.
        /// </summary>
        Task<bool> CanResumeAsync(Domain.Agents.ToolApproval.ToolApprovalRecord record, CancellationToken cancellationToken = default);

        Task<string> ResumeAfterToolApprovalAsync(Domain.Agents.ToolApproval.ToolApprovalRecord record, CancellationToken cancellationToken = default);

        Task<string> RunRunnableAgentAsync(RunnableAgent runnableAgent, string input, List<FileAttachment> files, string sessionId, bool chainOfThought, string noProviderMessage = "No provider found!", bool? rootIsWidget = null, bool isContactFormReplay = false, Guid? originalMessageId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Voice counterpart to <see cref="RunRunnableAgentAsync"/> — the shared voice execution core
        /// (typing indicator, tag lookup, provider resolution, voice name/language resolution from the
        /// agent's Amais integration tool config, Semantic Kernel voice invocation, response
        /// notification) reused by <see cref="ITenantAISyncService.RunVoiceTenantAgentByIdAsync"/> and
        /// <c>IPublicAISyncService.RunVoicePublicAgentByIdAsync</c>. Same null/no-provider semantics as
        /// <see cref="RunRunnableAgentAsync"/>. <paramref name="rootIsWidget"/>: see
        /// <see cref="RunRunnableAgentByIdAsync"/>.
        /// </summary>
        Task<string> RunRunnableAgentVoiceAsync(RunnableAgent runnableAgent, string input, string sessionId, string branchId, bool chainOfThought, string noProviderMessage = "No provider found!", bool? rootIsWidget = null, CancellationToken cancellationToken = default);
    }
}
