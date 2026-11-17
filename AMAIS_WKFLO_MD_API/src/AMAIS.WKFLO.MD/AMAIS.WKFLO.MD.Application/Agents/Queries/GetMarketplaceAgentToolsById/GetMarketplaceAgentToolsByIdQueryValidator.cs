using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentToolsById
{
    public class GetMarketplaceAgentToolsByIdQueryValidator : BaseMasterDataValidator<GetMarketplaceAgentToolsByIdQuery>
    {
        public GetMarketplaceAgentToolsByIdQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
