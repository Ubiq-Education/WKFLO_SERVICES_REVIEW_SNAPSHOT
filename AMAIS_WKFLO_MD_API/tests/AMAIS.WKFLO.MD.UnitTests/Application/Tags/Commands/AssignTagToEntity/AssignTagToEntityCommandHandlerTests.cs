using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Commands.AssignTagToEntity;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Commands.AssignTagToEntity
{
    public class AssignTagToEntityCommandHandlerTests(AssignTagToEntityCommandHandlerTesting testing) : IClassFixture<AssignTagToEntityCommandHandlerTesting>
    {
        private AssignTagToEntityCommandHandlerTesting Testing => testing;

        [Theory]
        [InlineData(1, "Agent", 10, ResultType.Ok)]
        [InlineData(1, "Tool", 5, ResultType.Ok)]
        [InlineData(1, "AIModel", 3, ResultType.Ok)]
        public async Task TestHandler(int tagId, string entityType, int entityId, ResultType resultType)
        {
            var result = await Testing.AssignTagToEntityCommandHandler.Handle(new AssignTagToEntityCommand
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
                Assert.Equal(entityType, success.Data.EntityType);
                Assert.Equal(entityId, success.Data.EntityId);
            }
        }
    }
}
