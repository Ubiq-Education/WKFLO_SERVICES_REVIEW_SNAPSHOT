namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay
{
    /// <summary>
    /// Tracks, server-side, that session X has a pending ContactForm-completion replay. The client-supplied
    /// <c>IsContactFormReplay</c> flag on <c>MessagePublicAgentByShareIdCommand</c>/
    /// <c>MessageTenantAgentByIdCommand</c> must never be honored without this being validated first (see
    /// those commands' validators) — trusting the flag directly from the client, with no server-side check,
    /// would let any caller (public or authenticated) mark an arbitrary message as a "replay" to suppress
    /// topic classification and the persisted <c>AuthorRole.User</c> chat-history entry for it, a real
    /// audit/integrity gap, not just a data-quality one.
    /// </summary>
    /// <remarks>
    /// Backed by the same Dapr state store (<c>statestore-ai</c>) <c>BaseWorkflowActivity.BaseRunAsync</c>
    /// already uses for its own idempotency tracking (save/get/delete against a small keyed state entry) —
    /// this mirrors that established precedent rather than introducing a new storage mechanism.
    /// </remarks>
    public interface IContactFormReplayStateService
    {
        /// <summary>
        /// Records the MessageId of the question that triggered a ContactForm request THIS turn — called
        /// from <c>SemanticKernelAgentServiceRun.RunChatCompletionAgentAsync</c> at REQUEST time (the only
        /// point the id is known), deliberately separate from <see cref="SetPendingReplayAsync"/>'s own
        /// security-relevant gate (a replay is only ever honored once contact info is genuinely
        /// SUBMITTED — recording this early must never, by itself, let a replay be honored). Expires
        /// automatically (see the implementation's TTL) if the form is never submitted.
        /// </summary>
        Task RecordDeferredQuestionAsync(string sessionId, Guid messageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a replay as pending for this session, recording the deferred question text (and, when one
        /// was recorded via <see cref="RecordDeferredQuestionAsync"/> for this session, its MessageId too —
        /// see <see cref="ContactFormReplayPendingState.OriginalMessageId"/>). Expires automatically (see
        /// the implementation's TTL) if a replay is never actually sent, so an unconsumed pending flag
        /// doesn't stay valid indefinitely.
        /// </summary>
        Task SetPendingReplayAsync(string sessionId, string originalMessage, CancellationToken cancellationToken = default);

        /// <summary>Returns the pending replay state for this session, or <c>null</c> if none is pending.</summary>
        Task<ContactFormReplayPendingState> GetPendingReplayAsync(string sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears the pending replay state for this session — single-use. Call ONLY once a replay request has
        /// passed validation and is genuinely about to be honored, never from inside the validation check
        /// itself (a validator that also fails a request for an unrelated reason must not have already
        /// consumed a legitimate pending state, or a retry would then be wrongly rejected).
        /// </summary>
        Task ConsumePendingReplayAsync(string sessionId, CancellationToken cancellationToken = default);
    }
}
