using AMAIS.WKFLO.AI.Application.Agents.Commands.MessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Commands.MessageTenantAgentById;
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
    public class AIWorkerSubscriber : ControllerBase
    {

        [Topic(CommonConsts.CommonAiPubSubConsts.PubSubName, AiWorkerTopicConsts.MessageTenantAgentByIdTopic)]
        [HttpPost($"/{AiWorkerTopicConsts.MessageTenantAgentByIdTopic}")]
        public async Task<ActionResult<bool>> MessageTenantAgentByIdCall(CloudEvent<string> cloudEvent,
            [FromServices] DaprWorkflowClient workflowClient,
            [FromServices] ILogger<AIWorkerSubscriber> logger,
            [FromServices] DaprClient daprClient,
            [FromServices] IChatTurnErrorNotifier chatTurnErrorNotifier)
        {
            var key = cloudEvent.Data;
            string instanceId = null;
            try
            {
                instanceId = key[CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix.Length..];

                await workflowClient.ScheduleNewWorkflowAsync(
                    nameof(MessageTenantAgentByIdWorkflow),
                    instanceId, // workflow instance ID
                    key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "MessageTenantAgentByIdCall: Failed to schedule workflow for key {Key}",
                    cloudEvent.Data);

                // US 7959 / Task 7961: no activity will run this turn - tell the chat (best effort) and drop the queued message.
                await ChatTurnScheduleFailureReporter.ReportAsync<MessageTenantAgentByIdCommand>(key, instanceId,
                    async () => await workflowClient.GetWorkflowStateAsync(instanceId) != null,
                    daprClient, chatTurnErrorNotifier, logger);

                // IMPORTANT: swallow the exception
                // so Dapr considers the message processed
            }

            return Ok();
        }

        [Topic(CommonConsts.CommonAiPubSubConsts.PubSubName, AiWorkerTopicConsts.MessagePublicAgentByShareIdTopic)]
        [HttpPost($"/{AiWorkerTopicConsts.MessagePublicAgentByShareIdTopic}")]
        public async Task<ActionResult<bool>> MessagePublicAgentByShareIdCall(CloudEvent<string> cloudEvent,
            [FromServices] DaprWorkflowClient workflowClient,
            [FromServices] ILogger<AIWorkerSubscriber> logger,
            [FromServices] DaprClient daprClient,
            [FromServices] IChatTurnErrorNotifier chatTurnErrorNotifier)
        {
            var key = cloudEvent.Data;
            string instanceId = null;
            try
            {
                instanceId = key[CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix.Length..];

                await workflowClient.ScheduleNewWorkflowAsync(
                    nameof(MessagePublicAgentByShareIdWorkflow),
                    instanceId, // workflow instance ID
                    key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "MessagePublicAgentByShareIdCall: Failed to schedule workflow for key {Key}",
                    cloudEvent.Data);

                // US 7959 / Task 7961: no activity will run this turn - tell the chat (best effort) and drop the queued message.
                await ChatTurnScheduleFailureReporter.ReportAsync<MessagePublicAgentByShareIdCommand>(key, instanceId,
                    async () => await workflowClient.GetWorkflowStateAsync(instanceId) != null,
                    daprClient, chatTurnErrorNotifier, logger);

                // IMPORTANT: swallow the exception
                // so Dapr considers the message processed
            }

            return Ok();
        }
    }
}
