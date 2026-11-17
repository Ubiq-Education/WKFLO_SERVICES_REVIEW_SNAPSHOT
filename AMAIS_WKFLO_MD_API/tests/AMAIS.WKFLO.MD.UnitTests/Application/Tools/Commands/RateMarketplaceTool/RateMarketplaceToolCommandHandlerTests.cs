using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.RateMarketplaceTool;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.RateMarketplaceTool
{
    public class RateMarketplaceToolCommandHandlerTests : IClassFixture<RateMarketplaceToolCommandHandlerTesting>
    {
        private readonly RateMarketplaceToolCommandHandlerTesting _testing;

        public RateMarketplaceToolCommandHandlerTests(RateMarketplaceToolCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_WithNonExistentTool_ShouldReturnNotFound()
        {
            // Arrange
            _testing.Reset();
            _testing.SetupNonExistentTool();
            var command = GetRateMarketplaceToolCommand(1, 5);

            // Act
            var result = await _testing.RateMarketplaceToolCommandHandler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(ResultType.NotFound, result.ResultType);
        }

        [Theory]
        [InlineData(1, 5, ResultType.Ok)]
        [InlineData(1, 4, ResultType.Ok)]
        [InlineData(1, 3, ResultType.Ok)]
        public async Task Handle_WithValidToolAndNewRating_ShouldCreateNewUserStar(int toolId, int rating, ResultType expectedResultType)
        {
            // Arrange
            _testing.Reset();
            var command = GetRateMarketplaceToolCommand(toolId, rating);

            // Act
            var result = await _testing.RateMarketplaceToolCommandHandler.Handle(command, CancellationToken.None);

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
        public async Task Handle_WithValidToolAndExistingUserStar_ShouldUpdateUserStar(int toolId, int newRating, int previousRating, ResultType expectedResultType)
        {
            // Arrange
            _testing.Reset();
            _testing.SetupExistingUserStar(toolId, previousRating);
            var command = GetRateMarketplaceToolCommand(toolId, newRating);

            // Act
            var result = await _testing.RateMarketplaceToolCommandHandler.Handle(command, CancellationToken.None);

            // Assert
            Assert.Equal(expectedResultType, result.ResultType);
            if (result is SuccessResult<Boolean> success)
            {
                Assert.True(success.Data);
            }
            _testing.VerifyDatabaseSaved();
        }

        private static RateMarketplaceToolCommand GetRateMarketplaceToolCommand(int id, int rating)
        {
            return new RateMarketplaceToolCommand
            {
                Id = id,
                Rating = rating
            };
        }
    }
}
