using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using AMAIS.WKFLO.MD.Application.Tags.Queries.ListEntitiesByTag;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Queries.ListEntitiesByTag
{
    public class ListEntitiesByTagQueryHandlerTests : IClassFixture<ListEntitiesByTagQueryHandlerTesting>
    {
        private readonly ListEntitiesByTagQueryHandlerTesting _testing;

        public ListEntitiesByTagQueryHandlerTests(ListEntitiesByTagQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_ReturnsOnlyLiveEntities_ExcludingSoftDeletedTargetsPerType()
        {
            // Act
            var result = await _testing.ListEntitiesByTagQueryHandler.Handle(new ListEntitiesByTagQuery()
            {
                Id = _testing.TagId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<System.Collections.Generic.List<EntityTagResult>>>(result);
            Assert.NotNull(success.Data);

            Assert.Equal(4, success.Data.Count);

            Assert.Contains(success.Data, t => t.EntityType == CommonConsts.TaggableEntityTypes.Agent && t.EntityId == _testing.LiveAgentId);
            Assert.Contains(success.Data, t => t.EntityType == CommonConsts.TaggableEntityTypes.Tool && t.EntityId == _testing.LiveToolId);
            Assert.Contains(success.Data, t => t.EntityType == CommonConsts.TaggableEntityTypes.AIModel && t.EntityId == _testing.LiveAIModelId);

            Assert.DoesNotContain(success.Data, t => t.EntityType == CommonConsts.TaggableEntityTypes.Agent && t.EntityId == _testing.DeadAgentId);
            Assert.DoesNotContain(success.Data, t => t.EntityType == CommonConsts.TaggableEntityTypes.Tool && t.EntityId == _testing.DeadToolId);
            Assert.DoesNotContain(success.Data, t => t.EntityType == CommonConsts.TaggableEntityTypes.AIModel && t.EntityId == _testing.DeadAIModelId);
        }

        [Fact]
        public async Task Handle_ReturnsLiveEntity_WhenEntityTypeCasingDiffersFromTheConstant()
        {
            // Act
            var result = await _testing.ListEntitiesByTagQueryHandler.Handle(new ListEntitiesByTagQuery()
            {
                Id = _testing.TagId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<System.Collections.Generic.List<EntityTagResult>>>(result);
            Assert.NotNull(success.Data);

            // Validators accept EntityType case-insensitively and the assign handler stores it verbatim, so
            // a row tagged with e.g. "agent" (lowercase) instead of the "Agent" constant is still legitimate
            // and its live target must still be returned, regardless of exact casing.
            Assert.Contains(success.Data, t =>
                string.Equals(t.EntityType, "agent", System.StringComparison.OrdinalIgnoreCase) &&
                t.EntityId == _testing.LiveAgentIdWithLowercaseEntityType);
        }
    }
}
