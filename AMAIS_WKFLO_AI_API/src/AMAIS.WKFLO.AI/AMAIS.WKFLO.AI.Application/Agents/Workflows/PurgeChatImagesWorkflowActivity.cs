using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;
using AMAIS.WKFLO.Common.Models.Storage;
using Dapr.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows
{
    /// <summary>
    /// One tick of <see cref="PurgeChatImagesWorkflow"/> (US 7974 / Task 7980): deletes the chat images of every tenant
    /// whose local midnight has passed since its last purge, keeping images created since that midnight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tenant is due when its local date (in its <see cref="TenantSetting.TimeZone"/>, UTC when it has none or it is
    /// invalid) is later than <see cref="ChatImagePurgeState.LastPurgeLocalDate"/>; see <see cref="TenantMidnightCalculator"/>.
    /// Only blobs created before the tenant's most recent local midnight (the cutoff) are deleted.
    /// </para>
    /// <para>
    /// Blobs are found under the tenant prefix (<see cref="ChatImageBlobNames.TenantPrefix"/>). Blobs with legacy,
    /// tenant-less names are found with a single container listing: <c>{sessionId}/{file}</c> belongs to the tenant(s) of
    /// that session's <see cref="Conversation"/> rows (a session id is not unique across tenants: when it maps to several
    /// tenants, the blob is only deleted once it is older than the start of the current local day of every one of them);
    /// <c>edited/{file}</c>, unknown sessions and any other name belong to no tenant and are purged at UTC midnight,
    /// tracked by the <see cref="ChatImagePurgeState"/> row with no tenant.
    /// </para>
    /// <para>
    /// The day is recorded only after the purge of that tenant succeeded (no listing or delete failures). A crash or a
    /// failure therefore repeats the purge on the next tick, which is harmless because deletion is cutoff-based, rather
    /// than skipping the day and leaving old images for another 24 hours. The record is written with a conditional update
    /// (<c>LastPurgeLocalDate &lt; today</c>) or an insert guarded by the unique TenantId, so concurrent runs record a day
    /// once.
    /// </para>
    /// </remarks>
    public class PurgeChatImagesWorkflowActivity : WorkflowActivity<string, bool>
    {
        private const int SessionLookupBatchSize = 500;

        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly IMasterDataDbContext _db;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<PurgeChatImagesWorkflowActivity> _logger;

        public PurgeChatImagesWorkflowActivity(IBlobStorageProvider blobStorageProvider, IMasterDataDbContext db,
            IDateTimeProvider dateTimeProvider, ILogger<PurgeChatImagesWorkflowActivity> logger)
        {
            _blobStorageProvider = blobStorageProvider;
            _db = db;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <param name="input">The workflow's tick time (round-trip "O" format); the current time is used when it is later or the input is not a date.</param>
        public override async Task<bool> RunAsync(WorkflowActivityContext context, string input)
        {
            try
            {
                await PurgeAsync(ResolveNowUtc(input), CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PurgeChatImages: activity failed.");
                return false;
            }
        }

        /// <summary>Purges the chat images of every tenant (and of the no-tenant scope) that is due at <paramref name="nowUtc"/>.</summary>
        public async Task PurgeAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            var scopes = await LoadScopesAsync(nowUtc, cancellationToken);
            var unowned = scopes.Single(s => s.TenantId is null);
            var due = scopes.Where(s => s.Decision.IsDue).ToList();

            if (due.Count == 0)
            {
                _logger.LogInformation("PurgeChatImages: tick {NowUtc:O}, no tenant is due.", nowUtc);
                return;
            }

            _logger.LogInformation("PurgeChatImages: tick {NowUtc:O}, {DueTenants} tenant(s) due, no-tenant (UTC) scope due: {UnownedDue}.",
                nowUtc, due.Count(s => s.TenantId is not null), unowned.Decision.IsDue);

            foreach (var scope in due.Where(s => s.TenantGuid is { } guid && guid != Guid.Empty))
            {
                try
                {
                    var blobs = await _blobStorageProvider.ListBlobsAsync(ChatImageBlobNames.ContainerName,
                        ChatImageBlobNames.TenantPrefix(scope.TenantGuid.Value), cancellationToken);

                    foreach (var blob in blobs)
                        await DeleteIfBeforeCutoffAsync(blob, scope.Decision.CutoffUtc, scope, [scope], isLegacy: false, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "PurgeChatImages: failed to list chat images of tenant {TenantId}.", scope.TenantId);
                    scope.Failed++;
                }
            }

            await PurgeLegacyBlobsAsync(scopes, unowned, cancellationToken);

            foreach (var scope in due)
            {
                var recorded = false;
                if (scope.Failed == 0)
                    recorded = await TryRecordPurgeAsync(scope.TenantId, scope.Decision.LocalDate, cancellationToken);

                _logger.LogInformation(
                    "PurgeChatImages: {Scope} local date {LocalDate:yyyy-MM-dd}, cutoff {CutoffUtc:O}: deleted {Deleted} (legacy {LegacyDeleted}), kept {Kept}, failed {Failed}, recorded {Recorded}.",
                    scope.TenantId is { } id ? $"tenant {id}" : "no-tenant scope", scope.Decision.LocalDate, scope.Decision.CutoffUtc,
                    scope.Deleted, scope.LegacyDeleted, scope.Kept, scope.Failed, recorded);
            }
        }

        /// <summary>
        /// The tick time: the later of <paramref name="input"/> (the workflow's tick time) and the current time, so a
        /// tick is never evaluated before the top of the hour it was scheduled for, nor in the past after a delay.
        /// </summary>
        public DateTimeOffset ResolveNowUtc(string input)
        {
            var now = _dateTimeProvider.DateTimeOffsetUtcNow();

            if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var tick)
                && tick > now)
                return tick;

            return now;
        }

        private async Task<List<PurgeScope>> LoadScopesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            // Every tenant, deleted ones included: their images still have to go.
            var tenants = await _db.Tenants
                .AsNoTracking()
                .Select(t => new
                {
                    t.Id,
                    t.Guid,
                    TimeZone = _db.TenantSettings.Where(s => s.TenantId == t.Id && !s.IsDeleted).Select(s => s.TimeZone).FirstOrDefault(),
                    LastPurgeLocalDate = _db.ChatImagePurgeStates.Where(p => p.TenantId == t.Id).Select(p => (DateOnly?)p.LastPurgeLocalDate).FirstOrDefault(),
                })
                .ToListAsync(cancellationToken);

            var unownedLastPurge = await _db.ChatImagePurgeStates
                .AsNoTracking()
                .Where(p => p.TenantId == null)
                .Select(p => (DateOnly?)p.LastPurgeLocalDate)
                .FirstOrDefaultAsync(cancellationToken);

            var scopes = new List<PurgeScope>(tenants.Count + 1);

            foreach (var tenant in tenants)
            {
                var timeZoneId = tenant.TimeZone ?? TenantSettingDefaults.TimeZone;
                var isValid = TenantMidnightCalculator.TryResolveTimeZone(timeZoneId, out var timeZone);
                var decision = TenantMidnightCalculator.Evaluate(timeZone, nowUtc, tenant.LastPurgeLocalDate);

                // Warn once per day (when due) rather than on every hourly tick.
                if (!isValid && decision.IsDue)
                    _logger.LogWarning("PurgeChatImages: tenant {TenantId} has an invalid time zone {TimeZone}; purging at UTC midnight.", tenant.Id, timeZoneId);

                scopes.Add(new PurgeScope(tenant.Id, tenant.Guid, decision));
            }

            scopes.Add(new PurgeScope(null, null, TenantMidnightCalculator.Evaluate(TimeZoneInfo.Utc, nowUtc, unownedLastPurge)));
            return scopes;
        }

        /// <summary>
        /// One listing of the whole container for blobs with legacy names (see the class remarks). Blobs under a known
        /// tenant prefix are skipped: they were handled by the per-tenant listing.
        /// </summary>
        private async Task PurgeLegacyBlobsAsync(List<PurgeScope> scopes, PurgeScope unowned, CancellationToken cancellationToken)
        {
            var dueScopes = scopes.Where(s => s.Decision.IsDue).ToList();

            List<BlobListItem> blobs;
            try
            {
                blobs = await _blobStorageProvider.ListBlobsAsync(ChatImageBlobNames.ContainerName, string.Empty, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Legacy blobs of every due scope are unknown, so none of them is recorded as done.
                _logger.LogWarning(ex, "PurgeChatImages: failed to list chat images with legacy names.");
                foreach (var scope in dueScopes)
                    scope.Failed++;
                return;
            }

            // Lowercase "D" Guids compared ordinally, exactly like the (case-sensitive) tenant prefix listing: a name
            // with an uppercase Guid is not found by that listing, so it must not be skipped here either.
            var tenantSegments = scopes
                .Where(s => s.TenantGuid is { } guid && guid != Guid.Empty)
                .Select(s => s.TenantGuid.Value.ToString("D"))
                .ToHashSet(StringComparer.Ordinal);

            var sessionBlobs = new List<(BlobListItem Blob, string SessionId)>();
            var unownedBlobs = new List<BlobListItem>();

            foreach (var blob in blobs)
            {
                var segments = blob.Name.Split('/');

                // Anything under a known tenant prefix, at any depth (a client-supplied session id can contain '/'),
                // was handled by that tenant's prefix listing with its own cutoff.
                if (segments.Length >= 2 && tenantSegments.Contains(segments[0]))
                    continue;

                if (segments.Length == 2 && segments[0].Length > 0 && segments[0] != ChatImageBlobNames.EditedFolder)
                    sessionBlobs.Add((blob, segments[0]));
                else
                    unownedBlobs.Add(blob);
            }

            var tenantIdsBySession = await GetTenantIdsBySessionAsync(sessionBlobs.Select(b => b.SessionId).Distinct().ToList(), cancellationToken);
            var scopeByTenantId = scopes.Where(s => s.TenantId is not null).ToDictionary(s => s.TenantId.Value);

            var mapped = 0;
            foreach (var (blob, sessionId) in sessionBlobs)
            {
                var candidates = tenantIdsBySession.TryGetValue(sessionId, out var tenantIds)
                    ? tenantIds.Where(scopeByTenantId.ContainsKey).Select(id => scopeByTenantId[id]).ToList()
                    : [];

                if (candidates.Count == 0)
                {
                    unownedBlobs.Add(blob);
                    continue;
                }

                mapped++;
                var dueCandidates = candidates.Where(s => s.Decision.IsDue).ToList();
                if (dueCandidates.Count == 0)
                    continue;

                // Most conservative cutoff: the EARLIEST start of the current local day among the candidate tenants, so
                // the blob is deleted only when it is from before "today" for every tenant that may own it. Evaluated
                // whenever any of them is due, so it is still purged at most about a day after the last one's midnight.
                var cutoff = candidates.Min(s => s.Decision.CutoffUtc);
                var owner = dueCandidates.MaxBy(s => s.Decision.CutoffUtc);
                await DeleteIfBeforeCutoffAsync(blob, cutoff, owner, dueCandidates, isLegacy: true, cancellationToken);
            }

            if (unowned.Decision.IsDue)
            {
                foreach (var blob in unownedBlobs)
                    await DeleteIfBeforeCutoffAsync(blob, unowned.Decision.CutoffUtc, unowned, [unowned], isLegacy: true, cancellationToken);
            }

            _logger.LogInformation("PurgeChatImages: legacy names: {SessionMapped} mapped to a tenant by session, {Unowned} with no tenant.",
                mapped, unownedBlobs.Count);
        }

        private async Task<Dictionary<string, HashSet<int>>> GetTenantIdsBySessionAsync(List<string> sessionIds, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

            foreach (var batch in sessionIds.Chunk(SessionLookupBatchSize))
            {
                var rows = await _db.Conversations
                    .AsNoTracking()
                    .Where(c => batch.Contains(c.SessionId))
                    .Select(c => new { c.SessionId, c.TenantId })
                    .ToListAsync(cancellationToken);

                foreach (var row in rows)
                {
                    if (!result.TryGetValue(row.SessionId, out var tenantIds))
                        result[row.SessionId] = tenantIds = [];
                    tenantIds.Add(row.TenantId);
                }
            }

            return result;
        }

        /// <param name="owner">The scope the outcome is counted on.</param>
        /// <param name="failScopes">The scopes that must not record their day when the delete fails (retried next tick).</param>
        private async Task DeleteIfBeforeCutoffAsync(BlobListItem blob, DateTimeOffset cutoffUtc, PurgeScope owner,
            IReadOnlyList<PurgeScope> failScopes, bool isLegacy, CancellationToken cancellationToken)
        {
            // Images created on/after the cutoff (today, in the tenant's time zone) are kept until the next midnight;
            // so is a blob with no creation time, since its age is unknown.
            if (blob.CreatedOn is not { } createdOn || createdOn >= cutoffUtc)
            {
                owner.Kept++;
                return;
            }

            try
            {
                await _blobStorageProvider.DeleteAsync(ChatImageBlobNames.ContainerName, blob.Name, cancellationToken);
                owner.Deleted++;
                if (isLegacy)
                    owner.LegacyDeleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // No blob name: it contains the session id and the user's original file name.
                _logger.LogWarning(ex, "PurgeChatImages: failed to delete a chat image of {Scope} (legacy name: {IsLegacy}).",
                    owner.TenantId is { } id ? $"tenant {id}" : "no-tenant scope", isLegacy);
                foreach (var scope in failScopes)
                    scope.Failed++;
            }
        }

        /// <summary>
        /// Records that <paramref name="tenantId"/> (null = the no-tenant scope) was purged for <paramref name="localDate"/>.
        /// True when this call recorded it; false when the day (or a later one) was already recorded, e.g. by another
        /// replica.
        /// </summary>
        private async Task<bool> TryRecordPurgeAsync(int? tenantId, DateOnly localDate, CancellationToken cancellationToken)
        {
            var now = _dateTimeProvider.DateTimeOffsetUtcNow();

            var updated = await _db.ChatImagePurgeStates
                .Where(p => p.TenantId == tenantId && p.LastPurgeLocalDate < localDate)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.LastPurgeLocalDate, localDate)
                    .SetProperty(p => p.LastUpdatedOn, now), cancellationToken);

            if (updated > 0)
                return true;

            if (await _db.ChatImagePurgeStates.AnyAsync(p => p.TenantId == tenantId, cancellationToken))
                return false;

            var state = new ChatImagePurgeState { TenantId = tenantId, LastPurgeLocalDate = localDate };
            _db.ChatImagePurgeStates.Add(state);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex)
            {
                _db.Entry(state).State = EntityState.Detached;

                if (IsUniqueKeyViolation(ex))
                {
                    // Another run inserted the row first (unique TenantId): the day is recorded either way.
                    _logger.LogInformation("PurgeChatImages: purge of {LocalDate:yyyy-MM-dd} was already recorded by another run.", localDate);
                    return false;
                }

                // Anything else is a real failure: the day stays unrecorded, so the next tick purges and records again.
                _logger.LogWarning(ex, "PurgeChatImages: failed to record the purge of {LocalDate:yyyy-MM-dd}; it is retried on the next tick.", localDate);
                return false;
            }
        }

        /// <summary>SQL Server error numbers for a duplicate key: 2627 (UNIQUE/PRIMARY KEY constraint) and 2601 (unique index).</summary>
        public static bool IsUniqueKeyViolationNumber(int sqlErrorNumber) => sqlErrorNumber is 2627 or 2601;

        /// <summary>Whether <paramref name="exception"/> or one of its inner exceptions is a SQL Server duplicate-key error.</summary>
        public static bool IsUniqueKeyViolation(Exception exception)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                if (current is SqlException sqlException && IsUniqueKeyViolationNumber(sqlException.Number))
                    return true;
            }

            return false;
        }

        private sealed class PurgeScope(int? tenantId, Guid? tenantGuid, TenantMidnightDecision decision)
        {
            public int? TenantId { get; } = tenantId;
            public Guid? TenantGuid { get; } = tenantGuid;
            public TenantMidnightDecision Decision { get; } = decision;
            public int Deleted { get; set; }
            public int LegacyDeleted { get; set; }
            public int Kept { get; set; }
            public int Failed { get; set; }
        }
    }
}
