using AMAIS.WKFLO.AI.Application.Agents.Workflows;
using Dapr.Workflow;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Workflows
{
    public class PurgeChatImagesWorkflowTests
    {
        [Theory]
        [InlineData("2026-10-22T10:17:23Z", "2026-10-22T11:00:00Z")]
        [InlineData("2026-10-22T10:00:00Z", "2026-10-22T11:00:00Z")]
        [InlineData("2026-10-22T10:59:59.999Z", "2026-10-22T11:00:00Z")]
        [InlineData("2026-10-22T23:30:00Z", "2026-10-23T00:00:00Z")]
        [InlineData("2026-12-31T23:00:01Z", "2027-01-01T00:00:00Z")]
        public void NextTickUtc_IsTopOfTheNextUtcHour(string now, string expected)
        {
            var nowUtc = DateTime.Parse(now, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            var expectedUtc = DateTime.Parse(expected, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

            Assert.Equal(expectedUtc, PurgeChatImagesWorkflow.NextTickUtc(nowUtc));
            Assert.Equal(expectedUtc - nowUtc, PurgeChatImagesWorkflow.DelayUntilNextTick(nowUtc));
        }

        [Fact]
        public async Task RunAsync_RunsActivityWithTickTime_SleepsUntilNextUtcHour_AndContinuesAsNew()
        {
            var now = new DateTime(2026, 10, 22, 10, 17, 0, DateTimeKind.Utc);
            var contextMock = new Mock<WorkflowContext>();
            contextMock.SetupGet(x => x.CurrentUtcDateTime).Returns(now);

            var workflow = new PurgeChatImagesWorkflow();

            var result = await workflow.RunAsync(contextMock.Object, string.Empty);

            Assert.True(result);
            contextMock.Verify(x => x.CallActivityAsync(nameof(PurgeChatImagesWorkflowActivity), "2026-10-22T10:17:00.0000000Z", null), Times.Once);
            contextMock.Verify(x => x.CreateTimer(TimeSpan.FromMinutes(43), It.IsAny<CancellationToken>()), Times.Once);
            contextMock.Verify(x => x.ContinueAsNew(string.Empty, It.IsAny<bool>()), Times.Once);
        }
    }
}
