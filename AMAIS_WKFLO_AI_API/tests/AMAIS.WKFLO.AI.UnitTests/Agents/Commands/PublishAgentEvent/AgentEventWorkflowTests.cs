using AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent;
using Dapr.Workflow;
using Moq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Commands.PublishAgentEvent
{
    public class AgentEventWorkflowTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RunAsync_CallsActivityWithMessage_ReturnsActivityResult(bool activityResult)
        {
            var contextMock = new Mock<WorkflowContext>();
            contextMock
                .Setup(x => x.CallActivityAsync<bool>(nameof(AgentEventWorkflowActivity), "msg:instance-1", null))
                .ReturnsAsync(activityResult);

            var workflow = new AgentEventWorkflow();

            var result = await workflow.RunAsync(contextMock.Object, "msg:instance-1");

            Assert.Equal(activityResult, result);
            contextMock.Verify(x => x.CallActivityAsync<bool>(nameof(AgentEventWorkflowActivity), "msg:instance-1", null), Times.Once);
        }
    }
}
