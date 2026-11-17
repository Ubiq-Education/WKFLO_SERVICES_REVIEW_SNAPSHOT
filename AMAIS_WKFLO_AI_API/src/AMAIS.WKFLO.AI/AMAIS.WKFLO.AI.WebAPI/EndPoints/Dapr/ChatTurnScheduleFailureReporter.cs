using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using Dapr.Client;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Dapr
{
    /// <summary>
    /// US 7959 / Task 7961: a chat message reached the worker but its workflow could not be scheduled, so no activity
    /// will ever run it. Best effort: tells the chat (<see cref="ChatErrorCodes.ChatInfrastructureError"/>) when the queued
    /// message can be read, and deletes that queued message, which would otherwise stay in the state store for good.
    /// Nothing when the workflow exists after all (a redelivery of a message that was already scheduled). Never throws.
    /// </summary>
    public static class ChatTurnScheduleFailureReporter
    {
        public static async Task ReportAsync<TCommand>(string key, string instanceId, Func<Task<bool>> workflowExists,
            DaprClient daprClient, IChatTurnErrorNotifier notifier, ILogger logger)
            where TCommand : IWkfloQueueMessageCommand
        {
            // A malformed key: there is no queued message to read or to delete.
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(instanceId))
                return;

            try
            {
                if (await workflowExists())
                {
                    logger.LogInformation("Workflow {InstanceId} already exists; its queued message {Key} is kept", instanceId, key);
                    return;
                }
            }
            catch (Exception ex)
            {
                // Cannot tell: the queued message is kept (the workflow may still need it), the chat is still told.
                logger.LogWarning(ex, "Checking workflow {InstanceId} after a failed schedule failed", instanceId);
                await NotifyAsync<TCommand>(key, instanceId, daprClient, notifier, logger);
                return;
            }

            await NotifyAsync<TCommand>(key, instanceId, daprClient, notifier, logger);

            try
            {
                await daprClient.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Deleting the queued message {Key} of an unscheduled workflow failed", key);
            }
        }

        /// <summary>
        /// US 7959 / Task 7963: same, for a subscriber that already read its queued message and deletes it itself (the
        /// multi-agent orchestrator). Tells the chat (terminal <see cref="ChatErrorCodes.ChatInfrastructureError"/>) unless
        /// the workflow exists after all. Never throws.
        /// </summary>
        public static async Task ReportAsync(string instanceId, string tenantName, string sessionId, DateTimeOffset? queuedAt,
            Func<Task<bool>> workflowExists, IChatTurnErrorNotifier notifier, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(sessionId))
                return;

            try
            {
                if (await workflowExists())
                {
                    logger.LogInformation("Workflow {InstanceId} already exists; the chat is not told of a failed schedule", instanceId);
                    return;
                }
            }
            catch (Exception ex)
            {
                // Cannot tell: the chat is still told.
                logger.LogWarning(ex, "Checking workflow {InstanceId} after a failed schedule failed", instanceId);
            }

            await NotifyAsync(instanceId, tenantName, sessionId, queuedAt, notifier, logger);
        }

        private static async Task NotifyAsync(string instanceId, string tenantName, string sessionId, DateTimeOffset? queuedAt,
            IChatTurnErrorNotifier notifier, ILogger logger)
        {
            try
            {
                await notifier.NotifyAsync(new ChatTurnErrorNotification
                {
                    TenantName = tenantName,
                    SessionId = sessionId,
                    TurnId = instanceId,
                    TurnStartedAt = queuedAt,
                    Failure = ChatTurnFailure.Create(ChatErrorCodes.ChatInfrastructureError)
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telling the chat that workflow {InstanceId} could not be scheduled failed", instanceId);
            }
        }

        private static async Task NotifyAsync<TCommand>(string key, string instanceId, DaprClient daprClient, IChatTurnErrorNotifier notifier, ILogger logger)
            where TCommand : IWkfloQueueMessageCommand
        {
            try
            {
                var message = await daprClient.GetStateAsync<WkfloQueueMessage<TCommand>>(CommonConsts.CommonAiPubSubConsts.StateStoreName, key);
                var sessionId = message?.Command?.GetId();
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    logger.LogWarning("The queued message {Key} of an unscheduled workflow has no readable session; the chat is not told", key);
                    return;
                }

                await NotifyAsync(instanceId, message.TenantContext?.TenantName, sessionId, message.QueuedAt, notifier, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telling the chat that workflow {InstanceId} could not be scheduled failed", instanceId);
            }
        }
    }
}
