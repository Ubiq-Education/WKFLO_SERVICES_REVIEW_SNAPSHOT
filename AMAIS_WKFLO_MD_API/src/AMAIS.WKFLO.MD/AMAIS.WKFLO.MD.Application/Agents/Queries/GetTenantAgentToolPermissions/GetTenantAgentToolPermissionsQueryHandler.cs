using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Common;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentToolPermissions
{
    public class GetTenantAgentToolPermissionsQueryHandler : IRequestHandler<GetTenantAgentToolPermissionsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public GetTenantAgentToolPermissionsQueryHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetTenantAgentToolPermissionsQuery query, CancellationToken cancellationToken)
        {
            var agent = await _db.TenantAgents.AsNoTracking()
                .Include(a => a.AgentTools)
                    .ThenInclude(at => at.Tool)
                .Where(a => a.Id == query.Id && a.Tenant.Name == _tenantContext.TenantName && !a.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent == null)
                return new NotFoundResult(nameof(TenantAgent), ErrorConst.AgentNotFound);

            return new SuccessResult<TenantAgentToolPermissionsResult>(await AgentToolPermissionsBuilder.BuildAsync(_db, agent, cancellationToken));
        }
    }
}
