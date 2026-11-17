using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListMarketplaceAgents;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.ListMarketplaceAgents
{
    public class ListMarketplaceAgentsQueryHandlerTests : IClassFixture<ListMarketplaceAgentsQueryHandlerTesting>
    {
        private readonly ListMarketplaceAgentsQueryHandlerTesting _testing;

        public ListMarketplaceAgentsQueryHandlerTests(ListMarketplaceAgentsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.ListMarketplaceAgentsQueryHandler.Handle(new ListMarketplaceAgentsQuery(), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<List<MarketplaceAgentGetResult>> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(3, success.Data.Count);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Ok");
            }
        }

        [Fact]
        public async Task TestHandler_MapsIconUrlFromEntity()
        {
            // Act — verifies MarketplaceAgent.IconUrl flows through the entity -> DTO mapping
            // automatically (identically-named scalar properties, no explicit .Map() call needed).
            var result = await _testing.ListMarketplaceAgentsQueryHandler.Handle(new ListMarketplaceAgentsQuery(), TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<MarketplaceAgentGetResult>>>(result);
            var agent = success.Data.Single(a => a.Id == _testing.MarketplaceAgentId);
            Assert.Equal("https://static.example.com/test-agent-icon.png", agent.IconUrl);
        }

        [Fact]
        public async Task TestHandler_FilterByTag_ReturnsOnlyTaggedAgent()
        {
            // Act
            var result = await _testing.ListMarketplaceAgentsQueryHandler.Handle(
                new ListMarketplaceAgentsQuery { Tag = ListMarketplaceAgentsQueryHandlerTesting.TagName },
                TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<MarketplaceAgentGetResult>>>(result);
            var item = Assert.Single(success.Data);
            Assert.Equal(_testing.TaggedMarketplaceAgentId, item.Id);
        }

        [Fact]
        public async Task TestHandler_FilterByTag_UnknownTagReturnsEmpty()
        {
            // Act
            var result = await _testing.ListMarketplaceAgentsQueryHandler.Handle(
                new ListMarketplaceAgentsQuery { Tag = "NoSuchTag" },
                TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<MarketplaceAgentGetResult>>>(result);
            Assert.Empty(success.Data);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task TestHandler_BlankTag_ReturnsAllAgents(string tag)
        {
            // Act
            var result = await _testing.ListMarketplaceAgentsQueryHandler.Handle(
                new ListMarketplaceAgentsQuery { Tag = tag },
                TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<MarketplaceAgentGetResult>>>(result);
            Assert.Equal(3, success.Data.Count);
        }

        [Fact]
        public async Task TestHandler_ComputesInstallationsLiveFromTenantAgents()
        {
            // Act — MarketplaceAgent.Installations is no longer a stored column; it's computed as the
            // live count of non-deleted TenantAgents installed from each marketplace agent (see the
            // fixture: one TenantAgent installed from MarketplaceAgentId, none from the others).
            var result = await _testing.ListMarketplaceAgentsQueryHandler.Handle(new ListMarketplaceAgentsQuery(), TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<MarketplaceAgentGetResult>>>(result);

            var installedAgent = success.Data.Single(a => a.Id == _testing.MarketplaceAgentId);
            Assert.Equal(1, installedAgent.Installations);

            var neverInstalledAgent = success.Data.Single(a => a.Id == _testing.TaggedMarketplaceAgentId);
            Assert.Equal(0, neverInstalledAgent.Installations);
        }

        [Fact]
        public async Task TestHandler_ComputesStarsLiveFromUserStars()
        {
            // Act — MarketplaceAgent.Stars is no longer a stored column; it's computed as the rounded
            // average of all non-deleted UserStar ratings for each marketplace agent (fixture seeds
            // ratings of 4 and 5 for MarketplaceAgentId, plus a soft-deleted 1 that must be excluded).
            var result = await _testing.ListMarketplaceAgentsQueryHandler.Handle(new ListMarketplaceAgentsQuery(), TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<MarketplaceAgentGetResult>>>(result);

            var ratedAgent = success.Data.Single(a => a.Id == _testing.MarketplaceAgentId);
            Assert.Equal(5, ratedAgent.Stars); // avg(4, 5) = 4.5, rounded away from zero => 5

            var unratedAgent = success.Data.Single(a => a.Id == _testing.TaggedMarketplaceAgentId);
            Assert.Equal(0, unratedAgent.Stars);
        }

        [Fact]
        public async Task TestHandler_IncludesTagsAcrossTenants()
        {
            // Act — the tag on TaggedMarketplaceAgentId belongs to "OtherTenant" (see the fixture);
            // the response must still include it, proving Tags is resolved cross-tenant like the
            // Tag filter itself, not scoped to whichever tenant a caller happens to have selected
            // (unlike ListTagsByEntityQueryHandler).
            var result = await _testing.ListMarketplaceAgentsQueryHandler.Handle(
                new ListMarketplaceAgentsQuery(), TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<List<MarketplaceAgentGetResult>>>(result);

            var taggedAgent = success.Data.Single(a => a.Id == _testing.TaggedMarketplaceAgentId);
            var taggedAgentTag = Assert.Single(taggedAgent.Tags);
            Assert.Equal(_testing.TagId, taggedAgentTag.Id);
            Assert.Equal(ListMarketplaceAgentsQueryHandlerTesting.TagName, taggedAgentTag.Name);

            var untaggedAgent = success.Data.Single(a => a.Id == _testing.MarketplaceAgentId);
            Assert.Empty(untaggedAgent.Tags);
        }
    }
}
