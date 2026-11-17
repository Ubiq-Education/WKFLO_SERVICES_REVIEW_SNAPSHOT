using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetAgentChatSettingByShareId;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetAgentChatSettingByShareId
{
    public class GetAgentChatSettingByShareIdQueryHandlerTests : IClassFixture<GetAgentChatSettingByShareIdQueryHandlerTesting>
    {
        private readonly GetAgentChatSettingByShareIdQueryHandlerTesting _testing;

        public GetAgentChatSettingByShareIdQueryHandlerTests(GetAgentChatSettingByShareIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_WithAgentHavingMaisyTag_ReturnsCanSelectFeatureTrue()
        {
            // Act
            var result = await _testing.GetAgentChatSettingByShareIdQueryHandler.Handle(new GetAgentChatSettingByShareIdQuery()
            {
                ShareId = _testing.ShareIdWithMaisyTag
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<AgentChatSettingResult> success)
            {
                Assert.True(success.Data.CanSelectFeature);
            }
            else
            {
                Assert.Fail("Expected a SuccessResult<AgentChatSettingResult>");
            }
        }

        [Fact]
        public async Task Handle_WithAgentNotHavingMaisyTag_ReturnsCanSelectFeatureFalse()
        {
            // Act
            var result = await _testing.GetAgentChatSettingByShareIdQueryHandler.Handle(new GetAgentChatSettingByShareIdQuery()
            {
                ShareId = _testing.ShareIdWithoutMaisyTag
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<AgentChatSettingResult> success)
            {
                Assert.False(success.Data.CanSelectFeature);
            }
            else
            {
                Assert.Fail("Expected a SuccessResult<AgentChatSettingResult>");
            }
        }

        [Fact]
        public async Task Handle_WithNonExistentShareId_ReturnsNotFoundResult()
        {
            // Act
            var result = await _testing.GetAgentChatSettingByShareIdQueryHandler.Handle(new GetAgentChatSettingByShareIdQuery()
            {
                ShareId = "non-existent-share-id"
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
