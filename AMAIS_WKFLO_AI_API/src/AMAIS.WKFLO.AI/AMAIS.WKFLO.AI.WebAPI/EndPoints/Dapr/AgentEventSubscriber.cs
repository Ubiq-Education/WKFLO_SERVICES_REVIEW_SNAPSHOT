using AMAIS.WKFLO.AI.Application.Agents.Actors;
using AMAIS.WKFLO.AI.Application.Agents.ChatErrors;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using Dapr;
using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Dapr
{
    /// <summary>
    /// Subscriber for cross-agent async notifications (story #6480 / task #6485). Hands the event
    /// off to an <see cref="IAgentActor"/> gatekeeper and returns immediately — the actor queues the
    /// event and serializes execution of <c>AgentEventWorkflow</c> per session/agent (task #6589),
    /// so two events for the same session/agent never run concurrently.
    ///
    /// Receives only a state-store correlation key on the CloudEvent body — not the payload
    /// itself — because the message broker backing <c>pubsub-ai</c> has a payload size limit. The full
    /// <c>AgentEventMessage</c> lives in <c>statestore-ai</c> under that key (written by
    /// <c>WebAPI/EndPoints/Agent/AgentEventPublish.cs</c>). This handler reads that state up front —
    /// unlike before, when only the downstream workflow activity did — purely to learn the
    /// <c>SessionId</c>/<c>AgentId</c> needed to address the right actor instance.
    /// </summary>
    [ApiController]
    public class AgentEventSubscriber : ControllerBase
    {
        [Topic(CommonConsts.CommonAiPubSubConsts.PubSubName, AiWorkerTopicConsts.AgentEventMessageTopic)]
        [HttpPost($"/{AiWorkerTopicConsts.AgentEventMessageTopic}")]
        public async Task<ActionResult> AgentEventCall(
            CloudEvent<string> cloudEvent,
            [FromServices] DaprClient daprClient,
            [FromServices] IActorProxyFactory actorProxyFactory,
            [FromServices] ILogger<AgentEventSubscriber> logger,
            [FromServices] IChatAgentFailureReporter failureReporter,
            [FromServices] IChatNotificationService notificationService)
        {
            var key = cloudEvent.Data;
            WkfloQueueMessage<AgentEventMessage> queueMessage = null;

            try
            {
                queueMessage = await daprClient.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, key);

                var eventMessage = queueMessage?.Command;
                if (eventMessage == null)
                {
                    logger.LogWarning("AgentEventCall: no queued event found for key {Key}", key);
                    return Ok();
                }

                var actorId = new ActorId($"{eventMessage.SessionId}:{eventMessage.AgentId}");
                var actorProxy = actorProxyFactory.CreateActorProxy<IAgentActor>(actorId, nameof(AgentActor));

                await actorProxy.EnqueueEventAsync(key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "AgentEventCall: failed to enqueue event for key {Key}",
                    key);

                // US 7959 / Task 7963: this hand-off will not run - its agent fails (best effort, only when the queued event
                // could be read). The chat's own agent already answered, so it is not a turn-level error.
                await ReportHandOffNotQueuedAsync(key, queueMessage, failureReporter, notificationService, logger);

                // IMPORTANT: swallow the exception
                // so Dapr considers the message processed
            }

            return Ok();
        }

        /// <summary>
        /// US 7959 / Task 7963: the hand-off's agent fails with <see cref="ChatErrorCodes.ChatInfrastructureError"/>: its
        /// "started" card turns "failed" with that code, its bubble stops and a non-terminal marker is sent. Never throws.
        /// </summary>
        internal static async Task ReportHandOffNotQueuedAsync(string key, WkfloQueueMessage<AgentEventMessage> queueMessage,
            IChatAgentFailureReporter failureReporter, IChatNotificationService notificationService, ILogger logger)
        {
            var eventMessage = queueMessage?.Command;
            if (eventMessage == null || eventMessage.AgentId <= 0 || string.IsNullOrWhiteSpace(eventMessage.SessionId))
                return;

            try
            {
                var turnId = key?.StartsWith(CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix, StringComparison.Ordinal) == true
                    ? key[CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix.Length..]
                    : key;

                var notified = await failureReporter.ReportAsync(new ChatAgentFailure
                {
                    TenantName = queueMessage.TenantContext?.TenantName,
                    SessionId = eventMessage.SessionId,
                    TurnId = turnId,
                    TurnStartedAt = queueMessage.QueuedAt,
                    ErrorCode = ChatErrorCodes.ChatInfrastructureError,
                    AgentId = eventMessage.AgentId,
                    AgentName = eventMessage.AgentRole,
                    IsTerminal = false
                });

                if (notified)
                {
                    await notificationService.SendSubagentActivityFailedUpdate(eventMessage.SessionId, eventMessage.AgentId, eventMessage.AgentRole,
                        ChatErrorCodes.ChatInfrastructureError, action: eventMessage.EventType);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AgentEventCall: telling the chat that the hand-off {Key} could not be queued failed", key);
            }
        }
    }
}
