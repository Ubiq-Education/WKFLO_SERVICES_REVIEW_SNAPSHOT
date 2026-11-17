using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Attributes;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Extensions;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AMAIS.WKFLO.Common.MD.Domain.Data
{
    public class MasterDataDbContext : DbContext, IMasterDataDbContext
    {
        private readonly IDateTimeProvider _dateTimeProvider;

        public MasterDataDbContext(DbContextOptions<MasterDataDbContext> options, IDateTimeProvider dateTimeProvider)
         : base(options)
        {
            _dateTimeProvider = dateTimeProvider;
        }

        public virtual DbSet<Tenant> Tenants { get; set; }

        public virtual DbSet<Agent> Agents { get; set; }
        public virtual DbSet<MarketplaceAgent> MarketplaceAgents { get; set; }
        public virtual DbSet<TenantAgent> TenantAgents { get; set; }
        public virtual DbSet<PublishedTenantAgent> PublishedTenantAgents { get; set; }
        public virtual DbSet<MarketplaceTool> MarketplaceTools { get; set; }
        public virtual DbSet<TenantTool> TenantTools { get; set; }
        public virtual DbSet<PublishedTenantTool> PublishedTenantTools { get; set; }

        public virtual DbSet<AgentTool> AgentTools { get; set; }
        public virtual DbSet<AgentChatSetting> AgentChatSettings { get; set; }
        public virtual DbSet<PredefinedPrompt> PredefinedPrompts { get; set; }

        public virtual DbSet<AIModel> AIModels { get; set; }
        public virtual DbSet<AICredit> AICredits { get; set; }
        public virtual DbSet<Secret> Secrets { get; set; }
        public virtual DbSet<ToolOAuthToken> ToolOAuthTokens { get; set; }
        public virtual DbSet<McpOAuthPendingFlow> McpOAuthPendingFlows { get; set; }

        public virtual DbSet<WebOrigin> WebOrigins { get; set; }

        public virtual DbSet<Conversation> Conversations { get; set; }
        public virtual DbSet<ConversationAnalysis> ConversationAnalyses { get; set; }

        public virtual DbSet<UserStar> UserStars { get; set; }
        public virtual DbSet<UserInstall> UserInstalls { get; set; }

        public virtual DbSet<User> Users { get; set; }

        public virtual DbSet<MessageTopicClassification> MessageTopicClassifications { get; set; }

        public virtual DbSet<ClassificationJob> ClassificationJobs { get; set; }

        public virtual DbSet<Tag> Tags { get; set; }
        public virtual DbSet<EntityTag> EntityTags { get; set; }

        public virtual DbSet<AgentAllowedSpinUpAgent> AgentAllowedSpinUpAgents { get; set; }

        public virtual DbSet<DxpIntegration> DxpIntegrations { get; set; }

        public virtual DbSet<TenantSetting> TenantSettings { get; set; }
        public virtual DbSet<SuperPowerUser> SuperPowerUsers { get; set; }
        public virtual DbSet<TenantAccess> TenantAccesses { get; set; }

        public virtual DbSet<ChatImagePurgeState> ChatImagePurgeStates { get; set; }

        public virtual DbSet<ContactFormPluginLog> ContactFormPluginLogs { get; set; }

        public virtual DbSet<AuditLogEntry> AuditLogEntries { get; set; }

        // Current request's tenant/actor identity for the audit trail, set via SetAuditActor (never via the
        // constructor — this context is pooled via AddDbContextPool, so constructor-injected scoped services
        // would be captured once per pooled instance and go stale across requests/tenants). See
        // AuditActorContextResolver middleware for where this is populated.
        private string _auditTenantGuid;
        private string _auditActorUserGuid;
        private string _auditActorUserName;
        private string _auditActorUserEmail;
        private string _auditCorrelationId;

        public void SetAuditActor(string tenantGuid, string actorUserGuid, string actorUserName, string actorUserEmail, string correlationId)
        {
            _auditTenantGuid = tenantGuid;
            _auditActorUserGuid = actorUserGuid;
            _auditActorUserName = actorUserName;
            _auditActorUserEmail = actorUserEmail;
            _auditCorrelationId = correlationId;
        }

        private void ClearAuditActor()
        {
            _auditTenantGuid = null;
            _auditActorUserGuid = null;
            _auditActorUserName = null;
            _auditActorUserEmail = null;
            _auditCorrelationId = null;
        }

        /// <summary>
        /// Clears the audit actor fields before returning this instance to the pool.
        /// </summary>
        /// <remarks>
        /// This is the fix for a real cross-tenant leak: <see cref="SetAuditActor"/> is called once per
        /// request/scope by <c>AuditActorContextResolver</c>, but with <c>AddDbContextPool</c> the same
        /// physical <see cref="MasterDataDbContext"/> object is handed out to a DIFFERENT, later scope once
        /// this one ends — including non-HTTP callers (e.g. <c>ClassificationJobSchedulerActor</c>, which
        /// resolves <see cref="IMasterDataDbContext"/> via its own <c>IServiceProvider.CreateScope()</c> and
        /// never calls <see cref="SetAuditActor"/> at all). Without clearing here, that next caller's
        /// <c>SaveChangesAsync</c> would silently reuse whatever tenant/actor was stamped by the PREVIOUS
        /// occupant of this pooled instance, attributing its audit rows to the wrong tenant — a cross-tenant
        /// data leak visible through GET /api/v1/tenant/audit itself.
        ///
        /// <c>Dispose()</c>/<c>DisposeAsync()</c> are the correct hook: EF Core calls one of them exactly once
        /// per scope when the DI container disposes the scoped <see cref="IMasterDataDbContext"/> — internally
        /// this is what returns a pooled context to the pool rather than actually destroying it — so clearing
        /// state here guarantees every later rental (by any caller, HTTP or not) starts with a clean slate.
        /// A caller that never calls <see cref="SetAuditActor"/> for its own scope then correctly hits the
        /// existing null-guard in <see cref="AppendAuditLogEntries"/> and skips audit-entry generation
        /// entirely, rather than attributing to a stale tenant.
        /// </remarks>
        public override void Dispose()
        {
            ClearAuditActor();
            base.Dispose();
        }

        /// <inheritdoc cref="Dispose()"/>
        public override async ValueTask DisposeAsync()
        {
            ClearAuditActor();
            await base.DisposeAsync();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.ConfigureWarnings(w => w.Ignore(
                CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MarketplaceTool>().HasOne(m => m.Tenant);
            modelBuilder.Entity<TenantTool>().HasOne(m => m.Tenant);
            modelBuilder.Entity<PublishedTenantTool>().HasOne(m => m.Tenant);

            modelBuilder.Entity<MarketplaceAgent>().HasOne(m => m.Tenant);
            modelBuilder.Entity<TenantAgent>().HasOne(m => m.Tenant);
            modelBuilder.Entity<PublishedTenantAgent>().HasOne(m => m.Tenant);

            modelBuilder.Entity<MarketplaceTool>()
                .HasOne(m => m.SourcePublishedTool)
                .WithOne(m => m.MarketplaceTool)
                .HasForeignKey<PublishedTenantTool>(m => m.MarketplaceToolId)
                .IsRequired(false);

            modelBuilder.Entity<MarketplaceAgent>()
                .HasOne(m => m.SourcePublishedAgent)
                .WithOne(m => m.MarketplaceAgent)
                .HasForeignKey<PublishedTenantAgent>(m => m.MarketplaceAgentId)
                .IsRequired(false);

            modelBuilder.Entity<TenantTool>()
                .HasOne(m => m.PublishedTenantTool)
                .WithOne(m => m.CreatedByTenantTool)
                .HasForeignKey<PublishedTenantTool>(m => m.CreatedByTenantToolId)
                .IsRequired(false);

            modelBuilder.Entity<TenantAgent>()
                .HasOne(m => m.PublishedTenantAgent)
                .WithOne(m => m.CreatedByTenantAgent)
                .HasForeignKey<PublishedTenantAgent>(m => m.CreatedByTenantAgentId)
                .IsRequired(false);

            // NEW, independent relationship from MarketplaceAgent/SourcePublishedAgent above — that
            // one tracks the SHARE direction (which PublishedTenantAgent was shared to create a
            // MarketplaceAgent). This one tracks the INSTALL direction (which MarketplaceAgent a
            // TenantAgent/PublishedTenantAgent was installed from) and is one-to-many, since many
            // tenants (and their published snapshots) can install the same MarketplaceAgent.
            modelBuilder.Entity<Agent>()
                .HasOne(m => m.InstalledFromMarketplaceAgent)
                .WithMany()
                .HasForeignKey(m => m.InstalledFromMarketplaceAgentId)
                .IsRequired(false);

            modelBuilder.Entity<Tool>()
                .HasOne(m => m.InstalledFromMarketplaceTool)
                .WithMany()
                .HasForeignKey(m => m.InstalledFromMarketplaceToolId)
                .IsRequired(false);

            modelBuilder.Entity<AgentChatSetting>()
                .HasOne(c => c.Agent)
                .WithMany()
                .HasForeignKey(c => c.AgentId);

            modelBuilder.Entity<Agent>().HasQueryFilter(m => !m.IsDeleted);
            modelBuilder.Entity<Tool>().HasQueryFilter(m => !m.IsDeleted);
            modelBuilder.Entity<AgentChatSetting>().HasQueryFilter(m => !m.IsDeleted);
            modelBuilder.Entity<PredefinedPrompt>().HasQueryFilter(m => !m.IsDeleted);

            modelBuilder.Entity<Secret>().HasQueryFilter(m => !m.IsDeleted);
            modelBuilder.Entity<AIModel>().HasQueryFilter(m => !m.IsDeleted);
            modelBuilder.Entity<AICredit>().HasQueryFilter(m => !m.IsDeleted);
            modelBuilder.Entity<User>().HasQueryFilter(m => !m.IsDeleted);
            modelBuilder.Entity<Tag>().HasQueryFilter(m => !m.IsDeleted);

            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyAllConfigurationsFromCurrentAssembly();
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Must run BEFORE the stamping loop below: AppendAuditLogEntries adds new AuditLogEntry rows to
            // the ChangeTracker (via AuditLogEntries.AddRange), and AuditLogEntry is itself an IEntity whose
            // own CreatedOn needs stamping too. If this ran after the loop instead, the loop would never see
            // the newly-added audit rows and AuditLogEntry.CreatedOn would stay at default(DateTimeOffset)
            // forever (a real, previously-shipped bug). Running it first means the loop — unchanged — simply
            // re-enumerates a now-larger tracker and picks the audit rows up along with everything else.
            // BuildChangesJson already excludes CreatedOn/LastUpdatedOn from diffs via BookkeepingPropertyNames
            // regardless of stamping order, so this reordering has no effect on diff content.
            var pendingCreatePatches = AppendAuditLogEntries();

            foreach (var entry in ChangeTracker.Entries<IEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedOn = _dateTimeProvider.DateTimeOffsetUtcNow();
                        break;

                    case EntityState.Modified:
                        entry.Entity.LastUpdatedOn = _dateTimeProvider.DateTimeOffsetUtcNow();
                        break;
                }
            }

            var result = await base.SaveChangesAsync(cancellationToken);

            await PatchCreateAuditEntityIdsAsync(pendingCreatePatches, cancellationToken);

            return result;
        }

        public override int SaveChanges()
        {
            // See the async overload's remarks for why this must run before the stamping loop.
            var pendingCreatePatches = AppendAuditLogEntries();

            foreach (var entry in ChangeTracker.Entries<IEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedOn = _dateTimeProvider.DateTimeOffsetUtcNow();
                        break;

                    case EntityState.Modified:
                        entry.Entity.LastUpdatedOn = _dateTimeProvider.DateTimeOffsetUtcNow();
                        break;
                }
            }

            var result = base.SaveChanges();

            PatchCreateAuditEntityIds(pendingCreatePatches);

            return result;
        }

        /// <summary>
        /// Pairs a Create <see cref="AuditLogEntry"/> built by <see cref="AppendAuditLogEntries"/> with the
        /// <see cref="EntityEntry"/> it was built from, so <see cref="PatchCreateAuditEntityIdsAsync"/>
        /// can correct <see cref="AuditLogEntry.EntityId"/> once the source entity's real,
        /// database-generated Id exists (it doesn't yet at the point <see cref="AppendAuditLogEntries"/> runs
        /// — see that method's remarks).
        /// </summary>
        private readonly record struct PendingCreateAuditIdPatch(AuditLogEntry AuditEntry, EntityEntry SourceEntry);

        /// <summary>
        /// Builds one <see cref="AuditLogEntry"/> per Added/Modified/Deleted tracked entity (excluding
        /// <see cref="AuditLogEntry"/> itself, to avoid self-referential audit rows) and adds them to
        /// <see cref="AuditLogEntries"/> so they're persisted in the same SaveChanges call as the change that
        /// triggered them.
        /// </summary>
        /// <remarks>
        /// Iterates <c>ChangeTracker.Entries()</c> (every tracked entity), not just <see cref="IEntity"/> ones
        /// — some domain entities are plain POCOs with no base class at all, e.g. <see cref="EntityTag"/>
        /// (composite key <c>TagId</c>/<c>EntityType</c>/<c>EntityId</c>, tag assign/unassign),
        /// <see cref="AgentTool"/> and <see cref="AgentAllowedSpinUpAgent"/> (composite-key junction rows), and
        /// <see cref="McpOAuthPendingFlow"/> (single-column <c>State</c> key) — these were structurally
        /// invisible to the audit trail entirely under the old <c>ChangeTracker.Entries&lt;IEntity&gt;()</c>
        /// iteration, not because of any timing issue but because they simply don't implement the interface.
        /// <see cref="ResolveAuditAction"/> and <see cref="BuildChangesJson"/> already work generically off
        /// <see cref="EntityEntry"/>'s <c>State</c>/<c>Properties</c>/<c>Metadata</c> (never touched
        /// <see cref="IEntity"/> members directly), so only <see cref="GetEntityId"/> needed a non-IEntity
        /// fallback (EF Core's own primary-key metadata) to make this generalize automatically to any other
        /// non-<see cref="IEntity"/> entity, present or future, without special-casing each one's key
        /// property names here. <see cref="IEntity"/> entries behave identically to before this change.
        /// </remarks>
        /// <returns>
        /// One <see cref="PendingCreateAuditIdPatch"/> per Create action recorded, or <see langword="null"/>
        /// if no request-scoped audit actor is set or audit-entry construction failed. For a Create, the
        /// entity's real Id doesn't exist yet at this point — see <see cref="PatchCreateAuditEntityIdsAsync"/>,
        /// which must be called with this return value after <c>base.SaveChangesAsync</c>/<c>SaveChanges</c>
        /// to correct <see cref="AuditLogEntry.EntityId"/> once it does.
        /// </returns>
        /// <remarks>
        /// Deliberately swallows all exceptions: the audit trail must never block or fail the original
        /// CRUD operation (per acceptance criteria). If audit-entry construction fails for any reason, the
        /// original save proceeds without new audit rows.
        /// </remarks>
        private List<PendingCreateAuditIdPatch> AppendAuditLogEntries()
        {
            // TenantGuid is a required column. If no request-scoped audit actor has been set for this
            // context instance (e.g. a DAPR pub/sub handler or another service that writes through
            // IMasterDataDbContext without going through AuditActorContextResolver), skip entirely rather
            // than writing a row that would fail the NOT NULL constraint and, in turn, the original save.
            if (string.IsNullOrWhiteSpace(_auditTenantGuid))
                return null;

            try
            {
                var auditEntries = new List<AuditLogEntry>();
                var pendingCreatePatches = new List<PendingCreateAuditIdPatch>();

                foreach (var entry in ChangeTracker.Entries())
                {
                    if (entry.Entity is AuditLogEntry)
                        continue;

                    var action = ResolveAuditAction(entry);
                    if (action is null)
                        continue;

                    // A required column — an entity with no discoverable primary key (shouldn't happen for
                    // any real, mapped entity type, but GetEntityId returns null defensively rather than
                    // throwing) would otherwise build a row that fails to insert outside this try/catch, at
                    // base.SaveChangesAsync time — skip it here instead so that can never happen.
                    var entityId = GetEntityId(entry);
                    if (entityId is null)
                        continue;

                    string changes = action switch
                    {
                        CommonConsts.AuditActionConsts.Create => BuildChangesJson(entry, isCreate: true),
                        CommonConsts.AuditActionConsts.Update => BuildChangesJson(entry, isCreate: false),
                        _ => null // Delete: the entity is going away, no diff to show.
                    };

                    var auditEntry = new AuditLogEntry
                    {
                        TenantGuid = _auditTenantGuid,
                        EntityType = entry.Entity.GetType().Name,
                        // For a Create, this is necessarily "0" (or, for a composite key, contains a "0" /
                        // temporary-placeholder component) here — the database hasn't assigned the real,
                        // generated key yet. Corrected post-save via the pending-patch list returned below;
                        // see PatchCreateAuditEntityIdsAsync.
                        EntityId = entityId,
                        Action = action,
                        ActorUserGuid = _auditActorUserGuid,
                        ActorUserName = _auditActorUserName,
                        ActorUserEmail = _auditActorUserEmail,
                        Timestamp = _dateTimeProvider.DateTimeOffsetUtcNow(),
                        Changes = changes,
                        CorrelationId = _auditCorrelationId
                    };

                    auditEntries.Add(auditEntry);

                    if (action == CommonConsts.AuditActionConsts.Create)
                    {
                        pendingCreatePatches.Add(new PendingCreateAuditIdPatch(auditEntry, entry));
                    }
                }

                if (auditEntries.Count > 0)
                {
                    AuditLogEntries.AddRange(auditEntries);
                }

                return pendingCreatePatches;
            }
            catch
            {
                // Never let audit-trail construction fail the original CRUD operation.
                return null;
            }
        }

        /// <summary>
        /// Computes the audit-log <see cref="AuditLogEntry.EntityId"/> for a tracked entry: the entity's own
        /// <see cref="IEntity.GetEntityId"/> when it implements that interface (unchanged from before), or —
        /// for a plain POCO entity with no single int Id of its own, e.g. a composite-key junction row like
        /// <see cref="EntityTag"/> — a <c>:</c>-joined string of its primary key property values, read via EF
        /// Core's own metadata (<c>entry.Metadata.FindPrimaryKey()</c>) rather than hardcoding any particular
        /// entity's key property names, e.g. <c>"5:Agent:42"</c> for EntityTag's
        /// (<c>TagId</c>, <c>EntityType</c>, <c>EntityId</c>) key. Generalizes automatically to any other
        /// non-<see cref="IEntity"/> entity without changes here.
        /// </summary>
        private static string GetEntityId(EntityEntry entry)
        {
            if (entry.Entity is IEntity identifiableEntity)
                return identifiableEntity.GetEntityId();

            var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
            if (keyProperties is null || keyProperties.Count == 0)
                return null;

            return string.Join(":", keyProperties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "null"));
        }

        /// <summary>
        /// Corrects <see cref="AuditLogEntry.EntityId"/> for every Create audit row <see cref="AppendAuditLogEntries"/>
        /// built for this save, now that <c>base.SaveChangesAsync</c> has run and EF Core's key fixup has
        /// propagated each source entity's real, database-generated Id back onto its tracked CLR instance —
        /// then persists just that correction.
        /// </summary>
        /// <remarks>
        /// Calls <c>base.SaveChangesAsync</c> directly rather than <c>this.SaveChangesAsync</c>: this
        /// follow-up write is purely an internal correction to rows already inserted moments ago, not a new
        /// business change, so it must not re-run the CreatedOn/LastUpdatedOn stamping loop or
        /// <see cref="AppendAuditLogEntries"/> again — doing so would, harmlessly but confusingly, stamp a
        /// <c>LastUpdatedOn</c> on an <see cref="AuditLogEntry"/> row that, from a human's perspective, was
        /// just created a moment ago, not "updated". Wrapped in the same swallow-all-exceptions pattern as
        /// the rest of the audit trail: never let this correction fail the original CRUD operation — worst
        /// case, the affected rows simply keep their "0" <see cref="AuditLogEntry.EntityId"/> fallback.
        /// </remarks>
        private async Task PatchCreateAuditEntityIdsAsync(List<PendingCreateAuditIdPatch> pendingPatches, CancellationToken cancellationToken)
        {
            if (pendingPatches is null || pendingPatches.Count == 0)
                return;

            try
            {
                var patchedAny = false;

                foreach (var pending in pendingPatches)
                {
                    var realEntityId = GetEntityId(pending.SourceEntry);
                    if (realEntityId is not null && pending.AuditEntry.EntityId != realEntityId)
                    {
                        pending.AuditEntry.EntityId = realEntityId;
                        patchedAny = true;
                    }
                }

                if (patchedAny)
                {
                    await base.SaveChangesAsync(cancellationToken);
                }
            }
            catch
            {
                // Never let this follow-up correction fail the original CRUD operation.
            }
        }

        /// <inheritdoc cref="PatchCreateAuditEntityIdsAsync"/>
        private void PatchCreateAuditEntityIds(List<PendingCreateAuditIdPatch> pendingPatches)
        {
            if (pendingPatches is null || pendingPatches.Count == 0)
                return;

            try
            {
                var patchedAny = false;

                foreach (var pending in pendingPatches)
                {
                    var realEntityId = GetEntityId(pending.SourceEntry);
                    if (realEntityId is not null && pending.AuditEntry.EntityId != realEntityId)
                    {
                        pending.AuditEntry.EntityId = realEntityId;
                        patchedAny = true;
                    }
                }

                if (patchedAny)
                {
                    base.SaveChanges();
                }
            }
            catch
            {
                // Never let this follow-up correction fail the original CRUD operation.
            }
        }

        /// <summary>
        /// Maps an entry's EF <see cref="EntityState"/> to an audit action. A soft-delete — an
        /// <c>IsDeleted</c> flip from false to true on an otherwise-<c>Modified</c> entry — is reported as
        /// <see cref="CommonConsts.AuditActionConsts.Delete"/> rather than
        /// <see cref="CommonConsts.AuditActionConsts.Update"/>, since most master data entities are
        /// soft-deleted rather than removed from the change tracker.
        /// </summary>
        private static string ResolveAuditAction(EntityEntry entry)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    return CommonConsts.AuditActionConsts.Create;

                case EntityState.Deleted:
                    return CommonConsts.AuditActionConsts.Delete;

                case EntityState.Modified:
                    var isDeletedProperty = entry.Metadata.FindProperty("IsDeleted");
                    if (isDeletedProperty is not null)
                    {
                        var isDeletedEntry = entry.Property(isDeletedProperty.Name);
                        if (isDeletedEntry.IsModified
                            && isDeletedEntry.CurrentValue is true
                            && isDeletedEntry.OriginalValue is false)
                        {
                            return CommonConsts.AuditActionConsts.Delete;
                        }
                    }

                    return CommonConsts.AuditActionConsts.Update;

                default:
                    return null;
            }
        }

        /// <summary>Fixed value written in place of the real old/new values for a property decorated with
        /// <see cref="SensitiveAuditFieldAttribute"/> — the diff still records that the field changed, just
        /// not what it changed to/from.</summary>
        private const string SensitiveValueMask = "*******";

        /// <summary>Bookkeeping properties every <see cref="IEntity"/> carries that are stamped on every save
        /// regardless of what the caller actually changed (see the top of <see cref="SaveChangesAsync"/>) —
        /// always excluded from the diff, since including them would flag every single Update as having
        /// "changed" <see cref="IEntity.LastUpdatedOn"/> even when nothing else did.</summary>
        private static readonly HashSet<string> BookkeepingPropertyNames =
        [
            nameof(IEntity.CreatedOn),
            nameof(IEntity.LastUpdatedOn)
        ];

        /// <summary>
        /// Serializes the properties worth recording for an audit diff as {PropertyName: {old, new}} JSON,
        /// redacting any property decorated with <see cref="SensitiveAuditFieldAttribute"/> to a fixed mask.
        /// Shared by both Create and Update — the only difference between them is what counts as "a change
        /// worth recording": for an Update that's a genuinely modified property (old ≠ new); for a Create
        /// there's no meaningful "old" value at all, so instead it's any property the caller actually set to
        /// something other than null/default, recorded as <c>{old: null, new: &lt;initial value&gt;}</c> so the
        /// row shows what the entity was created with. Untouched/default-valued columns on a Create are
        /// skipped — recording <c>{Field: {old: null, new: null}}</c> or <c>{..., new: 0}}</c> for every
        /// column the caller never set adds noise, not information.
        /// </summary>
        /// <param name="isCreate">
        /// <see langword="true"/> for a Create diff (property.CurrentValue vs. "nothing"); <see langword="false"/>
        /// for an Update diff (property.CurrentValue vs. property.OriginalValue).
        /// </param>
        /// <remarks>
        /// Skips any property whose <see cref="PropertyEntry.IsTemporary"/> flag is set, for BOTH Create and
        /// Update. This matters because <see cref="AppendAuditLogEntries"/> reads <c>ChangeTracker.Entries()</c>
        /// — which forces a <c>DetectChanges()</c> pass — before <c>base.SaveChangesAsync</c> actually
        /// persists anything. If that <c>DetectChanges()</c> call lands mid one-to-one relationship fixup
        /// (e.g. assigning a navigation to a principal whose own key isn't resolved yet, such as a
        /// newly-<c>Added</c> entity with a database-generated key), EF Core can assign the DEPENDENT's FK
        /// property a temporary placeholder value — a huge negative int near <see cref="int.MinValue"/> — as
        /// bookkeeping, to be replaced with the real value once the principal's insert actually resolves it.
        /// That placeholder is never meant to be observed outside EF's own internals; a naive diff captures it
        /// into a permanent audit row (real regression: sharing a tool wrote
        /// <c>{"MarketplaceToolId":{"old":null,"new":-2147482647}}</c> for a column whose real, final DB value
        /// never actually changed). On Create specifically, this also guards a newly-Added entity's own
        /// not-yet-resolved database-generated Id (or any other FK pointing at another not-yet-saved entity in
        /// the same graph) — those are exactly the properties most likely to carry a temporary placeholder
        /// at this point. This bug class can recur for any FK/relationship-fixup scenario, not just the one
        /// that surfaced it, so the check is unconditional here rather than special-cased to a particular
        /// entity/property.
        /// </remarks>
        private static string BuildChangesJson(EntityEntry entry, bool isCreate)
        {
            var changes = new Dictionary<string, object>();

            foreach (var property in entry.Properties)
            {
                if (BookkeepingPropertyNames.Contains(property.Metadata.Name))
                    continue;

                // A temporary placeholder is never a meaningful value to a human reading the audit trail —
                // skip the property entirely rather than risk recording either a bogus old or new value.
                if (property.IsTemporary)
                    continue;

                object oldValue;
                object newValue;

                if (isCreate)
                {
                    if (IsDefaultOrNull(property.CurrentValue, property.Metadata.ClrType))
                        continue;

                    oldValue = null;
                    newValue = property.CurrentValue;
                }
                else
                {
                    if (!property.IsModified || Equals(property.OriginalValue, property.CurrentValue))
                        continue;

                    oldValue = property.OriginalValue;
                    newValue = property.CurrentValue;
                }

                var isSensitive = property.Metadata.PropertyInfo?.GetCustomAttribute<SensitiveAuditFieldAttribute>() is not null;

                changes[property.Metadata.Name] = isSensitive
                    ? new { old = SensitiveValueMask, @new = SensitiveValueMask }
                    : new { old = RedactClientSecrets(oldValue), @new = RedactClientSecrets(newValue) };
            }

            return changes.Count > 0 ? JsonSerializer.Serialize(changes) : null;
        }

        /// <summary>
        /// US 7849: an MCP tool's Config can hold an OAuth client secret (encrypted, or plaintext on rows saved
        /// before encryption existed) — mask it in the audit trail either way.
        /// </summary>
        private static object RedactClientSecrets(object value) =>
            value is string text ? McpClientSecretHelper.RedactSecrets(text) : value;

        /// <summary>
        /// True when <paramref name="value"/> is null, or is the CLR default for <paramref name="clrType"/>
        /// (0/false/etc., unwrapping a nullable type first) — used to decide which properties are worth
        /// recording on a Create diff. Deliberately simple: a property explicitly set to a value that happens
        /// to equal its type's default (e.g. a brand-new <c>Agent.TenantId = 0</c>) is treated the same
        /// as one never touched at all — an accepted trade-off for keeping Create rows compact and readable
        /// rather than trying to distinguish "explicitly set to the default" from "never set."
        /// </summary>
        private static bool IsDefaultOrNull(object value, Type clrType)
        {
            if (value is null)
                return true;

            var underlyingType = Nullable.GetUnderlyingType(clrType) ?? clrType;
            if (!underlyingType.IsValueType)
                return false; // non-null reference-typed value (e.g. a non-empty string) is meaningful

            return Equals(value, Activator.CreateInstance(underlyingType));
        }
    }
}
