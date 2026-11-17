using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetMarketplaceToolById;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.GetMarketplaceToolById
{
    public class GetMarketplaceToolByIdQueryHandlerTests : IClassFixture<GetMarketplaceToolByIdQueryHandlerTesting>
    {
        private readonly GetMarketplaceToolByIdQueryHandlerTesting _testing;

        public GetMarketplaceToolByIdQueryHandlerTests(GetMarketplaceToolByIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.GetMarketplaceToolByIdQueryHandler.Handle(new GetMarketplaceToolByIdQuery()
            {
                Id = _testing.MarketplaceToolId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<MarketplaceToolGetResult> success)
            {
                Assert.NotNull(success.Data);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Ok");
            }
        }

        [Fact]
        public async Task TestHandler_ComputesInstallationsLiveFromTenantTools()
        {
            // Act — MarketplaceTool.Installations is no longer a stored column; it's computed as the
            // live count of non-deleted TenantTools installed from this marketplace tool (fixture
            // seeds one such row).
            var result = await _testing.GetMarketplaceToolByIdQueryHandler.Handle(new GetMarketplaceToolByIdQuery()
            {
                Id = _testing.MarketplaceToolId
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<MarketplaceToolGetResult>>(result);
            Assert.Equal(1, success.Data.Installations);
        }

        [Fact]
        public async Task TestHandler_ComputesStarsLiveFromUserStars()
        {
            // Act — MarketplaceTool.Stars is no longer a stored column; it's computed as the rounded
            // average of all non-deleted UserStar ratings for this marketplace tool (fixture seeds a
            // single rating of 4).
            var result = await _testing.GetMarketplaceToolByIdQueryHandler.Handle(new GetMarketplaceToolByIdQuery()
            {
                Id = _testing.MarketplaceToolId
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<MarketplaceToolGetResult>>(result);
            Assert.Equal(4, success.Data.Stars);
        }

        [Fact]
        public async Task TestHandler_NoRatings_StarsDefaultsToZero()
        {
            // Act — a tool with no UserStar rows at all must default to 0, not throw on an empty
            // average.
            var result = await _testing.GetMarketplaceToolByIdQueryHandler.Handle(new GetMarketplaceToolByIdQuery()
            {
                Id = _testing.UnratedMarketplaceToolId
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<MarketplaceToolGetResult>>(result);
            Assert.Equal(0, success.Data.Stars);
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            // Act
            var result = await _testing.GetMarketplaceToolByIdQueryHandler.Handle(new GetMarketplaceToolByIdQuery()
            {
                Id = GetMarketplaceToolByIdQueryHandlerTesting.RandomNumber.Next()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
