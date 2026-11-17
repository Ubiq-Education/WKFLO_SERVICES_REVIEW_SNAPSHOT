using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.ListMarketplaceTools;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.ListMarketplaceTools
{
    public class ListMarketplaceToolsQueryHandlerTests : IClassFixture<ListMarketplaceToolsQueryHandlerTesting>
    {
        private readonly ListMarketplaceToolsQueryHandlerTesting _testing;

        public ListMarketplaceToolsQueryHandlerTests(ListMarketplaceToolsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.ListMarketplaceToolsQueryHandler.Handle(new ListMarketplaceToolsQuery(), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<List<MarketplaceToolGetResult>> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(2, success.Data.Count);
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
            // live count of non-deleted TenantTools installed from each marketplace tool (fixture seeds
            // one install for `MarketplaceToolId`, none for the other).
            var result = await _testing.ListMarketplaceToolsQueryHandler.Handle(new ListMarketplaceToolsQuery(), TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<MarketplaceToolGetResult>>>(result);
            var installedTool = success.Data.Single(t => t.Id == _testing.MarketplaceToolId);
            Assert.Equal(1, installedTool.Installations);

            var otherTool = success.Data.Single(t => t.Id != _testing.MarketplaceToolId);
            Assert.Equal(0, otherTool.Installations);
        }

        [Fact]
        public async Task TestHandler_ComputesStarsLiveFromUserStars()
        {
            // Act — MarketplaceTool.Stars is no longer a stored column; it's computed as the rounded
            // average of all non-deleted UserStar ratings for each marketplace tool (fixture seeds
            // ratings of 2 and 4 for MarketplaceToolId, plus a soft-deleted 5 that must be excluded).
            var result = await _testing.ListMarketplaceToolsQueryHandler.Handle(new ListMarketplaceToolsQuery(), TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<MarketplaceToolGetResult>>>(result);

            var ratedTool = success.Data.Single(t => t.Id == _testing.MarketplaceToolId);
            Assert.Equal(3, ratedTool.Stars); // avg(2, 4) = 3

            var unratedTool = success.Data.Single(t => t.Id != _testing.MarketplaceToolId);
            Assert.Equal(0, unratedTool.Stars);
        }
    }
}
