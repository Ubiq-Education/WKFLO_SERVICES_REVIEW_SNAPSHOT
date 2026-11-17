using AMAIS.WKFLO.AI.Application.Common.Activities;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Mediator;
using Dapr.Client;
using Dapr.Workflow;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessagePublicAgentByShareId
{
    public class VoiceMessagePublicAgentByShareIdWorkflowActivity : BaseWorkflowActivity<string, bool>
    {
        public VoiceMessagePublicAgentByShareIdWorkflowActivity(ILogger<VoiceMessagePublicAgentByShareIdWorkflowActivity> logger, IWkfloMediator mediator, IWkfloMapper mapper, IDateTimeProvider dateTimeProvider,
            TenantContext tenantContext, ChatContext chatContext, UserContext userContext, AgentSharableContext agentSharableContext, DaprClient daprClient,
            IChatTurnRegistry turnRegistry, IChatStopCheckpoint stopCheckpoint, IChatNotificationService notificationService,
            IChatTurnErrorNotifier chatTurnErrorNotifier = null, IChatTurnFailureState chatTurnFailureState = null, IChatTurnErrorReporter chatTurnErrorReporter = null)
            : base(logger, mediator, mapper, dateTimeProvider, tenantContext, chatContext, userContext, agentSharableContext, daprClient, turnRegistry, stopCheckpoint, notificationService,
                  chatTurnErrorNotifier, chatTurnFailureState, chatTurnErrorReporter)
        {
        }

        public async override Task<bool> RunAsync(WorkflowActivityContext context, string messageKey)
        {
            try
            {
                return await BaseRunAsync<VoiceMessagePublicAgentByShareIdCommand>(context, messageKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Activity failed");

                return false; // or throw if you want Dapr to retry
            }
        }
    }
}
