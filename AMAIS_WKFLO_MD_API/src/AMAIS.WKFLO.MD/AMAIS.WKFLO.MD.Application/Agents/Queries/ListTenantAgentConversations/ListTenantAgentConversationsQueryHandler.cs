using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using EFCoreSecondLevelCacheInterceptor;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgentConversations
{
    public class ListTenantAgentConversationsQueryHandler : IRequestHandler<ListTenantAgentConversationsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public ListTenantAgentConversationsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(ListTenantAgentConversationsQuery query, CancellationToken cancellationToken)
        {
            var tenantId = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Name == _tenantContext.TenantName)
                .Select(t => t.Id)
                .Cacheable()
                .FirstOrDefaultAsync(cancellationToken);

            var baseQuery = _db.Conversations
                .AsNoTracking()
                .Where(c => c.AgentId == query.Id
                         && c.TenantId == tenantId
                         && !c.IsDeleted
                         && (!query.HideTestChats || !c.IsTestChat))
                .OrderByDescending(c => c.ConversationDate ?? (DateTimeOffset?)c.CreatedOn);

            var totalCount = await baseQuery.CountAsync(cancellationToken);

            var items = await baseQuery
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);

            var result = new TenantAgentConversationsPageResult
            {
                Items = _mapper.Map<List<TenantAgentChatListResult>>(items),
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / query.PageSize)
            };

            return new SuccessResult<TenantAgentConversationsPageResult>(result);
        }
    }
}
