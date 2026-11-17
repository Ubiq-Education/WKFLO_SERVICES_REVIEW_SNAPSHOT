using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.InstallMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.InstallMarketplaceAgent
{
    public class InstallMarketplaceAgentCommandHandlerTests : IClassFixture<InstallMarketplaceAgentCommandHandlerTesting>
    {
        private readonly InstallMarketplaceAgentCommandHandlerTesting _testing;

        public InstallMarketplaceAgentCommandHandlerTests(InstallMarketplaceAgentCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_WithValidMarketplaceAgent_ShouldReturnSuccessResult()
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand
            {
                Id = _testing.MarketplaceAgentId
            };

            // Act
            var result = await _testing.InstallMarketplaceAgentCommandHandler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsType<SuccessResult<TenantAgentCreateResult>>(result);
            var successResult = ((SuccessResult<TenantAgentCreateResult>)result).Data;
            Assert.NotNull(successResult);
            Assert.Equal("Test Agent", successResult.Name);
            Assert.Equal("Test Agent Description", successResult.Description);
            Assert.Equal("Test System Prompt", successResult.SystemPrompt);
            Assert.True(successResult.IsActive);
        }

        [Fact]
        public async Task Handle_WithNonExistentMarketplaceAgent_ShouldReturnNotFoundResult()
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand
            {
                Id = 999 // Non-existent ID
            };

            // Act
            var result = await _testing.InstallMarketplaceAgentCommandHandler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            var notFoundResult = (NotFoundResult)result;
            Assert.Contains(notFoundResult.Errors, e => e.Code == ErrorConst.AgentNotFound);
        }

        [Fact]
        public async Task Handle_WithValidCommand_ShouldCreateTenantAgentWithCorrectProperties()
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand
            {
                Id = _testing.MarketplaceAgentId
            };

            // Act
            var result = await _testing.InstallMarketplaceAgentCommandHandler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsType<SuccessResult<TenantAgentCreateResult>>(result);
            var successResult = ((SuccessResult<TenantAgentCreateResult>)result).Data;

            // Verify that the TenantAgent was created with correct default values
            Assert.True(successResult.IsActive); // Should be active by default
            Assert.False(successResult.EnableSTM); // Should be false by default
            Assert.False(successResult.EnableLTM); // Should be false by default
            Assert.False(successResult.EnableSpinUpAgents); // Should be false by default
            Assert.Null(successResult.LastRunAt); // Should be null initially
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public async Task Handle_WithInvalidId_ShouldReturnNotFoundResult(int invalidId)
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand
            {
                Id = invalidId
            };

            // Act
            var result = await _testing.InstallMarketplaceAgentCommandHandler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }
    }
}