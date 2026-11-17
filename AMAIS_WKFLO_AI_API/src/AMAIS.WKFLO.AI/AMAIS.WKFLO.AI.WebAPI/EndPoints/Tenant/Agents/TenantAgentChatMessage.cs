using AMAIS.WKFLO.AI.Application.Agents.Commands.MessageTenantAgentById;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentChatMessage : BaseAsyncEndpoint<int, MessageTenantAgentByIdCommand, Result>
    {
        private readonly DaprClient _daprClient;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly AgentSharableContext _agentSharableContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IChatSessionOwnerStore _sessionOwnerStore;

        public TenantAgentChatMessage(IWkfloMediator mediator, DaprClient daprClient, TenantContext tenantContext, ChatContext chatContext
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

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{id}/chat/message")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Tenant API", "Agent Chat")]
        [EndpointName("TenantAgent.MessageChat")]
        [EndpointSummary("Message a Tenant Agent by Id on a chat")]
        [EndpointDescription("Execute a tenant agent by Id.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, MessageTenantAgentByIdCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            command.Input?.Files.ClearServerOnlyFields();
            var data = new WkfloQueueMessage<MessageTenantAgentByIdCommand>()
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
                return AmaisWkfloResult(new InvalidResult(nameof(MessageTenantAgentInputCommand.Files), ErrorConst.ImageTooLarge));
            }

            if (command.Input?.Files?.FindUnsupportedImageFormat() != null)
            {
                return AmaisWkfloResult(new InvalidResult(nameof(MessageTenantAgentInputCommand.Files), ErrorConst.UnsupportedImageFormat));
            }

            if (data.ExceedsMaxSize(out _))
            {
                return AmaisWkfloResult(new InvalidResult(nameof(MessageTenantAgentInputCommand.Files), ErrorConst.AttachmentsTooLarge));
            }

            // US 7501: who started the session, so only this chat and user can stop its first turn (no conversation row yet).
            await _sessionOwnerStore.RecordAsync(_tenantContext.TenantName, command.SessionId,
                new ChatSessionOwner { AgentId = id, UserId = _userContext?.Guid }, cancellationToken);

            var key = $"{CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix}{data.InstanceId}";
            await this._daprClient.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key, data, cancellationToken: cancellationToken);
            await this._daprClient.PublishEventAsync(CommonConsts.CommonAiPubSubConsts.PubSubName, AiWorkerTopicConsts.MessageTenantAgentByIdTopic, key, cancellationToken);
            return AmaisWkfloResult(new SuccessResult<bool>(true));
        }
    }
}
