using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Models;
using Dapr.Client;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.ChatStop
{
    /// <summary>
    /// US 7501: publishes a Stop on <c>pubsub-ai-broadcast</c> / <c>wkflo-chat-stop</c>. That component's consumer id is unique
    /// per replica, so every AI API replica receives it (see <c>ChatStopSubscriber</c>).
    /// </summary>
    /// <remarks>
    /// Never fails the Stop: when the component is missing or the publish fails, it logs a warning. The replica that
    /// received the Stop still cancels its own turns, and every other turn finds the Stop at its next checkpoint read.
    /// </remarks>
    public sealed class DaprChatStopBroadcaster : IChatStopBroadcaster
    {
        private readonly DaprClient _daprClient;
        private readonly ILogger<DaprChatStopBroadcaster> _logger;

        public DaprChatStopBroadcaster(DaprClient daprClient, ILogger<DaprChatStopBroadcaster> logger)
        {
            _daprClient = daprClient;
            _logger = logger;
        }

        public async Task<bool> PublishAsync(ChatStopSignal signal, CancellationToken cancellationToken = default)
        {
            try
            {
                await _daprClient.PublishEventAsync(
                    CommonConsts.CommonAiBroadcastPubSubConsts.PubSubName,
                    CommonConsts.CommonAiBroadcastPubSubConsts.ChatStopTopic,
                    signal,
                    cancellationToken);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Broadcasting the stop of session {SessionId} on {PubSub}/{Topic} failed; other replicas will find it at their next checkpoint read",
                    signal.SessionId, CommonConsts.CommonAiBroadcastPubSubConsts.PubSubName, CommonConsts.CommonAiBroadcastPubSubConsts.ChatStopTopic);
                return false;
            }
        }
    }
}
