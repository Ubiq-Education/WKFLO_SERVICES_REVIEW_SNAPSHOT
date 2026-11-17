using AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessageTenantAgentById;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Models;
using Dapr;
using Dapr.Client;
using Dapr.Workflow;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Dapr
{
    [ApiController]
    public class VoiceWorkerSubscriber : ControllerBase
    {
        [Topic(CommonConsts.CommonAiPubSubConsts.PubSubName, AiWorkerTopicConsts.VoiceMessageTenantAgentByIdTopic)]
        [HttpPost($"/{AiWorkerTopicConsts.VoiceMessageTenantAgentByIdTopic}")]
        public async Task<ActionResult> VoiceMessageTenantAgentByIdCall(
            CloudEvent<string> cloudEvent,
            [FromServices] DaprWorkflowClient workflowClient,
            [FromServices] ILogger<VoiceWorkerSubscriber> logger,
            [FromServices] DaprClient daprClient,
            [FromServices] IChatTurnErrorNotifier chatTurnErrorNotifier)
        {
            var key = cloudEvent.Data;
            string instanceId = null;
            try
            {
                instanceId = key[CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix.Length..];

                await workflowClient.ScheduleNewWorkflowAsync(
                    nameof(VoiceMessageTenantAgentByIdWorkflow),
                    instanceId, // workflow instance ID
                    key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "VoiceMessageTenantAgentByIdCall: Failed to process voice message for key {Key}",
                    cloudEvent.Data);

                // US 7959 / Task 7961: no activity will run this turn - tell the chat (best effort) and drop the queued message.
                await ChatTurnScheduleFailureReporter.ReportAsync<VoiceMessageTenantAgentByIdCommand>(key, instanceId,
                    async () => await workflowClient.GetWorkflowStateAsync(instanceId) != null,
                    daprClient, chatTurnErrorNotifier, logger);

                // Swallow so Dapr considers the message processed.
                // Errors are surfaced to the client via SignalR.
            }

            return Ok();
        }

        [Topic(CommonConsts.CommonAiPubSubConsts.PubSubName, AiWorkerTopicConsts.VoiceMessagePublicAgentByShareIdTopic)]
        [HttpPost($"/{AiWorkerTopicConsts.VoiceMessagePublicAgentByShareIdTopic}")]
        public async Task<ActionResult> VoiceMessagePublicAgentByShareIdCall(
            CloudEvent<string> cloudEvent,
            [FromServices] DaprWorkflowClient workflowClient,
            [FromServices] ILogger<VoiceWorkerSubscriber> logger,
            [FromServices] DaprClient daprClient,
            [FromServices] IChatTurnErrorNotifier chatTurnErrorNotifier)
        {
            var key = cloudEvent.Data;
            string instanceId = null;
            try
            {
                instanceId = key[CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix.Length..];

                await workflowClient.ScheduleNewWorkflowAsync(
                    nameof(VoiceMessagePublicAgentByShareIdWorkflow),
                    instanceId, // workflow instance ID
                    key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "VoiceMessagePublicAgentByShareIdCall: Failed to process voice message for key {Key}",
                    cloudEvent.Data);

                // US 7959 / Task 7961: no activity will run this turn - tell the chat (best effort) and drop the queued message.
                await ChatTurnScheduleFailureReporter.ReportAsync<VoiceMessagePublicAgentByShareIdCommand>(key, instanceId,
                    async () => await workflowClient.GetWorkflowStateAsync(instanceId) != null,
                    daprClient, chatTurnErrorNotifier, logger);

                // Swallow so Dapr considers the message processed.
                // Errors are surfaced to the client via SignalR.
            }

            return Ok();
        }
    }
}
