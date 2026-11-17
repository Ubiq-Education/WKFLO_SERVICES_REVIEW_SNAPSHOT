using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.ListTenantAgentChatMessages
{
    public class ListTenantAgentChatMessagesQueryHandler : IRequestHandler<ListTenantAgentChatMessagesQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public ListTenantAgentChatMessagesQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, IChatStoreService chatStoreService, TenantContext tenantContext, UserContext userContext)
        {
            _db = db;
            _mapper = mapper;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(ListTenantAgentChatMessagesQuery query, CancellationToken cancellationToken)
        {
            var (_, _, chatHistory, messageRatings) = await _chatStoreService.LoadChatAsync(_tenantContext.TenantName, query.Id, query.SessionId, _userContext.Guid);
            var messages = _mapper.Map<List<TenantAgentChatMessageResult>>(chatHistory);
            ApplyRatings(messages, messageRatings);

            var agentName = await _db.Agents.AsNoTracking()
                .Where(a => a.Id == query.Id)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken);

            foreach (var message in messages)
            {
                message.AgentId = query.Id;
                message.AgentName = agentName;
            }

            return new SuccessResult<List<TenantAgentChatMessageResult>>(messages);
        }

        private static void ApplyRatings(List<TenantAgentChatMessageResult> messages, Dictionary<string, string> messageRatings)
        {
            if (messageRatings?.Count is null or 0) return;

            foreach (var message in messages.Where(m => m.MessageId != null))
            {
                if (messageRatings.TryGetValue(message.MessageId!, out var rating))
                    message.Rating = rating;
            }
        }
    }
}
