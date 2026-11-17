using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using System;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Data
{
    /// <summary>
    /// Asserts against EF Core's actual RESOLVED model rather than against a SQLite database
    /// created fresh from that same model. A functional/integration test that spins up SQLite via
    /// <c>EnsureCreatedAsync</c>-style setup and then queries successfully does NOT prove the model
    /// matches the real, statically-defined SQL Server schema (SSDT) — SQLite happily creates
    /// whatever columns the CURRENT model says it needs, including ones that don't exist in
    /// production. This is exactly what masked the WI 7317 regression, where a second, independent
    /// <c>EnableSTM</c> declaration on <see cref="MarketplaceAgent"/> (a sibling branch to
    /// <see cref="RunnableAgent"/>, which already declared its own <c>EnableSTM</c>) made EF Core
    /// treat the two as unrelated properties and split them into type-prefixed shadow columns
    /// (confirmed below via <c>StoreObjectIdentifier</c>-aware resolution: <c>TenantAgent_EnableSTM</c>,
    /// <c>PublishedTenantAgent_EnableSTM</c>) instead of mapping every subtype to the single shared
    /// <c>[Agent].[EnableSTM]</c> column that actually exists on SQL Server — every unit/functional
    /// test still passed because SQLite just created those shadow columns too.
    ///
    /// Important implementation detail confirmed while writing this test: the parameterless
    /// <see cref="IReadOnlyProperty.GetColumnName()"/> extension does NOT reflect this
    /// disambiguation (it kept returning the unprefixed "EnableSTM" even against a deliberately
    /// reintroduced copy of the WI 7317 bug, generating a false pass). The store-object-aware
    /// overload, <c>GetColumnName(StoreObjectIdentifier)</c> — the same API EF Core's own migration
    /// scaffolding and <c>Database.GenerateCreateScript()</c> path use internally — is the one that
    /// actually resolves the final, physical column name, confirmed by cross-checking against the
    /// literal generated CREATE TABLE script (which does show the shadow-prefixed column names) for
    /// a deliberately-reintroduced copy of the bug before writing this assertion.
    /// </summary>
    public class MasterDataDbContextModelTests
    {
        private const string AgentTableName = "Agent";

        private static IModel BuildModel()
        {
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();

            // No connection is ever opened and no schema is ever created against it — the Sqlite
            // provider is only used so EF Core can build its relational model. Table/column naming
            // conventions, including the TPH shared-column-vs-shadow-column decision this test is
            // guarding, are resolved by the shared EFCore.Relational conventions, not by anything
            // SQL-Server-specific or SQLite-specific, so the result is provider-independent.
            var options = new DbContextOptionsBuilder<MasterDataDbContext>()
                .UseSqlite(new SqliteConnection("DataSource=:memory:"))
                .Options;

            using var context = new MasterDataDbContext(options, dateTimeProviderMock.Object);
            return context.Model;
        }

        [Theory]
        [InlineData(typeof(MarketplaceAgent))]
        [InlineData(typeof(TenantAgent))]
        [InlineData(typeof(PublishedTenantAgent))]
        public void EnableSTM_ResolvesToSharedAgentColumn_NotATypePrefixedShadowColumn(Type entityClrType)
        {
            var model = BuildModel();

            var entityType = model.FindEntityType(entityClrType);
            Assert.NotNull(entityType);

            var property = entityType.FindProperty(nameof(Agent.EnableSTM));
            Assert.NotNull(property);

            var storeObject = StoreObjectIdentifier.Table(AgentTableName, schema: null);
            var columnName = property.GetColumnName(storeObject);

            Assert.Equal("EnableSTM", columnName);
        }
    }
}
