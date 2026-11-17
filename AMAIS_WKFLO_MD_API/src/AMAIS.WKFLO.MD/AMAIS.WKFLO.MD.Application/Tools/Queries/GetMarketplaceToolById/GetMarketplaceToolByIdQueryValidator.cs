using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.Tools.Queries.GetMarketplaceToolById
{
    public class GetMarketplaceToolByIdQueryValidator : BaseMasterDataValidator<GetMarketplaceToolByIdQuery>
    {
        public GetMarketplaceToolByIdQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
