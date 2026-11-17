using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Data
{
    /// <summary>
    /// Exercises <see cref="MasterDataDbContext"/>'s audit-trail logic directly (real SQLite, real
    /// SaveChangesAsync) rather than through a mocked <c>IMasterDataDbContext</c> — this is where the audit
    /// building/masking/lifecycle logic actually lives, so a mock can't verify it.
    /// </summary>
    public class MasterDataDbContextAuditTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private static async Task<(SqliteConnection Connection, DbContextOptions<MasterDataDbContext> Options)> CreateSchemaAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            return (connection, options);
        }

        private static IDateTimeProvider BuildDateTimeProvider()
        {
            var mock = new Mock<IDateTimeProvider>();
            mock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);
            return mock.Object;
        }

        [Fact]
        public async Task SaveChangesAsync_WithAuditActorSet_WritesAuditLogEntry_ForCreate()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            await using var db = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider());
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");

            db.Secrets.Add(new Secret { Name = "s1", ApiKey = "key-1", IsActive = true });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await db.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Secret", entry.EntityType);
            Assert.Equal(CommonConsts.AuditActionConsts.Create, entry.Action);
            Assert.Equal("tenant-guid-1", entry.TenantGuid);
            Assert.Equal("user-1", entry.ActorUserGuid);
            Assert.Equal("Alice", entry.ActorUserName);
            Assert.Equal("alice@test.com", entry.ActorUserEmail);
            Assert.Equal("corr-1", entry.CorrelationId);

            // Create now records the initial values (old: null, new: <initial value>) rather than a null diff.
            Assert.NotNull(entry.Changes);
            Assert.Contains("\"Name\"", entry.Changes);
            Assert.Contains("\"old\":null", entry.Changes);
            Assert.Contains("\"s1\"", entry.Changes);
            Assert.Contains("\"IsActive\"", entry.Changes);

            // Sensitive field ([SensitiveAuditField] on Secret.ApiKey): masked on Create too, real value never leaked.
            Assert.Contains("\"ApiKey\"", entry.Changes);
            Assert.DoesNotContain("key-1", entry.Changes);
            Assert.Contains("*******", entry.Changes);
        }

        [Fact]
        public async Task SaveChangesAsync_Create_SkipsDefaultAndNullValuedProperties()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            await using var db = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider());
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");

            // ApiKey left null, IsActive left at its default (false) — neither was "set to something".
            db.Secrets.Add(new Secret { Name = "s1" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await db.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(entry.Changes);
            Assert.Contains("\"Name\"", entry.Changes);
            Assert.DoesNotContain("\"ApiKey\"", entry.Changes);
            Assert.DoesNotContain("\"IsActive\"", entry.Changes);
            Assert.DoesNotContain("\"IsDeleted\"", entry.Changes);
        }

        [Fact]
        public async Task SaveChangesAsync_Create_ExcludesBookkeepingFields_FromInitialValues()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            await using var db = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider());
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");

            // CreatedOn gets stamped to a real, non-default timestamp by SaveChangesAsync's own bookkeeping
            // loop — it must never show up as an "initial value" even though it's non-default at that point.
            db.Secrets.Add(new Secret { Name = "s1" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await db.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("CreatedOn", entry.Changes);
            Assert.DoesNotContain("LastUpdatedOn", entry.Changes);
        }

        [Fact]
        public async Task SaveChangesAsync_Create_WritesRealNonDefaultCreatedOn()
        {
            // Bug: the CreatedOn-stamping loop at the top of SaveChangesAsync used to run BEFORE
            // AppendAuditLogEntries() added the new AuditLogEntry row to the ChangeTracker — so the loop
            // never saw it, and AuditLogEntry.CreatedOn (itself an IEntity property) stayed at
            // default(DateTimeOffset) (0001-01-01) forever.
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            await using var db = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider());
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");

            db.Secrets.Add(new Secret { Name = "s1", ApiKey = "key-1", IsActive = true });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await db.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.NotEqual(default, entry.CreatedOn);
            Assert.Equal(Now, entry.CreatedOn);
        }

        [Fact]
        public async Task SaveChangesAsync_Create_RecordsTheRealGeneratedEntityId_NotZero()
        {
            // Bug: AppendAuditLogEntries() builds EntityId = entry.Entity.GetEntityId() (Id.ToString())
            // BEFORE base.SaveChangesAsync() runs the INSERT — for a newly-Added entity with a
            // database-generated key, Id is still the CLR default 0 at that point, so every Create's audit
            // row permanently recorded EntityId = "0" regardless of the entity's real, final Id.
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            await using var db = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider());
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");

            var secret = new Secret { Name = "s1", ApiKey = "key-1", IsActive = true };
            db.Secrets.Add(secret);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await db.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.NotEqual("0", entry.EntityId);
            Assert.Equal(secret.Id.ToString(), entry.EntityId);
        }

        [Fact]
        public async Task SaveChangesAsync_Create_EntityTagAssign_ProducesAuditEntryWithCompositeEntityId()
        {
            // Bug: EntityTag (tag assign/unassign) is a plain POCO with no base class at all — no IEntity, no
            // Id, just a composite key (TagId, EntityType, EntityId). The old
            // ChangeTracker.Entries<IEntity>() iteration made it structurally invisible to the whole audit
            // system: not a timing bug like the previous two, a coverage gap. Assigning a tag never produced
            // any AuditLogEntry at all.
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;
            var dateTimeProvider = BuildDateTimeProvider();

            int tagId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = new Tenant { Name = "T1" };
                setupDb.Tenants.Add(tenant);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);

                var tag = new Tag { Name = "Priority", TenantId = tenant.Id };
                setupDb.Tags.Add(tag);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken); // no actor set
                tagId = tag.Id;
            }

            await using var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");

            db.EntityTags.Add(new EntityTag { TagId = tagId, EntityType = "Agent", EntityId = 42 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await db.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("EntityTag", entry.EntityType);
            Assert.Equal(CommonConsts.AuditActionConsts.Create, entry.Action);
            Assert.Equal($"{tagId}:Agent:42", entry.EntityId);
        }

        [Fact]
        public async Task SaveChangesAsync_Delete_EntityTagUnassign_ProducesDeleteAuditEntry()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;
            var dateTimeProvider = BuildDateTimeProvider();

            int tagId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = new Tenant { Name = "T1" };
                setupDb.Tenants.Add(tenant);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);

                var tag = new Tag { Name = "Priority", TenantId = tenant.Id };
                setupDb.Tags.Add(tag);
                setupDb.EntityTags.Add(new EntityTag { Tag = tag, EntityType = "Agent", EntityId = 42 });
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken); // no actor set
                tagId = tag.Id;
            }

            await using var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");

            // EntityTag has no soft-delete — CLAUDE.md: "rows are hard-deleted on unassign" — so the real
            // unassign handler pattern is Remove(), not an IsDeleted flip.
            var tracked = await db.EntityTags.SingleAsync(TestContext.Current.CancellationToken);
            db.EntityTags.Remove(tracked);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await db.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("EntityTag", entry.EntityType);
            Assert.Equal(CommonConsts.AuditActionConsts.Delete, entry.Action);
            Assert.Equal($"{tagId}:Agent:42", entry.EntityId);
        }

        [Fact]
        public async Task SaveChangesAsync_Create_EntityTagForABrandNewTag_ResolvesTheRealTagIdInsteadOfATemporaryPlaceholder()
        {
            // Watch-out flagged during the fix: a composite key that includes an FK to another entity
            // created in the SAME SaveChanges call (e.g. a brand-new Tag applied to something in the same
            // request it was created) can carry an EF Core temporary placeholder value at the point
            // AppendAuditLogEntries runs, exactly like the earlier ShareTenantTool MarketplaceToolId bug —
            // just via a composite key's component this time instead of a single-int Id. The same
            // pending-Create-patch mechanism that corrects IEntity.Id post-save must also correct this.
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;
            var dateTimeProvider = BuildDateTimeProvider();

            int tenantId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = new Tenant { Name = "T1" };
                setupDb.Tenants.Add(tenant);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                tenantId = tenant.Id;
            }

            await using var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");

            var tag = new Tag { Name = "BrandNewTag", TenantId = tenantId };
            db.Tags.Add(tag);
            // TagId deliberately left unset — wired up via the Tag navigation instead, so EF Core's
            // relationship fixup (not a direct assignment) resolves TagId, exactly like the real "assign
            // tag" flow would look for a tag created moments earlier in the same request. Both the Tag and
            // the EntityTag are Added in this SAME SaveChangesAsync call, so Tag.Id genuinely doesn't exist
            // yet at the point AppendAuditLogEntries builds the EntityTag's composite EntityId.
            db.EntityTags.Add(new EntityTag { Tag = tag, EntityType = "Agent", EntityId = 99 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entityTagEntry = await db.AuditLogEntries.SingleAsync(
                e => e.EntityType == "EntityTag", TestContext.Current.CancellationToken);
            Assert.Equal(CommonConsts.AuditActionConsts.Create, entityTagEntry.Action);
            Assert.Equal($"{tag.Id}:Agent:99", entityTagEntry.EntityId);
        }

        [Fact]
        public async Task SaveChangesAsync_WithoutAuditActorSet_SkipsAuditLogEntry()
        {
            // Simulates a non-HTTP caller (e.g. ClassificationJobSchedulerActor) that resolves
            // IMasterDataDbContext via its own DI scope and never calls SetAuditActor.
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            await using var db = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider());
            db.Secrets.Add(new Secret { Name = "s1", ApiKey = "key-1", IsActive = true });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.Empty(await db.AuditLogEntries.ToListAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SaveChangesAsync_MasksSensitiveAuditFields_OnUpdate()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;
            var dateTimeProvider = BuildDateTimeProvider();

            int secretId;
            await using (var createDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var secret = new Secret { Name = "Original", ApiKey = "super-secret-key", IsActive = true };
                createDb.Secrets.Add(secret);
                await createDb.SaveChangesAsync(TestContext.Current.CancellationToken); // no actor set
                secretId = secret.Id;
            }

            await using var updateDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            updateDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");

            var tracked = await updateDb.Secrets.FirstAsync(s => s.Id == secretId, TestContext.Current.CancellationToken);
            tracked.Name = "Renamed";
            tracked.ApiKey = "brand-new-key";
            await updateDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await updateDb.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(CommonConsts.AuditActionConsts.Update, entry.Action);
            Assert.NotNull(entry.Changes);

            // Non-sensitive field: real values recorded.
            Assert.Contains("\"Name\"", entry.Changes);
            Assert.Contains("Original", entry.Changes);
            Assert.Contains("Renamed", entry.Changes);

            // Sensitive field ([SensitiveAuditField] on Secret.ApiKey): masked, real values never leaked.
            Assert.Contains("\"ApiKey\"", entry.Changes);
            Assert.DoesNotContain("super-secret-key", entry.Changes);
            Assert.DoesNotContain("brand-new-key", entry.Changes);
            Assert.Contains("*******", entry.Changes);
        }

        [Fact]
        public async Task SaveChangesAsync_ExcludesBookkeepingFields_FromDiff()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;
            var dateTimeProvider = BuildDateTimeProvider();

            int secretId;
            await using (var createDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var secret = new Secret { Name = "Original", ApiKey = "key-1", IsActive = true };
                createDb.Secrets.Add(secret);
                await createDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                secretId = secret.Id;
            }

            await using var updateDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            updateDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-3");

            var tracked = await updateDb.Secrets.FirstAsync(s => s.Id == secretId, TestContext.Current.CancellationToken);
            tracked.Name = "Renamed only";
            // LastUpdatedOn also gets stamped by SaveChangesAsync's own bookkeeping loop below — it must
            // never show up as a "changed" field in the diff even though EF sees it as modified.
            await updateDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await updateDb.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("LastUpdatedOn", entry.Changes);
            Assert.DoesNotContain("CreatedOn", entry.Changes);
        }

        [Fact]
        public async Task SaveChangesAsync_ClassifiesSoftDelete_AsDeleteAction_NotUpdate()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;
            var dateTimeProvider = BuildDateTimeProvider();

            int secretId;
            await using (var createDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var secret = new Secret { Name = "s1", ApiKey = "key-1", IsActive = true, IsDeleted = false };
                createDb.Secrets.Add(secret);
                await createDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                secretId = secret.Id;
            }

            await using var deleteDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            deleteDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-4");

            var tracked = await deleteDb.Secrets.FirstAsync(s => s.Id == secretId, TestContext.Current.CancellationToken);
            tracked.IsDeleted = true;
            await deleteDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await deleteDb.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(CommonConsts.AuditActionConsts.Delete, entry.Action);
        }

        [Fact]
        public async Task Dispose_ClearsAuditActorState_SoAPooledInstanceCannotLeakItToTheNextRenter()
        {
            // The fix for the cross-tenant leak: SetAuditActor is only ever called by HTTP middleware, but
            // MasterDataDbContext is registered with AddDbContextPool — the same physical object can later be
            // handed to a completely unrelated caller (e.g. ClassificationJobSchedulerActor, which resolves
            // IMasterDataDbContext via its own IServiceProvider.CreateScope() and never calls SetAuditActor
            // at all). Dispose() is what EF Core calls when a pooled instance is returned to the pool, so
            // clearing the audit fields there is what guarantees the next renter — whoever it is — starts
            // clean instead of silently inheriting a previous request's tenant/actor identity.
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            var db = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider());
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");

            db.Dispose();

            Assert.Null(GetPrivateField(db, "_auditTenantGuid"));
            Assert.Null(GetPrivateField(db, "_auditActorUserGuid"));
            Assert.Null(GetPrivateField(db, "_auditActorUserName"));
            Assert.Null(GetPrivateField(db, "_auditActorUserEmail"));
            Assert.Null(GetPrivateField(db, "_auditCorrelationId"));
        }

        [Fact]
        public async Task DisposeAsync_ClearsAuditActorState_SoAPooledInstanceCannotLeakItToTheNextRenter()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            var db = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider());
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");

            await db.DisposeAsync();

            Assert.Null(GetPrivateField(db, "_auditTenantGuid"));
            Assert.Null(GetPrivateField(db, "_auditActorUserGuid"));
        }

        private static object GetPrivateField(MasterDataDbContext db, string fieldName)
        {
            var field = typeof(MasterDataDbContext).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return field.GetValue(db);
        }
    }
}
