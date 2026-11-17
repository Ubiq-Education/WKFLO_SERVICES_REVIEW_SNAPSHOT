using AMAIS.WKFLO.AI.WebAPI.Infrastructure.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Infrastructure.ChatStop
{
    /// <summary>US 7501: Stop is broadcast to every replica over Dapr pub/sub, and a failed broadcast never fails the Stop.</summary>
    public class DaprChatStopBroadcasterTests
    {
        private static readonly ChatStopSignal Signal = new("tenant-a", "session-1", new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

        private readonly Mock<DaprClient> _daprClient = new();
        private readonly Mock<ILogger<DaprChatStopBroadcaster>> _logger = new();
        private readonly DaprChatStopBroadcaster _broadcaster;

        public DaprChatStopBroadcasterTests()
        {
            _broadcaster = new DaprChatStopBroadcaster(_daprClient.Object, _logger.Object);
        }

        [Fact]
        public async Task PublishAsync_PublishesTheSignalOnTheBroadcastComponentAndTopic()
        {
            var published = await _broadcaster.PublishAsync(Signal, TestContext.Current.CancellationToken);

            Assert.True(published);
            _daprClient.Verify(x => x.PublishEventAsync("pubsub-ai-broadcast", "wkflo-chat-stop", Signal, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PublishAsync_PublishFails_LogsAWarningAndDoesNotThrow()
        {
            // E.g. the component is not deployed: the replica that got the Stop and the checkpoint reads still stop the turns.
            _daprClient
                .Setup(x => x.PublishEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ChatStopSignal>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("pubsub pubsub-ai-broadcast not found"));

            var published = true;
            var exception = await Record.ExceptionAsync(async () => published = await _broadcaster.PublishAsync(Signal, TestContext.Current.CancellationToken));

            Assert.Null(exception);
            Assert.False(published);
            _logger.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }
    }
}
