using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.InstallMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.PublishTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.ShareTenantAgent;
using AMAIS.WKFLO.MD.Application.Tools.Commands.InstallMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.PublishTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.ShareTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands
{
    /// <summary>
    /// WI 7575: the AgentTool rows are rebuilt from scratch on publish, share and install, so the agent's tool
    /// permissions (ToolPermission / FunctionPermissions) must be copied explicitly - and the tool-level
    /// columns (PermissionsEnabled, MCP allowlist) must travel with the Mapster tool copies. Real SQLite.
    /// </summary>
    public class AgentToolPermissionsCarryOverTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        private static readonly IDateTimeProvider Clock = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
        private const string FunctionPermissions = "{\"delete\":\"Block\",\"search\":\"Ask\"}";
        private const string EnabledFunctions = "[\"search\",\"delete\"]";

        private static async Task<(SqliteConnection, DbContextOptions<MasterDataDbContext>, Tenant)> CreateDatabaseAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using var db = new SqliteTestMasterDataDbContext(options, Clock);
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var tenant = new Tenant { Name = "T1" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return (connection, options, tenant);
        }

        private static SqliteTestMasterDataDbContext Open(DbContextOptions<MasterDataDbContext> options)
        {
            var db = new SqliteTestMasterDataDbContext(options, Clock);
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
            return db;
        }

        private static TenantContext TenantContext => new() { TenantName = "T1", TenantGuid = "tenant-guid-1" };

        private static T Tool<T>(int tenantId, string name, string type) where T : Tool, new() => new()
        {
            TenantId = tenantId,
            Name = name,
            Description = $"{name} description",
            Type = type,
            Config = "{}",
            PermissionsEnabled = true
        };

        [Fact]
        public async Task Publish_CopiesAgentToolPermissionsAndToolColumnsToThePublishedAgent()
        {
            var (connection, options, tenant) = await CreateDatabaseAsync();
            await using var _ = connection;

            int agentId;
            await using (var setup = Open(options))
            {
                var http = Tool<TenantTool>(tenant.Id, "HTTP tool", "HTTP");
                http.IsActive = true;
                var mcp = Tool<TenantTool>(tenant.Id, "MCP tool", "MCP");
                mcp.IsActive = true;
                mcp.EnabledFunctions = EnabledFunctions;
                setup.TenantTools.AddRange(http, mcp);
                var agent = new TenantAgent { TenantId = tenant.Id, Name = "Support Agent", Description = "d", SystemPrompt = "p" };
                setup.TenantAgents.Add(agent);
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                setup.AgentTools.AddRange(
                    new AgentTool { AgentId = agent.Id, ToolId = http.Id, ToolPermission = "Ask" },
                    new AgentTool { AgentId = agent.Id, ToolId = mcp.Id, FunctionPermissions = FunctionPermissions });
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                agentId = agent.Id;
            }

            await using (var db = Open(options))
            {
                var mediator = new Mock<IWkfloMediator>();
                var publishTool = new PublishTenantToolCommandHandler(db, WkfloMapperExtensions.CreateMapper(), TenantContext, Clock);
                mediator.Setup(m => m.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                    .Returns((IWkfloRequest<Result> request, CancellationToken ct) => publishTool.Handle((PublishTenantToolCommand)request, ct).AsTask());

                var handler = new PublishTenantAgentCommandHandler(db, WkfloMapperExtensions.CreateMapper(), TenantContext, mediator.Object, Clock);
                Assert.Equal(ResultType.Ok, (await handler.Handle(new PublishTenantAgentCommand { Id = agentId }, TestContext.Current.CancellationToken)).ResultType);
            }

            await using var verify = Open(options);
            var published = await verify.PublishedTenantAgents.Include(a => a.AgentTools).ThenInclude(at => at.Tool).SingleAsync(TestContext.Current.CancellationToken);
            var publishedHttp = published.AgentTools.Single(at => at.Tool.Type == "HTTP");
            var publishedMcp = published.AgentTools.Single(at => at.Tool.Type == "MCP");
            Assert.IsType<PublishedTenantTool>(publishedHttp.Tool);
            Assert.Equal("Ask", publishedHttp.ToolPermission);
            Assert.Equal(FunctionPermissions, publishedMcp.FunctionPermissions);
            Assert.True(publishedMcp.Tool.PermissionsEnabled);
            Assert.Equal(EnabledFunctions, publishedMcp.Tool.EnabledFunctions);
        }

        [Fact]
        public async Task Share_CopiesAgentToolPermissionsToTheMarketplaceAgent()
        {
            var (connection, options, tenant) = await CreateDatabaseAsync();
            await using var _ = connection;

            int agentId, marketplaceToolId;
            await using (var setup = Open(options))
            {
                var tenantTool = Tool<TenantTool>(tenant.Id, "HTTP tool", "HTTP");
                var marketplaceTool = Tool<MarketplaceTool>(tenant.Id, "HTTP tool", "HTTP");
                setup.TenantTools.Add(tenantTool);
                setup.MarketplaceTools.Add(marketplaceTool);
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                var publishedTool = Tool<PublishedTenantTool>(tenant.Id, "HTTP tool", "HTTP");
                publishedTool.CreatedByTenantToolId = tenantTool.Id;
                setup.PublishedTenantTools.Add(publishedTool);

                var agent = new TenantAgent { TenantId = tenant.Id, Name = "Support Agent", Description = "d", SystemPrompt = "p" };
                setup.TenantAgents.Add(agent);
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                var publishedAgent = new PublishedTenantAgent { TenantId = tenant.Id, Name = "Support Agent", Description = "d", SystemPrompt = "p", CreatedByTenantAgentId = agent.Id };
                setup.PublishedTenantAgents.Add(publishedAgent);
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                setup.AgentTools.Add(new AgentTool { AgentId = publishedAgent.Id, ToolId = publishedTool.Id, ToolPermission = "Block" });
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                agentId = agent.Id;
                marketplaceToolId = marketplaceTool.Id;
            }

            await using (var db = Open(options))
            {
                var mediator = new Mock<IWkfloMediator>();
                mediator.Setup(m => m.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((IWkfloRequest<Result> request, CancellationToken _) =>
                    {
                        Assert.IsType<ShareTenantToolCommand>(request);
                        return new SuccessResult<TenantToolShareResult>(new TenantToolShareResult { Id = marketplaceToolId, Name = "HTTP tool" });
                    });

                var handler = new ShareTenantAgentCommandHandler(db, WkfloMapperExtensions.CreateMapper(), TenantContext, mediator.Object, Clock);
                Assert.Equal(ResultType.Ok, (await handler.Handle(new ShareTenantAgentCommand { Id = agentId }, TestContext.Current.CancellationToken)).ResultType);
            }

            await using var verify = Open(options);
            var marketplaceAgent = await verify.MarketplaceAgents.Include(a => a.AgentTools).SingleAsync(TestContext.Current.CancellationToken);
            var link = Assert.Single(marketplaceAgent.AgentTools);
            Assert.Equal(marketplaceToolId, link.ToolId);
            Assert.Equal("Block", link.ToolPermission);
        }

        [Fact]
        public async Task Install_CopiesAgentToolPermissionsToTheInstalledTenantAgent()
        {
            var (connection, options, tenant) = await CreateDatabaseAsync();
            await using var _ = connection;

            int marketplaceAgentId, installedToolId;
            await using (var setup = Open(options))
            {
                var marketplaceTool = Tool<MarketplaceTool>(tenant.Id, "MCP tool", "MCP");
                var installedTool = Tool<TenantTool>(tenant.Id, "MCP tool", "MCP");
                setup.MarketplaceTools.Add(marketplaceTool);
                setup.TenantTools.Add(installedTool);
                var marketplaceAgent = new MarketplaceAgent { TenantId = tenant.Id, Name = "Support Agent", Description = "d", SystemPrompt = "p" };
                setup.MarketplaceAgents.Add(marketplaceAgent);
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                setup.AgentTools.Add(new AgentTool { AgentId = marketplaceAgent.Id, ToolId = marketplaceTool.Id, FunctionPermissions = FunctionPermissions });
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                marketplaceAgentId = marketplaceAgent.Id;
                installedToolId = installedTool.Id;
            }

            await using (var db = Open(options))
            {
                var mediator = new Mock<IWkfloMediator>();
                mediator.Setup(m => m.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((IWkfloRequest<Result> request, CancellationToken _) =>
                    {
                        Assert.IsType<InstallMarketplaceToolCommand>(request);
                        return new SuccessResult<TenantToolCreateResult>(new TenantToolCreateResult { Id = installedToolId, Name = "MCP tool" });
                    });

                var handler = new InstallMarketplaceAgentCommandHandler(db, WkfloMapperExtensions.CreateMapper(), mediator.Object, TenantContext);
                Assert.Equal(ResultType.Ok, (await handler.Handle(new InstallMarketplaceAgentCommand { Id = marketplaceAgentId }, TestContext.Current.CancellationToken)).ResultType);
            }

            await using var verify = Open(options);
            var installedAgent = await verify.TenantAgents.Include(a => a.AgentTools).SingleAsync(TestContext.Current.CancellationToken);
            var link = Assert.Single(installedAgent.AgentTools);
            Assert.Equal(installedToolId, link.ToolId);
            Assert.Equal(FunctionPermissions, link.FunctionPermissions);
        }
    }
}
