using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.AgentTokensUsed;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadRunnableAgentChatHistoryBySession;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    /// <summary>
    /// US 7502: a turn the user stopped stores nothing of its own work. Whatever it added in memory (the question, tool
    /// calls and results, the partial answer) is dropped: the agent's history is saved as it was before the turn, plus
    /// one "Stopped" marker. The marker is always sent live; it is only saved when the agent already has a saved history
    /// (a first turn stopped leaves no chat and no conversation behind).
    /// </summary>
    public partial class SemanticKernelAgentService
    {
        /// <summary>
        /// US 7502: the final save of a turn stopped while running. Records the tokens used, sends the marker live and
        /// saves the pre-turn history plus the marker. Post messages and message-resolution scheduling are skipped.
        /// Uses <see cref="CancellationToken.None"/> (the turn's token is the one that was cancelled). Never throws: the
        /// caller still reports the turn as cancelled. Each step is independent: a failed usage report never costs the
        /// marker, and a failed marker never costs the usage report or the thread cleanup.
        /// </summary>
        private async Task FinishStoppedTurnAsync(RunChatData data, string messageId, long inputTokensCount, long outputTokensCount, string chatbotIconUrl)
        {
            data.TurnSaved = true;
            data.StoppedMarkerOutcome = StoppedMarkerOutcome.Failed;

            try
            {
                // The tokens were consumed even though nothing of the answer is kept.
                await _mediator.Publish(new AgentTokensUsedDomainEvent(BuildAgentAnswer(data, messageId, string.Empty, inputTokensCount, outputTokensCount)),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recording the token usage of the stopped turn of session {SessionId} failed", data.SessionId);
            }

            try
            {
                data.StoppedMarkerOutcome = await SendAndSaveStoppedMarkerAsync(data.Agent, data.SessionId, chatbotIconUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving the stopped turn of session {SessionId} failed", data.SessionId);
            }

            try
            {
                await data.ChatHistoryAgentThread.DeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleaning up the stopped turn's thread of session {SessionId} failed", data.SessionId);
            }
        }

        /// <summary>
        /// US 7502: a turn stopped before its answer started streaming (or before it ran at all). Sends the marker live and
        /// saves it after the existing history; the question is not stored. Does nothing when the turn already saved
        /// itself (<paramref name="runData"/>). No answer, so no token usage event. Never throws.
        /// </summary>
        /// <returns>What happened to the marker (the run's own outcome when the turn already saved itself).</returns>
        internal async Task<StoppedMarkerOutcome> SaveEarlyStoppedTurnAsync(RunTextOptions options, RunChatData runData)
        {
            if (runData?.TurnSaved == true)
                return runData.StoppedMarkerOutcome ?? StoppedMarkerOutcome.TurnAlreadySaved;

            if (runData != null)
                runData.TurnSaved = true;

            try
            {
                var chatbotIconUrl = await TryGetChatbotIconUrlAsync(options.Agent.Id, options.RootIsWidget, CancellationToken.None);
                var outcome = await SendAndSaveStoppedMarkerAsync(options.Agent, options.SessionId, chatbotIconUrl);
                if (runData != null)
                    runData.StoppedMarkerOutcome = outcome;

                return outcome;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving the early stopped turn of session {SessionId} failed", options.SessionId);
                if (runData != null)
                    runData.StoppedMarkerOutcome = StoppedMarkerOutcome.Failed;

                return StoppedMarkerOutcome.Failed;
            }
        }

        public async Task SaveTurnStoppedBeforeRunAsync(RunnableAgent agent, string sessionId, bool? rootIsWidget)
        {
            if (agent == null || string.IsNullOrWhiteSpace(sessionId))
                return;

            var marker = await SaveEarlyStoppedTurnAsync(new RunTextOptions { Agent = agent, SessionId = sessionId, RootIsWidget = rootIsWidget }, runData: null);
            _logger.LogInformation("Turn stopped: session {SessionId}, agent {AgentId}, phase {StopPhase}, Stopped marker {StoppedMarker}",
                sessionId, agent.Id, "queued", marker);
        }

        public Task<StoppedMarkerOutcome> SaveStoppedMarkerAsync(RunnableAgent agent, string sessionId, DateTimeOffset stoppedAt) =>
            SaveStoppedMarkerAsync(agent, sessionId, stoppedAt, Guid.NewGuid().ToString());

        /// <summary>
        /// Saves the agent's persisted history (freshly loaded, never the stopped turn's working copy) plus one marker for
        /// the Stop at <paramref name="stoppedAt"/>. Saves nothing when the agent has no saved history in the session yet,
        /// or when that history already holds a marker of the same Stop. The save is flagged
        /// <see cref="ChatHistoryUpdatedDomainEvent.IsStoppedTurn"/>: no conversation analysis, no status change, no
        /// long-term memory. The marker is stamped with <paramref name="stoppedAt"/> and placed by that time, so a save
        /// that lands late (after the person already sent the next message) never puts the divider after newer messages.
        /// Returns what happened to the marker. Never throws.
        /// </summary>
        internal async Task<StoppedMarkerOutcome> SaveStoppedMarkerAsync(RunnableAgent agent, string sessionId, DateTimeOffset stoppedAt, string markerMessageId)
        {
            if (agent == null || string.IsNullOrWhiteSpace(sessionId))
                return StoppedMarkerOutcome.NoAgent;

            try
            {
                var (createdAt, chatName, history) = await _mediator.Send(new LoadRunnableAgentChatHistoryBySessionQuery
                {
                    Agent = agent,
                    SessionId = sessionId
                }, CancellationToken.None);

                // Nothing saved before this turn: no chat and no conversation are started with a lone divider.
                if (history == null || history.Count == 0)
                    return StoppedMarkerOutcome.NoHistoryYet;

                var savedMarkers = StoppedTurnMarker.Detach(history);
                if (savedMarkers.Any(m => StoppedTurnMarker.ReadStoppedAt(m.Metadata) is { } at && at.UtcTicks == stoppedAt.UtcTicks))
                    return StoppedMarkerOutcome.AlreadyPresent;

                // Where the Stop happened: after every message before the Stop, before any message sent after it.
                StoppedTurnMarker.Reattach(history, [StoppedTurnMarker.Build(markerMessageId, stoppedAt, stoppedAt)]);

                await _mediator.Publish(new ChatHistoryUpdatedDomainEvent
                (
                    agent,
                    sessionId,
                    chatName,
                    createdAt,
                    _dateTimeProvider.DateTimeOffsetUtcNow(),
                    BuildPersistableChatHistory(history, savedMarkers),
                    null
                )
                { IsStoppedTurn = true }, CancellationToken.None);

                return StoppedMarkerOutcome.Saved;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving the Stopped marker of session {SessionId} for agent {AgentId} failed", sessionId, agent.Id);
                return StoppedMarkerOutcome.Failed;
            }
        }

        /// <summary>
        /// Sends the marker live (SignalR <c>ReceiveMessage</c>), then saves it with the same id and time (the Stop's). The
        /// save does not depend on the live send: a failed send is logged and the marker is still saved.
        /// </summary>
        private async Task<StoppedMarkerOutcome> SendAndSaveStoppedMarkerAsync(RunnableAgent agent, string sessionId, string chatbotIconUrl)
        {
            var now = _dateTimeProvider.DateTimeOffsetUtcNow();
            var stoppedAt = await ReadStoppedAtAsync(sessionId, now);
            var markerMessageId = Guid.NewGuid().ToString();

            try
            {
                await _notificationService.SendMessageToChat(sessionId,
                    StoppedTurnMarker.BuildLiveMessage(markerMessageId, stoppedAt, stoppedAt),
                    agent.Id, agent.Name, chatbotIconUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sending the Stopped marker of session {SessionId} live failed; it is still saved", sessionId);
            }

            return await SaveStoppedMarkerAsync(agent, sessionId, stoppedAt, markerMessageId);
        }

        /// <summary>
        /// When the user pressed Stop, as written by the Stop command before it cancels anything. Every agent stopped by
        /// the same Stop reads the same value, which is what lets the merged history show a single divider.
        /// </summary>
        private async Task<DateTimeOffset> ReadStoppedAtAsync(string sessionId, DateTimeOffset fallback)
        {
            if (_chatStopStateStore == null || string.IsNullOrWhiteSpace(sessionId))
                return fallback;

            try
            {
                return await _chatStopStateStore.GetStoppedAtAsync(_tenantContext.TenantName, sessionId, CancellationToken.None) ?? fallback;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reading the stop time of session {SessionId} failed", sessionId);
                return fallback;
            }
        }
    }
}
