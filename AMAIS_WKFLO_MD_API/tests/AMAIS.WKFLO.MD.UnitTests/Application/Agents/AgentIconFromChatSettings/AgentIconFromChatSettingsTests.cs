using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateAgentChatSetting;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateAgentChatSetting;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentById;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgents;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.AgentIconFromChatSettings
{
    /// <summary>
    /// Bug 8218: agents created directly in WKFLO only store their icon in AgentChatSetting.ChatbotIconPath
    /// (keyed to the PublishedTenantAgent), so Agent.IconUrl (what TenantAgent.List/Get expose to the CMS)
    /// stayed null. Covers the write-through in the chat-setting handlers and the read fallback in the
    /// List/Get handlers. Uses a real SQLite-backed MasterDataDbContext (same approach as
    /// ShareTenantAgentCommandHandlerIconUrlTests).
    /// </summary>
    public class AgentIconFromChatSettingsTests
    {
        private const string TenantName = "T1";
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private sealed class Seed
        {
            public DbContextOptions<MasterDataDbContext> Options { get; init; }
            public int TenantAgentId { get; init; }
            public int PublishedAgentId { get; init; }
        }

        private static IDateTimeProvider Clock() => Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

        private static SqliteTestMasterDataDbContext NewDb(DbContextOptions<MasterDataDbContext> options)
            => new(options, Clock());

        private static async Task<DbContextOptions<MasterDataDbContext>> CreateDatabaseAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;
            await using var schemaContext = NewDb(options);
            await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            return options;
        }

        /// <summary>Seeds one tenant agent (optionally published) and returns their ids.</summary>
        private static async Task<Seed> SeedAgentAsync(
            DbContextOptions<MasterDataDbContext> options,
            Tenant existingTenant = null,
            string name = "Agent",
            string tenantAgentIconUrl = null,
            bool published = true)
        {
            await using var db = NewDb(options);
            var tenant = existingTenant != null
                ? await db.Tenants.SingleAsync(t => t.Id == existingTenant.Id, TestContext.Current.CancellationToken)
                : new Tenant { Name = TenantName };
            if (existingTenant == null)
            {
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var tenantAgent = new TenantAgent
            {
                TenantId = tenant.Id,
                Name = name,
                Description = "An agent",
                SystemPrompt = "You are a helpful assistant.",
                IconUrl = tenantAgentIconUrl
            };
            db.TenantAgents.Add(tenantAgent);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var publishedId = 0;
            if (published)
            {
                var publishedAgent = WkfloMapperExtensions.CreateMapper().Map<PublishedTenantAgent>(tenantAgent);
                publishedAgent.TenantId = tenant.Id;
                publishedAgent.CreatedByTenantAgentId = tenantAgent.Id;
                db.PublishedTenantAgents.Add(publishedAgent);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
                publishedId = publishedAgent.Id;
            }

            return new Seed { Options = options, TenantAgentId = tenantAgent.Id, PublishedAgentId = publishedId };
        }

        private static async Task AddChatSettingAsync(
            DbContextOptions<MasterDataDbContext> options,
            int agentId,
            string iconPath,
            bool isWidget,
            DateTimeOffset? lastUpdatedOn = null)
        {
            await using var db = NewDb(options);
            db.AgentChatSettings.Add(new AgentChatSetting
            {
                AgentId = agentId,
                IsWidget = isWidget,
                ChatbotIconPath = iconPath,
                LastUpdatedOn = lastUpdatedOn
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        private static UpdateAgentChatSettingCommand UpdateCommand(int id, string iconPath, bool isWidget = false) => new()
        {
            Id = id,
            PrimaryColor = "#112233",
            ChatbotIconPath = iconPath,
            IsWidget = isWidget
        };

        private static async Task<Result> RunUpdateAsync(DbContextOptions<MasterDataDbContext> options, UpdateAgentChatSettingCommand command)
        {
            await using var db = NewDb(options);
            var handler = new UpdateAgentChatSettingCommandHandler(
                db,
                WkfloMapperExtensions.CreateMapper(),
                new TenantContext { TenantName = TenantName },
                Mock.Of<ILogger<UpdateAgentChatSettingCommandHandler>>());
            return await handler.Handle(command, TestContext.Current.CancellationToken);
        }

        private static async Task<(string TenantIcon, string PublishedIcon)> ReadIconsAsync(Seed seed)
        {
            await using var db = NewDb(seed.Options);
            var tenantIcon = (await db.TenantAgents.AsNoTracking().SingleAsync(a => a.Id == seed.TenantAgentId, TestContext.Current.CancellationToken)).IconUrl;
            var publishedIcon = (await db.PublishedTenantAgents.AsNoTracking().SingleAsync(a => a.Id == seed.PublishedAgentId, TestContext.Current.CancellationToken)).IconUrl;
            return (tenantIcon, publishedIcon);
        }

        // ---------- Write-through: UpdateAgentChatSettingCommandHandler ----------

        [Fact]
        public async Task Update_NewRow_WritesChatIconToTenantAndPublishedAgent()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync());

            var result = await RunUpdateAsync(seed.Options, UpdateCommand(seed.TenantAgentId, "https://static.example.com/chat.png"));

            Assert.IsType<SuccessResult<AgentChatSettingUpdateResult>>(result);
            var (tenantIcon, publishedIcon) = await ReadIconsAsync(seed);
            Assert.Equal("https://static.example.com/chat.png", tenantIcon);
            Assert.Equal("https://static.example.com/chat.png", publishedIcon);
        }

        [Fact]
        public async Task Update_ExistingRow_WritesChatIconToTenantAndPublishedAgent()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync());
            await AddChatSettingAsync(seed.Options, seed.PublishedAgentId, "https://static.example.com/old.png", isWidget: false);

            var result = await RunUpdateAsync(seed.Options, UpdateCommand(seed.TenantAgentId, "https://static.example.com/new.png"));

            Assert.IsType<SuccessResult<AgentChatSettingUpdateResult>>(result);
            var (tenantIcon, publishedIcon) = await ReadIconsAsync(seed);
            Assert.Equal("https://static.example.com/new.png", tenantIcon);
            Assert.Equal("https://static.example.com/new.png", publishedIcon);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Update_NewRow_EmptyChatIcon_DoesNotClearExistingIconUrl(string emptyIcon)
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync(), tenantAgentIconUrl: "https://static.example.com/template.png");

            await RunUpdateAsync(seed.Options, UpdateCommand(seed.TenantAgentId, emptyIcon));

            var (tenantIcon, _) = await ReadIconsAsync(seed);
            Assert.Equal("https://static.example.com/template.png", tenantIcon);
        }

        [Fact]
        public async Task Update_ExistingRow_EmptyChatIcon_DoesNotClearExistingIconUrl()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync(), tenantAgentIconUrl: "https://static.example.com/template.png");
            await AddChatSettingAsync(seed.Options, seed.PublishedAgentId, "https://static.example.com/old.png", isWidget: false);

            await RunUpdateAsync(seed.Options, UpdateCommand(seed.TenantAgentId, null));

            var (tenantIcon, _) = await ReadIconsAsync(seed);
            Assert.Equal("https://static.example.com/template.png", tenantIcon);
        }

        [Fact]
        public async Task Update_ChatIconOverwritesTemplateIconUrl()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync(), tenantAgentIconUrl: "https://static.example.com/template.png");

            await RunUpdateAsync(seed.Options, UpdateCommand(seed.TenantAgentId, "https://static.example.com/custom.png"));

            var (tenantIcon, publishedIcon) = await ReadIconsAsync(seed);
            Assert.Equal("https://static.example.com/custom.png", tenantIcon);
            Assert.Equal("https://static.example.com/custom.png", publishedIcon);
        }

        // ---------- Write-through: CreateAgentChatSettingCommandHandler ----------

        [Fact]
        public async Task Create_WithChatIcon_WritesIconUrlToTenantAgent()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync());

            await using (var db = NewDb(seed.Options))
            {
                var handler = new CreateAgentChatSettingCommandHandler(
                    db,
                    WkfloMapperExtensions.CreateMapper(),
                    new TenantContext { TenantName = TenantName },
                    Mock.Of<ILogger<CreateAgentChatSettingCommandHandler>>());
                var result = await handler.Handle(
                    new CreateAgentChatSettingCommand { Id = seed.TenantAgentId, PrimaryColor = "#112233", ChatbotIconPath = "https://static.example.com/created.png" },
                    TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<AgentChatSettingCreateResult>>(result);
            }

            var (tenantIcon, publishedIcon) = await ReadIconsAsync(seed);
            Assert.Equal("https://static.example.com/created.png", tenantIcon);
            Assert.Equal("https://static.example.com/created.png", publishedIcon);
        }

        [Fact]
        public async Task Create_WithoutChatIcon_DoesNotClearExistingIconUrl()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync(), tenantAgentIconUrl: "https://static.example.com/template.png");

            await using (var db = NewDb(seed.Options))
            {
                var handler = new CreateAgentChatSettingCommandHandler(
                    db,
                    WkfloMapperExtensions.CreateMapper(),
                    new TenantContext { TenantName = TenantName },
                    Mock.Of<ILogger<CreateAgentChatSettingCommandHandler>>());
                await handler.Handle(
                    new CreateAgentChatSettingCommand { Id = seed.TenantAgentId, PrimaryColor = "#112233" },
                    TestContext.Current.CancellationToken);
            }

            var (tenantIcon, _) = await ReadIconsAsync(seed);
            Assert.Equal("https://static.example.com/template.png", tenantIcon);
        }

        // ---------- Read fallback: ListTenantAgentsQueryHandler ----------

        private static async Task<List<TenantAgentGetResult>> RunListAsync(DbContextOptions<MasterDataDbContext> options)
        {
            await using var db = NewDb(options);
            var handler = new ListTenantAgentsQueryHandler(db, WkfloMapperExtensions.CreateMapper(), new TenantContext { TenantName = TenantName });
            var result = await handler.Handle(new ListTenantAgentsQuery(), TestContext.Current.CancellationToken);
            return Assert.IsType<SuccessResult<List<TenantAgentGetResult>>>(result).Data;
        }

        [Fact]
        public async Task List_FallsBackToChatIcon_WhenIconUrlEmpty_AndKeepsExistingIconUrl_AndNullWhenNeither()
        {
            var options = await CreateDatabaseAsync();
            var fromChat = await SeedAgentAsync(options, name: "FromChat");
            var tenant = await NewDb(options).Tenants.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
            var withIcon = await SeedAgentAsync(options, tenant, name: "WithIcon", tenantAgentIconUrl: "https://static.example.com/own.png");
            var neither = await SeedAgentAsync(options, tenant, name: "Neither");
            var unpublished = await SeedAgentAsync(options, tenant, name: "Unpublished", published: false);

            await AddChatSettingAsync(options, fromChat.PublishedAgentId, "https://static.example.com/chat.png", isWidget: false);
            await AddChatSettingAsync(options, withIcon.PublishedAgentId, "https://static.example.com/ignored.png", isWidget: false);

            var data = await RunListAsync(options);

            Assert.Equal("https://static.example.com/chat.png", data.Single(a => a.Id == fromChat.TenantAgentId).IconUrl);
            Assert.Equal("https://static.example.com/own.png", data.Single(a => a.Id == withIcon.TenantAgentId).IconUrl);
            Assert.Null(data.Single(a => a.Id == neither.TenantAgentId).IconUrl);
            Assert.Null(data.Single(a => a.Id == unpublished.TenantAgentId).IconUrl);
        }

        [Fact]
        public async Task List_Fallback_PrefersWidgetRow_ThenMostRecentlyUpdated()
        {
            var options = await CreateDatabaseAsync();
            var widgetPreferred = await SeedAgentAsync(options, name: "WidgetPreferred");
            var tenant = await NewDb(options).Tenants.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
            var recencyPreferred = await SeedAgentAsync(options, tenant, name: "RecencyPreferred");

            // Widget row wins even though the non-widget row is newer.
            await AddChatSettingAsync(options, widgetPreferred.PublishedAgentId, "https://static.example.com/widget.png", isWidget: true, lastUpdatedOn: Now.AddDays(-5));
            await AddChatSettingAsync(options, widgetPreferred.PublishedAgentId, "https://static.example.com/non-widget.png", isWidget: false, lastUpdatedOn: Now);

            // Three rows with the SAME IsWidget value: the most recently updated wins, with a null
            // LastUpdatedOn falling back to CreatedOn (the audit clock, Now). Newest (Now + 2 days) beats
            // the null row (CreatedOn = Now), which beats the older explicit one (Now - 5 days).
            await AddChatSettingAsync(options, recencyPreferred.PublishedAgentId, "https://static.example.com/old.png", isWidget: false, lastUpdatedOn: Now.AddDays(-5));
            await AddChatSettingAsync(options, recencyPreferred.PublishedAgentId, "https://static.example.com/null-updated.png", isWidget: false, lastUpdatedOn: null);
            await AddChatSettingAsync(options, recencyPreferred.PublishedAgentId, "https://static.example.com/newest.png", isWidget: false, lastUpdatedOn: Now.AddDays(2));

            var data = await RunListAsync(options);

            Assert.Equal("https://static.example.com/widget.png", data.Single(a => a.Id == widgetPreferred.TenantAgentId).IconUrl);
            Assert.Equal("https://static.example.com/newest.png", data.Single(a => a.Id == recencyPreferred.TenantAgentId).IconUrl);
        }

        [Fact]
        public async Task GetById_Fallback_SameWidgetFlag_NullLastUpdatedFallsBackToCreatedOn()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync());

            // Null LastUpdatedOn -> CreatedOn (Now) is newer than the explicit Now - 5 days row.
            await AddChatSettingAsync(seed.Options, seed.PublishedAgentId, "https://static.example.com/old.png", isWidget: false, lastUpdatedOn: Now.AddDays(-5));
            await AddChatSettingAsync(seed.Options, seed.PublishedAgentId, "https://static.example.com/null-updated.png", isWidget: false, lastUpdatedOn: null);

            var data = await RunGetAsync(seed.Options, seed.TenantAgentId);

            Assert.Equal("https://static.example.com/null-updated.png", data.IconUrl);
        }

        [Fact]
        public async Task Fallback_WhitespaceOnlyChatIcon_IsIgnored_InFavourOfUsableIcon()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync());

            // The whitespace widget row would win the widget-first ordering if it were not filtered out.
            await AddChatSettingAsync(seed.Options, seed.PublishedAgentId, "   ", isWidget: true, lastUpdatedOn: Now.AddDays(2));
            await AddChatSettingAsync(seed.Options, seed.PublishedAgentId, "https://static.example.com/usable.png", isWidget: false, lastUpdatedOn: Now);

            var single = await RunGetAsync(seed.Options, seed.TenantAgentId);
            var list = await RunListAsync(seed.Options);

            Assert.Equal("https://static.example.com/usable.png", single.IconUrl);
            Assert.Equal("https://static.example.com/usable.png", list.Single(a => a.Id == seed.TenantAgentId).IconUrl);
        }

        [Fact]
        public async Task Fallback_OnlyWhitespaceChatIcon_ReturnsNull()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync());
            await AddChatSettingAsync(seed.Options, seed.PublishedAgentId, "   ", isWidget: true);

            var data = await RunGetAsync(seed.Options, seed.TenantAgentId);

            Assert.Null(data.IconUrl);
        }

        [Fact]
        public async Task Create_AgentWithoutPublishedTenantAgent_WritesIconUrlToTenantAgentOnly()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync(), published: false);

            await using (var db = NewDb(seed.Options))
            {
                var handler = new CreateAgentChatSettingCommandHandler(
                    db,
                    WkfloMapperExtensions.CreateMapper(),
                    new TenantContext { TenantName = TenantName },
                    Mock.Of<ILogger<CreateAgentChatSettingCommandHandler>>());
                var result = await handler.Handle(
                    new CreateAgentChatSettingCommand { Id = seed.TenantAgentId, PrimaryColor = "#112233", ChatbotIconPath = "https://static.example.com/created.png" },
                    TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<AgentChatSettingCreateResult>>(result);
            }

            await using var verifyDb = NewDb(seed.Options);
            var tenantAgent = await verifyDb.TenantAgents.AsNoTracking().SingleAsync(a => a.Id == seed.TenantAgentId, TestContext.Current.CancellationToken);
            Assert.Equal("https://static.example.com/created.png", tenantAgent.IconUrl);
        }

        // ---------- Read fallback: GetTenantAgentByIdQueryHandler ----------

        private static async Task<TenantAgentGetResult> RunGetAsync(DbContextOptions<MasterDataDbContext> options, int id)
        {
            await using var db = NewDb(options);
            var handler = new GetTenantAgentByIdQueryHandler(db, WkfloMapperExtensions.CreateMapper(), new TenantContext { TenantName = TenantName });
            var result = await handler.Handle(new GetTenantAgentByIdQuery { Id = id }, TestContext.Current.CancellationToken);
            return Assert.IsType<SuccessResult<TenantAgentGetResult>>(result).Data;
        }

        [Fact]
        public async Task GetById_FallsBackToChatIcon_WhenIconUrlEmpty()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync());
            await AddChatSettingAsync(seed.Options, seed.PublishedAgentId, "https://static.example.com/chat.png", isWidget: true);

            var data = await RunGetAsync(seed.Options, seed.TenantAgentId);

            Assert.Equal("https://static.example.com/chat.png", data.IconUrl);
        }

        [Fact]
        public async Task GetById_KeepsExistingIconUrl_WhenChatIconAlsoPresent()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync(), tenantAgentIconUrl: "https://static.example.com/own.png");
            await AddChatSettingAsync(seed.Options, seed.PublishedAgentId, "https://static.example.com/chat.png", isWidget: true);

            var data = await RunGetAsync(seed.Options, seed.TenantAgentId);

            Assert.Equal("https://static.example.com/own.png", data.IconUrl);
        }

        [Fact]
        public async Task GetById_NoIconAnywhere_ReturnsNull()
        {
            var seed = await SeedAgentAsync(await CreateDatabaseAsync());

            var data = await RunGetAsync(seed.Options, seed.TenantAgentId);

            Assert.Null(data.IconUrl);
        }
    }
}
