using AMAIS.WKFLO.AI.Application.Agents.Queries.CheckPublicAgentContactExists;
using AMAIS.WKFLO.Common.Models.Results;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.CheckPublicAgentContactExists
{
    public class CheckPublicAgentContactExistsQueryHandlerTests : IClassFixture<CheckPublicAgentContactExistsQueryHandlerTesting>
    {
        private readonly CheckPublicAgentContactExistsQueryHandlerTesting _testing;

        public CheckPublicAgentContactExistsQueryHandlerTests(CheckPublicAgentContactExistsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Handle_WithValidSubjectId_ReturnsLogServiceResult(bool contactExists)
        {
            _testing.ContactFormLogServiceMock
                .Setup(x => x.ExistsAsync(It.IsAny<Guid>(), "test-tenant", It.IsAny<CancellationToken>()))
                .ReturnsAsync(contactExists);

            // Act
            var result = await _testing.Handler.Handle(new CheckPublicAgentContactExistsQuery
            {
                ShareId = "share-001",
                SubjectId = Guid.NewGuid().ToString()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            var success = Assert.IsType<SuccessResult<bool>>(result);
            Assert.Equal(contactExists, success.Data);
        }

        [Fact]
        public async Task Handle_WithInvalidSubjectId_ReturnsFalseWithoutCallingService()
        {
            _testing.ContactFormLogServiceMock.Invocations.Clear();

            // Act
            var result = await _testing.Handler.Handle(new CheckPublicAgentContactExistsQuery
            {
                ShareId = "share-001",
                SubjectId = "not-a-guid"
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            var success = Assert.IsType<SuccessResult<bool>>(result);
            Assert.False(success.Data);
            _testing.ContactFormLogServiceMock.Verify(
                x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
