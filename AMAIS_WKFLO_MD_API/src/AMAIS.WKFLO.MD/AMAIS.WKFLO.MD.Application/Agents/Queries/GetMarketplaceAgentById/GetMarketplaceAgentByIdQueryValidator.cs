using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentById
{
    public class GetMarketplaceAgentByIdQueryValidator : BaseMasterDataValidator<GetMarketplaceAgentByIdQuery>
    {
        public GetMarketplaceAgentByIdQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
