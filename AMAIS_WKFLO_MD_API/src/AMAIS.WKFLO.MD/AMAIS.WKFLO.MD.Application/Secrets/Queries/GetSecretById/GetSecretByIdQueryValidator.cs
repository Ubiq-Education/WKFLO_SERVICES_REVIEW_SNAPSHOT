using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.Secrets.Queries.GetSecretById
{
    public class GetSecretByIdQueryValidator : BaseMasterDataValidator<GetSecretByIdQuery>
    {
        public GetSecretByIdQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
