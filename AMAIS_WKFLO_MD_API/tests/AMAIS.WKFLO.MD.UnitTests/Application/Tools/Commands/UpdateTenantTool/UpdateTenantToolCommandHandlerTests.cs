using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.UpdateTenantTool
{
    public class UpdateTenantToolCommandHandlerTests : IClassFixture<UpdateTenantToolCommandHandlerTesting>
    {
        private readonly UpdateTenantToolCommandHandlerTesting _testing;

        public UpdateTenantToolCommandHandlerTests(UpdateTenantToolCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("Name Test 1", "Description Test 1", "Type 1", "{}", true, ResultType.Ok)]
        [InlineData("Name Test 2", "Description Test 2", "Type 2", "{}", false, ResultType.Ok)]
        public async Task TestHandler(string name, string description, string type, string config, bool isActive, ResultType resultType)
        {
            // Act
            var result = await _testing.UpdateTenantToolCommandHandler.Handle(new UpdateTenantToolCommand()
            {
                Id = _testing.TenantToolId,
                Name = name,
                Description = description,
                Type = type,
                Config = config,
                IsActive = isActive

            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<TenantToolUpdateResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }

        [Fact]
        public async Task Handle_OAuth2McpTool_ResultExposesProvisionedMcpOAuthConnectionId()
        {
            // Regression test: TenantToolUpdateResult is a hand-authored DTO, not automatically kept in
            // sync with RunableTool by Mapster convention mapping — a new entity field only appears in the
            // response if this class also declares it. McpOAuthConnectionIdProvisioner sets the field on
            // the entity during Handle(), but that's silently lost if the DTO doesn't carry it through.
            var config = new McpServerConfig
            {
                Id = "humantic-mcp",
                Name = "Humantic",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.humantic.ai/mcp",
                AuthType = McpAuthType.OAuth2,
                Scopes = new[] { "profiles:read" }
            };

            var result = await _testing.UpdateTenantToolCommandHandler.Handle(new UpdateTenantToolCommand()
            {
                Id = _testing.TenantToolId,
                Name = "Humantic",
                Description = "Humantic MCP tool",
                Type = CommonToolTypeConsts.MCP,
                Config = JsonConvert.SerializeObject(config),
                IsActive = true
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            Assert.NotNull(success.Data.McpOAuthConnectionId);
        }

        [Fact]
        public async Task Handle_ToolWithStalePreConnectionIdRedirectUri_RewritesItToTheCurrentCallbackUrl()
        {
            // Regression test for a live bug: a tool saved back before McpOAuthConnectionId existed already
            // had a RedirectUri baked into its Config (the old int-Tool.Id-based callback URL).
            // McpOAuthConnectionIdProvisioner used to only backfill RedirectUri when blank, so that stale
            // URL survived untouched even after a fresh connection id was generated — Humantic kept
            // redirecting to a URL the callback route could never parse, a 400 on every consent attempt.
            // RedirectUri must always be rewritten to match the current static callback URL (the route no
            // longer carries any id at all — see McpOAuthCallbackUrlBuilder's remarks).
            var tenantContext = new TenantContext { TenantName = "Test" };
            var staleConfig = new McpServerConfig
            {
                Id = "humantic-mcp",
                Name = "Humantic",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.humantic.ai/mcp",
                AuthType = McpAuthType.OAuth2,
                Scopes = new[] { "profiles:read" },
                RedirectUri = "https://localhost:32771/md/api/v1/tenant/tool/3002/mcp/oauth/callback"
            };

            var tenantTool = new TenantTool
            {
                Id = 3002,
                Name = "Humantic",
                Description = "Humantic MCP tool",
                Type = CommonToolTypeConsts.MCP,
                Config = JsonConvert.SerializeObject(staleConfig),
                McpOAuthConnectionId = null,
                IsActive = true
            };

            var tenantTools = new List<TenantTool> { tenantTool }.BuildMockDbSet();
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.SaveChangesAsync(default)).ReturnsAsync(1);

            var settings = Options.Create(new McpOAuthSettings { PublicBaseUrl = "https://localhost:32771" });
            var handler = new UpdateTenantToolCommandHandler(db.Object, WkfloMapperExtensions.CreateMapper(), settings, Mock.Of<IMcpTokenProtector>());

            var result = await handler.Handle(new UpdateTenantToolCommand
            {
                Id = 3002,
                Name = "Humantic",
                Description = "Humantic MCP tool",
                Type = CommonToolTypeConsts.MCP,
                Config = JsonConvert.SerializeObject(staleConfig),
                IsActive = true
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            Assert.NotNull(success.Data.McpOAuthConnectionId);

            var writtenConfig = JsonConvert.DeserializeObject<McpServerConfig>(success.Data.Config);
            Assert.Equal("https://localhost:32771/md/api/v1/tenant/tool/mcp/oauth/callback", writtenConfig.RedirectUri);
            Assert.DoesNotContain("/tool/3002/", writtenConfig.RedirectUri);
        }

        [Fact]
        public async Task Handle_MaxToolOutputLengthProvided_ResultExposesIt()
        {
            // Same regression class as Handle_OAuth2McpTool_ResultExposesProvisionedMcpOAuthConnectionId —
            // TenantToolUpdateResult is hand-authored, so a new command/entity field only reaches the
            // response if this DTO also declares it.
            var result = await _testing.UpdateTenantToolCommandHandler.Handle(new UpdateTenantToolCommand()
            {
                Id = _testing.TenantToolId,
                Name = "Name Test",
                Description = "Description Test",
                Type = "HTTP",
                Config = "{}",
                IsActive = true,
                MaxToolOutputLength = 5000
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            Assert.Equal(5000, success.Data.MaxToolOutputLength);
        }

        [Fact]
        public async Task Handle_MaxToolOutputLengthNotProvided_ResultLeavesItNull()
        {
            var result = await _testing.UpdateTenantToolCommandHandler.Handle(new UpdateTenantToolCommand()
            {
                Id = _testing.TenantToolId,
                Name = "Name Test",
                Description = "Description Test",
                Type = "HTTP",
                Config = "{}",
                IsActive = true
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            Assert.Null(success.Data.MaxToolOutputLength);
        }
    }
}
