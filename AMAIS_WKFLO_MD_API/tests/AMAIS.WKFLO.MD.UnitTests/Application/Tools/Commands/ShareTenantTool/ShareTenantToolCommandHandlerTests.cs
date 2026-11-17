using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.ShareTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.ShareTenantTool
{
    public class ShareTenantToolCommandHandlerTests : IClassFixture<ShareTenantToolCommandHandlerTesting>
    {
        private readonly ShareTenantToolCommandHandlerTesting _testing;

        public ShareTenantToolCommandHandlerTests(ShareTenantToolCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_WithOAuth2McpTool_StripsClientCredentialsAndRedirectUri_KeepsDescriptiveFields()
        {
            // Arrange
            var command = new ShareTenantToolCommand { Id = _testing.OAuthTenantToolId };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolShareResult>>(result);
            var shared = ((SuccessResult<TenantToolShareResult>)result).Data;

            var config = JsonConvert.DeserializeObject<McpServerConfig>(shared.Config);
            Assert.Equal(string.Empty, config.ClientId);
            Assert.Equal(string.Empty, config.ClientSecret);
            Assert.Equal(string.Empty, config.RedirectUri);

            // Descriptive fields (not secrets) must survive the copy untouched.
            Assert.Equal(McpAuthType.OAuth2, config.AuthType);
            Assert.Equal("https://mcp.humantic.ai/mcp", config.Endpoint);
            Assert.Equal(TransportType.Http, config.TransportType);
            Assert.Single(config.Scopes, "profiles:read");
        }

        [Fact]
        public async Task Handle_WithNonOAuthMcpToolCarryingCredentials_StillStripsThem()
        {
            var result = await _testing.Handler.Handle(new ShareTenantToolCommand { Id = _testing.NonOAuthMcpWithCredentialsTenantToolId }, TestContext.Current.CancellationToken);

            var shared = Assert.IsType<SuccessResult<TenantToolShareResult>>(result).Data;
            Assert.DoesNotContain("stray-client", shared.Config);
            Assert.DoesNotContain("stray-secret", shared.Config);
            Assert.Equal(McpAuthType.None, JsonConvert.DeserializeObject<McpServerConfig>(shared.Config).AuthType);
        }

        [Fact]
        public async Task Handle_WithNonMcpTool_DoesNotTouchConfig()
        {
            // Arrange
            var command = new ShareTenantToolCommand { Id = _testing.NonMcpTenantToolId };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolShareResult>>(result);
            var shared = ((SuccessResult<TenantToolShareResult>)result).Data;
            Assert.Equal("{\"endpoint\":\"https://api.example.com\"}", shared.Config);
        }

        [Fact]
        public async Task Handle_WithNeverPublishedTenantTool_ShouldReturnNotFoundResult()
        {
            // Arrange
            var command = new ShareTenantToolCommand { Id = _testing.NeverPublishedTenantToolId };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            var notFoundResult = (NotFoundResult)result;
            Assert.Contains(notFoundResult.Errors, e => e.Code == ErrorConst.ToolShareNotPublished);
        }
    }
}
