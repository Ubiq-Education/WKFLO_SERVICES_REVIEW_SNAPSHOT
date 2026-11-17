namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay
{
    /// <summary>
    /// Server-side record that a ContactForm-completion replay is legitimately pending for a session — set by
    /// <c>SubmitContactFormCommandHandler</c>/<c>SubmitPublicAgentContactFormCommandHandler</c> at the same
    /// point the <c>ContactFormCompleted</c> SignalR event is pushed, and consumed (single-use) once the
    /// replay request is actually processed. See <see cref="IContactFormReplayStateService"/>.
    /// </summary>
    public class ContactFormReplayPendingState
    {
        /// <summary>The original question text deferred behind the ContactForm tool, recorded so a replay
        /// request's own text can optionally be checked against it (defense in depth).</summary>
        public string OriginalMessage { get; set; }

        /// <summary>
        /// The MessageId of the original question deferred behind the ContactForm tool — recorded via
        /// <see cref="IContactFormReplayStateService.RecordDeferredQuestionAsync"/> at the moment the form
        /// was requested (the only point this id is known) and merged in here when the replay is confirmed
        /// pending. Null if the deferred-question record expired or was never made (e.g. a build predating
        /// this field) — callers must fall back to generating a fresh MessageId in that case, same as
        /// before this existed. Threaded through to the eventual replay turn so
        /// <c>MessageResolutionWorkflow</c> can correlate the real final answer back to the SAME
        /// <c>MessageTopicClassification</c> row the original question was classified under, instead of a
        /// freshly-generated (never classified) id that the workflow would silently no-op on.
        /// </summary>
        public Guid? OriginalMessageId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
