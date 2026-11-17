using AMAIS.WKFLO.AI.Application.Agents.Commands.MessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
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
    public class PublicAgentChatMessage : BaseAsyncEndpoint<string, MessagePublicAgentByShareIdCommand, Result>
    {
        private readonly DaprClient _daprClient;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly AgentSharableContext _agentSharableContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IChatSessionOwnerStore _sessionOwnerStore;

        public PublicAgentChatMessage(IWkfloMediator mediator, DaprClient daprClient, TenantContext tenantContext, ChatContext chatContext
            , UserContext userContext, AgentSharableContext agentSharableContext, IDateTimeProvider dateTimeProvider,
            IChatSessionOwnerStore sessionOwnerStore) : base(mediator)
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
        [HttpPost("/api/v1/agent/{shareId}/chat/message")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.MessageChat")]
        [EndpointSummary("Message a Public Agent by Id on a chat")]
        [EndpointDescription("Execute a public agent by ID. Authorization requirements are determined by database configuration.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, MessagePublicAgentByShareIdCommand command, CancellationToken cancellationToken = default)
        {
            command.ShareId = shareId;
            command.Input?.Files.ClearServerOnlyFields();
            var data = new WkfloQueueMessage<MessagePublicAgentByShareIdCommand>()
            {
                Command = command,
                InstanceId = Guid.NewGuid().ToString(),
                TenantContext = _tenantContext,
                ChatContext = _chatContext,
                UserContext = _userContext,
                AgentSharableContext = _agentSharableContext,
                QueuedAt = _dateTimeProvider.DateTimeOffsetUtcNow()
            };

            if (command.Input?.Files?.FindOversizedImage() != null)
            {
                return AmaisWkfloResult(new InvalidResult(nameof(MessagePublicAgentInputCommand.Files), ErrorConst.ImageTooLarge));
            }

            if (command.Input?.Files?.FindUnsupportedImageFormat() != null)
            {
                return AmaisWkfloResult(new InvalidResult(nameof(MessagePublicAgentInputCommand.Files), ErrorConst.UnsupportedImageFormat));
            }

            if (data.ExceedsMaxSize(out _))
            {
                return AmaisWkfloResult(new InvalidResult(nameof(MessagePublicAgentInputCommand.Files), ErrorConst.AttachmentsTooLarge));
            }

            // US 7501: who started the session, so only this chat and user can stop its first turn (no conversation row yet).
            await _sessionOwnerStore.RecordAsync(_tenantContext.TenantName, command.SessionId,
                new ChatSessionOwner { ShareId = shareId, UserId = _userContext?.Guid }, cancellationToken);

            var key = $"{CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix}{data.InstanceId}";
            await this._daprClient.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key, data, cancellationToken: cancellationToken);
            await this._daprClient.PublishEventAsync(CommonConsts.CommonAiPubSubConsts.PubSubName, AiWorkerTopicConsts.MessagePublicAgentByShareIdTopic, key, cancellationToken);
            return AmaisWkfloResult(new SuccessResult<bool>(true));
        }
    }
}
