using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
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
    /// Covers WI 7312's requirement that <see cref="Agent.InstalledFromMarketplaceAgentId"/> travels
    /// with a TenantAgent's published snapshot - i.e. the Mapster TenantAgent→PublishedTenantAgent
    /// config must NOT ignore this scalar (see the doc comment on Agent.InstalledFromMarketplaceAgentId
    /// and Agent.cs's Mapping method). Uses a real SQLite-backed MasterDataDbContext (same approach as
    /// PublishTenantAgentCommandHandlerAuditRegressionTests) to exercise the real Mapster mapping.
    /// </summary>
    public class PublishTenantAgentCommandHandlerInstalledFromMarketplaceAgentIdTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Handle_FirstPublish_CopiesInstalledFromMarketplaceAgentIdToPublishedSnapshot()
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };
            var mediator = Mock.Of<IWkfloMediator>();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            int tenantAgentId;
            int marketplaceAgentId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = new Tenant { Name = "T1" };
                setupDb.Tenants.Add(tenant);

                var marketplaceAgent = new MarketplaceAgent
                {
                    Name = "Template Agent",
                    Description = "Desc",
                    SystemPrompt = "You are a helpful assistant.",
                    Tenant = tenant
                };
                setupDb.MarketplaceAgents.Add(marketplaceAgent);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                marketplaceAgentId = marketplaceAgent.Id;

                var tenantAgent = new TenantAgent
                {
                    TenantId = tenant.Id,
                    Name = "My Installed Agent",
                    Description = "An agent",
                    SystemPrompt = "You are a helpful assistant.",
                    InstalledFromMarketplaceAgentId = marketplaceAgentId
                };
                setupDb.TenantAgents.Add(tenantAgent);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                tenantAgentId = tenantAgent.Id;
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new PublishTenantAgentCommandHandler(db, mapper, tenantContext, mediator, dateTimeProvider);
                var result = await handler.Handle(new PublishTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentPublishResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var publishedAgent = await verifyDb.PublishedTenantAgents.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(marketplaceAgentId, publishedAgent.InstalledFromMarketplaceAgentId);
        }

        [Fact]
        public async Task Handle_Republish_KeepsInstalledFromMarketplaceAgentIdOnExistingPublishedSnapshot()
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };
            var mediator = Mock.Of<IWkfloMediator>();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            int tenantAgentId;
            int marketplaceAgentId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = new Tenant { Name = "T1" };
                setupDb.Tenants.Add(tenant);

                var marketplaceAgent = new MarketplaceAgent
                {
                    Name = "Template Agent",
                    Description = "Desc",
                    SystemPrompt = "You are a helpful assistant.",
                    Tenant = tenant
                };
                setupDb.MarketplaceAgents.Add(marketplaceAgent);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                marketplaceAgentId = marketplaceAgent.Id;

                var tenantAgent = new TenantAgent
                {
                    TenantId = tenant.Id,
                    Name = "My Installed Agent",
                    Description = "An agent",
                    SystemPrompt = "You are a helpful assistant.",
                    InstalledFromMarketplaceAgentId = marketplaceAgentId
                };
                setupDb.TenantAgents.Add(tenantAgent);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                tenantAgentId = tenantAgent.Id;
            }

            // First publish
            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new PublishTenantAgentCommandHandler(db, mapper, tenantContext, mediator, dateTimeProvider);
                await handler.Handle(new PublishTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
            }

            // Republish (update-existing-published-agent branch)
            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");
                var handler = new PublishTenantAgentCommandHandler(db, mapper, tenantContext, mediator, dateTimeProvider);
                await handler.Handle(new PublishTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var publishedAgent = await verifyDb.PublishedTenantAgents.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(marketplaceAgentId, publishedAgent.InstalledFromMarketplaceAgentId);
        }
    }
}
