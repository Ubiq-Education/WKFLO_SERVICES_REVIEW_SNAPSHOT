using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.InstallMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.InstallMarketplaceAgent
{
    /// <summary>
    /// Covers WI 7317's install-time default-copy behavior added for <c>MarketplaceAgent.EnableSTM</c>:
    /// a newly-installed <see cref="TenantAgent"/> must inherit its source template's configured
    /// Short Term Memory default rather than the previously-hardcoded <c>false</c>. Uses a real
    /// SQLite-backed <see cref="MasterDataDbContext"/> (same approach as
    /// <see cref="InstallMarketplaceAgentCommandHandlerDedupTests"/>) so the real EF model - including
    /// the new <c>MarketplaceAgent.EnableSTM</c> property sharing the same physical [Agent].[EnableSTM]
    /// column as <see cref="RunnableAgent.EnableSTM"/> via TPH - is actually exercised, not just mocked.
    /// </summary>
    public class InstallMarketplaceAgentCommandHandlerEnableSTMTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private static async Task<(DbContextOptions<MasterDataDbContext> Options, int MarketplaceAgentId)> SeedAsync(bool templateEnableSTM)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var providerMock = new Mock<IDateTimeProvider>();
            providerMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            int marketplaceAgentId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, providerMock.Object))
            {
                var tenant = new Tenant { Name = "Test" };
                setupDb.Tenants.Add(tenant);

                var marketplaceAgent = new MarketplaceAgent
                {
                    Name = "Test Marketplace Agent",
                    Description = "Desc",
                    SystemPrompt = "You are a helpful assistant.",
                    AgentTools = new List<AgentTool>(),
                    Tenant = tenant,
                    EnableSTM = templateEnableSTM
                };
                setupDb.MarketplaceAgents.Add(marketplaceAgent);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                marketplaceAgentId = marketplaceAgent.Id;
            }

            return (options, marketplaceAgentId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Handle_NewInstall_CopiesTemplatesEnableSTMDefault(bool templateEnableSTM)
        {
            var (options, marketplaceAgentId) = await SeedAsync(templateEnableSTM);
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var mapper = WkfloMapperExtensions.CreateMapper();
            var mediator = Mock.Of<IWkfloMediator>();
            var tenantContext = new TenantContext { TenantName = "Test" };

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceAgentCommandHandler(db, mapper, mediator, tenantContext);
                var result = await handler.Handle(new InstallMarketplaceAgentCommand { Id = marketplaceAgentId }, TestContext.Current.CancellationToken);
                var success = Assert.IsType<SuccessResult<TenantAgentCreateResult>>(result);
                Assert.Equal(templateEnableSTM, success.Data.EnableSTM);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var tenantAgent = await verifyDb.TenantAgents.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(templateEnableSTM, tenantAgent.EnableSTM);
        }
    }
}
