using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.AICredits.Queries.GetAICreditByAIModelId
{
    public class GetAICreditByAIModelIdQueryValidator : BaseMasterDataValidator<GetAICreditByAIModelIdQuery>
    {
        public GetAICreditByAIModelIdQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
