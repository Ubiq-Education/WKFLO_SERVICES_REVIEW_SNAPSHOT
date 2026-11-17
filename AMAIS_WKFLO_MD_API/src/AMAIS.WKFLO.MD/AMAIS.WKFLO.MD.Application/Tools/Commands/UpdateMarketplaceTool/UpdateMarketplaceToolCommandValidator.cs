using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateMarketplaceTool
{
    public class UpdateMarketplaceToolCommandValidator : BaseMasterDataValidator<UpdateMarketplaceToolCommand>
    {
        public UpdateMarketplaceToolCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(BeExistingMarketplaceToolAndBelongToTenant)
               .WithMessage(ErrorConst.ToolMustExist)
               .WithName(nameof(Tool));
        }
    }
}
