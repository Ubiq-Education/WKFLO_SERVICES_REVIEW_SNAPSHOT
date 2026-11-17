using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.StartToolMcpOAuth;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.StartToolMcpOAuth
{
    public class StartToolMcpOAuthCommandHandlerTests : IClassFixture<StartToolMcpOAuthCommandHandlerTesting>
    {
        private readonly StartToolMcpOAuthCommandHandlerTesting _testing;

        public StartToolMcpOAuthCommandHandlerTests(StartToolMcpOAuthCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandlerOk()
        {
            var result = await _testing.Handler.Handle(new StartToolMcpOAuthCommand
            {
                Id = _testing.OAuthConnectionId
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<ToolMcpOAuthStartResult> success)
            {
                Assert.False(string.IsNullOrEmpty(success.Data.AuthorizationUrl));
            }
            else
            {
                Assert.Fail("Error on TestHandlerOk");
            }
        }

        [Fact]
        public async Task TestHandlerOk_DoesNotOverwriteMcpServerConfigId()
        {
            // Regression test: McpServerConfig.Id is purely McpClientService's own multi-server registry
            // key (whatever the admin typed, e.g. "placeholder" in the test fixture's config) and must never
            // be silently rewritten to the Tool's numeric id — the OAuth token store/consent flow is keyed
            // by the connection id passed as its own parameter instead.
            await _testing.Handler.Handle(new StartToolMcpOAuthCommand
            {
                Id = _testing.OAuthConnectionId
            }, TestContext.Current.CancellationToken);

            _testing.ConsentServiceMock.Verify(
                x => x.BeginAsync(
                    It.Is<McpServerConfig>(c => c.Id == "placeholder"),
                    _testing.OAuthConnectionId,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task TestHandlerOk_SharedScope_PassesNullRequestingUserId()
        {
            // Shared-vs-per-user is a fixed, mutually exclusive choice per tool: a Shared-scope tool (e.g.
            // Humantic) must never key its token to a specific caller, regardless of who started the flow.
            await _testing.Handler.Handle(new StartToolMcpOAuthCommand
            {
                Id = _testing.OAuthConnectionId
            }, TestContext.Current.CancellationToken);

            _testing.ConsentServiceMock.Verify(
                x => x.BeginAsync(
                    It.IsAny<McpServerConfig>(),
                    _testing.OAuthConnectionId,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task TestHandlerOk_PerUserScope_PassesCallingUsersId()
        {
            // A PerUser-scope tool (e.g. Canva, which has no tenant/org-shared mode at all) must key its
            // token to the real, authenticated caller — never resolved from the anonymous OAuth callback.
            await _testing.Handler.Handle(new StartToolMcpOAuthCommand
            {
                Id = _testing.PerUserOAuthConnectionId
            }, TestContext.Current.CancellationToken);

            _testing.ConsentServiceMock.Verify(
                x => x.BeginAsync(
                    It.IsAny<McpServerConfig>(),
                    _testing.PerUserOAuthConnectionId,
                    _testing.CallingUserId,
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task TestHandlerOk_RouteIsKeyedByConnectionIdNotToolId()
        {
            // Route unification regression test: Start is reachable by RunableTool.McpOAuthConnectionId,
            // never by Tool.Id — passing the tool's int id where a connection id is expected must not
            // resolve to the tool (it isn't a valid Guid match).
            var result = await _testing.Handler.Handle(new StartToolMcpOAuthCommand
            {
                Id = Guid.Empty
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.NotFound);
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            var result = await _testing.Handler.Handle(new StartToolMcpOAuthCommand
            {
                Id = Guid.NewGuid()
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.NotFound);
        }

        [Fact]
        public async Task TestHandler_NonOAuthTool_ReturnsInvalid()
        {
            var result = await _testing.Handler.Handle(new StartToolMcpOAuthCommand
            {
                Id = _testing.NonOAuthConnectionId
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Invalid);
        }

        [Fact]
        public async Task TestHandler_NonMcpTool_ReturnsInvalid()
        {
            var result = await _testing.Handler.Handle(new StartToolMcpOAuthCommand
            {
                Id = _testing.NonMcpConnectionId
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Invalid);
        }

        [Fact]
        public async Task TestHandler_PerUserScope_UnresolvableCallerIdentity_ReturnsControlledInvalidResult()
        {
            // Regression test (Copilot PR review, WI 7152): identity resolution used to be a raw
            // Guid.Parse(_userContext.Guid) call sitting BEFORE the try/catch that wraps the rest of this
            // method, so a missing/malformed UserContext.Guid threw an unhandled exception (a raw 500)
            // instead of the controlled InvalidResult every other failure path here returns. Resolution now
            // goes through McpOAuthScopeResolver inside the try, so this must come back as a normal Invalid
            // result — this should never actually happen behind this endpoint's [Authorize] gate, but it
            // must fail safely if it somehow does.
            var connectionId = Guid.Parse("99999999-3333-3333-3333-999999999999");
            var tenantContext = new TenantContext { TenantName = "Test" };
            var userContext = new UserContext { Guid = "not-a-guid" };
            var tenant = new Tenant { Id = 1, Name = "Test" };

            var perUserConfig = new McpServerConfig
            {
                Id = "canva-placeholder",
                Name = "Canva",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.canva.com/mcp",
                AuthType = McpAuthType.OAuth2,
                OAuthScope = McpOAuthScope.PerUser
            };
            var perUserTool = new TenantTool
            {
                Id = 8,
                Name = "Canva",
                Description = "Canva MCP",
                Type = CommonToolTypeConsts.MCP,
                Config = JsonConvert.SerializeObject(perUserConfig),
                McpOAuthConnectionId = connectionId,
                Tenant = tenant,
                IsActive = true
            };

            var tenantTools = new List<TenantTool> { perUserTool }.BuildMockDbSet();
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);

            var consentServiceMock = new Mock<IMcpOAuthConsentService>();
            var settings = Options.Create(new McpOAuthSettings { PublicBaseUrl = "https://wkflo.example.com" });

            var handler = new StartToolMcpOAuthCommandHandler(
                db.Object,
                tenantContext,
                userContext,
                consentServiceMock.Object,
                settings,
                Mock.Of<ILogger<StartToolMcpOAuthCommandHandler>>());

            var result = await handler.Handle(new StartToolMcpOAuthCommand { Id = connectionId }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Invalid);
            consentServiceMock.Verify(
                x => x.BeginAsync(It.IsAny<McpServerConfig>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
