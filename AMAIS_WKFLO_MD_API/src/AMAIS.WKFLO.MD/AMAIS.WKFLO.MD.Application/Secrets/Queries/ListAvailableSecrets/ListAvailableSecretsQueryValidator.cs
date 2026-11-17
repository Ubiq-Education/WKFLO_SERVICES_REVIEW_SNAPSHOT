using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.Secrets.Queries.ListAvailableSecrets
{
    public class ListAvailableSecretsQueryValidator : BaseMasterDataValidator<ListAvailableSecretsQuery>
    {
        public ListAvailableSecretsQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
