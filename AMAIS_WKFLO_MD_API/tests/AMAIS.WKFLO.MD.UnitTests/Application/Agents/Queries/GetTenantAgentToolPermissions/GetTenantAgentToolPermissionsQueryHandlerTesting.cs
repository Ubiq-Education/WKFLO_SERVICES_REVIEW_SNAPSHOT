using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.MD.UnitTests.Application.Tools;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetTenantAgentToolPermissions
{
    /// <summary>
    /// WI 7575: seeds a real SQLite-backed MasterDataDbContext (the "Also used by" query relies on TPH type
    /// tests and navigation joins) with one agent linked to every kind of tool the permissions view must
    /// handle. <see cref="SeedAsync"/> returns a fresh database per call because the update handler mutates it.
    /// </summary>
    public class GetTenantAgentToolPermissionsQueryHandlerTesting
    {
        public const string TenantName = "T1";

        public static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public IDateTimeProvider DateTimeProvider { get; } = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

        public TenantContext TenantContext { get; } = new() { TenantName = TenantName };

        public sealed record Seed(SqliteConnection Connection, DbContextOptions<MasterDataDbContext> Options,
            int AgentId, int OtherAgentId, int McpToolId, int HttpToolId, int ContactFormToolId, int InactiveToolId, int DeletedToolId, int UnlinkedToolId);

        internal SqliteTestMasterDataDbContext CreateDb(Seed seed) => new(seed.Options, DateTimeProvider);

        public async Task<Seed> SeedAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schema = new SqliteTestMasterDataDbContext(options, DateTimeProvider))
                await schema.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            await using var db = new SqliteTestMasterDataDbContext(options, DateTimeProvider);
            var tenant = new Tenant { Name = TenantName };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            TenantTool Tool(string name, string type, bool permissionsEnabled = true, bool isActive = true, bool isDeleted = false) => new()
            {
                Name = name,
                Description = $"{name} description",
                Type = type,
                Config = "{}",
                IsActive = isActive,
                IsDeleted = isDeleted,
                TenantId = tenant.Id,
                PermissionsEnabled = permissionsEnabled
            };

            var mcp = Tool("Docs MCP", "MCP");
            mcp.DiscoveredFunctions = ToolPermissionsTestData.Snapshot(("search", false), ("create", false), ("delete", false));
            mcp.EnabledFunctions = ToolPermissionsTestData.Enabled("search", "delete", "vanished");
            var http = Tool("Weather HTTP", "HTTP");
            var contactForm = Tool("Contact form", "Contact_Form", permissionsEnabled: false);
            var inactive = Tool("Inactive HTTP", "HTTP", isActive: false);
            var deleted = Tool("Deleted HTTP", "HTTP", isDeleted: true);
            var unlinked = Tool("Unlinked HTTP", "HTTP");
            db.TenantTools.AddRange(mcp, http, contactForm, inactive, deleted, unlinked);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            TenantAgent Agent(string name, bool isDeleted = false) => new()
            {
                TenantId = tenant.Id,
                Name = name,
                Description = "An agent",
                SystemPrompt = "You are a helpful assistant.",
                IsDeleted = isDeleted
            };

            var agent = Agent("Support Agent");
            var other = Agent("Sales Agent");
            var deletedAgent = Agent("Retired Agent", isDeleted: true);
            db.TenantAgents.AddRange(agent, other, deletedAgent);
            var published = new PublishedTenantAgent { TenantId = tenant.Id, Name = "Support Agent (published)", Description = "d", SystemPrompt = "p" };
            db.PublishedTenantAgents.Add(published);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.AgentTools.AddRange(new List<AgentTool>
            {
                new() { AgentId = agent.Id, ToolId = mcp.Id, FunctionPermissions = "{\"delete\":\"Block\"}" },
                new() { AgentId = agent.Id, ToolId = http.Id, ToolPermission = "Ask" },
                new() { AgentId = agent.Id, ToolId = contactForm.Id, ToolPermission = "Block" },
                new() { AgentId = agent.Id, ToolId = inactive.Id },
                new() { AgentId = agent.Id, ToolId = deleted.Id },
                new() { AgentId = other.Id, ToolId = mcp.Id },
                new() { AgentId = deletedAgent.Id, ToolId = http.Id },
                new() { AgentId = published.Id, ToolId = mcp.Id }
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            return new Seed(connection, options, agent.Id, other.Id, mcp.Id, http.Id, contactForm.Id, inactive.Id, deleted.Id, unlinked.Id);
        }
    }
}
