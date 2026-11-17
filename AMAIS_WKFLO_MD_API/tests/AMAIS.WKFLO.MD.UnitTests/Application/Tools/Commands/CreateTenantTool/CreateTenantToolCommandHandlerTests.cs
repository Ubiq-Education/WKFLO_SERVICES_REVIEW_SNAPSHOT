using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Newtonsoft.Json;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.CreateTenantTool
{
    public class CreateTenantToolCommandHandlerTests : IClassFixture<CreateTenantToolCommandHandlerTesting>
    {
        private readonly CreateTenantToolCommandHandlerTesting _testing;

        public CreateTenantToolCommandHandlerTests(CreateTenantToolCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("Name Test 1", "Description Test 1", "Type 1", "{}", true, ResultType.Ok)]
        [InlineData("Name Test 2", "Description Test 2", "Type 2", "{}", false, ResultType.Ok)]
        public async Task TestHandler(string name, string description, string systemPrompt, string config, bool isActive, ResultType resultType)
        {
            // Act
            var result = await _testing.CreateTenantToolCommandHandler.Handle(new CreateTenantToolCommand()
            {
                Name = name,
                Description = description,
                Type = systemPrompt,
                Config = config,
                IsActive = isActive

            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<TenantToolCreateResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }

        [Fact]
        public async Task Handle_OAuth2McpTool_ResultExposesProvisionedMcpOAuthConnectionId()
        {
            // Regression test: TenantToolCreateResult is a hand-authored DTO, not automatically kept in
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

            var result = await _testing.CreateTenantToolCommandHandler.Handle(new CreateTenantToolCommand()
            {
                Name = "Humantic",
                Description = "Humantic MCP tool",
                Type = CommonToolTypeConsts.MCP,
                Config = JsonConvert.SerializeObject(config),
                IsActive = true
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<TenantToolCreateResult>>(result);
            Assert.NotNull(success.Data.McpOAuthConnectionId);
        }

        [Fact]
        public async Task Handle_MaxToolOutputLengthProvided_ResultExposesIt()
        {
            // Same regression class as Handle_OAuth2McpTool_ResultExposesProvisionedMcpOAuthConnectionId —
            // TenantToolCreateResult is hand-authored, so a new command/entity field only reaches the
            // response if this DTO also declares it.
            var result = await _testing.CreateTenantToolCommandHandler.Handle(new CreateTenantToolCommand()
            {
                Name = "Name Test",
                Description = "Description Test",
                Type = "HTTP",
                Config = "{}",
                IsActive = true,
                MaxToolOutputLength = 5000
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<TenantToolCreateResult>>(result);
            Assert.Equal(5000, success.Data.MaxToolOutputLength);
        }

        [Fact]
        public async Task Handle_MaxToolOutputLengthNotProvided_ResultLeavesItNull()
        {
            var result = await _testing.CreateTenantToolCommandHandler.Handle(new CreateTenantToolCommand()
            {
                Name = "Name Test 2",
                Description = "Description Test",
                Type = "HTTP",
                Config = "{}",
                IsActive = true
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<TenantToolCreateResult>>(result);
            Assert.Null(success.Data.MaxToolOutputLength);
        }
    }
}
