using AMAIS.WKFLO.Common.Infrastructure.Behaviours;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CompleteToolMcpOAuth;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Common.Infrastructure.Behaviours
{
    /// <summary>
    /// US 7849 follow-up: the Mediator logging pipeline serializes the whole request before the handler
    /// encrypts the client secret, so the plaintext secret (inside the JSON-string Config) must be masked in
    /// what gets logged. Also covers the audit trail, which records Tool.Config old/new values.
    /// </summary>
    public class LoggingBehaviourClientSecretRedactionTests
    {
        private const string PlaintextSecret = "pl41n-t3xt-S3cret";

        private static string McpConfig(string clientSecret = PlaintextSecret) => new JObject
        {
            ["transportType"] = "Http",
            ["endpoint"] = "https://api.canva.com/connect/v1/mcp",
            ["authType"] = "OAuth2",
            ["clientId"] = "canva-client",
            ["clientSecret"] = clientSecret
        }.ToString(Newtonsoft.Json.Formatting.Indented);

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = [];
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) =>
                Messages.Add(formatter(state, exception));
        }

        private static async Task<string> LoggedRequestAsync<TMessage>(TMessage message) where TMessage : global::Mediator.IMessage
        {
            var logger = new CapturingLogger<TMessage>();
            var behaviour = new LoggingBehaviour<TMessage, Result>(logger);

            await behaviour.Handle(message, (_, _) => ValueTask.FromResult<Result>(new SuccessResult<object>(null)), TestContext.Current.CancellationToken);

            return Assert.Single(logger.Messages, m => m.StartsWith("Handling request"));
        }

        public static IEnumerable<object[]> Commands()
        {
            yield return [new CreateTenantToolCommand { Name = "Canva", Description = "d", Type = CommonToolTypeConsts.MCP, Config = McpConfig(), IsActive = true }];
            yield return [new UpdateTenantToolCommand { Id = 1, Name = "Canva", Description = "d", Type = CommonToolTypeConsts.MCP, Config = McpConfig(), IsActive = true }];
            yield return [new CreateMarketplaceToolCommand { Name = "Canva", Description = "d", Type = CommonToolTypeConsts.MCP, Config = McpConfig() }];
            yield return [new UpdateMarketplaceToolCommand { Id = 1, Name = "Canva", Description = "d", Type = CommonToolTypeConsts.MCP, Config = McpConfig() }];
        }

        [Theory]
        [MemberData(nameof(Commands))]
        public async Task Handle_ToolCommandWithClientSecret_LoggedPayloadHasNoSecret(object command)
        {
            var logged = command switch
            {
                CreateTenantToolCommand c => await LoggedRequestAsync(c),
                UpdateTenantToolCommand c => await LoggedRequestAsync(c),
                CreateMarketplaceToolCommand c => await LoggedRequestAsync(c),
                UpdateMarketplaceToolCommand c => await LoggedRequestAsync(c),
                _ => throw new InvalidOperationException()
            };

            Assert.DoesNotContain(PlaintextSecret, logged);
            Assert.Contains("***", logged);
            // The rest of the request is still logged.
            Assert.Contains("canva-client", logged);
        }

        [Fact]
        public async Task Handle_OAuthCallbackCommand_CodeAndStateMasked_IssKept()
        {
            const string code = "eyJhbGciOiJSUzI1NiJ9.authorization-code-payload.signature";
            const string state = "csrf-state-1234567890";

            var logged = await LoggedRequestAsync(new CompleteToolMcpOAuthCommand { Code = code, State = state, Iss = "https://www.canva.com" });

            Assert.DoesNotContain(code, logged);
            Assert.DoesNotContain(state, logged);
            Assert.Contains("https://www.canva.com", logged);
        }

        private sealed class NonOAuthRequestWithCode : AMAIS.WKFLO.Common.Mediator.IWkfloRequest<Result>
        {
            public string Code { get; set; }
            public string State { get; set; }
            public string AccessToken { get; set; }
        }

        [Fact]
        public async Task Handle_NonOAuthRequest_CodeAndStateStayReadable_TokensStillMasked()
        {
            var logged = await LoggedRequestAsync(new NonOAuthRequestWithCode { Code = "WKFLOMD0403", State = "Draft", AccessToken = "at-secret-value" });

            Assert.Contains("WKFLOMD0403", logged);
            Assert.Contains("Draft", logged);
            Assert.DoesNotContain("at-secret-value", logged);
        }

        [Fact]
        public async Task Handle_RequestWithoutClientSecret_LoggedUnchanged()
        {
            var command = new UpdateTenantToolCommand { Id = 1, Name = "Http", Description = "d", Type = "HTTP", Config = "{\"url\":\"https://example.com\"}" };

            var logged = await LoggedRequestAsync(command);

            Assert.Contains(Newtonsoft.Json.JsonConvert.SerializeObject(command), logged);
        }

        [Fact]
        public async Task AuditTrail_UpdatingToolWithLegacyPlaintextSecret_DoesNotRecordTheSecret()
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(x => x.DateTimeOffsetUtcNow() == new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            await using (var schema = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
                await schema.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            int toolId;
            await using (var setup = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = new Tenant { Name = "T1" };
                setup.Tenants.Add(tenant);
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                var tool = new TenantTool { TenantId = tenant.Id, Name = "Canva", Type = CommonToolTypeConsts.MCP, Config = McpConfig() };
                setup.TenantTools.Add(tool);
                await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
                toolId = tool.Id;
            }

            await using (var update = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                update.SetAuditActor("tenant-guid", "user-1", "Alice", "alice@test.com", "corr-1");
                var tool = await update.TenantTools.FirstAsync(t => t.Id == toolId, TestContext.Current.CancellationToken);
                tool.Config = McpConfig("enc:v1:bmV3LWNpcGhlcnRleHQ=");
                await update.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using var verify = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var entries = await verify.AuditLogEntries.ToListAsync(TestContext.Current.CancellationToken);
            var toolUpdate = Assert.Single(entries, e => e.EntityType == nameof(TenantTool) && e.Action == AuditActionConsts.Update);
            Assert.Contains("Config", toolUpdate.Changes);
            Assert.DoesNotContain(PlaintextSecret, toolUpdate.Changes);
            Assert.DoesNotContain("bmV3LWNpcGhlcnRleHQ=", toolUpdate.Changes);
        }
    }
}
