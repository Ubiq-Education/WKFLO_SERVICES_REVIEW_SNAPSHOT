using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentToolsById
{
    public class GetTenantAgentToolsByIdQueryHandler : IRequestHandler<GetTenantAgentToolsByIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public GetTenantAgentToolsByIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetTenantAgentToolsByIdQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.TenantTools.AsNoTracking()
                .Where(m =>
                    m.Tenant.Name == _tenantContext.TenantName &&
                    m.AgentTools.Any(m2 => m2.AgentId == query.Id && m2.Agent.Tenant.Name == _tenantContext.TenantName))
                .ToListAsync(cancellationToken) ?? new List<TenantTool>();

            return new SuccessResult<List<TenantAgentToolGetResult>>(_mapper.Map<List<TenantAgentToolGetResult>>(result));
        }
    }
}
