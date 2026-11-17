using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.MessageTopicClassifications.Models;
using AMAIS.WKFLO.MD.Application.MessageTopicClassifications.Queries.GetAgentTopicClassificationSummary;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.MessageTopicClassifications.Queries.GetAgentTopicClassificationSummary
{
    public class GetAgentTopicClassificationSummaryQueryHandlerTests : IClassFixture<GetAgentTopicClassificationSummaryQueryHandlerTesting>
    {
        private readonly GetAgentTopicClassificationSummaryQueryHandlerTesting _testing;

        public GetAgentTopicClassificationSummaryQueryHandlerTests(GetAgentTopicClassificationSummaryQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(1, DateRangeOption.Last30Days, ResultType.Ok, 2)]
        [InlineData(1, DateRangeOption.Last90Days, ResultType.Ok, 2)]
        public async Task Handle_ReturnsTopicSummary(int agentId, DateRangeOption dateRange, ResultType expectedResult, int expectedTopicCount)
        {
            // Act
            var result = await _testing.Handler.Handle(new GetAgentTopicClassificationSummaryQuery
            {
                AgentId = agentId,
                DateRange = dateRange
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == expectedResult);
            if (result is SuccessResult<List<AgentTopicClassificationSummaryResult>> success)
            {
                Assert.Equal(expectedTopicCount, success.Data.Count);
                Assert.True(success.Data[0].Count >= success.Data[^1].Count);
            }
        }

        [Theory]
        [InlineData(1, DateRangeOption.Last12Months, ResultType.Ok, 3)]
        [InlineData(99, DateRangeOption.Last30Days, ResultType.NotFound, 0)]
        public async Task Handle_ReturnsCorrectResultType(int agentId, DateRangeOption dateRange, ResultType expectedResult, int expectedTopicCount)
        {
            // Act
            var result = await _testing.Handler.Handle(new GetAgentTopicClassificationSummaryQuery
            {
                AgentId = agentId,
                DateRange = dateRange
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == expectedResult);
            if (result is SuccessResult<List<AgentTopicClassificationSummaryResult>> success)
            {
                Assert.Equal(expectedTopicCount, success.Data.Count);
            }
        }
    }
}
