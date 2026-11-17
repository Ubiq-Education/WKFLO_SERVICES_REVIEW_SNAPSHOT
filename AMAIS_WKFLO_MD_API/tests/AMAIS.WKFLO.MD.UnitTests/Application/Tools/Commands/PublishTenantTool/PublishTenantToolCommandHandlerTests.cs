using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.PublishTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Moq;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.PublishTenantTool
{
    /// <summary>
    /// Deliberately NOT <see cref="IClassFixture{TFixture}"/> — <see cref="Handle"/> mutates the fixture's
    /// in-memory entities (a publish adds a new PublishedTenantTool the next call would see as "already
    /// published"). xUnit constructs the test class fresh per [Fact], so instantiating
    /// <see cref="PublishTenantToolCommandHandlerTesting"/> directly here (one per test) gives every test its
    /// own isolated db state instead of accumulating across tests.
    /// </summary>
    public class PublishTenantToolCommandHandlerTests
    {
        private readonly PublishTenantToolCommandHandlerTesting _testing = new();

        [Fact]
        public async Task Handle_NewPublish_CopiesMcpOAuthConnectionIdFromTenantTool()
        {
            // Arrange
            var command = new PublishTenantToolCommand { Id = _testing.ConnectedToolId };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolPublishResult>>(result);

            _testing.PublishedToolsMock.Verify(
                x => x.AddAsync(
                    It.Is<PublishedTenantTool>(p => p.McpOAuthConnectionId == _testing.ConnectedToolMcpOAuthConnectionId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_NewPublish_ToolNeverConnected_PublishedCopyHasNoConnectionIdEither()
        {
            // Arrange
            var command = new PublishTenantToolCommand { Id = _testing.NotConnectedToolId };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolPublishResult>>(result);

            _testing.PublishedToolsMock.Verify(
                x => x.AddAsync(
                    It.Is<PublishedTenantTool>(p => p.McpOAuthConnectionId == null),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_McpOAuthClientSecret_CopiedEncryptedToPublishedTool_RedactedInResult()
        {
            // US 7849: the published copy runs at chat time, so it needs the (still encrypted) secret — it's
            // decrypted only at the sinks — but the publish response must never echo it back.
            const string storedSecret = "enc:v1:ZmFrZS1jaXBoZXJ0ZXh0";
            _testing.RepublishTool.Config =
                $"{{\"transportType\":\"Http\",\"endpoint\":\"https://api.canva.com/connect/v1/mcp\",\"authType\":\"OAuth2\",\"clientId\":\"canva-client\",\"clientSecret\":\"{storedSecret}\"}}";

            var result = await _testing.Handler.Handle(new PublishTenantToolCommand { Id = _testing.RepublishToolId }, TestContext.Current.CancellationToken);

            var published = Assert.IsType<SuccessResult<TenantToolPublishResult>>(result).Data;
            Assert.DoesNotContain(storedSecret, published.Config);
            Assert.True(Newtonsoft.Json.Linq.JObject.Parse(published.Config).Value<bool>("hasClientSecret"));

            var publishedEntity = _testing.PublishedTools.Single(p => p.Id == _testing.ExistingPublishedToolId);
            Assert.Contains(storedSecret, publishedEntity.Config);
        }

        [Fact]
        public async Task Handle_Republish_SyncsMcpOAuthConnectionIdOntoExistingPublishedCopy()
        {
            // Arrange
            var command = new PublishTenantToolCommand { Id = _testing.RepublishToolId };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolPublishResult>>(result);
            var published = ((SuccessResult<TenantToolPublishResult>)result).Data;
            Assert.Equal(_testing.ExistingPublishedToolId, published.Id);

            var publishedEntity = _testing.PublishedTools.Single(p => p.Id == _testing.ExistingPublishedToolId);
            Assert.Equal(_testing.RepublishToolMcpOAuthConnectionId, publishedEntity.McpOAuthConnectionId);
            Assert.Equal(PublishTenantToolCommandHandlerTesting.PublishNowUtc, publishedEntity.LastUpdatedOn);
        }

        [Fact]
        public async Task Handle_Republish_UnchangedTool_StillStampsLastUpdatedOn()
        {
            // A republish where the mapped fields produce no actual value differences from the existing
            // PublishedTenantTool copy must still bump LastUpdatedOn — otherwise the action leaves zero trace
            // (see MasterDataDbContextAuditTests/PublishTenantToolCommandHandlerAuditRegressionTests for the
            // matching real-EF-Core proof that this also produces an audit row).

            // Arrange
            var command = new PublishTenantToolCommand { Id = _testing.UnchangedRepublishToolId };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolPublishResult>>(result);

            var publishedEntity = _testing.PublishedTools.Single(p => p.Id == _testing.UnchangedExistingPublishedToolId);
            Assert.Equal(PublishTenantToolCommandHandlerTesting.PublishNowUtc, publishedEntity.LastUpdatedOn);
        }
    }
}
