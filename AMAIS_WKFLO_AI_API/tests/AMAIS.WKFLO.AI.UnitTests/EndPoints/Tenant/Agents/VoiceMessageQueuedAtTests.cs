using AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessageTenantAgentById;
using AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents;
using AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Queue;
using Dapr.Client;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.EndPoints.Tenant.Agents
{
    /// <summary>US 7501: the voice endpoints stamp QueuedAt from the time provider, like the text chat endpoints.</summary>
    public class VoiceMessageQueuedAtTests
    {
        private static readonly DateTimeOffset QueuedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private static IDateTimeProvider Clock() => Mock.Of<IDateTimeProvider>(x => x.DateTimeOffsetUtcNow() == QueuedAt);

        private static Mock<DaprClient> CaptureQueued<T>(Action<WkfloQueueMessage<T>> capture)
        {
            var dapr = new Mock<DaprClient>();
            dapr.Setup(x => x.SaveStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<T>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<T>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, message, _, _, _) => capture(message))
                .Returns(Task.CompletedTask);
            return dapr;
        }

        [Fact]
        public async Task TenantVoiceMessage_StampsQueuedAtFromTheTimeProvider()
        {
            WkfloQueueMessage<VoiceMessageTenantAgentByIdCommand> queued = null;
            var dapr = CaptureQueued<VoiceMessageTenantAgentByIdCommand>(m => queued = m);
            var endpoint = new TenantAgentVoiceMessage(Mock.Of<IWkfloMediator>(), dapr.Object, new TenantContext(), new ChatContext(),
                new UserContext(), new AgentSharableContext(), Clock(), Mock.Of<AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop.IChatSessionOwnerStore>());

            await endpoint.HandleAsync(1, new VoiceMessageTenantAgentByIdCommand(), TestContext.Current.CancellationToken);

            Assert.Equal(QueuedAt, queued?.QueuedAt);
        }

        [Fact]
        public async Task PublicVoiceMessage_StampsQueuedAtFromTheTimeProvider()
        {
            WkfloQueueMessage<VoiceMessagePublicAgentByShareIdCommand> queued = null;
            var dapr = CaptureQueued<VoiceMessagePublicAgentByShareIdCommand>(m => queued = m);
            var endpoint = new PublicAgentVoiceMessage(Mock.Of<IWkfloMediator>(), dapr.Object, new TenantContext(), new ChatContext(),
                new UserContext(), new AgentSharableContext(), Clock(), Mock.Of<AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop.IChatSessionOwnerStore>());

            await endpoint.HandleAsync("share-1", new VoiceMessagePublicAgentByShareIdCommand(), TestContext.Current.CancellationToken);

            Assert.Equal(QueuedAt, queued?.QueuedAt);
        }
    }
}
