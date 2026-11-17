using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentById;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetMarketplaceAgentById
{
    public class GetMarketplaceAgentByIdQueryHandlerTests : IClassFixture<GetMarketplaceAgentByIdQueryHandlerTesting>
    {
        private readonly GetMarketplaceAgentByIdQueryHandlerTesting _testing;

        public GetMarketplaceAgentByIdQueryHandlerTests(GetMarketplaceAgentByIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.GetMarketplaceAgentByIdQueryHandler.Handle(new GetMarketplaceAgentByIdQuery()
            {
                Id = _testing.MarketplaceAgentId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<MarketplaceAgentGetResult> success)
            {
                Assert.NotNull(success.Data);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Ok");
            }
        }

        [Fact]
        public async Task TestHandler_ComputesInstallationsLiveFromTenantAgents()
        {
            // Act — MarketplaceAgent.Installations is no longer a stored column; it's computed as the
            // live count of non-deleted TenantAgents installed from this marketplace agent (fixture
            // seeds two such rows).
            var result = await _testing.GetMarketplaceAgentByIdQueryHandler.Handle(new GetMarketplaceAgentByIdQuery()
            {
                Id = _testing.MarketplaceAgentId
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<MarketplaceAgentGetResult>>(result);
            Assert.Equal(2, success.Data.Installations);
        }

        [Fact]
        public async Task TestHandler_ComputesStarsLiveFromUserStars()
        {
            // Act — MarketplaceAgent.Stars is no longer a stored column; it's computed as the rounded
            // average of all non-deleted UserStar ratings for this marketplace agent (fixture seeds a
            // single rating of 3).
            var result = await _testing.GetMarketplaceAgentByIdQueryHandler.Handle(new GetMarketplaceAgentByIdQuery()
            {
                Id = _testing.MarketplaceAgentId
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<MarketplaceAgentGetResult>>(result);
            Assert.Equal(3, success.Data.Stars);
        }

        [Fact]
        public async Task TestHandler_NoRatings_StarsDefaultsToZero()
        {
            // Act — an agent with no UserStar rows at all must default to 0, not throw on an empty
            // average.
            var result = await _testing.GetMarketplaceAgentByIdQueryHandler.Handle(new GetMarketplaceAgentByIdQuery()
            {
                Id = _testing.UnratedMarketplaceAgentId
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<MarketplaceAgentGetResult>>(result);
            Assert.Equal(0, success.Data.Stars);
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            // Act
            var result = await _testing.GetMarketplaceAgentByIdQueryHandler.Handle(new GetMarketplaceAgentByIdQuery()
            {
                Id = GetMarketplaceAgentByIdQueryHandlerTesting.RandomNumber.Next()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
