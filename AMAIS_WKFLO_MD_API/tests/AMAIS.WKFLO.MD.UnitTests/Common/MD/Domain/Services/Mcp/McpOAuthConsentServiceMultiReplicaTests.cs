using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp.OAuth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// SQLite has no native DATETIMEOFFSET type and — unlike equality — EF Core's SQLite provider can't
    /// translate ordering comparisons (&gt;=, &lt;=, etc.) on <see cref="DateTimeOffset"/> columns without an
    /// explicit value converter (a well-known EF Core/SQLite limitation; irrelevant on the real SQL Server
    /// provider this context normally runs against, which maps DATETIMEOFFSET natively). This test-only
    /// subclass applies the standard workaround so <c>McpOAuthConsentService.CompleteAsync</c>'s real
    /// <c>ExpiresOnUtc &gt;= now</c> query can execute against a real (if temporary) SQLite database in this
    /// regression test — production code and its <see cref="MasterDataDbContext"/> base are untouched.
    /// </summary>
    internal sealed class SqliteTestMasterDataDbContext : MasterDataDbContext
    {
        public SqliteTestMasterDataDbContext(DbContextOptions<MasterDataDbContext> options, IDateTimeProvider dateTimeProvider)
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
    /// The actual regression test for Bug 7129: proves <see cref="McpOAuthConsentService.BeginAsync"/> and
    /// <see cref="McpOAuthConsentService.CompleteAsync"/> correlate a pending flow correctly when run through
    /// two completely independent <see cref="McpOAuthConsentService"/>/<see cref="MasterDataDbContext"/>
    /// instances sharing nothing in process memory — the exact scenario (an interactive OAuth "start" landing
    /// on one Azure Container Apps replica and the provider's "callback" redirect landing on a different one)
    /// that broke the previous in-process <c>TaskCompletionSource</c>/<c>ConcurrentDictionary</c> design.
    /// </summary>
    public class McpOAuthConsentServiceMultiReplicaTests
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        // Nothing listens here — the post-token MCP handshake verification is expected to fail fast and
        // deterministically; see McpOAuthConsentServiceTests' class remarks for why that's the correct,
        // honest thing for a unit test to assert (a genuinely successful handshake needs a real MCP server,
        // covered instead by PLAN.md's manual verification against Humantic.ai).
        private const string UnreachableMcpEndpoint = "http://127.0.0.1:1/mcp";

        private static IMcpTokenProtector BuildIdentityProtector()
        {
            var protector = new Mock<IMcpTokenProtector>();
            protector.Setup(x => x.Protect(It.IsAny<string>())).Returns((string s) => s);
            protector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string s) => s);
            return protector.Object;
        }

        [Fact]
        public async Task BeginAsync_OnOneReplica_CompleteAsync_OnAnIndependentReplica_StillCorrelatesTheFlow()
        {
            // One shared database — simulating the real SQL Server every replica talks to — but every
            // MasterDataDbContext/McpOAuthConsentService pair below is independently constructed, mirroring
            // two (or three) completely separate ASP.NET Core request scopes on two separate processes.
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            var connectionId = Guid.NewGuid();
            var config = new McpServerConfig
            {
                Id = "multi-replica-test",
                Name = "Multi Replica Test Server",
                TransportType = TransportType.Http,
                Endpoint = UnreachableMcpEndpoint,
                AuthType = McpAuthType.OAuth2,
                Scopes = ["read"],
                ClientId = "preconfigured-client",
                ClientSecret = "preconfigured-secret",
                RedirectUri = "https://wkflo.example.com/md/api/v1/tenant/tool/mcp/oauth/callback"
            };

            // --- "Replica one" handles /start ---------------------------------------------------------
            await using var replicaOneDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider.Object);
            var replicaOneService = new McpOAuthConsentService(
                new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
                replicaOneDb,
                Mock.Of<IMcpOAuthTokenStore>(),
                BuildIdentityProtector(),
                dateTimeProvider.Object,
                Mock.Of<ILogger<McpOAuthConsentService>>());

            var authorizationUri = await replicaOneService.BeginAsync(config, connectionId, null, TestContext.Current.CancellationToken);
            var state = System.Web.HttpUtility.ParseQueryString(authorizationUri.Query).Get("state");
            Assert.False(string.IsNullOrEmpty(state));

            // Sanity check: replica one's own in-process view has nothing that would let CompleteAsync work
            // without the database round-trip below — there is no shared static/singleton state at all.

            // CompleteAsync only saves a token while a tool on the connection still uses the flow's client.
            await using (var seedDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider.Object))
            {
                var tenant = new Tenant { Name = "T1" };
                seedDb.Tenants.Add(tenant);
                await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                seedDb.TenantTools.Add(new TenantTool
                {
                    TenantId = tenant.Id,
                    Name = "MCP",
                    Type = AMAIS.WKFLO.Common.Models.CommonConsts.CommonToolTypeConsts.MCP,
                    Config = Newtonsoft.Json.JsonConvert.SerializeObject(config),
                    McpOAuthConnectionId = connectionId
                });
                await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            // --- "Replica two" — an entirely different process — handles the provider's callback --------
            await using var replicaTwoDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider.Object);
            var replicaTwoTokenStore = new Mock<IMcpOAuthTokenStore>();
            var replicaTwoService = new McpOAuthConsentService(
                new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"replica-two-access-token","token_type":"Bearer","expires_in":3600}""")
                })),
                replicaTwoDb,
                replicaTwoTokenStore.Object,
                BuildIdentityProtector(),
                dateTimeProvider.Object,
                Mock.Of<ILogger<McpOAuthConsentService>>());

            var completed = await replicaTwoService.CompleteAsync("auth-code", state, null, TestContext.Current.CancellationToken);

            // Replica two found and consumed the row replica one wrote purely via the database — proving
            // correctness no longer depends on both requests landing on the same process. The overall
            // `true`/`false` outcome here is secondary (the post-token MCP handshake against
            // UnreachableMcpEndpoint necessarily fails in a unit test — see the class remarks); what this
            // test exists to prove is that the token exchange happened at all, using state only replica one
            // ever knew.
            replicaTwoTokenStore.Verify(x => x.SaveAsync(
                connectionId,
                It.Is<McpOAuthTokenData>(d => d.AccessToken == "replica-two-access-token"),
                null,
                It.IsAny<System.Threading.CancellationToken>()),
                Times.Once);
            Assert.False(completed);

            // --- A third, brand-new DbContext confirms the pending flow was consumed exactly once --------
            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider.Object);
            var remaining = await verifyDb.McpOAuthPendingFlows.ToListAsync(TestContext.Current.CancellationToken);
            Assert.Empty(remaining);
        }
    }
}
