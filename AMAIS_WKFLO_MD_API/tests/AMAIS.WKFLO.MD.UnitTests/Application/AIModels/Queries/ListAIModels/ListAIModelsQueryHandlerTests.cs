using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.AIModels.Queries.ListAIModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Queries.ListAIModels
{
    public class ListAIModelsQueryHandlerTests : IClassFixture<ListAIModelsQueryHandlerTesting>
    {
        private readonly ListAIModelsQueryHandlerTesting _testing;

        public ListAIModelsQueryHandlerTests(ListAIModelsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.ListAIModelsQueryHandler.Handle(new ListAIModelsQuery(), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<List<AIModelGetResult>> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(2, success.Data.Count);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Ok");
            }
        }
    }
}
