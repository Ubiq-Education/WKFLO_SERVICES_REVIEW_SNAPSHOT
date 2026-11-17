using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using AMAIS.WKFLO.MD.Application.AICredits.Queries.ListAICredits;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Queries.ListAICredits
{
    public class ListAICreditsQueryHandlerTests : IClassFixture<ListAICreditsQueryHandlerTesting>
    {
        private readonly ListAICreditsQueryHandlerTesting _testing;

        public ListAICreditsQueryHandlerTests(ListAICreditsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.ListAICreditsQueryHandler.Handle(new ListAICreditsQuery(), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<List<AICreditGetResult>> success)
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
