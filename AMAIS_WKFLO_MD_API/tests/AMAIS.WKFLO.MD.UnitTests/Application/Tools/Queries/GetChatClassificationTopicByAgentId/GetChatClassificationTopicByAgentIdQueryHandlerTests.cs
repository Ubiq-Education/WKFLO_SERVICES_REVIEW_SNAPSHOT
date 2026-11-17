using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetChatClassificationTopicByAgentId;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.GetChatClassificationTopicByAgentId
{
    public class GetChatClassificationTopicByAgentIdQueryHandlerTests : IClassFixture<GetChatClassificationTopicByAgentIdQueryHandlerTesting>
    {
        private readonly GetChatClassificationTopicByAgentIdQueryHandlerTesting _testing;

        public GetChatClassificationTopicByAgentIdQueryHandlerTests(GetChatClassificationTopicByAgentIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Handle_ReturnsTopic_WhenAgentToolAndTopicExist(bool active)
        {
            var topicId = active ? _testing.ActiveTopicId : _testing.InactiveTopicId;

            // Act
            var result = await _testing.Handler.Handle(new GetChatClassificationTopicByAgentIdQuery
            {
                AgentId = 1,
                TopicId = topicId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<ChatClassificationTopicGetResult> success)
            {
                Assert.Equal(topicId, success.Data.Id);
                Assert.Equal(active, success.Data.IsActive);
                Assert.Equal(1, success.Data.AgentId);
                Assert.Equal(10, success.Data.ToolId);
                Assert.Equal("3", success.Data.Version);
            }
        }

        [Theory]
        [InlineData(2)] // agent exists but has no linked Chat_Classification tool
        [InlineData(99)] // agent does not exist at all
        public async Task Handle_ReturnsNotFound_WhenAgentHasNoChatClassificationTool(int agentId)
        {
            // Act
            var result = await _testing.Handler.Handle(new GetChatClassificationTopicByAgentIdQuery
            {
                AgentId = agentId,
                TopicId = _testing.ActiveTopicId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }

        [Fact]
        public async Task Handle_ReturnsNotFound_WhenTopicIdIsNotInTheToolConfig()
        {
            // Act
            var result = await _testing.Handler.Handle(new GetChatClassificationTopicByAgentIdQuery
            {
                AgentId = 1,
                TopicId = Guid.NewGuid()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }

        [Fact]
        public async Task Handle_ReturnsNotFound_WhenToolConfigIsNotValidJson()
        {
            // Act
            var result = await _testing.Handler.Handle(new GetChatClassificationTopicByAgentIdQuery
            {
                AgentId = 4,
                TopicId = _testing.ActiveTopicId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
