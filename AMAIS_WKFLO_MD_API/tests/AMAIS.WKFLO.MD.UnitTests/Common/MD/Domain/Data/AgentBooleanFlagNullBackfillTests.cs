using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Data
{
    /// <summary>
    /// Regression coverage for the second WI 7317 regression: <c>[Agent].[EnableSTM]</c>,
    /// <c>[EnableLTM]</c> and <c>[EnableSpinUpAgents]</c> were all <c>BIT NULL</c> in the real SQL
    /// Server schema while every C# entity that maps a real value to them
    /// (<see cref="AMAIS.WKFLO.Common.MD.Domain.Entities.RunnableAgent"/> for all three,
    /// <see cref="AMAIS.WKFLO.Common.MD.Domain.Entities.MarketplaceAgent"/> for <c>EnableSTM</c>)
    /// declares them as non-nullable <c>bool</c>. Every pre-existing <c>MarketplaceAgent</c> row
    /// (created before this feature ever mapped <c>EnableSTM</c> for that AgentType) is NULL in
    /// that column in the real database, and the first query that materializes one as a full
    /// entity throws "Data is Null. This method or property cannot be called on Null values." on
    /// real SQL Server (confirmed live, in <c>ShareTenantAgentCommandHandler</c>'s lookup by
    /// <c>SourcePublishedAgentId</c>, but really any query anywhere that loads such a row).
    ///
    /// The chosen fix is entirely at the SQL/deployment layer, not in this C# model: a
    /// pre-deployment backfill (<c>Script.PreDeployment.sql</c>, which MUST run before the
    /// generated schema-sync ALTER COLUMN statements, since those would otherwise fail on any
    /// remaining NULL) followed by tightening all three columns to <c>NOT NULL DEFAULT 0</c> (see
    /// <c>Agent.sql</c>). This suite cannot exercise that pipeline directly (no real SQL Server in
    /// this sandbox), so it does three things instead:
    ///
    /// 1. <see cref="ReadingLegacyNullRow_Throws"/> reproduces the exact failure mode in isolation,
    ///    by hand-crafting a SQLite table that mirrors the real PRE-backfill production schema
    ///    (nullable columns) rather than letting EF's own <c>EnsureCreated</c> derive a schema from
    ///    the current (non-nullable-bool) C# model — that would just make SQLite enforce NOT NULL
    ///    at the raw INSERT and never reproduce the bug at all. This is the "insert a raw NULL,
    ///    read it back through the normal query path" reproduction requested when this regression
    ///    was reported, and it is what proves this class of gap is real and worth guarding.
    /// 2. <see cref="AgentSqlDeclaresAllThreeFlagsNotNullWithDefault"/> and
    ///    <see cref="PreDeploymentScriptBackfillsAllThreeFlags"/> assert directly against the
    ///    actual fix artifacts (the SSDT table definition and the pre-deployment script), so a
    ///    future revert of either half of the fix is caught here rather than only in production.
    /// </summary>
    public class AgentBooleanFlagNullBackfillTests
    {
        private static readonly string DbCommonSrcRoot = FindDbCommonSrcRoot();

        private static string FindDbCommonSrcRoot()
        {
            // Walk up from the test assembly's output directory to the repo root, then down into
            // the SSDT project - avoids hardcoding a machine-specific absolute path.
            var dir = AppContext.BaseDirectory;
            var current = new DirectoryInfo(dir);
            while (current != null && !Directory.Exists(Path.Combine(current.FullName, "AMAIS_WKFLO_MD_API")))
            {
                current = current.Parent;
            }

            if (current is null)
            {
                throw new InvalidOperationException(
                    $"Could not locate repo root by walking up from '{dir}' looking for 'AMAIS_WKFLO_MD_API'.");
            }

            return Path.Combine(
                current.FullName,
                "AMAIS_WKFLO", "AMAIS_WKFLO_COMMON", "src", "AMAIS.WKFLO.Common.MD.DB");
        }

        [Fact]
        public async Task ReadingLegacyNullRow_Throws()
        {
            // Mirrors the REAL pre-backfill production shape (nullable columns) - deliberately NOT
            // using MasterDataDbContext.Database.EnsureCreated(), which would derive NOT NULL from
            // the current (correct) non-nullable-bool C# properties and make the raw INSERT below
            // fail at the SQLite constraint instead of reproducing the read-time crash.
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            await using (var createCmd = connection.CreateCommand())
            {
                createCmd.CommandText = """
                    CREATE TABLE "Agent" (
                        "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        "AgentType" TEXT NOT NULL,
                        "Name" TEXT NULL,
                        "Description" TEXT NULL,
                        "SystemPrompt" TEXT NULL,
                        "ResponseTone" TEXT NULL,
                        "AIModelId" INTEGER NULL,
                        "TenantId" INTEGER NOT NULL,
                        "IconUrl" TEXT NULL,
                        "InstalledFromMarketplaceAgentId" INTEGER NULL,
                        "EnableSTM" INTEGER NULL,
                        "SourcePublishedAgentId" INTEGER NULL,
                        "CreatedByTenantAgentId" INTEGER NULL,
                        "MarketplaceAgentId" INTEGER NULL,
                        "IsActive" INTEGER NULL,
                        "EnableLTM" INTEGER NULL,
                        "EnableSpinUpAgents" INTEGER NULL,
                        "LastRunAt" TEXT NULL,
                        "MaxToolCallsPerTurn" INTEGER NULL,
                        "PublishedTenantAgentId" INTEGER NULL,
                        "CreatedOn" TEXT NOT NULL,
                        "LastUpdatedOn" TEXT NULL,
                        "IsDeleted" INTEGER NOT NULL
                    );
                    """;
                await createCmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            await using (var insertCmd = connection.CreateCommand())
            {
                // A legacy MarketplaceAgent row exactly like the real ones on SQL Server today:
                // EnableSTM/EnableLTM/EnableSpinUpAgents were never written for this AgentType
                // before this feature, so they're still NULL. Inserted via raw ADO.NET,
                // completely bypassing EF's model/defaults.
                insertCmd.CommandText = """
                    INSERT INTO "Agent"
                        ("AgentType", "Name", "Description", "SystemPrompt", "ResponseTone",
                         "TenantId", "EnableSTM", "EnableLTM", "EnableSpinUpAgents",
                         "CreatedOn", "IsDeleted")
                    VALUES
                        ('MarketplaceAgent', 'Legacy Template', 'desc', 'prompt', 'Friendly',
                         1, NULL, NULL, NULL,
                         '2026-01-01T00:00:00.0000000+00:00', 0);
                    """;
                await insertCmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var options = new DbContextOptionsBuilder<MasterDataDbContext>()
                .UseSqlite(connection)
                .Options;
            await using var context = new MasterDataDbContext(options, dateTimeProviderMock.Object);

            // This is the exact failure mode reported live against real SQL Server: reading a
            // pre-existing row through the normal query path throws instead of returning a usable
            // entity. The fix that prevents this is the pre-deployment backfill + NOT NULL
            // tightening (verified separately below and, for real, only by an actual SQL Server
            // deployment) - nothing in this C# model changed, so this read is still expected to
            // throw here. This test exists to prove the failure mode is real and understood, not
            // to prove the SQL-side fix (which this in-memory SQLite test cannot exercise).
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await context.MarketplaceAgents.AsNoTracking().FirstOrDefaultAsync(
                    TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("EnableSTM")]
        [InlineData("EnableLTM")]
        [InlineData("EnableSpinUpAgents")]
        public void AgentSqlDeclaresAllThreeFlagsNotNullWithDefault(string columnName)
        {
            var sql = File.ReadAllText(Path.Combine(DbCommonSrcRoot, "dbo", "Tables", "Agent.sql"));

            Assert.Contains($"[{columnName}] BIT NOT NULL DEFAULT 0", sql);
        }

        [Theory]
        [InlineData("[EnableSTM]")]
        [InlineData("[EnableLTM]")]
        [InlineData("[EnableSpinUpAgents]")]
        public void PreDeploymentScriptBackfillsAllThreeFlags(string bracketedColumnName)
        {
            var preDeploymentPath = Path.Combine(DbCommonSrcRoot, "Script.PreDeployment.sql");
            Assert.True(File.Exists(preDeploymentPath), $"Expected {preDeploymentPath} to exist.");

            var script = File.ReadAllText(preDeploymentPath);

            Assert.Contains(
                $"UPDATE [dbo].[Agent] SET {bracketedColumnName} = 0 WHERE {bracketedColumnName} IS NULL;",
                script);
        }

        [Fact]
        public void SqlProjRegistersPreDeploymentScript()
        {
            var sqlprojPath = Path.Combine(DbCommonSrcRoot, "AMAIS.WKFLO.Common.MD.DB.sqlproj");
            var sqlproj = File.ReadAllText(sqlprojPath);

            Assert.Contains("<PreDeploy Include=\"Script.PreDeployment.sql\" />", sqlproj);
        }
    }
}
