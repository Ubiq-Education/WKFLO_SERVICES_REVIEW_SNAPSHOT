using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.RateMarketplaceAgent;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.RateMarketplaceAgent
{
    public class RateMarketplaceAgentCommandHandlerTests : IClassFixture<RateMarketplaceAgentCommandHandlerTesting>
    {
        private readonly RateMarketplaceAgentCommandHandlerTesting _testing;

        public RateMarketplaceAgentCommandHandlerTests(RateMarketplaceAgentCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_WithNonExistentAgent_ShouldReturnNotFound()
        {
            // Arrange
            _testing.Reset();
            _testing.SetupNonExistentAgent();
            var command = GetRateMarketplaceAgentCommand(1, 5);

            // Act
            var result = await _testing.RateMarketplaceAgentCommandHandler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(ResultType.NotFound, result.ResultType);
        }

        [Theory]
        [InlineData(1, 5, ResultType.Ok)]
        [InlineData(1, 4, ResultType.Ok)]
        [InlineData(1, 3, ResultType.Ok)]
        public async Task Handle_WithValidAgentAndNewRating_ShouldCreateNewUserStar(int toolId, int rating, ResultType expectedResultType)
        {
            // Arrange
            _testing.Reset();
            var command = GetRateMarketplaceAgentCommand(toolId, rating);

            // Act
            var result = await _testing.RateMarketplaceAgentCommandHandler.Handle(command, CancellationToken.None);

            // Assert
            Assert.Equal(expectedResultType, result.ResultType);
            if (result is SuccessResult<Boolean> success)
            {
                Assert.True(success.Data);
            }
            _testing.VerifyDatabaseSaved();
        }

        [Theory]
        [InlineData(1, 4, 3, ResultType.Ok)]
        [InlineData(1, 5, 2, ResultType.Ok)]
        [InlineData(1, 1, 4, ResultType.Ok)]
        public async Task Handle_WithValidAgentAndExistingUserStar_ShouldUpdateUserStar(int toolId, int newRating, int previousRating, ResultType expectedResultType)
        {
            // Arrange
            _testing.Reset();
            _testing.SetupExistingUserStar(toolId, previousRating);
            var command = GetRateMarketplaceAgentCommand(toolId, newRating);

            // Act
            var result = await _testing.RateMarketplaceAgentCommandHandler.Handle(command, CancellationToken.None);

            // Assert
            Assert.Equal(expectedResultType, result.ResultType);
            if (result is SuccessResult<Boolean> success)
            {
                Assert.True(success.Data);
            }
            _testing.VerifyDatabaseSaved();
        }

        private static RateMarketplaceAgentCommand GetRateMarketplaceAgentCommand(int id, int rating)
        {
            return new RateMarketplaceAgentCommand
            {
                Id = id,
                Rating = rating
            };
        }
    }
}
