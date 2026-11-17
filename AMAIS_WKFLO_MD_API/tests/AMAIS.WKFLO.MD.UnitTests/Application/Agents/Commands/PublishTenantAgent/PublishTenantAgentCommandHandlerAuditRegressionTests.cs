using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.PublishTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.PublishTenantAgent
{
    /// <summary>
    /// Regression test: republishing an already-published TenantAgent that hasn't changed since its last
    /// publish must still bump <see cref="PublishedTenantAgent.LastUpdatedOn"/> and produce an Update
    /// <see cref="AuditLogEntry"/>.
    /// </summary>
    /// <remarks>
    /// Same gap and same fix as <c>PublishTenantToolCommandHandler</c> (see
    /// <c>PublishTenantToolCommandHandlerAuditRegressionTests</c> for the full mechanism writeup):
    /// <see cref="PublishTenantAgentCommandHandler"/>'s "update existing published agent" branch mapped the
    /// TenantAgent onto the existing PublishedTenantAgent via Mapster with no explicit LastUpdatedOn stamp —
    /// when that map produced zero real value differences, EF Core's change tracker left the entity
    /// Unchanged, so neither MasterDataDbContext.SaveChangesAsync's bookkeeping loop nor AppendAuditLogEntries
    /// (whose ResolveAuditAction returns null for Unchanged) ever ran for it. This exercises the real handler
    /// against a real SQLite-backed MasterDataDbContext (not a mock), since the bug is about EF Core
    /// change-tracking state, which a mocked IMasterDataDbContext can't reveal.
    /// </remarks>
    public class PublishTenantAgentCommandHandlerAuditRegressionTests
    {
        private static readonly DateTimeOffset FirstPublishNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset RepublishNow = FirstPublishNow.AddDays(1);

        [Fact]
        public async Task RepublishingAnUnchangedAgent_StillBumpsLastUpdatedOn_AndWritesAnUpdateAuditEntry()
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };

            // No AgentTools on the agent, so the mediator-driven "publish all tenant tools" branch never
            // runs — a no-op mediator is sufficient for this test.
            var mediator = Mock.Of<IWkfloMediator>();

            var firstProviderMock = new Mock<IDateTimeProvider>();
            firstProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(FirstPublishNow);

            int tenantAgentId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, firstProviderMock.Object))
            {
                var tenant = new Tenant { Name = "T1" };
                setupDb.Tenants.Add(tenant);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);

                var tenantAgent = new TenantAgent
                {
                    TenantId = tenant.Id,
                    Name = "My Agent",
                    Description = "An agent",
                    SystemPrompt = "You are a helpful assistant."
                };
                setupDb.TenantAgents.Add(tenantAgent);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                tenantAgentId = tenantAgent.Id;
            }

            // --- First publish: creates the PublishedTenantAgent ---
            await using (var firstPublishDb = new SqliteTestMasterDataDbContext(options, firstProviderMock.Object))
            {
                firstPublishDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new PublishTenantAgentCommandHandler(firstPublishDb, mapper, tenantContext, mediator, firstProviderMock.Object);
                var result = await handler.Handle(new PublishTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentPublishResult>>(result);
            }

            // --- Republish: same TenantAgent, no field changes at all — the regression scenario. Uses a
            // different IDateTimeProvider "now" so LastUpdatedOn's before/after is unambiguous.
            var republishProviderMock = new Mock<IDateTimeProvider>();
            republishProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(RepublishNow);

            await using (var republishDb = new SqliteTestMasterDataDbContext(options, republishProviderMock.Object))
            {
                republishDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");
                var handler = new PublishTenantAgentCommandHandler(republishDb, mapper, tenantContext, mediator, republishProviderMock.Object);
                var result = await handler.Handle(new PublishTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentPublishResult>>(result);
            }

            // --- Assert: the republish bumped LastUpdatedOn and produced its own Update audit row ---
            await using var verifyDb = new SqliteTestMasterDataDbContext(options, republishProviderMock.Object);
            var publishedAgent = await verifyDb.PublishedTenantAgents.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(RepublishNow, publishedAgent.LastUpdatedOn);

            var republishAuditEntry = await verifyDb.AuditLogEntries.SingleAsync(
                a => a.EntityType == "PublishedTenantAgent" && a.CorrelationId == "corr-2",
                TestContext.Current.CancellationToken);
            Assert.Equal(CommonConsts.AuditActionConsts.Update, republishAuditEntry.Action);
        }
    }
}
