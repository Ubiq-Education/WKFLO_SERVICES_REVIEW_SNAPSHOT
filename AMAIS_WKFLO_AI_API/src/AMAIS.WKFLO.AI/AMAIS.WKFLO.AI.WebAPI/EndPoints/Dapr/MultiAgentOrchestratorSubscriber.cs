using AMAIS.WKFLO.AI.Application.Agents.Commands.TriggerMultiAgentOrchestrator;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Workflows.MultiAgentOrchestrator;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using Dapr;
using Dapr.Client;
using Dapr.Workflow;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Dapr
{
    /// <summary>
    /// Starts a <see cref="MultiAgentOrchestratorWorkflow"/> run (story #6480). Unlike
    /// <c>AIWorkerSubscriber</c>'s methods (which thread the raw key into the workflow via
    /// <c>BaseWorkflowActivity</c>), this one loads the full queued
    /// <see cref="TriggerMultiAgentOrchestratorCommand"/> itself — because
    /// <see cref="MultiAgentOrchestratorWorkflow"/> doesn't use <c>BaseWorkflowActivity</c> — and
    /// passes the fully-resolved <c>MultiAgentOrchestratorWorkflowInput</c> straight into
    /// <c>ScheduleNewWorkflowAsync</c>. That hop goes through Dapr's own workflow-state persistence,
    /// not the size-limited pub/sub broker, so there's no indirection needed beyond this point. The
    /// queued state entry is deleted here since nothing else will clean it up.
    /// </summary>
    [ApiController]
    public class MultiAgentOrchestratorSubscriber : ControllerBase
    {
        [Topic(CommonConsts.CommonAiPubSubConsts.PubSubName, AiWorkerTopicConsts.TriggerMultiAgentOrchestratorTopic)]
        [HttpPost($"/{AiWorkerTopicConsts.TriggerMultiAgentOrchestratorTopic}")]
        public async Task<ActionResult<bool>> TriggerMultiAgentOrchestratorCall(CloudEvent<string> cloudEvent,
            [FromServices] DaprClient daprClient,
            [FromServices] DaprWorkflowClient workflowClient,
            [FromServices] ILogger<MultiAgentOrchestratorSubscriber> logger,
            [FromServices] IChatTurnErrorNotifier chatTurnErrorNotifier)
        {
            var key = cloudEvent.Data;
            WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand> queueMessage = null;
            string instanceId = null;

            try
            {
                queueMessage = await daprClient.GetStateAsync<WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, key);

                if (queueMessage?.Command == null)
                {
                    logger.LogWarning("TriggerMultiAgentOrchestratorCall: no queued request found for key {Key}", key);
                    return Ok();
                }

                instanceId = key[CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix.Length..];

                var workflowInput = new MultiAgentOrchestratorWorkflowInput
                {
                    Input = queueMessage.Command.Input,
                    SessionId = queueMessage.Command.SessionId,
                    LeadAgent = queueMessage.Command.LeadAgent,
                    FanOutAgents = queueMessage.Command.FanOutAgents,
                    ChainOfThought = queueMessage.Command.ChainOfThought,
                    TenantContext = queueMessage.TenantContext,
                    ChatContext = queueMessage.ChatContext,
                    UserContext = queueMessage.UserContext,
                    AgentSharableContext = queueMessage.AgentSharableContext,
                    RootIsWidget = queueMessage.Command.RootIsWidget
                };

                await workflowClient.ScheduleNewWorkflowAsync(
                    nameof(MultiAgentOrchestratorWorkflow),
                    instanceId, // workflow instance ID
                    workflowInput);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "TriggerMultiAgentOrchestratorCall: Failed to schedule workflow for key {Key}",
                    key);

                // US 7959 / Task 7963: no agent will run this turn - tell the chat (best effort, only when the queued request
                // could be read; the finally below drops it).
                if (queueMessage?.Command != null)
                {
                    var scheduledInstanceId = instanceId;
                    await ChatTurnScheduleFailureReporter.ReportAsync(instanceId, queueMessage.TenantContext?.TenantName, queueMessage.Command.SessionId,
                        queueMessage.QueuedAt, async () => await workflowClient.GetWorkflowStateAsync(scheduledInstanceId) != null,
                        chatTurnErrorNotifier, logger);
                }

                // IMPORTANT: swallow the exception
                // so Dapr considers the message processed
            }
            finally
            {
                try
                {
                    await daprClient.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "TriggerMultiAgentOrchestratorCall: failed to delete queued request state for key {Key}", key);
                }
            }

            return Ok();
        }
    }
}
