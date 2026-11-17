using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.AICredits.Queries.GetAICreditById
{
    public class GetAICreditByIdQueryValidator : BaseMasterDataValidator<GetAICreditByIdQuery>
    {
        public GetAICreditByIdQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
