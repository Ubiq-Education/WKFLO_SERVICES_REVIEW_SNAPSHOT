using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.PublishTenantTool;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.PublishTenantTool
{
    /// <summary>
    /// WI 7447/7575 draft/publish model: PermissionsEnabled and the MCP function allowlist columns are saved on the
    /// tenant tool only and reach the published copy exactly when the tool is published (first publish and
    /// republish). Real SQLite-backed MasterDataDbContext with the real Mapster config.
    /// </summary>
    public class PublishTenantToolCommandHandlerPermissionFieldsTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset DiscoveredOn = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        private static readonly IDateTimeProvider Clock = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

        private static async Task<(SqliteConnection, DbContextOptions<MasterDataDbContext>, int ToolId)> SeedAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using var db = Open(options);
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var tenant = new Tenant { Name = "T1" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var tool = new TenantTool
            {
                TenantId = tenant.Id,
                Name = "Docs",
                Description = "An MCP tool",
                Type = "MCP",
                Config = "{}",
                IsActive = true,
                PermissionsEnabled = true,
                DiscoveredFunctions = "[{\"name\":\"search\",\"isNew\":false}]",
                FunctionsDiscoveredOn = DiscoveredOn,
                EnabledFunctions = "[\"search\"]"
            };
            db.TenantTools.Add(tool);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return (connection, options, tool.Id);
        }

        private static SqliteTestMasterDataDbContext Open(DbContextOptions<MasterDataDbContext> options)
        {
            var db = new SqliteTestMasterDataDbContext(options, Clock);
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
            return db;
        }

        private static async Task PublishAsync(DbContextOptions<MasterDataDbContext> options, int toolId)
        {
            await using var db = Open(options);
            var handler = new PublishTenantToolCommandHandler(db, WkfloMapperExtensions.CreateMapper(), new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" }, Clock);
            Assert.Equal(ResultType.Ok, (await handler.Handle(new PublishTenantToolCommand { Id = toolId }, TestContext.Current.CancellationToken)).ResultType);
        }

        private static async Task<PublishedTenantTool> ReadPublishedAsync(DbContextOptions<MasterDataDbContext> options)
        {
            await using var db = Open(options);
            return await db.PublishedTenantTools.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task FirstPublish_CopiesPermissionFields()
        {
            var (connection, options, toolId) = await SeedAsync();
            await using var _ = connection;

            await PublishAsync(options, toolId);

            var published = await ReadPublishedAsync(options);
            Assert.True(published.PermissionsEnabled);
            Assert.Equal("[{\"name\":\"search\",\"isNew\":false}]", published.DiscoveredFunctions);
            Assert.Equal(DiscoveredOn, published.FunctionsDiscoveredOn);
            Assert.Equal("[\"search\"]", published.EnabledFunctions);
        }

        [Fact]
        public async Task Republish_CopiesChangedAndClearedPermissionFields()
        {
            var (connection, options, toolId) = await SeedAsync();
            await using var _ = connection;
            await PublishAsync(options, toolId);

            var newDiscoveredOn = DiscoveredOn.AddDays(1);
            await using (var db = Open(options))
            {
                var tool = await db.TenantTools.SingleAsync(t => t.Id == toolId, TestContext.Current.CancellationToken);
                tool.PermissionsEnabled = false;
                tool.DiscoveredFunctions = "[{\"name\":\"search\",\"isNew\":false},{\"name\":\"create\",\"isNew\":true}]";
                tool.FunctionsDiscoveredOn = newDiscoveredOn;
                tool.EnabledFunctions = null;
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            // Saving the draft does not touch the published copy...
            var beforeRepublish = await ReadPublishedAsync(options);
            Assert.True(beforeRepublish.PermissionsEnabled);
            Assert.Equal("[\"search\"]", beforeRepublish.EnabledFunctions);

            // ...republishing does.
            await PublishAsync(options, toolId);

            var published = await ReadPublishedAsync(options);
            Assert.False(published.PermissionsEnabled);
            Assert.Contains("create", published.DiscoveredFunctions);
            Assert.Equal(newDiscoveredOn, published.FunctionsDiscoveredOn);
            Assert.Null(published.EnabledFunctions);
        }
    }
}
