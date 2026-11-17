using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetToolMcpOAuthStatus;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.GetToolMcpOAuthStatus
{
    public class GetToolMcpOAuthStatusQueryHandlerTests : IClassFixture<GetToolMcpOAuthStatusQueryHandlerTesting>
    {
        private readonly GetToolMcpOAuthStatusQueryHandlerTesting _testing;

        public GetToolMcpOAuthStatusQueryHandlerTests(GetToolMcpOAuthStatusQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Connected_WhenNoOutcomeRecordedYet()
        {
            // LastCheckSucceeded == null (token saved, no connection outcome recorded yet) must still read
            // as Connected, matching the pre-existing behavior before this tri-state existed.
            var result = await _testing.Handler.Handle(new GetToolMcpOAuthStatusQuery
            {
                Id = _testing.ConnectedConnectionId
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<ToolMcpOAuthStatusResult> success)
            {
                Assert.Equal(McpOAuthStatus.Connected, success.Data.Status);
                Assert.Null(success.Data.Reason);
                Assert.Equal(_testing.ConnectedOnUtc, success.Data.ConnectedOnUtc);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Connected_WhenNoOutcomeRecordedYet");
            }
        }

        [Fact]
        public async Task TestHandler_Connected_WhenLastCheckSucceeded()
        {
            var result = await _testing.Handler.Handle(new GetToolMcpOAuthStatusQuery
            {
                Id = _testing.ConnectedAfterSuccessfulCheckConnectionId
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<ToolMcpOAuthStatusResult> success)
            {
                Assert.Equal(McpOAuthStatus.Connected, success.Data.Status);
                Assert.Null(success.Data.Reason);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Connected_WhenLastCheckSucceeded");
            }
        }

        [Fact]
        public async Task TestHandler_NotConnected()
        {
            var result = await _testing.Handler.Handle(new GetToolMcpOAuthStatusQuery
            {
                Id = _testing.NotConnectedConnectionId
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<ToolMcpOAuthStatusResult> success)
            {
                Assert.Equal(McpOAuthStatus.NotConnected, success.Data.Status);
                Assert.Null(success.Data.Reason);
                Assert.Null(success.Data.ConnectedOnUtc);
            }
            else
            {
                Assert.Fail("Error on TestHandler_NotConnected");
            }
        }

        [Fact]
        public async Task TestHandler_ConnectionIssue()
        {
            // A token exists (the OAuth exchange succeeded), but the last real connection attempt (the MCP
            // handshake) failed — e.g. Humantic's "no active subscription" 401 seen during local testing.
            var result = await _testing.Handler.Handle(new GetToolMcpOAuthStatusQuery
            {
                Id = _testing.ConnectionIssueConnectionId
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<ToolMcpOAuthStatusResult> success)
            {
                Assert.Equal(McpOAuthStatus.ConnectionIssue, success.Data.Status);
                Assert.Equal(_testing.ConnectionIssueError, success.Data.Reason);
                // A token row still exists — ConnectedOnUtc reflects when it was first saved, not whether
                // the connection currently works.
                Assert.Equal(_testing.ConnectedOnUtc, success.Data.ConnectedOnUtc);
            }
            else
            {
                Assert.Fail("Error on TestHandler_ConnectionIssue");
            }
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            var result = await _testing.Handler.Handle(new GetToolMcpOAuthStatusQuery
            {
                Id = Guid.NewGuid()
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.NotFound);
        }

        [Fact]
        public async Task TestHandler_PerUserScope_OtherUsersTokenDoesNotCountAsConnected()
        {
            // A PerUser-scope tool (e.g. Canva) with a token belonging to a DIFFERENT user must still read as
            // NotConnected for the calling user — per-user status is never "anyone at the tenant is connected".
            var result = await _testing.Handler.Handle(new GetToolMcpOAuthStatusQuery
            {
                Id = _testing.PerUserConnectionId
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<ToolMcpOAuthStatusResult> success)
            {
                Assert.Equal(McpOAuthStatus.NotConnected, success.Data.Status);
            }
            else
            {
                Assert.Fail("Error on TestHandler_PerUserScope_OtherUsersTokenDoesNotCountAsConnected");
            }
        }

        [Fact]
        public async Task TestHandler_PerUserScope_UnresolvableCallerIdentity_ReportsNotConnected_NotTheStrayScopedRow()
        {
            // Regression test (Copilot PR review, WI 7152): McpOAuthScopeResolver throws
            // McpOAuthPerUserIdentityUnavailableException rather than resolving null when a PerUser tool's
            // caller identity can't be resolved — null is also the exact key a Shared row is stored under, so
            // resolving to it here could have silently reported the status of a stray Shared row left over
            // for the same connection (e.g. from before the tool was switched to PerUser) instead of the
            // honest "we can't identify you" answer. This should never actually happen behind this endpoint's
            // [Authorize] gate, but the handler must degrade to NotConnected, not throw an unhandled 500.
            var connectionId = Guid.Parse("99999999-1111-1111-1111-999999999999");
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
                Id = 6,
                Name = "Canva",
                Description = "Canva MCP",
                Type = "MCP",
                Config = JsonConvert.SerializeObject(perUserConfig),
                McpOAuthConnectionId = connectionId,
                Tenant = tenant,
                IsActive = true
            };

            // A Shared-mode row for this same connection id, as if it existed from before the tool was
            // switched to PerUser (or any other way a mixed-mode row could linger) — the handler must never
            // report this row's status just because the caller's own identity couldn't be resolved.
            var strayScopedRow = new ToolOAuthToken
            {
                Id = 5,
                McpOAuthConnectionId = connectionId,
                UserId = null,
                AccessToken = "encrypted-access-token",
                ConnectedOnUtc = DateTimeOffset.UtcNow,
                TokenObtainedAtUtc = DateTimeOffset.UtcNow,
                LastCheckSucceeded = true
            };

            var tenantTools = new List<TenantTool> { perUserTool }.BuildMockDbSet();
            var tokens = new List<ToolOAuthToken> { strayScopedRow }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.ToolOAuthTokens).Returns(tokens.Object);

            var handler = new GetToolMcpOAuthStatusQueryHandler(db.Object, tenantContext, userContext);

            var result = await handler.Handle(new GetToolMcpOAuthStatusQuery { Id = connectionId }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<ToolMcpOAuthStatusResult> success)
            {
                Assert.Equal(McpOAuthStatus.NotConnected, success.Data.Status);
                Assert.Null(success.Data.Reason);
                Assert.Null(success.Data.ConnectedOnUtc);
            }
            else
            {
                Assert.Fail("Error on TestHandler_PerUserScope_UnresolvableCallerIdentity_ReportsNotConnected_NotTheStrayScopedRow");
            }
        }
    }
}
