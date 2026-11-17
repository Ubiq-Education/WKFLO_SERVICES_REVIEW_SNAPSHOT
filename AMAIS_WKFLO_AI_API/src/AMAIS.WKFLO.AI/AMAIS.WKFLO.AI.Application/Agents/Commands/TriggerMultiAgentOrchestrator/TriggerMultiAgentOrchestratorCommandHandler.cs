using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr.Client;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.TriggerMultiAgentOrchestrator
{
    /// <summary>
    /// Wraps the request in a <see cref="WkfloQueueMessage{T}"/>, saves it into <c>statestore-ai</c>,
    /// and publishes only the short correlation key onto
    /// <see cref="AiWorkerTopicConsts.TriggerMultiAgentOrchestratorTopic"/> — mirroring
    /// <c>TenantAgentChatMessage</c>/<c>PublicAgentChatMessage</c>'s strategy for the same payload-size
    /// reason. <c>MultiAgentOrchestratorSubscriber</c> loads the queued message, builds a
    /// <c>MultiAgentOrchestratorWorkflowInput</c> from it (including the ambient contexts populated
    /// here), and starts the workflow directly — no <c>BaseWorkflowActivity</c> involved, since
    /// <c>MultiAgentOrchestratorWorkflow</c> doesn't use that pattern.
    /// </summary>
    public class TriggerMultiAgentOrchestratorCommandHandler : IRequestHandler<TriggerMultiAgentOrchestratorCommand, Result>
    {
        private readonly DaprClient _daprClient;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly AgentSharableContext _agentSharableContext;
        private readonly IMasterDataDbContext _db;

        public TriggerMultiAgentOrchestratorCommandHandler(DaprClient daprClient, TenantContext tenantContext, ChatContext chatContext,
            UserContext userContext, AgentSharableContext agentSharableContext, IMasterDataDbContext db)
        {
            _daprClient = daprClient;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _userContext = userContext;
            _agentSharableContext = agentSharableContext;
            _db = db;
        }

        public async ValueTask<Result> Handle(TriggerMultiAgentOrchestratorCommand command, CancellationToken cancellationToken)
        {
            command.Input?.Files.ClearServerOnlyFields();

            // The lead agent IS the root of this orchestration run — resolve its own widget/non-widget
            // context once here, at trigger time, so it can be forwarded onto every fan-out agent's
            // AgentInvocationRequest (see MultiAgentOrchestratorWorkflow), rather than each fan-out
            // agent independently guessing its own context in its own fresh DI scope.
            command.RootIsWidget = await AgentChatIconHelper.GetIsWidgetContextAsync(_db, command.LeadAgent.AgentId, cancellationToken);

            var data = new WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>
            {
                Command = command,
                InstanceId = Guid.NewGuid().ToString(),
                TenantContext = _tenantContext,
                ChatContext = _chatContext,
                UserContext = _userContext,
                AgentSharableContext = _agentSharableContext
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

            var key = $"{CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix}{data.InstanceId}";
            await _daprClient.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key, data, cancellationToken: cancellationToken);
            await _daprClient.PublishEventAsync(
                CommonConsts.CommonAiPubSubConsts.PubSubName,
                AiWorkerTopicConsts.TriggerMultiAgentOrchestratorTopic,
                key,
                cancellationToken);

            return new SuccessResult<bool>(true);
        }
    }
}
