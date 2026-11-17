using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Data
{
    /// <summary>
    /// Regression test for a bug found by GitHub Copilot's automated review on PR !16299 (confirmed against
    /// the real code, not a false positive): <see cref="AuditLogEntry.EntityId"/> was configured with
    /// <c>HasMaxLength(64)</c>, but <see cref="McpOAuthPendingFlow.State"/> — that entity's own primary key,
    /// configured with <c>HasMaxLength(256)</c> in <c>McpOAuthPendingFlowConfiguration</c> — flows straight
    /// into <see cref="AuditLogEntry.EntityId"/> for any Create/Delete of that entity, since the EntityTag/
    /// AgentTool generalization made <c>MasterDataDbContext.GetEntityId</c> read primary keys generically
    /// for any tracked entity, not just ones implementing <c>IEntity</c>.
    ///
    /// This was worse than an ordinary truncation risk: <c>AppendAuditLogEntries</c>'s try/catch only guards
    /// in-memory audit-row construction, not the actual database write, which happens via the outer
    /// <c>base.SaveChangesAsync()</c>/<c>base.SaveChanges()</c> call — outside that try/catch. A State value
    /// too long for the (pre-fix) 64-character column would fail the actual INSERT and, with it, the
    /// original CRUD operation the audit trail is required to never block.
    /// </summary>
    /// <remarks>
    /// Fixed by widening <c>AuditLogEntry.EntityId</c> to 450 characters in both
    /// <c>AuditLogEntryConfiguration</c> (EF Core) and the SSDT table definition
    /// (<c>AMAIS.WKFLO.Common.MD.DB/dbo/Tables/AuditLogEntry.sql</c>) — comfortably covering
    /// <see cref="McpOAuthPendingFlow.State"/>'s own 256-character max, plus headroom for composite keys
    /// (e.g. EntityTag's "TagId:EntityType:EntityId").
    /// </remarks>
    public class MasterDataDbContextAuditEntityIdLengthRegressionTests
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

        /// <summary>
        /// SQLite (used by the other test in this file) doesn't enforce column length at all — it's
        /// dynamically typed and, unlike SQL Server, never truncates or errors on an over-length string
        /// regardless of a configured <c>HasMaxLength</c>. So a SQLite round-trip alone cannot actually
        /// reproduce the truncation/constraint failure this bug describes on the real (SQL Server) database.
        /// This test instead asserts directly on the EF Core model metadata — provider-agnostic, and exactly
        /// what <c>AuditLogEntryConfiguration</c> configures — that <see cref="AuditLogEntry.EntityId"/>'s
        /// configured max length actually covers the longest real primary key known to flow into it
        /// (<see cref="McpOAuthPendingFlow.State"/>'s own 256-character configured max).
        /// </summary>
        [Fact]
        public async Task AuditLogEntry_EntityId_ConfiguredMaxLength_CoversLongestKnownPrimaryKey()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            await using var db = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>());

            var entityIdMaxLength = db.Model
                .FindEntityType(typeof(AuditLogEntry))
                .FindProperty(nameof(AuditLogEntry.EntityId))
                .GetMaxLength();

            var stateMaxLength = db.Model
                .FindEntityType(typeof(McpOAuthPendingFlow))
                .FindProperty(nameof(McpOAuthPendingFlow.State))
                .GetMaxLength();

            Assert.NotNull(entityIdMaxLength);
            Assert.NotNull(stateMaxLength);
            Assert.True(
                entityIdMaxLength >= stateMaxLength,
                $"AuditLogEntry.EntityId's configured max length ({entityIdMaxLength}) must be at least as " +
                $"long as McpOAuthPendingFlow.State's ({stateMaxLength}) — State is that entity's primary " +
                "key and flows straight into EntityId for any Create/Delete audit row.");
        }

        [Fact]
        public async Task CreateAndDelete_McpOAuthPendingFlow_WithNear256CharState_DoesNotThrow_AndWritesUntruncatedEntityId()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            // 256 characters — McpOAuthPendingFlow.State's own configured max length, and the longest
            // primary key value flowing into AuditLogEntry.EntityId anywhere in the codebase today.
            var state = new string('a', 256);

            await using (var createDb = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider()))
            {
                createDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");

                createDb.McpOAuthPendingFlows.Add(new McpOAuthPendingFlow
                {
                    State = state,
                    ConnectionId = Guid.NewGuid(),
                    ClientId = "client-1",
                    CreatedOnUtc = Now,
                    ExpiresOnUtc = Now.AddMinutes(10)
                });

                // Must not throw. Before the fix, this failed inside base.SaveChangesAsync (outside
                // AppendAuditLogEntries' try/catch) with a truncation/constraint error, failing the real
                // Create operation the audit trail is required to never block.
                await createDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using (var verifyDb = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider()))
            {
                var createEntry = await verifyDb.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
                Assert.Equal("McpOAuthPendingFlow", createEntry.EntityType);
                Assert.Equal(CommonConsts.AuditActionConsts.Create, createEntry.Action);

                // The full, untruncated State value must be preserved as EntityId.
                Assert.Equal(state, createEntry.EntityId);
                Assert.Equal(256, createEntry.EntityId.Length);

                // CodeVerifier/ClientSecret are [SensitiveAuditField] on this entity — sanity-check the
                // Create diff still built successfully alongside the long key.
                Assert.NotNull(createEntry.Changes);
            }

            await using (var deleteDb = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider()))
            {
                deleteDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");

                var flow = await deleteDb.McpOAuthPendingFlows.SingleAsync(TestContext.Current.CancellationToken);
                deleteDb.McpOAuthPendingFlows.Remove(flow);

                // Must not throw either — same EntityId-length concern applies to the Delete audit row.
                await deleteDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using (var verifyDb2 = new SqliteTestMasterDataDbContext(options, BuildDateTimeProvider()))
            {
                var deleteEntry = await verifyDb2.AuditLogEntries.SingleAsync(
                    a => a.CorrelationId == "corr-2", TestContext.Current.CancellationToken);
                Assert.Equal("McpOAuthPendingFlow", deleteEntry.EntityType);
                Assert.Equal(CommonConsts.AuditActionConsts.Delete, deleteEntry.Action);
                Assert.Equal(state, deleteEntry.EntityId);
            }
        }
    }
}
