using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr.Client;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent
{
    /// <summary>
    /// Wraps the event payload in a <see cref="WkfloQueueMessage{T}"/>, saves it into
    /// <c>statestore-ai</c>, and publishes only the short correlation key onto
    /// <c>AiWorkerTopicConsts.AgentEventMessageTopic</c> — the message broker backing
    /// <c>pubsub-ai</c> has a payload size limit, so the full payload (which can carry an
    /// arbitrarily long <see cref="AgentInvocationInput.Text"/>) is never published directly, matching
    /// <c>TenantAgentChatMessage</c>/<c>PublicAgentChatMessage</c>'s strategy. Also populates the
    /// queue message's ambient contexts (<see cref="TenantContext"/>, <see cref="ChatContext"/>,
    /// <see cref="UserContext"/>, <see cref="AgentSharableContext"/>) from this request's own DI
    /// scope, so <c>AgentEventSubscriber</c> can restore them onto <c>IAgentInvoker</c>'s scoped
    /// context instances — without this, every invocation would run with a blank tenant context.
    /// </summary>
    public class PublishAgentEventCommandHandler : IRequestHandler<PublishAgentEventCommand, Result>
    {
        private readonly DaprClient _daprClient;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly AgentSharableContext _agentSharableContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<PublishAgentEventCommandHandler> _logger;
        private readonly ISessionWorkingStateStore _workingState;

        public PublishAgentEventCommandHandler(DaprClient daprClient, TenantContext tenantContext, ChatContext chatContext,
            UserContext userContext, AgentSharableContext agentSharableContext, IDateTimeProvider dateTimeProvider,
            ILogger<PublishAgentEventCommandHandler> logger, ISessionWorkingStateStore workingState)
        {
            _workingState = workingState;
            _logger = logger;
            _dateTimeProvider = dateTimeProvider;
            _daprClient = daprClient;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _userContext = userContext;
            _agentSharableContext = agentSharableContext;
        }

        public async ValueTask<Result> Handle(PublishAgentEventCommand command, CancellationToken cancellationToken)
        {
            var eventMessage = new AgentEventMessage
            {
                AgentId = command.Id,
                AgentRole = command.AgentRole,
                Input = command.Input,
                SessionId = command.SessionId,
                EventType = command.EventType,
                RootIsWidget = command.RootIsWidget,
                ApprovalRootAgentId = command.ApprovalRootAgentId
            };

            var data = new WkfloQueueMessage<AgentEventMessage>
            {
                Command = eventMessage,
                InstanceId = Guid.NewGuid().ToString(),
                TenantContext = _tenantContext,
                ChatContext = _chatContext,
                UserContext = _userContext,
                AgentSharableContext = _agentSharableContext,
                // US 7501: a Stop cancels the session's hand-offs queued before it, even when they only run later. A hand-off
                // requested by a turn is that turn's (its start time, inherited down a hand-off chain), so it never looks
                // newer than a Stop that lands while it is being published. Otherwise (e.g. the HTTP endpoint): now.
                QueuedAt = command.RequestedByTurnStartedAt ?? _dateTimeProvider.DateTimeOffsetUtcNow()
            };

            if (command.Input?.Files?.FindOversizedImage() != null)
            {
                return new InvalidResult(nameof(AgentInvocationInput.Files), ErrorConst.ImageTooLarge);
            }

            if (command.Input?.Files?.FindUnsupportedImageFormat() != null)
            {
                return new InvalidResult(nameof(AgentInvocationInput.Files), ErrorConst.UnsupportedImageFormat);
            }

            if (data.ExceedsMaxSize(out _))
            {
                return new InvalidResult(nameof(AgentInvocationInput.Files), ErrorConst.AttachmentsTooLarge);
            }

            // A stopped turn queues nothing.
            cancellationToken.ThrowIfCancellationRequested();

            var key = $"{CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix}{data.InstanceId}";
            await _daprClient.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key, data, cancellationToken: cancellationToken);

            // US 7506: the session is working from now until the hand-off's turn ends (which removes this entry), including
            // while it waits in the agent's queue. Written before the publish, so even a hand-off that runs at once cannot
            // end before its entry exists. Best effort: never fails the hand-off.
            await _workingState.MarkWorkingAsync(_tenantContext.TenantName, command.SessionId, key, data.QueuedAt.Value, SessionWorkingStateConsts.HandOffTimeToLive);

            try
            {
                await _daprClient.PublishEventAsync(
                    CommonConsts.CommonAiPubSubConsts.PubSubName,
                    AiWorkerTopicConsts.AgentEventMessageTopic,
                    key,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await _workingState.ClearWorkingAsync(_tenantContext.TenantName, command.SessionId, [key]);

                // Stopped between the save and the publish: nothing will ever read this payload, so it is removed.
                _logger.LogInformation("Hand-off to agent {AgentId} in session {SessionId} cancelled by the Stop before it was queued", command.Id, command.SessionId);
                try
                {
                    await _daprClient.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key, cancellationToken: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Deleting the unpublished hand-off state {Key} failed", key);
                }

                throw;
            }
            catch (Exception)
            {
                // US 7506: the publish failed, so no turn will ever run (and clear) this hand-off: without this the session
                // would show as working until the entry's time to live ran out.
                await _workingState.ClearWorkingAsync(_tenantContext.TenantName, command.SessionId, [key]);
                throw;
            }

            return new SuccessResult<bool>(true);
        }
    }
}
