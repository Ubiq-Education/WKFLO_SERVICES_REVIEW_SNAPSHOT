using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Tools.Queries.GetTenantToolFunctions
{
    public class GetTenantToolFunctionsQueryHandler : IRequestHandler<GetTenantToolFunctionsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public GetTenantToolFunctionsQueryHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetTenantToolFunctionsQuery query, CancellationToken cancellationToken)
        {
            var tool = await _db.TenantTools.AsNoTracking()
                .Where(m => m.Id == query.Id && m.Tenant.Name == _tenantContext.TenantName && !m.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (tool == null)
                return new NotFoundResult(nameof(TenantTool), ErrorConst.ToolNotFound);

            if (tool.Type != CommonToolTypeConsts.MCP)
                return new InvalidResult(nameof(Tool.Type), ErrorConst.ToolMustBeMcp);

            return new SuccessResult<ToolFunctionsResult>(ToolFunctionsBuilder.Build(tool));
        }
    }
}
