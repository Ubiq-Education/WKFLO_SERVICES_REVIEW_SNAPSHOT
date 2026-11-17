using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
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
    /// Covers resolving the shared MarketplaceAgent.IconUrl from either the configured chat icon
    /// (AgentChatSetting.ChatbotIconPath) or PublishedTenantAgent.IconUrl — see
    /// ShareTenantAgentCommandHandler's icon-resolution block and Agent.IconUrl's doc comment.
    /// PublishedTenantAgent.IconUrl wins when set; the chat-widget icon is only a fallback. Uses a
    /// real SQLite-backed MasterDataDbContext (same approach as
    /// ShareTenantAgentCommandHandlerAuditRegressionTests) since the scenario needs an actual
    /// AgentChatSetting row joined by AgentId, not just mocked DbSets.
    /// <para>
    /// AgentChatSetting rows are seeded keyed to the PUBLISHED agent's id, not the draft TenantAgent's
    /// id — verified against UpdateAgentChatSettingCommandHandler (the handler behind the only
    /// chat-setting save endpoint wkflo_ui actually calls), which always resolves and writes
    /// AgentChatSetting.AgentId = tenantAgent.PublishedTenantAgent.Id. Seeding against tenantAgent.Id
    /// instead would exercise a row shape that's never produced by any real write path, and previously
    /// only passed because the handler under test had the same (now-fixed) bug.
    /// </para>
    /// </summary>
    public class ShareTenantAgentCommandHandlerIconUrlTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private static async Task<(DbContextOptions<MasterDataDbContext> Options, int TenantAgentId)> SeedAsync(
            string chatbotIconPath,
            bool isWidget = true,
            string publishedAgentIconUrl = null)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var providerMock = new Mock<IDateTimeProvider>();
            providerMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            int tenantAgentId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, providerMock.Object))
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

                // Published first, so AgentChatSetting rows below can be keyed to its real id — matching
                // UpdateAgentChatSettingCommandHandler's actual write behavior (see class doc comment).
                var publishedAgent = WkfloMapperExtensions.CreateMapper().Map<PublishedTenantAgent>(tenantAgent);
                publishedAgent.TenantId = tenant.Id;
                publishedAgent.CreatedByTenantAgentId = tenantAgent.Id;
                publishedAgent.IconUrl = publishedAgentIconUrl;
                setupDb.PublishedTenantAgents.Add(publishedAgent);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);

                if (chatbotIconPath != null)
                {
                    setupDb.AgentChatSettings.Add(new AgentChatSetting
                    {
                        AgentId = publishedAgent.Id,
                        IsWidget = isWidget,
                        ChatbotIconPath = chatbotIconPath
                    });
                    await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                }
            }

            return (options, tenantAgentId);
        }

        [Fact]
        public async Task Handle_TenantAgentHasChatIcon_CopiesItToMarketplaceAgentIconUrl()
        {
            var (options, tenantAgentId) = await SeedAsync("https://static.example.com/tenant-agent-icon.png");

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };
            var mediator = Mock.Of<IWkfloMediator>();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new ShareTenantAgentCommandHandler(db, mapper, tenantContext, mediator, dateTimeProvider);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var marketplaceAgent = await verifyDb.MarketplaceAgents.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("https://static.example.com/tenant-agent-icon.png", marketplaceAgent.IconUrl);
        }

        [Fact]
        public async Task Handle_TenantAgentHasNoChatIcon_LeavesMarketplaceAgentIconUrlNull()
        {
            var (options, tenantAgentId) = await SeedAsync(chatbotIconPath: null);

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };
            var mediator = Mock.Of<IWkfloMediator>();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new ShareTenantAgentCommandHandler(db, mapper, tenantContext, mediator, dateTimeProvider);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var marketplaceAgent = await verifyDb.MarketplaceAgents.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Null(marketplaceAgent.IconUrl);
        }

        [Fact]
        public async Task Handle_MultipleChatSettingRows_PrefersWidgetRow()
        {
            var (options, tenantAgentId) = await SeedAsync("https://static.example.com/widget.png", isWidget: true);

            await using (var db = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now)))
            {
                var publishedAgent = await db.PublishedTenantAgents.SingleAsync(a => a.CreatedByTenantAgentId == tenantAgentId, TestContext.Current.CancellationToken);
                db.AgentChatSettings.Add(new AgentChatSetting
                {
                    AgentId = publishedAgent.Id,
                    IsWidget = false,
                    ChatbotIconPath = "https://static.example.com/non-widget.png"
                });
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };
            var mediator = Mock.Of<IWkfloMediator>();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new ShareTenantAgentCommandHandler(db, mapper, tenantContext, mediator, dateTimeProvider);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var marketplaceAgent = await verifyDb.MarketplaceAgents.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("https://static.example.com/widget.png", marketplaceAgent.IconUrl);
        }

        [Fact]
        public async Task Handle_PublishedAgentIconUrlSetAndNoChatIcon_UsesPublishedAgentIconUrl()
        {
            var (options, tenantAgentId) = await SeedAsync(
                chatbotIconPath: null,
                publishedAgentIconUrl: "https://static.example.com/published-agent-icon.png");

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };
            var mediator = Mock.Of<IWkfloMediator>();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new ShareTenantAgentCommandHandler(db, mapper, tenantContext, mediator, dateTimeProvider);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var marketplaceAgent = await verifyDb.MarketplaceAgents.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("https://static.example.com/published-agent-icon.png", marketplaceAgent.IconUrl);
        }

        [Fact]
        public async Task Handle_BothPublishedAgentIconUrlAndChatIconSet_PublishedAgentIconUrlWins()
        {
            var (options, tenantAgentId) = await SeedAsync(
                chatbotIconPath: "https://static.example.com/chat-icon.png",
                publishedAgentIconUrl: "https://static.example.com/published-agent-icon.png");

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };
            var mediator = Mock.Of<IWkfloMediator>();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new ShareTenantAgentCommandHandler(db, mapper, tenantContext, mediator, dateTimeProvider);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var marketplaceAgent = await verifyDb.MarketplaceAgents.SingleAsync(TestContext.Current.CancellationToken);
            // PublishedTenantAgent.IconUrl is the more direct/authoritative signal, so it wins over the
            // tenant's chat-widget icon even though both are set.
            Assert.Equal("https://static.example.com/published-agent-icon.png", marketplaceAgent.IconUrl);
        }

        /// <summary>
        /// Re-share regression coverage: on the "reuse existing marketplace agent" branch,
        /// <c>_mapper.Map(publishedAgent, existingMarketplaceAgent)</c> runs BEFORE the icon-resolution
        /// block and blindly copies <c>publishedAgent.IconUrl</c> (Mapster's config for
        /// PublishedTenantAgent → MarketplaceAgent doesn't ignore IconUrl) onto
        /// <c>existingMarketplaceAgent.IconUrl</c> — including clobbering it back to null when
        /// <c>publishedAgent.IconUrl</c> is empty. This test's first share sets
        /// <c>publishedAgent.IconUrl</c>, so the mapper's blind copy sets the MarketplaceAgent's IconUrl
        /// to that value. Before the second share, <c>publishedAgent.IconUrl</c> is cleared but a
        /// chat-widget icon is added. The re-share's mapper call then blindly nulls the MarketplaceAgent's
        /// IconUrl again — but the icon-resolution block that runs immediately afterward re-evaluates
        /// both sources fresh and falls back to the chat icon, so the final persisted value is correct
        /// despite the mapper's intermediate wrong value.
        /// </summary>
        [Fact]
        public async Task Handle_ReShare_PublishedAgentIconUrlClearedButChatIconAdded_FinalValueIsChatIcon()
        {
            var (options, tenantAgentId) = await SeedAsync(
                chatbotIconPath: null,
                publishedAgentIconUrl: "https://static.example.com/published-agent-icon.png");

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };
            var mediator = Mock.Of<IWkfloMediator>();
            var firstShareProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            // --- First share: creates the MarketplaceAgent, IconUrl comes from publishedAgent.IconUrl ---
            await using (var db = new SqliteTestMasterDataDbContext(options, firstShareProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new ShareTenantAgentCommandHandler(db, mapper, tenantContext, mediator, firstShareProvider);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            // --- Between shares: clear publishedAgent.IconUrl, add a chat-widget icon instead ---
            var reShareNow = Now.AddDays(1);
            var reShareProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == reShareNow);
            await using (var db = new SqliteTestMasterDataDbContext(options, reShareProvider))
            {
                var publishedAgent = await db.PublishedTenantAgents.SingleAsync(TestContext.Current.CancellationToken);
                publishedAgent.IconUrl = null;
                db.PublishedTenantAgents.Update(publishedAgent);

                db.AgentChatSettings.Add(new AgentChatSetting
                {
                    AgentId = publishedAgent.Id,
                    IsWidget = true,
                    ChatbotIconPath = "https://static.example.com/chat-icon.png"
                });
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            // --- Re-share: reuses the existing MarketplaceAgent ---
            await using (var db = new SqliteTestMasterDataDbContext(options, reShareProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");
                var handler = new ShareTenantAgentCommandHandler(db, mapper, tenantContext, mediator, reShareProvider);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, reShareProvider);
            var marketplaceAgents = await verifyDb.MarketplaceAgents.ToListAsync(TestContext.Current.CancellationToken);
            var marketplaceAgent = Assert.Single(marketplaceAgents); // still the same reused row, not a duplicate
            Assert.Equal("https://static.example.com/chat-icon.png", marketplaceAgent.IconUrl);
        }

        /// <summary>
        /// Mirror of the above re-share scenario in the other direction: first share resolves IconUrl from
        /// the chat icon (publishedAgent.IconUrl unset), then before the second share publishedAgent.IconUrl
        /// is set directly. The mapper's blind copy sets the MarketplaceAgent's IconUrl to the new
        /// publishedAgent.IconUrl value, and the icon-resolution block confirms/keeps it (publishedAgent's
        /// value takes priority over the still-present chat icon) rather than reverting to the chat icon.
        /// </summary>
        [Fact]
        public async Task Handle_ReShare_PublishedAgentIconUrlSetAfterFirstShareUsedChatIcon_FinalValueIsPublishedAgentIcon()
        {
            var (options, tenantAgentId) = await SeedAsync(chatbotIconPath: "https://static.example.com/chat-icon.png");

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };
            var mediator = Mock.Of<IWkfloMediator>();
            var firstShareProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            // --- First share: creates the MarketplaceAgent, IconUrl falls back to the chat icon ---
            await using (var db = new SqliteTestMasterDataDbContext(options, firstShareProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new ShareTenantAgentCommandHandler(db, mapper, tenantContext, mediator, firstShareProvider);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            // --- Between shares: set publishedAgent.IconUrl directly (chat icon is still present) ---
            var reShareNow = Now.AddDays(1);
            var reShareProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == reShareNow);
            await using (var db = new SqliteTestMasterDataDbContext(options, reShareProvider))
            {
                var publishedAgent = await db.PublishedTenantAgents.SingleAsync(TestContext.Current.CancellationToken);
                publishedAgent.IconUrl = "https://static.example.com/published-agent-icon.png";
                db.PublishedTenantAgents.Update(publishedAgent);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            // --- Re-share: reuses the existing MarketplaceAgent ---
            await using (var db = new SqliteTestMasterDataDbContext(options, reShareProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");
                var handler = new ShareTenantAgentCommandHandler(db, mapper, tenantContext, mediator, reShareProvider);
                var result = await handler.Handle(new ShareTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentShareResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, reShareProvider);
            var marketplaceAgents = await verifyDb.MarketplaceAgents.ToListAsync(TestContext.Current.CancellationToken);
            var marketplaceAgent = Assert.Single(marketplaceAgents); // still the same reused row, not a duplicate
            Assert.Equal("https://static.example.com/published-agent-icon.png", marketplaceAgent.IconUrl);
        }
    }
}
