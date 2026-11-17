namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval
{
    public enum ToolApprovalDecisionOutcome
    {
        /// <summary>The decision was recorded (exactly one caller ever gets this for a given approval).</summary>
        Decided,
        NotFound,
        AlreadyDecided,

        /// <summary>The approval was already expired before this call (nothing changed).</summary>
        Expired,

        /// <summary>This call found the pending approval past its expiry and performed the Pending -> Expired transition.</summary>
        ExpiredNow
    }

    /// <summary>
    /// WI 7575: pending tool approvals and "Allow for this conversation" grants. Backed by the Dapr state store
    /// (<c>statestore-ai</c>) with ETag (optimistic concurrency) writes, so a decision is single-use even when two
    /// AI API instances receive it at the same time.
    /// </summary>
    public interface IToolApprovalStateService
    {
        Task SaveNewAsync(ToolApprovalRecord record, CancellationToken cancellationToken = default);

        Task<ToolApprovalRecord> GetAsync(string approvalId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Records <paramref name="decision"/> if the approval is still pending and not past its expiry. A pending
        /// approval found past its expiry is marked Expired and <see cref="ToolApprovalDecisionOutcome.ExpiredNow"/>
        /// is returned; an approval that was already expired returns <see cref="ToolApprovalDecisionOutcome.Expired"/>.
        /// </summary>
        Task<(ToolApprovalDecisionOutcome Outcome, ToolApprovalRecord Record)> TryDecideAsync(string approvalId, string decision, DateTimeOffset now, CancellationToken cancellationToken = default);

        /// <summary>Marks a still-pending approval Expired. Returns the record only when THIS call expired it.</summary>
        Task<ToolApprovalRecord> TryExpireAsync(string approvalId, DateTimeOffset now, CancellationToken cancellationToken = default);

        /// <summary>Expires every approval still open in the session (the person moved on with a new message).</summary>
        Task<IReadOnlyList<ToolApprovalRecord>> ExpireOpenForSessionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default);

        /// <summary>
        /// US 7501: like <see cref="ExpireOpenForSessionAsync(string, DateTimeOffset, CancellationToken)"/>, but only the
        /// approvals of <paramref name="tenantName"/> (a Stop never touches another tenant's approvals).
        /// </summary>
        Task<IReadOnlyList<ToolApprovalRecord>> ExpireOpenForSessionAsync(string tenantName, string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default);

        /// <summary>
        /// Turns an approved decision into a refusal when the resume finds the call can no longer run (agent switched
        /// off/deleted, share revoked). Returns the updated record, or null when it was not in an approved state.
        /// </summary>
        Task<ToolApprovalRecord> MarkRefusedAsync(string approvalId, DateTimeOffset now, CancellationToken cancellationToken = default);

        Task AddConversationGrantAsync(string sessionId, string grantKey, CancellationToken cancellationToken = default);

        Task<bool> HasConversationGrantAsync(string sessionId, string grantKey, CancellationToken cancellationToken = default);
    }
}
