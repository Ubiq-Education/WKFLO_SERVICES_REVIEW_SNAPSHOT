using AMAIS.WKFLO.AI.Application.Agents.Actors;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.WebAPI.EndPoints.Dapr;
using AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Models.SignalR;
using System;
using System.Linq;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using Dapr;
using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.EndPoints.Dapr
{
    public class AgentEventSubscriberTests
    {
        private const string Key = "msg:instance-1";

        private readonly Mock<DaprClient> _daprClientMock;
        private readonly Mock<IActorProxyFactory> _actorProxyFactoryMock;
        private readonly Mock<IAgentActor> _agentActorMock;
        private readonly AgentEventSubscriber _subscriber;
        private readonly ChatErrorHarness _harness = new();

        public AgentEventSubscriberTests()
        {
            _daprClientMock = new Mock<DaprClient>();
            _actorProxyFactoryMock = new Mock<IActorProxyFactory>();
            _agentActorMock = new Mock<IAgentActor>();
            _subscriber = new AgentEventSubscriber();
        }

        private static WkfloQueueMessage<AgentEventMessage> BuildQueueMessage(string sessionId = "session-1", int agentId = 42) => new()
        {
            TenantContext = new TenantContext { TenantName = "tenant-a" },
            Command = new AgentEventMessage
            {
                AgentId = agentId,
                AgentRole = "watcher",
                Input = new AgentInvocationInput { Text = "react" },
                SessionId = sessionId,
                EventType = "task-completed"
            }
        };

        [Fact]
        public async Task AgentEventCall_ValidQueuedEvent_EnqueuesOnActorForSessionAndAgent()
        {
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildQueueMessage());

            _actorProxyFactoryMock
                .Setup(x => x.CreateActorProxy<IAgentActor>(
                    It.Is<ActorId>(id => id.GetId() == "session-1:42"), nameof(AgentActor), null))
                .Returns(_agentActorMock.Object);

            var result = await _subscriber.AgentEventCall(new CloudEvent<string>(Key), _daprClientMock.Object, _actorProxyFactoryMock.Object, NullLogger<AgentEventSubscriber>.Instance, _harness.Reporter, _harness.Chat.Object);

            Assert.IsType<Microsoft.AspNetCore.Mvc.OkResult>(result);
            _agentActorMock.Verify(x => x.EnqueueEventAsync(Key), Times.Once);
        }

        [Fact]
        public async Task AgentEventCall_NoQueuedMessageFound_ReturnsOkWithoutResolvingActor()
        {
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((WkfloQueueMessage<AgentEventMessage>)null);

            var result = await _subscriber.AgentEventCall(new CloudEvent<string>(Key), _daprClientMock.Object, _actorProxyFactoryMock.Object, NullLogger<AgentEventSubscriber>.Instance, _harness.Reporter, _harness.Chat.Object);

            Assert.IsType<Microsoft.AspNetCore.Mvc.OkResult>(result);
            _actorProxyFactoryMock.Verify(x => x.CreateActorProxy<IAgentActor>(It.IsAny<ActorId>(), It.IsAny<string>(), It.IsAny<ActorProxyOptions>()), Times.Never);
        }

        [Fact]
        public async Task AgentEventCall_LoadStateThrows_SwallowsExceptionAndReturnsOk()
        {
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.InvalidOperationException("state store unavailable"));

            var result = await _subscriber.AgentEventCall(new CloudEvent<string>(Key), _daprClientMock.Object, _actorProxyFactoryMock.Object, NullLogger<AgentEventSubscriber>.Instance, _harness.Reporter, _harness.Chat.Object);

            Assert.IsType<Microsoft.AspNetCore.Mvc.OkResult>(result);
        }

        [Fact]
        public async Task AgentEventCall_ActorEnqueueThrows_SwallowsExceptionAndReturnsOk()
        {
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildQueueMessage());

            _actorProxyFactoryMock
                .Setup(x => x.CreateActorProxy<IAgentActor>(It.IsAny<ActorId>(), nameof(AgentActor), null))
                .Returns(_agentActorMock.Object);

            _agentActorMock
                .Setup(x => x.EnqueueEventAsync(Key))
                .ThrowsAsync(new System.InvalidOperationException("actor unreachable"));

            var result = await _subscriber.AgentEventCall(new CloudEvent<string>(Key), _daprClientMock.Object, _actorProxyFactoryMock.Object, NullLogger<AgentEventSubscriber>.Instance, _harness.Reporter, _harness.Chat.Object);

            Assert.IsType<Microsoft.AspNetCore.Mvc.OkResult>(result);
        }

        [Fact]
        public async Task AgentEventCall_ActorEnqueueThrows_FailsThatHandOffsAgentInTheChat_WithACodeOnly()
        {
            // US 7959 / Task 7963: the hand-off will never run - its card fails (1115), its bubble stops, a non-terminal marker.
            var queueMessage = BuildQueueMessage();
            queueMessage.QueuedAt = ChatErrorHarness.Now.AddSeconds(-5);
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);
            _actorProxyFactoryMock
                .Setup(x => x.CreateActorProxy<IAgentActor>(It.IsAny<ActorId>(), nameof(AgentActor), null))
                .Returns(_agentActorMock.Object);
            _agentActorMock
                .Setup(x => x.EnqueueEventAsync(Key))
                .ThrowsAsync(new InvalidOperationException("SENTINEL actor unreachable"));

            await _subscriber.AgentEventCall(new CloudEvent<string>(Key), _daprClientMock.Object, _actorProxyFactoryMock.Object, NullLogger<AgentEventSubscriber>.Instance, _harness.Reporter, _harness.Chat.Object);

            var card = Assert.Single(_harness.Chat.SubagentUpdatesOf(42));
            Assert.Equal(SubagentActivityStatus.Failed, card.Status);
            Assert.Equal(ChatErrorCodes.ChatInfrastructureError, card.ErrorCode);
            Assert.Equal("task-completed", card.Action);
            Assert.Equal([false], _harness.Chat.TypingOf(42));
            var marker = Assert.Single(_harness.Chat.Markers());
            Assert.Equal(false, marker.Message.Metadata[AMAIS.WKFLO.Common.Models.CommonConsts.CommonMetadataConsts.IsTerminal]);
            Assert.Empty(_harness.Chat.States());
            _harness.Chat.AssertNoLeak("SENTINEL", "actor unreachable");
        }

        [Fact]
        public async Task AgentEventCall_LoadStateThrows_TellsTheChatNothing()
        {
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("state store unavailable"));

            await _subscriber.AgentEventCall(new CloudEvent<string>(Key), _daprClientMock.Object, _actorProxyFactoryMock.Object, NullLogger<AgentEventSubscriber>.Instance, _harness.Reporter, _harness.Chat.Object);

            Assert.Empty(_harness.Chat.Sent);
        }
    }
}
