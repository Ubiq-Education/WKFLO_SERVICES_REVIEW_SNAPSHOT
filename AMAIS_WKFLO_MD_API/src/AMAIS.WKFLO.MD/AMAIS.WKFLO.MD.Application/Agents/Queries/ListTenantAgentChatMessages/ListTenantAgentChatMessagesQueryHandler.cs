using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Mediator;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgentChatMessages
{
    public class ListTenantAgentChatMessagesQueryHandler : IRequestHandler<ListTenantAgentChatMessagesQuery, Result>
    {
        private readonly IWkfloMapper _mapper;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public ListTenantAgentChatMessagesQueryHandler(IWkfloMapper mapper, IChatStoreService chatStoreService, TenantContext tenantContext, UserContext userContext)
        {
            _mapper = mapper;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(ListTenantAgentChatMessagesQuery query, CancellationToken cancellationToken)
        {
            var (_, _, chatHistory, _) = await _chatStoreService.LoadChatAsync(_tenantContext.TenantName, query.Id, query.SessionId, _userContext.Guid);
            return new SuccessResult<List<TenantAgentChatMessageResult>>(_mapper.Map<List<TenantAgentChatMessageResult>>(chatHistory));
        }
    }
}
