using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop
{
    /// <summary>
    /// US 7501: sends a Stop to every AI API replica. The replica that receives the Stop request is usually not the
    /// one running the turn, so the signal must reach all of them.
    /// </summary>
    public interface IChatStopBroadcaster
    {
        /// <returns>Whether the Stop was actually published (false when the publish failed or there is no transport).</returns>
        Task<bool> PublishAsync(ChatStopSignal signal, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Used when no broadcast transport is configured (e.g. the functional test host). Stops then only reach the local
    /// replica's turns (cancelled directly by the stop command) and the checkpoint reads.
    /// </summary>
    public sealed class NullChatStopBroadcaster : IChatStopBroadcaster
    {
        public Task<bool> PublishAsync(ChatStopSignal signal, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    /// <summary>
    /// US 7501: what a replica does with a received broadcast: cancel its own turns of that session that started
    /// before the Stop. Replicas with no matching turn do nothing.
    /// </summary>
    public sealed class ChatStopBroadcastHandler
    {
        private readonly IChatTurnRegistry _turnRegistry;
        private readonly ILogger<ChatStopBroadcastHandler> _logger;

        public ChatStopBroadcastHandler(IChatTurnRegistry turnRegistry, ILogger<ChatStopBroadcastHandler> logger)
        {
            _turnRegistry = turnRegistry;
            _logger = logger;
        }

        /// <returns>How many local turns were cancelled.</returns>
        public int Handle(string payload)
        {
            if (!ChatStopSignal.TryParse(payload, out var signal))
            {
                _logger.LogWarning("Ignoring a malformed chat stop broadcast");
                return 0;
            }

            return Handle(signal);
        }

        /// <returns>How many local turns were cancelled.</returns>
        public int Handle(ChatStopSignal signal)
        {
            if (signal == null || string.IsNullOrWhiteSpace(signal.TenantName) || string.IsNullOrWhiteSpace(signal.SessionId) || signal.StoppedAt == default)
            {
                _logger.LogWarning("Ignoring a malformed chat stop broadcast");
                return 0;
            }

            var cancelled = _turnRegistry.CancelTurnsStartedBefore(signal.TenantName, signal.SessionId, signal.StoppedAt);

            // Every replica receives every Stop: most have nothing to cancel, so that case stays at Debug.
            _logger.Log(cancelled > 0 ? LogLevel.Information : LogLevel.Debug,
                "Stop broadcast received for session {SessionId} (tenant {TenantName}), stopped at {StoppedAt}: {CancelledTurns} turn(s) cancelled on this replica",
                signal.SessionId, signal.TenantName, signal.StoppedAt, cancelled);

            return cancelled;
        }
    }
}
