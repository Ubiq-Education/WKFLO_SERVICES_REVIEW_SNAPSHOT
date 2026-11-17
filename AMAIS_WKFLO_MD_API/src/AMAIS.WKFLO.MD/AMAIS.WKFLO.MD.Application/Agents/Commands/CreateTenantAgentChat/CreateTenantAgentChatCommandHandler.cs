using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.SyncConversation;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Mediator;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgentChat
{
    public class CreateTenantAgentChatCommandHandler : IRequestHandler<CreateTenantAgentChatCommand, Result>
    {
        private readonly IWkfloMapper _mapper;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly IWkfloMediator _mediator;
        private readonly IDateTimeProvider _dateTimeProvider;

        public CreateTenantAgentChatCommandHandler(IWkfloMapper mapper, IChatStoreService chatStoreService, TenantContext tenantContext, ChatContext chatContext, UserContext userContext, IWkfloMediator mediator, IDateTimeProvider dateTimeProvider)
        {
            _mapper = mapper;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _userContext = userContext;
            _mediator = mediator;
            _dateTimeProvider = dateTimeProvider;
        }

        public async ValueTask<Result> Handle(CreateTenantAgentChatCommand command, CancellationToken cancellationToken)
        {
            var (thumbsUp, thumbsDown) = await _chatStoreService.SaveChatAsync(_tenantContext.TenantName, command.Id, command.SessionId, null, [], _dateTimeProvider.DateTimeOffsetUtcNow(), _userContext.Guid);

            await _mediator.Send(new SyncConversationCommand
            {
                AgentId = command.Id,
                TenantName = _tenantContext.TenantName,
                SessionId = command.SessionId,
                ConversationDate = _dateTimeProvider.DateTimeOffsetUtcNow(),
                UserId = _userContext.Guid,
                SubjectId = _chatContext.SubjectId,
                TotalThumbsUp = thumbsUp,
                TotalThumbsDown = thumbsDown,
                IsTestChat = _chatContext.IsTestChat
            }, cancellationToken);

            return new SuccessResult<TenantAgentChatCreateResult>(_mapper.Map<TenantAgentChatCreateResult>(command));
        }
    }
}
