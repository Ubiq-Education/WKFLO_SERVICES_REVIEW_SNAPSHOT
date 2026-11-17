using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.AIModels.Queries.GetAIModelById
{
    public class GetAIModelByIdQueryValidator : BaseMasterDataValidator<GetAIModelByIdQuery>
    {
        public GetAIModelByIdQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
