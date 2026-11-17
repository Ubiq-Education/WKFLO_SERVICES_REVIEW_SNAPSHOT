using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DisconnectToolMcpOAuth;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.DisconnectToolMcpOAuth
{
    public class DisconnectToolMcpOAuthCommandHandlerTests : IClassFixture<DisconnectToolMcpOAuthCommandHandlerTesting>
    {
        private readonly DisconnectToolMcpOAuthCommandHandlerTesting _testing;

        public DisconnectToolMcpOAuthCommandHandlerTests(DisconnectToolMcpOAuthCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandlerOk()
        {
            var result = await _testing.Handler.Handle(new DisconnectToolMcpOAuthCommand
            {
                Id = _testing.McpOAuthConnectionId
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<ToolMcpOAuthDisconnectResult> success)
            {
                Assert.Equal(_testing.McpOAuthConnectionId, success.Data.Id);
            }
            else
            {
                Assert.Fail("Error on TestHandlerOk");
            }

            _testing.TokenStoreMock.Verify(
                x => x.DeleteAsync(_testing.McpOAuthConnectionId, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            var result = await _testing.Handler.Handle(new DisconnectToolMcpOAuthCommand
            {
                Id = Guid.NewGuid()
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.NotFound);
        }

        [Fact]
        public async Task TestHandlerOk_PerUserScope_OnlyDeletesCallingUsersRow()
        {
            // A PerUser-scope tool (e.g. Canva) must never delete another user's row — disconnecting is
            // scoped to the calling user alone, unlike a Shared-scope tool's single connection-wide row.
            var result = await _testing.Handler.Handle(new DisconnectToolMcpOAuthCommand
            {
                Id = _testing.PerUserMcpOAuthConnectionId
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);

            _testing.TokenStoreMock.Verify(
                x => x.DeleteAsync(_testing.PerUserMcpOAuthConnectionId, _testing.CallingUserId, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task TestHandlerOk_PerUserScope_UnresolvableCallerIdentity_NoOpsRatherThanDeletingTheStraySharedRow()
        {
            // Regression test (Copilot PR review, WI 7152): McpOAuthScopeResolver throws
            // McpOAuthPerUserIdentityUnavailableException rather than resolving null when a PerUser tool's
            // caller identity can't be resolved — null is also the exact key a Shared row is stored under, so
            // resolving to it here could have silently deleted a stray Shared row for the same connection
            // (e.g. left over from before the tool was switched to PerUser) instead of correctly finding
            // nothing to delete for this unidentifiable caller. This should never actually happen behind this
            // endpoint's [Authorize] gate, but the handler must no-op, not throw or delete the wrong row.
            var connectionId = Guid.Parse("99999999-2222-2222-2222-999999999999");
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
                Id = 7,
                Name = "Canva",
                Description = "Canva MCP",
                Type = "MCP",
                Config = JsonConvert.SerializeObject(perUserConfig),
                McpOAuthConnectionId = connectionId,
                Tenant = tenant,
                IsActive = true
            };

            var tenantTools = new List<TenantTool> { perUserTool }.BuildMockDbSet();
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);

            var tokenStoreMock = new Mock<IMcpOAuthTokenStore>();
            tokenStoreMock
                .Setup(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var handler = new DisconnectToolMcpOAuthCommandHandler(db.Object, tenantContext, userContext, tokenStoreMock.Object);

            var result = await handler.Handle(new DisconnectToolMcpOAuthCommand { Id = connectionId }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            tokenStoreMock.Verify(
                x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
