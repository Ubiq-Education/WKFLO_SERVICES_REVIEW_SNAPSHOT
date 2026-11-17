using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenants
{
    public class ListTenantsQueryValidator : BaseMasterDataValidator<ListTenantsQuery>
    {
        public ListTenantsQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
