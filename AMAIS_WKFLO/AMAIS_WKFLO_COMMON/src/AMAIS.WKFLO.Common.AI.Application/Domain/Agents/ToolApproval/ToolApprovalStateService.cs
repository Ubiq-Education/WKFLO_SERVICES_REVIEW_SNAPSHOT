using AMAIS.WKFLO.Common.Models;
using Dapr.Client;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval
{
    /// <summary>
    /// Dapr state store implementation of <see cref="IToolApprovalStateService"/> (same store and key/TTL
    /// approach as <c>ContactFormReplayStateService</c>). Every read-modify-write uses the entry's ETag and
    /// retries a few times on a concurrent write, so concurrent decisions cannot both win.
    /// </summary>
    public class ToolApprovalStateService : IToolApprovalStateService
    {
        // Kept well beyond the 10-minute expiry so a late decision still finds the record and gets 410 (Expired)
        // or 409 (AlreadyDecided) instead of 404.
        private const int ApprovalTtlSeconds = 24 * 60 * 60;

        // A conversation grant lives as long as a conversation reasonably stays open.
        private const int GrantTtlSeconds = 7 * 24 * 60 * 60;

        private const int MaxConcurrencyRetries = 5;

        private static string Store => CommonConsts.CommonAiPubSubConsts.StateStoreName;

        private readonly DaprClient _daprClient;

        public ToolApprovalStateService(DaprClient daprClient)
        {
            _daprClient = daprClient;
        }

        public async Task SaveNewAsync(ToolApprovalRecord record, CancellationToken cancellationToken = default)
        {
            await _daprClient.SaveStateAsync(Store, ApprovalKey(record.ApprovalId), record, metadata: Ttl(ApprovalTtlSeconds), cancellationToken: cancellationToken);

            await UpdateSetAsync(OpenKey(record.SessionId), ApprovalTtlSeconds, set => set.Add(record.ApprovalId), cancellationToken);
        }

        public Task<ToolApprovalRecord> GetAsync(string approvalId, CancellationToken cancellationToken = default) =>
            string.IsNullOrWhiteSpace(approvalId)
                ? Task.FromResult<ToolApprovalRecord>(null)
                : _daprClient.GetStateAsync<ToolApprovalRecord>(Store, ApprovalKey(approvalId), cancellationToken: cancellationToken);

        public async Task<(ToolApprovalDecisionOutcome Outcome, ToolApprovalRecord Record)> TryDecideAsync(string approvalId, string decision, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(approvalId))
                return (ToolApprovalDecisionOutcome.NotFound, null);

            for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
            {
                var (record, etag) = await _daprClient.GetStateAndETagAsync<ToolApprovalRecord>(Store, ApprovalKey(approvalId), cancellationToken: cancellationToken);
                if (record == null)
                    return (ToolApprovalDecisionOutcome.NotFound, null);

                if (record.Status == ToolApprovalConsts.Status.Expired)
                    return (ToolApprovalDecisionOutcome.Expired, record);

                if (!record.IsPending)
                    return (ToolApprovalDecisionOutcome.AlreadyDecided, record);

                var expired = now >= record.ExpiresAt;
                var outcomeDecision = expired ? ToolApprovalConsts.Decision.Expired : decision;
                record.Decision = outcomeDecision;
                record.Status = ToolApprovalConsts.Decision.ToStatus(outcomeDecision);
                record.DecidedAt = now;

                if (await _daprClient.TrySaveStateAsync(Store, ApprovalKey(approvalId), record, etag, metadata: Ttl(ApprovalTtlSeconds), cancellationToken: cancellationToken))
                {
                    await RemoveOpenAsync(record, cancellationToken);
                    return (expired ? ToolApprovalDecisionOutcome.ExpiredNow : ToolApprovalDecisionOutcome.Decided, record);
                }
            }

            // Lost every race: someone else decided it meanwhile.
            return (ToolApprovalDecisionOutcome.AlreadyDecided, await GetAsync(approvalId, cancellationToken));
        }

        public async Task<ToolApprovalRecord> TryExpireAsync(string approvalId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
            {
                var (record, etag) = await _daprClient.GetStateAndETagAsync<ToolApprovalRecord>(Store, ApprovalKey(approvalId), cancellationToken: cancellationToken);
                if (record == null || !record.IsPending)
                    return null;

                record.Status = ToolApprovalConsts.Status.Expired;
                record.Decision = ToolApprovalConsts.Decision.Expired;
                record.DecidedAt = now;

                if (await _daprClient.TrySaveStateAsync(Store, ApprovalKey(approvalId), record, etag, metadata: Ttl(ApprovalTtlSeconds), cancellationToken: cancellationToken))
                {
                    await RemoveOpenAsync(record, cancellationToken);
                    return record;
                }
            }

            return null;
        }

        public async Task<ToolApprovalRecord> MarkRefusedAsync(string approvalId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
            {
                var (record, etag) = await _daprClient.GetStateAndETagAsync<ToolApprovalRecord>(Store, ApprovalKey(approvalId), cancellationToken: cancellationToken);
                if (record == null || record.Status != ToolApprovalConsts.Status.Approved)
                    return null;

                record.Status = ToolApprovalConsts.Status.Denied;
                record.Decision = ToolApprovalConsts.Decision.Deny;
                record.DecidedAt = now;

                if (await _daprClient.TrySaveStateAsync(Store, ApprovalKey(approvalId), record, etag, metadata: Ttl(ApprovalTtlSeconds), cancellationToken: cancellationToken))
                    return record;
            }

            return null;
        }

        public async Task<IReadOnlyList<ToolApprovalRecord>> ExpireOpenForSessionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return [];

            var open = await _daprClient.GetStateAsync<List<string>>(Store, OpenKey(sessionId), cancellationToken: cancellationToken) ?? [];
            var expired = new List<ToolApprovalRecord>();
            foreach (var approvalId in open.Distinct())
            {
                var record = await TryExpireAsync(approvalId, now, cancellationToken);
                if (record != null)
                    expired.Add(record);
            }

            return expired;
        }

        public async Task<IReadOnlyList<ToolApprovalRecord>> ExpireOpenForSessionAsync(string tenantName, string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(sessionId))
                return [];

            var open = await _daprClient.GetStateAsync<List<string>>(Store, OpenKey(sessionId), cancellationToken: cancellationToken) ?? [];
            var expired = new List<ToolApprovalRecord>();
            foreach (var approvalId in open.Distinct())
            {
                var current = await GetAsync(approvalId, cancellationToken);
                if (current == null || !string.Equals(current.TenantName?.Trim(), tenantName.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                var record = await TryExpireAsync(approvalId, now, cancellationToken);
                if (record != null)
                    expired.Add(record);
            }

            return expired;
        }

        public Task AddConversationGrantAsync(string sessionId, string grantKey, CancellationToken cancellationToken = default) =>
            UpdateSetAsync(GrantsKey(sessionId), GrantTtlSeconds, set => set.Add(grantKey), cancellationToken);

        public async Task<bool> HasConversationGrantAsync(string sessionId, string grantKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(grantKey))
                return false;

            var grants = await _daprClient.GetStateAsync<List<string>>(Store, GrantsKey(sessionId), cancellationToken: cancellationToken);
            return grants?.Contains(grantKey, StringComparer.Ordinal) == true;
        }

        private async Task RemoveOpenAsync(ToolApprovalRecord record, CancellationToken cancellationToken)
        {
            try
            {
                await UpdateSetAsync(OpenKey(record.SessionId), ApprovalTtlSeconds, set => set.Remove(record.ApprovalId), cancellationToken);
            }
            catch (Exception)
            {
                // Best effort: the index only drives ExpireOpenForSessionAsync, which skips decided records anyway.
            }
        }

        private async Task UpdateSetAsync(string key, int ttlSeconds, Func<HashSet<string>, bool> mutate, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
            {
                var (current, etag) = await _daprClient.GetStateAndETagAsync<List<string>>(Store, key, cancellationToken: cancellationToken);
                var set = new HashSet<string>(current ?? [], StringComparer.Ordinal);
                if (!mutate(set))
                    return;

                if (string.IsNullOrEmpty(etag))
                {
                    await _daprClient.SaveStateAsync(Store, key, set.ToList(), metadata: Ttl(ttlSeconds), cancellationToken: cancellationToken);
                    return;
                }

                if (await _daprClient.TrySaveStateAsync(Store, key, set.ToList(), etag, metadata: Ttl(ttlSeconds), cancellationToken: cancellationToken))
                    return;
            }
        }

        private static Dictionary<string, string> Ttl(int seconds) => new() { ["ttlInSeconds"] = seconds.ToString() };

        private static string ApprovalKey(string approvalId) => $"tool-approval-{approvalId}";
        private static string OpenKey(string sessionId) => $"tool-approval-open-{sessionId}";
        private static string GrantsKey(string sessionId) => $"tool-approval-grants-{sessionId}";
    }
}
