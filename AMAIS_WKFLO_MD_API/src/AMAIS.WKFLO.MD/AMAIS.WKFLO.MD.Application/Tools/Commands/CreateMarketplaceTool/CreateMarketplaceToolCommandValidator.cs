using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool
{
    public class CreateMarketplaceToolCommandValidator : BaseMasterDataValidator<CreateMarketplaceToolCommand>
    {
        public CreateMarketplaceToolCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
        }
    }
}
