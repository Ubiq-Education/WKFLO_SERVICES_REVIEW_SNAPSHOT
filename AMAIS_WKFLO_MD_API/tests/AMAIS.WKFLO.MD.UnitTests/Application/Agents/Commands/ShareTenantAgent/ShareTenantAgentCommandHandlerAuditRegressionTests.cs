using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.ShareTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.ShareTenantAgent
{
    /// <summary>
    /// Regression test: re-sharing an already-shared PublishedTenantAgent that hasn't changed since its last
    /// share must still bump <see cref="MarketplaceAgent.LastUpdatedOn"/> and produce an Update
    /// <see cref="AuditLogEntry"/>.
    /// </summary>
    /// <remarks>
    /// Same gap and same fix as <c>PublishTenantToolCommandHandler</c> (see
    /// <c>PublishTenantToolCommandHandlerAuditRegressionTests</c> for the full mechanism writeup):
    /// <see cref="ShareTenantAgentCommandHandler"/>'s "reuse existing marketplace agent" branch mapped the
    /// PublishedTenantAgent onto the existing MarketplaceAgent via Mapster with no explicit LastUpdatedOn
    /// stamp — when that map produced zero real value differences, EF Core's change tracker left the entity
    /// Unchanged, so neither MasterDataDbContext.SaveChangesAsync's bookkeeping loop nor AppendAuditLogEntries
    /// (whose ResolveAuditAction returns null for Unchanged) ever ran for it. This exercises the real handler
    /// against a real SQLite-backed MasterDataDbContext (not a mock), since the bug is about EF Core
    /// change-tracking state, which a mocked IMasterDataDbContext can't reveal.
    /// </remarks>
    public class ShareTenantAgentCommandHandlerAuditRegressionTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset ReShareNow = Now.AddDays(1);

        [Fact]
        public async Task ReSharingAnUnchangedAgent_StillBumpsLastUpdatedOn_AndWritesAnUpdateAuditEntry()
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };

            // No AgentTools on the published agent, so the mediator-driven "share published tools" branch
            // never runs — a no-op mediator is sufficient for this test.
            var mediator = Mock.Of<IWkfloMediator>();

            var firstProviderMock = new Mock<IDateTimeProvider>();
            firstProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

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

                var publishedAgent = mapper.Map<PublishedTenantAgent>(tenantAgent);
                publishedAgent.TenantId = tenant.Id;
                publishedAgent.CreatedByTenantAgentId = tenantAgent.Id;
                setupDb.PublishedTenantAgents.Add(publishedAgent);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            // --- First share: creates the MarketplaceAgent (the "else" branch) ---
            await using (var firstShareDb = new SqliteTestMasterDataDbContext(options, firstProviderMock.Object))
            {
                firstShareDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new ShareTenantAgentCommandHandler(firstShareDb, mapper, tenantContext, mediator, firstProviderMock.Object);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            // --- Re-share: same PublishedTenantAgent, no field changes at all — the regression scenario.
            // Uses a different IDateTimeProvider "now" so LastUpdatedOn's before/after is unambiguous.
            var reShareProviderMock = new Mock<IDateTimeProvider>();
            reShareProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(ReShareNow);

            await using (var reShareDb = new SqliteTestMasterDataDbContext(options, reShareProviderMock.Object))
            {
                reShareDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");
                var handler = new ShareTenantAgentCommandHandler(reShareDb, mapper, tenantContext, mediator, reShareProviderMock.Object);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            // --- Assert: the re-share bumped LastUpdatedOn and produced its own Update audit row ---
            await using var verifyDb = new SqliteTestMasterDataDbContext(options, reShareProviderMock.Object);
            var marketplaceAgent = await verifyDb.MarketplaceAgents.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(ReShareNow, marketplaceAgent.LastUpdatedOn);

            var reShareAuditEntry = await verifyDb.AuditLogEntries.SingleAsync(
                a => a.EntityType == "MarketplaceAgent" && a.CorrelationId == "corr-2",
                TestContext.Current.CancellationToken);
            Assert.Equal(CommonConsts.AuditActionConsts.Update, reShareAuditEntry.Action);
        }
    }
}
