using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Commands.UnassignTagFromEntity;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Commands.UnassignTagFromEntity
{
    public class UnassignTagFromEntityCommandHandlerTests(UnassignTagFromEntityCommandHandlerTesting testing) : IClassFixture<UnassignTagFromEntityCommandHandlerTesting>
    {
        private UnassignTagFromEntityCommandHandlerTesting Testing => testing;

        [Theory]
        [InlineData(1, "Agent", 10, ResultType.Ok)]
        [InlineData(1, "Agent", 99, ResultType.NotFound)]
        [InlineData(99, "Agent", 10, ResultType.NotFound)]
        public async Task TestHandler(int tagId, string entityType, int entityId, ResultType resultType)
        {
            var result = await Testing.UnassignTagFromEntityCommandHandler.Handle(new UnassignTagFromEntityCommand
            {
                Id = tagId,
                EntityType = entityType,
                EntityId = entityId
            }, TestContext.Current.CancellationToken);

            Assert.Equal(resultType, result.ResultType);
            if (result is SuccessResult<TagAssignResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(tagId, success.Data.TagId);
            }
        }
    }
}
