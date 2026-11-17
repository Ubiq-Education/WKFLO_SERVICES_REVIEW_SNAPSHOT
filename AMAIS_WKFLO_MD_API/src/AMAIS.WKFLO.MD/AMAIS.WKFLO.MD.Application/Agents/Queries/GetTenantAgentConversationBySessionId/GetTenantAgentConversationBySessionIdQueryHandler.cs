using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using EFCoreSecondLevelCacheInterceptor;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentConversationBySessionId
{
    public class GetTenantAgentConversationBySessionIdQueryHandler : IRequestHandler<GetTenantAgentConversationBySessionIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly IChatStoreService _chatStoreService;

        public GetTenantAgentConversationBySessionIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext, IChatStoreService chatStoreService)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
            _chatStoreService = chatStoreService;
        }

        public async ValueTask<Result> Handle(GetTenantAgentConversationBySessionIdQuery query, CancellationToken cancellationToken)
        {
            var tenantId = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Name == _tenantContext.TenantName)
                .Select(t => t.Id)
                .Cacheable()
                .FirstOrDefaultAsync(cancellationToken);

            // Intentionally NOT .Cacheable(): IsResolved/LastActivityAt are written by the AI API
            // (ConversationAnalysisWorkflowActivity), a different process, so this API's in-memory
            // second-level cache is never invalidated and would serve a stale resolution status.
            var conversation = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.AgentId == query.Id
                         && c.TenantId == tenantId
                         && c.SessionId == query.SessionId
                         && !c.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (conversation == null)
                return new NotFoundResult();

            var result = _mapper.Map<TenantAgentConversationResult>(conversation);

            var (_, _, chatHistory, messageRatings) = await _chatStoreService.LoadChatAsync(_tenantContext.TenantName, query.Id, query.SessionId, conversation.UserId);
            result.Messages = _mapper.Map<List<TenantAgentChatMessageResult>>(chatHistory);

            if (messageRatings?.Count > 0)
            {
                foreach (var message in result.Messages.Where(m => m.MessageId != null))
                {
                    if (messageRatings.TryGetValue(message.MessageId!, out var rating))
                        message.Rating = rating;
                }
            }

            return new SuccessResult<TenantAgentConversationResult>(result);
        }
    }
}
