using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.ListPublicAgentChatMessages
{
    public class ListPublicAgentChatMessagesQueryHandler : IRequestHandler<ListPublicAgentChatMessagesQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public ListPublicAgentChatMessagesQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, IChatStoreService chatStoreService, TenantContext tenantContext, UserContext userContext)
        {
            _db = db;
            _mapper = mapper;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _userContext = userContext;

        }

        public async ValueTask<Result> Handle(ListPublicAgentChatMessagesQuery query, CancellationToken cancellationToken)
        {
            // Check AgentChatSetting
            var chatSetting = await _db.AgentChatSettings.AsNoTracking()
                .Include(acs => acs.Agent)
                .Where(acs => acs.ShareId == query.ShareId && !acs.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (chatSetting == null || chatSetting.Agent.IsDeleted || (chatSetting.Agent is RunnableAgent runnableAgent && !runnableAgent.IsActive))
            {
                return new SuccessResult<List<PublicAgentChatMessageResult>>(new List<PublicAgentChatMessageResult>());
            }

            var (creationAt, name, chatHistory, messageRatings) = await _chatStoreService.LoadChatAsync(_tenantContext.TenantName, chatSetting.Agent.Id, query.SessionId, _userContext.Guid);
            var messages = _mapper.Map<List<PublicAgentChatMessageResult>>(chatHistory);
            ApplyRatings(messages, messageRatings);

            foreach (var message in messages)
            {
                message.AgentId = chatSetting.Agent.Id;
                message.AgentName = chatSetting.Agent.Name;
            }

            return new SuccessResult<List<PublicAgentChatMessageResult>>(messages);
        }

        private static void ApplyRatings(List<PublicAgentChatMessageResult> messages, Dictionary<string, string> messageRatings)
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
