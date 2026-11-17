using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.MessageTopicClassifications.Models;
using EFCoreSecondLevelCacheInterceptor;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.MessageTopicClassifications.Queries.GetAgentTopicClassificationSummary
{
    public class GetAgentTopicClassificationSummaryQueryHandler : IRequestHandler<GetAgentTopicClassificationSummaryQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public GetAgentTopicClassificationSummaryQueryHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetAgentTopicClassificationSummaryQuery query, CancellationToken cancellationToken)
        {
            var agentBelongsToTenant = await _db.Agents
                .AsNoTracking()
                .Where(a => a.Id == query.AgentId && a.Tenant.Name == _tenantContext.TenantName)
                .Cacheable()
                .AnyAsync(cancellationToken);

            if (!agentBelongsToTenant)
                return new NotFoundResult();

            var startDate = GetStartDate(query.DateRange);

            var result = await _db.MessageTopicClassifications
                .AsNoTracking()
                .Where(m => m.AgentId == query.AgentId && m.MessageDate >= startDate && !m.IsDeleted)
                .GroupBy(m => m.TopicName)
                .Select(g => new AgentTopicClassificationSummaryResult
                {
                    TopicName = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(r => r.Count)
                .ToListAsync(cancellationToken);

            return new SuccessResult<List<AgentTopicClassificationSummaryResult>>(result);
        }

        private static DateTimeOffset GetStartDate(DateRangeOption dateRange)
        {
            var now = DateTimeOffset.UtcNow;
            return dateRange switch
            {
                DateRangeOption.Last30Days => now.AddDays(-30),
                DateRangeOption.Last90Days => now.AddDays(-90),
                DateRangeOption.Last3Months => now.AddMonths(-3),
                DateRangeOption.Last6Months => now.AddMonths(-6),
                DateRangeOption.Last12Months => now.AddMonths(-12),
                _ => now.AddDays(-30)
            };
        }
    }
}
