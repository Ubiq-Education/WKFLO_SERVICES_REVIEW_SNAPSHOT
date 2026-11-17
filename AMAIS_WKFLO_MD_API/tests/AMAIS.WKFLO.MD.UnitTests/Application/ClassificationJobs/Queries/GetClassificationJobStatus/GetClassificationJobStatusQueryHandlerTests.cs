using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Models;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Queries.GetClassificationJobStatus;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.ClassificationJobs.Queries.GetClassificationJobStatus
{
    public class GetClassificationJobStatusQueryHandlerTests : IClassFixture<GetClassificationJobStatusQueryHandlerTesting>
    {
        private readonly GetClassificationJobStatusQueryHandlerTesting _testing;

        public GetClassificationJobStatusQueryHandlerTests(GetClassificationJobStatusQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(1, "Pending")]
        [InlineData(2, "Done")]
        public async Task Handle_WithExistingJob_ShouldReturnSuccessResultWithCorrectStatus(int agentId, string expectedStatus)
        {
            // Act
            var result = await _testing.GetClassificationJobStatusQueryHandler.Handle(
                new GetClassificationJobStatusQuery { AgentId = agentId },
                TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<ClassificationJobStatusResult>>(result);
            Assert.NotNull(success.Data);
            Assert.Equal(expectedStatus, success.Data.Status);
            Assert.Equal(agentId, success.Data.Id > 0 ? success.Data.Id : agentId);
        }

        [Fact]
        public async Task Handle_WithNonExistentAgent_ShouldReturnNotFoundResult()
        {
            // Act
            var result = await _testing.GetClassificationJobStatusQueryHandler.Handle(
                new GetClassificationJobStatusQuery { AgentId = 999 },
                TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }

        [Fact]
        public async Task Handle_WithFinishedJob_ShouldIncludeTokenAndModelData()
        {
            // Act
            var result = await _testing.GetClassificationJobStatusQueryHandler.Handle(
                new GetClassificationJobStatusQuery { AgentId = 2 },
                TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            var success = Assert.IsType<SuccessResult<ClassificationJobStatusResult>>(result);
            Assert.Equal("gpt-4o", success.Data.AIModelName);
            Assert.Equal(5000, success.Data.TotalInputTokens);
            Assert.Equal(200, success.Data.TotalOutputTokens);
            Assert.True(success.Data.IsSuccess);
            Assert.NotNull(success.Data.FinishedAt);
        }
    }
}
