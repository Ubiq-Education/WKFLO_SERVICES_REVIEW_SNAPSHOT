using AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentVoiceMessage : BaseAsyncEndpoint<string, VoiceMessagePublicAgentByShareIdCommand, Result>
    {
        private readonly DaprClient _daprClient;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly AgentSharableContext _agentSharableContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IChatSessionOwnerStore _sessionOwnerStore;

        public PublicAgentVoiceMessage(IWkfloMediator mediator, DaprClient daprClient, TenantContext tenantContext,
            ChatContext chatContext, UserContext userContext, AgentSharableContext agentSharableContext, IDateTimeProvider dateTimeProvider,
            IChatSessionOwnerStore sessionOwnerStore)
            : base(mediator)
        {
            _dateTimeProvider = dateTimeProvider;
            _sessionOwnerStore = sessionOwnerStore;
            _daprClient = daprClient;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _userContext = userContext;
            _agentSharableContext = agentSharableContext;
        }

        [WebChatAuthorization(AuthorizationMethod.AmaisJwtOrDatabase)]
        [HttpPost("/api/v1/agent/{shareId}/chat/voice/message")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Tenant API", "Agent Chat")]
        [EndpointName("PublicAgent.VoiceMessage")]
        [EndpointSummary("Send a voice message to a Tenant Agent")]
        [EndpointDescription("Publishes a voice message command. Audio responses are delivered via SignalR (ai_audio_streaming / ai_response_complete events).")]
        public override async Task<ActionResult<Result>> HandleAsync(
            [FromRoute] string shareId,
            VoiceMessagePublicAgentByShareIdCommand command,
            CancellationToken cancellationToken = default)
        {
            command.ShareId = shareId;
            var data = new WkfloQueueMessage<VoiceMessagePublicAgentByShareIdCommand>
            {
                Command = command,
                InstanceId = Guid.NewGuid().ToString(),
                TenantContext = _tenantContext,
                ChatContext = _chatContext,
                UserContext = _userContext,
                AgentSharableContext = _agentSharableContext,
                // US 7501: decides which queued messages a Stop cancels, same as the text chat endpoints.
                QueuedAt = _dateTimeProvider.DateTimeOffsetUtcNow()
            };

            // US 7501: who started the session, so only this chat and user can stop its first turn (no conversation row yet).
            await _sessionOwnerStore.RecordAsync(_tenantContext.TenantName, command.SessionId,
                new ChatSessionOwner { ShareId = shareId, UserId = _userContext?.Guid }, cancellationToken);

            var key = $"{CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix}{data.InstanceId}";
            await _daprClient.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key, data, cancellationToken: cancellationToken);
            await _daprClient.PublishEventAsync(
                CommonConsts.CommonAiPubSubConsts.PubSubName,
                AiWorkerTopicConsts.VoiceMessagePublicAgentByShareIdTopic,
                key,
                cancellationToken);

            return StatusCode(StatusCodes.Status202Accepted, new SuccessResult<bool>(true));
        }
    }
}

