using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.UpdateTenantTool
{
    /// <summary>
    /// US 7849: the stored OAuth client secret is merged on update (keep on blank, replace, clear, drop on
    /// endpoint/client id change), encrypted at rest, and a credential change resets every OAuth connection
    /// made under the old credentials.
    /// </summary>
    public class UpdateTenantToolCommandHandlerClientSecretTests
    {
        private const int ToolId = 6001;
        private const string Endpoint = "https://api.canva.com/connect/v1/mcp";
        private static readonly Guid ConnectionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid OtherConnectionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        private static readonly IMcpTokenProtector Protector = BuildProtector();

        private static IMcpTokenProtector BuildProtector()
        {
            var configuration = new Mock<IConfiguration>();
            configuration.Setup(x => x[AesMcpTokenProtector.ConfigurationKey]).Returns(Convert.ToBase64String(new byte[32]));
            return new AesMcpTokenProtector(configuration.Object);
        }

        private static string Config(string clientId = "canva-client", string clientSecret = null, string endpoint = Endpoint, bool? clear = null, bool? hasClientSecret = null)
        {
            var config = new JObject
            {
                ["id"] = "canva",
                ["name"] = "Canva",
                ["transportType"] = "Http",
                ["endpoint"] = endpoint,
                ["authType"] = "OAuth2",
                ["oAuthScope"] = "PerUser",
                ["clientId"] = clientId
            };
            if (clientSecret != null) config["clientSecret"] = clientSecret;
            if (clear.HasValue) config["clearClientSecret"] = clear.Value;
            if (hasClientSecret.HasValue) config["hasClientSecret"] = hasClientSecret.Value;
            return config.ToString(Newtonsoft.Json.Formatting.None);
        }

        private sealed class Fixture
        {
            public UpdateTenantToolCommandHandler Handler { get; init; }
            public TenantTool Tool { get; init; }
            public Mock<IMasterDataDbContext> Db { get; init; }
            public Mock<Microsoft.EntityFrameworkCore.DbSet<ToolOAuthToken>> Tokens { get; init; }
            public Mock<Microsoft.EntityFrameworkCore.DbSet<McpOAuthPendingFlow>> Flows { get; init; }
        }

        private static Fixture Build(string storedConfig)
        {
            var tool = new TenantTool
            {
                Id = ToolId,
                Name = "Canva",
                Description = "Canva MCP",
                Type = CommonToolTypeConsts.MCP,
                Config = storedConfig,
                McpOAuthConnectionId = ConnectionId,
                IsActive = true
            };

            var tokens = new List<ToolOAuthToken>
            {
                new() { Id = 1, McpOAuthConnectionId = ConnectionId, UserId = null },
                new() { Id = 2, McpOAuthConnectionId = ConnectionId, UserId = Guid.NewGuid() },
                new() { Id = 3, McpOAuthConnectionId = ConnectionId, UserId = Guid.NewGuid() },
                new() { Id = 4, McpOAuthConnectionId = OtherConnectionId, UserId = null }
            }.BuildMockDbSet();

            var flows = new List<McpOAuthPendingFlow>
            {
                new() { State = "s1", ConnectionId = ConnectionId },
                new() { State = "s2", ConnectionId = OtherConnectionId }
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(new List<TenantTool> { tool }.BuildMockDbSet().Object);
            db.Setup(x => x.ToolOAuthTokens).Returns(tokens.Object);
            db.Setup(x => x.McpOAuthPendingFlows).Returns(flows.Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var handler = new UpdateTenantToolCommandHandler(db.Object, WkfloMapperExtensions.CreateMapper(),
                Options.Create(new McpOAuthSettings { PublicBaseUrl = "https://wkflo.example.com" }), Protector);

            return new Fixture { Handler = handler, Tool = tool, Db = db, Tokens = tokens, Flows = flows };
        }

        private static UpdateTenantToolCommand Command(string config) => new()
        {
            Id = ToolId,
            Name = "Canva",
            Description = "Canva MCP",
            Type = CommonToolTypeConsts.MCP,
            Config = config,
            IsActive = true
        };

        private static string StoredSecret(TenantTool tool) => JObject.Parse(tool.Config).Value<string>("clientSecret");

        private static void VerifyConnectionReset(Fixture fixture)
        {
            fixture.Tokens.Verify(x => x.RemoveRange(It.Is<IEnumerable<ToolOAuthToken>>(rows =>
                rows.Select(r => r.Id).OrderBy(id => id).SequenceEqual(new[] { 1, 2, 3 }))), Times.Once);
            fixture.Flows.Verify(x => x.RemoveRange(It.Is<IEnumerable<McpOAuthPendingFlow>>(rows =>
                rows.Select(r => r.State).SequenceEqual(new[] { "s1" }))), Times.Once);
        }

        private static void VerifyConnectionKept(Fixture fixture)
        {
            fixture.Tokens.Verify(x => x.RemoveRange(It.IsAny<IEnumerable<ToolOAuthToken>>()), Times.Never);
            fixture.Flows.Verify(x => x.RemoveRange(It.IsAny<IEnumerable<McpOAuthPendingFlow>>()), Times.Never);
        }

        [Fact]
        public async Task Handle_BlankSecret_KeepsStoredSecretAndConnections()
        {
            var storedSecret = McpClientSecretHelper.Protect(Protector, "s3cret");
            var fixture = Build(Config(clientSecret: storedSecret));

            var result = await fixture.Handler.Handle(Command(Config(hasClientSecret: true)), TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            Assert.Equal(storedSecret, StoredSecret(fixture.Tool));
            Assert.DoesNotContain("hasClientSecret", fixture.Tool.Config);
            VerifyConnectionKept(fixture);
        }

        [Fact]
        public async Task Handle_StoredConfigEchoedBack_SecretUnchangedAndConnectionsKept()
        {
            // What the PUT endpoint produces when the request omits `config`: the stored entity's config
            // (including the "enc:v1:..." secret) is pre-mapped into the command.
            var storedSecret = McpClientSecretHelper.Protect(Protector, "s3cret");
            var stored = Config(clientSecret: storedSecret);
            var fixture = Build(stored);

            var result = await fixture.Handler.Handle(Command(stored), TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            Assert.Equal(storedSecret, StoredSecret(fixture.Tool));
            Assert.Equal("s3cret", McpClientSecretHelper.Resolve(Protector, StoredSecret(fixture.Tool)));
            VerifyConnectionKept(fixture);
        }

        [Fact]
        public async Task Handle_NewSecret_IsEncryptedAndResetsAllConnectionsForTheTool()
        {
            var fixture = Build(Config(clientSecret: McpClientSecretHelper.Protect(Protector, "old")));

            var result = await fixture.Handler.Handle(Command(Config(clientSecret: "new")), TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            Assert.DoesNotContain("\"new\"", fixture.Tool.Config);
            Assert.Equal("new", McpClientSecretHelper.Resolve(Protector, StoredSecret(fixture.Tool)));
            VerifyConnectionReset(fixture);
            fixture.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_ClearFlag_RemovesSecretAndResetsConnections()
        {
            var fixture = Build(Config(clientSecret: McpClientSecretHelper.Protect(Protector, "s3cret")));

            await fixture.Handler.Handle(Command(Config(clear: true)), TestContext.Current.CancellationToken);

            var stored = JObject.Parse(fixture.Tool.Config);
            Assert.True(string.IsNullOrEmpty(stored.Value<string>("clientSecret")));
            Assert.False(stored.ContainsKey("clearClientSecret"));
            VerifyConnectionReset(fixture);
        }

        [Fact]
        public async Task Handle_EndpointChangedWithoutNewSecret_DropsSecretAndResetsConnections()
        {
            var fixture = Build(Config(clientSecret: McpClientSecretHelper.Protect(Protector, "s3cret")));

            await fixture.Handler.Handle(Command(Config(endpoint: "https://other.example.com/mcp")), TestContext.Current.CancellationToken);

            Assert.True(string.IsNullOrEmpty(StoredSecret(fixture.Tool)));
            VerifyConnectionReset(fixture);
        }

        [Fact]
        public async Task Handle_ClientIdChanged_ResetsConnectionsEvenWithoutAStoredSecret()
        {
            var fixture = Build(Config());

            await fixture.Handler.Handle(Command(Config(clientId: "another-client")), TestContext.Current.CancellationToken);

            VerifyConnectionReset(fixture);
        }

        [Fact]
        public async Task Handle_LegacyPlaintextSecret_IsReEncryptedOnSave()
        {
            var fixture = Build(Config(clientSecret: "legacy-plain"));

            await fixture.Handler.Handle(Command(Config()), TestContext.Current.CancellationToken);

            var stored = StoredSecret(fixture.Tool);
            Assert.True(McpClientSecretHelper.IsProtected(stored));
            Assert.Equal("legacy-plain", McpClientSecretHelper.Resolve(Protector, stored));
            VerifyConnectionKept(fixture);
        }

        [Fact]
        public async Task Handle_McpConfigNotJson_ReturnsInvalidAndSavesNothing()
        {
            var fixture = Build(Config(clientSecret: McpClientSecretHelper.Protect(Protector, "s3cret")));

            var result = await fixture.Handler.Handle(Command("{ \"clientSecret\": \"pl41n\", broken"), TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Code == ErrorConst.ToolMcpConfigInvalid);
            fixture.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_TypeChangedFromMcp_StripsCredentialsAndResetsConnections()
        {
            // The PUT endpoint pre-fills the old MCP config, so the stored secret would otherwise be carried into
            // a non-MCP tool nothing manages or redacts as MCP any more.
            var storedSecret = McpClientSecretHelper.Protect(Protector, "s3cret");
            var fixture = Build(Config(clientSecret: storedSecret));
            var command = Command(Config(clientSecret: storedSecret));
            command.Type = "HTTP";

            var result = await fixture.Handler.Handle(command, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            Assert.DoesNotContain(storedSecret, fixture.Tool.Config);
            Assert.DoesNotContain("canva-client", fixture.Tool.Config);
            Assert.DoesNotContain(storedSecret, success.Data.Config);
            VerifyConnectionReset(fixture);
        }

        [Fact]
        public async Task Handle_SecretWithoutClientId_ReturnsInvalidAndSavesNothing()
        {
            var fixture = Build(Config(clientSecret: McpClientSecretHelper.Protect(Protector, "s3cret")));

            var result = await fixture.Handler.Handle(Command(Config(clientId: "", clientSecret: "new")), TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Code == ErrorConst.ToolMcpOAuthClientSecretRequiresClientId);
            fixture.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
