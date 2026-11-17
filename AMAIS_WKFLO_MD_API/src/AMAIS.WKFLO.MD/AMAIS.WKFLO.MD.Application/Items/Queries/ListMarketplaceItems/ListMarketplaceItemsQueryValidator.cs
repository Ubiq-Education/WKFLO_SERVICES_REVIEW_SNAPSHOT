using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.Items.Queries.ListMarketplaceItems
{
    public class ListMarketplaceItemsQueryValidator : BaseMasterDataValidator<ListMarketplaceItemsQuery>
    {
        public ListMarketplaceItemsQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
