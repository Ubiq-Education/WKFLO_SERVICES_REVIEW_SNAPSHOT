using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.ListPublicAgentConversations;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.ListPublicAgentConversations
{
    /// <summary>
    /// SQLite cannot aggregate, order or compare DateTimeOffset values (SQL Server, the provider this context
    /// runs against in production, maps DATETIMEOFFSET natively). This test-only subclass stores them as
    /// ticks-based binary (the same workaround as McpOAuthConsentServiceMultiReplicaTests in the MD unit tests),
    /// so the list's real GROUP BY / MAX / HAVING / ORDER BY / TOP query can run on a relational provider.
    /// Production code and its <see cref="MasterDataDbContext"/> base are untouched.
    /// </summary>
    internal sealed class DateTimeOffsetAsBinarySqliteMasterDataDbContext : MasterDataDbContext
    {
        public DateTimeOffsetAsBinarySqliteMasterDataDbContext(DbContextOptions<MasterDataDbContext> options, IDateTimeProvider dateTimeProvider)
            : base(options, dateTimeProvider)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var dateTimeOffsetProperties = entityType.ClrType
                    .GetProperties()
                    .Where(p => p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(DateTimeOffset?));

                foreach (var property in dateTimeOffsetProperties)
                {
                    modelBuilder.Entity(entityType.Name).Property(property.Name)
                        .HasConversion(new DateTimeOffsetToBinaryConverter());
                }
            }
        }
    }

    /// <summary>
    /// US 7507: runs <see cref="ListPublicAgentConversationsQueryHandler"/>'s queries on a real relational provider
    /// (SQLite in-memory), which MockQueryable (ListPublicAgentConversationsQueryHandlerTests) cannot prove: that
    /// the GroupBy / MAX(COALESCE) / HAVING / ORDER BY / TOP list query translates, and that the 90-day window,
    /// the newest-first order and the 50 cap are applied by the database.
    /// </summary>
    public sealed class ListPublicAgentConversationsQueryHandlerRelationalTests : IDisposable
    {
        private const string TenantName = "T1";
        private const string CallerGuid = "11111111-1111-1111-1111-111111111111";
        private const string OtherUserGuid = "22222222-2222-2222-2222-222222222222";
        private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        private readonly SqliteConnection _connection;
        private readonly MasterDataDbContext _db;
        private readonly string _shareId = Guid.NewGuid().ToString();
        private readonly int _tenantId;
        private readonly int _maisyId;
        private readonly int _pagesAgentId;

        public ListPublicAgentConversationsQueryHandlerRelationalTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            _connection.Open();
            _db = new DateTimeOffsetAsBinarySqliteMasterDataDbContext(
                new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(_connection).Options, Mock.Of<IDateTimeProvider>());
            _db.Database.EnsureCreated();

            var tenant = new Tenant { Name = TenantName };
            _db.Tenants.Add(tenant);
            _db.SaveChanges();
            _tenantId = tenant.Id;

            var maisy = new PublishedTenantAgent { TenantId = tenant.Id, Name = "MAISY", Description = "Your AI assistant", SystemPrompt = "p", IsActive = true };
            var pagesAgent = new PublishedTenantAgent { TenantId = tenant.Id, Name = "Pages Agent", Description = "Creates and edits pages", SystemPrompt = "p", IsActive = true };
            _db.PublishedTenantAgents.AddRange(maisy, pagesAgent);
            _db.SaveChanges();
            _maisyId = maisy.Id;
            _pagesAgentId = pagesAgent.Id;

            _db.AgentChatSettings.Add(new AgentChatSetting { ShareId = _shareId, AgentId = maisy.Id, IsShareable = true });
            var tag = new Tag { Name = FeatureTagConsts.Maisy, TenantId = tenant.Id };
            _db.Tags.Add(tag);
            _db.SaveChanges();
            _db.EntityTags.Add(new EntityTag { TagId = tag.Id, EntityType = TaggableEntityTypes.Agent, EntityId = maisy.Id });
            _db.SaveChanges();
        }

        private void AddRow(string sessionId, DateTimeOffset? lastActivityAt, int? agentId = null, string userId = CallerGuid,
            DateTimeOffset? createdOn = null, DateTimeOffset? lastUpdatedOn = null, bool isTestChat = false)
        {
            _db.Conversations.Add(new Conversation
            {
                SessionId = sessionId,
                AgentId = agentId ?? _maisyId,
                TenantId = _tenantId,
                UserId = userId,
                Name = $"Title {sessionId}",
                PreviewLine = $"Preview {sessionId}",
                IsTestChat = isTestChat,
                LastActivityAt = lastActivityAt,
                LastUpdatedOn = lastUpdatedOn,
                CreatedOn = createdOn ?? Now.AddDays(-100)
            });
        }

        private async Task<List<PublicAgentConversationResult>> ListAsync()
        {
            _db.SaveChanges();
            _db.ChangeTracker.Clear();

            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);
            var handler = new ListPublicAgentConversationsQueryHandler(_db, new TenantContext { TenantName = TenantName }, new UserContext { Guid = CallerGuid }, dateTimeProvider.Object);

            var result = await handler.Handle(new ListPublicAgentConversationsQuery { ShareId = _shareId }, TestContext.Current.CancellationToken);

            return Assert.IsType<SuccessResult<List<PublicAgentConversationResult>>>(result).Data;
        }

        [Fact]
        public async Task Handle_OnlyListsSessionsActiveInTheLast90Days()
        {
            AddRow("inside-window", Now.AddDays(-89));
            AddRow("outside-window", Now.AddDays(-91));
            // No LastActivityAt: falls back to LastUpdatedOn, then CreatedOn (the COALESCE).
            AddRow("fallback-updated-inside", lastActivityAt: null, lastUpdatedOn: Now.AddDays(-10));
            AddRow("fallback-created-outside", lastActivityAt: null, createdOn: Now.AddDays(-120));

            var items = await ListAsync();

            Assert.Equal(new[] { "fallback-updated-inside", "inside-window" }, items.Select(i => i.SessionId));
        }

        [Fact]
        public async Task Handle_OrdersByLatestActivityAcrossEveryAgentInTheSession()
        {
            AddRow("older", Now.AddDays(-5));
            AddRow("newer", Now.AddDays(-2));
            // A spun-up agent's later activity on "older" moves it to the top, even though MAISY's own row is older.
            AddRow("older", Now.AddHours(-1), agentId: _pagesAgentId, createdOn: Now.AddDays(-5));

            var items = await ListAsync();

            Assert.Equal(new[] { "older", "newer" }, items.Select(i => i.SessionId));
            Assert.Equal(Now.AddHours(-1), items[0].LastActivityAt);
            Assert.Equal(new[] { "MAISY", "Pages Agent" }, items[0].Agents.Select(a => a.Name));
        }

        [Fact]
        public async Task Handle_ExcludesOtherUsersAndTestChats()
        {
            AddRow("mine", Now.AddDays(-1));
            AddRow("someone-else", Now.AddDays(-1), userId: OtherUserGuid);
            AddRow("test-chat", Now.AddDays(-1), isTestChat: true);

            var items = await ListAsync();

            Assert.Equal("mine", Assert.Single(items).SessionId);
        }

        [Fact]
        public async Task Handle_ReturnsAtMost50NewestSessions()
        {
            for (var i = 0; i < ListPublicAgentConversationsQueryHandler.MaxConversations + 5; i++)
                AddRow($"session-{i:D2}", Now.AddMinutes(-i));

            var items = await ListAsync();

            Assert.Equal(ListPublicAgentConversationsQueryHandler.MaxConversations, items.Count);
            Assert.Equal("session-00", items[0].SessionId);
            Assert.Equal($"session-{ListPublicAgentConversationsQueryHandler.MaxConversations - 1:D2}", items[^1].SessionId);
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }
    }
}
