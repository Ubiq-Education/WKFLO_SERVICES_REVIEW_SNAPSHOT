using AMAIS.WKFLO.AI.WebAPI.EndPoints.Dapr;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using System;

namespace AMAIS.WKFLO.AI.UnitTests.EndPoints.Dapr
{
    /// <summary>US 7501: each replica applies a received Stop to its own turns and always acknowledges it.</summary>
    public class ChatStopSubscriberTests
    {
        private static readonly DateTimeOffset StoppedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly ChatTurnRegistry _registry = new();
        private readonly ChatStopBroadcastHandler _handler;
        private readonly ChatStopSubscriber _subscriber = new();

        public ChatStopSubscriberTests()
        {
            _handler = new ChatStopBroadcastHandler(_registry, NullLogger<ChatStopBroadcastHandler>.Instance);
        }

        private ActionResult Call(ChatStopSignal signal) =>
            _subscriber.ChatStopCall(new CloudEvent<ChatStopSignal>(signal), _handler, NullLogger<ChatStopSubscriber>.Instance);

        [Fact]
        public void ChatStopCall_CancelsOnlyThisSessionsTurnsStartedBeforeTheStop()
        {
            using var running = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(-10));
            using var nextMessage = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(1));
            using var otherSession = _registry.Register("tenant-a", "session-2", StoppedAt.AddSeconds(-10));

            var result = Call(new ChatStopSignal("tenant-a", "session-1", StoppedAt));

            Assert.IsType<OkResult>(result);
            Assert.True(running.StopRequested);
            Assert.False(nextMessage.StopRequested);
            Assert.False(otherSession.StopRequested);
        }

        [Fact]
        public void ChatStopCall_SessionNotRunningHere_StillSucceeds()
        {
            // Not this replica's session: acknowledged, so it is never retried or dead-lettered.
            Assert.IsType<OkResult>(Call(new ChatStopSignal("tenant-a", "session-9", StoppedAt)));
        }

        [Fact]
        public void ChatStopCall_MalformedMessage_StillSucceeds()
        {
            Assert.IsType<OkResult>(Call(null));
            Assert.IsType<OkResult>(Call(new ChatStopSignal("tenant-a", "", StoppedAt)));
        }

        [Fact]
        public void CloudEvent_FromTheWire_BindsTheCamelCasePayload()
        {
            // The app's MVC input formatter is Newtonsoft; this is the shape Dapr delivers for a published { tenantName, sessionId, stoppedAt }.
            const string body = """
                {"specversion":"1.0","type":"com.dapr.event.sent","source":"amais-wkflo-ai-webapi","id":"1",
                 "datacontenttype":"application/json","pubsubname":"pubsub-ai-broadcast","topic":"wkflo-chat-stop",
                 "data":{"tenantName":"tenant-a","sessionId":"session-1","stoppedAt":"2026-10-19T12:00:00+00:00"}}
                """;

            var cloudEvent = JsonConvert.DeserializeObject<CloudEvent<ChatStopSignal>>(body);

            Assert.Equal(new ChatStopSignal("tenant-a", "session-1", StoppedAt), cloudEvent.Data);
        }
    }
}
