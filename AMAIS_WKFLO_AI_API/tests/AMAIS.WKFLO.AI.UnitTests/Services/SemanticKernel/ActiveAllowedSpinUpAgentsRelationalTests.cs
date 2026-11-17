using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadSiblingAgentContextBySession;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Regression (WI 7318 follow-up): the "active, non-deleted allowlisted spin-up agent" filter must be
    /// translatable by a REAL relational EF Core provider. A type test/cast to the abstract, unmapped
    /// <see cref="RunnableAgent"/> works under LINQ-to-objects (MockQueryable) but cannot be translated to SQL,
    /// which broke every chat turn of an agent with EnableSpinUpAgents. Runs against SQLite in-memory.
    /// </summary>
    public sealed class ActiveAllowedSpinUpAgentsRelationalTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly MasterDataDbContext _db;
        private readonly int _rootId;

        public ActiveAllowedSpinUpAgentsRelationalTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            _connection.Open();
            _db = new MasterDataDbContext(new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(_connection).Options, Mock.Of<IDateTimeProvider>());
            _db.Database.EnsureCreated();

            var tenant = new Tenant { Name = "T1" };
            _db.Tenants.Add(tenant);
            _db.SaveChanges();

            TenantAgent Tenant(string name, bool isActive, bool isDeleted = false) =>
                new() { TenantId = tenant.Id, Name = name, Description = $"{name} description", SystemPrompt = "p", IsActive = isActive, IsDeleted = isDeleted };

            var root = Tenant("Root", true);
            root.EnableSpinUpAgents = true;
            var activeChild = Tenant("Active child", true);
            var inactiveChild = Tenant("Inactive child", false);
            var deletedChild = Tenant("Deleted child", true, isDeleted: true);
            var publishedChild = new PublishedTenantAgent { TenantId = tenant.Id, Name = "Published child", Description = "d", SystemPrompt = "p", IsActive = true };
            var inactivePublishedChild = new PublishedTenantAgent { TenantId = tenant.Id, Name = "Inactive published child", Description = "d", SystemPrompt = "p", IsActive = false };
            _db.TenantAgents.AddRange(root, activeChild, inactiveChild, deletedChild);
            _db.PublishedTenantAgents.AddRange(publishedChild, inactivePublishedChild);
            _db.SaveChanges();

            foreach (var child in new Agent[] { activeChild, inactiveChild, deletedChild, publishedChild, inactivePublishedChild })
            {
                _db.AgentAllowedSpinUpAgents.Add(new AgentAllowedSpinUpAgent { AgentId = root.Id, AllowedSpinUpAgentId = child.Id });
            }
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
            _rootId = root.Id;
        }

        [Fact]
        public async Task QueryActiveAllowedSpinUpTargets_TranslatesAndKeepsOnlyActiveNonDeletedTargets()
        {
            var targets = await SemanticKernelAgentService
                .QueryActiveAllowedSpinUpTargets(_db.AgentAllowedSpinUpAgents.AsNoTracking(), _rootId)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new[] { "Active child", "Published child" }, targets.Select(t => t.Name).OrderBy(n => n));
        }

        [Fact]
        public async Task LoadSiblingAgentContext_AllowlistQuery_TranslatesOnRealProvider()
        {
            var root = await _db.TenantAgents.AsNoTracking().SingleAsync(a => a.Id == _rootId, TestContext.Current.CancellationToken);
            var handler = new LoadSiblingAgentContextBySessionQueryHandler(_db, Mock.Of<IChatStoreService>(), Mock.Of<IBlobStorageProvider>(),
                new TenantContext { TenantName = "T1" }, new UserContext());

            // No conversation rows exist, so the handler returns empty right after the allowlist query - the
            // point is that the query itself no longer throws "could not be translated".
            var result = await handler.Handle(new LoadSiblingAgentContextBySessionQuery { Agent = root, SessionId = "session-1" }, TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }
    }
}
