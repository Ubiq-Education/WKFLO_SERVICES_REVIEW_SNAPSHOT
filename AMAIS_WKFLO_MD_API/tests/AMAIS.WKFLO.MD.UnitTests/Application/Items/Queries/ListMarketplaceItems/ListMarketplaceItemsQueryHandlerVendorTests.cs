using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Items.Models;
using AMAIS.WKFLO.MD.Application.Items.Queries.ListMarketplaceItems;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Items.Queries.ListMarketplaceItems
{
    /// <summary>
    /// Bug 8303: the marketplace showed "by UBIQ Education" on every item. The vendor is now the name of the tenant
    /// that published the item. Uses a real SQLite-backed MasterDataDbContext (same approach as
    /// AgentIconFromChatSettingsTests) because the query is a projected UNION that mocks cannot run.
    /// </summary>
    public class ListMarketplaceItemsQueryHandlerVendorTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private static IDateTimeProvider Clock() => Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

        [Fact]
        public async Task Handle_ItemsFromDifferentTenants_VendorIsEachPublishingTenantsName()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var db = new SqliteTestMasterDataDbContext(options, Clock()))
            {
                await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

                var agentTenant = new Tenant { Name = "tenant-a" };
                var toolTenant = new Tenant { Name = "tenant-b" };
                db.Tenants.AddRange(agentTenant, toolTenant);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);

                db.MarketplaceAgents.Add(new MarketplaceAgent { TenantId = agentTenant.Id, Name = "Agent", Description = "d", SystemPrompt = "p" });
                db.MarketplaceTools.Add(new MarketplaceTool { TenantId = toolTenant.Id, Name = "Tool", Description = "d", Type = "API", Config = "{}" });
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            List<MarketplaceItemGetResult> items;
            await using (var db = new SqliteTestMasterDataDbContext(options, Clock()))
            {
                var handler = new ListMarketplaceItemsQueryHandler(db, WkfloMapperExtensions.CreateMapper(), new UserContext { Guid = Guid.NewGuid().ToString() });

                var result = await handler.Handle(new ListMarketplaceItemsQuery(), TestContext.Current.CancellationToken);

                items = Assert.IsType<SuccessResult<List<MarketplaceItemGetResult>>>(result).Data;
            }

            Assert.Equal("tenant-a", items.Single(i => i.Name == "Agent").Vendor);
            Assert.Equal("tenant-b", items.Single(i => i.Name == "Tool").Vendor);
            Assert.DoesNotContain(items, i => i.Vendor == "UBIQ Education");
        }
    }
}
