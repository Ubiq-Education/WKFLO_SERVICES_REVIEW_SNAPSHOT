using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Mediator;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgentChats
{
    public class ListTenantAgentChatsQueryHandler : IRequestHandler<ListTenantAgentChatsQuery, Result>
    {
        private readonly IWkfloMapper _mapper;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public ListTenantAgentChatsQueryHandler(IWkfloMapper mapper, IChatStoreService chatStoreService, TenantContext tenantContext, UserContext userContext)
        {
            _mapper = mapper;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(ListTenantAgentChatsQuery query, CancellationToken cancellationToken)
        {
            var chats = await _chatStoreService.ListChatsAsync(_tenantContext.TenantName, query.Id, _userContext.Guid);
            return new SuccessResult<List<TenantAgentChatListResult>>(_mapper.Map<List<TenantAgentChatListResult>>(chats));
        }
    }
}
