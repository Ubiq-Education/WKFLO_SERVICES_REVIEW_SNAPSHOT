using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.RateMarketplaceTool
{
    public class RateMarketplaceToolCommandValidator : BaseMasterDataValidator<RateMarketplaceToolCommand>
    {
        public RateMarketplaceToolCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(ThereIsTenantInDataContext)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenant)
               .WithMessage(ErrorConst.TenantMustExist)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
                .Must(BeExistingMarketplaceTool)
                .WithMessage(ErrorConst.ToolNotFound)
                .WithName(nameof(MarketplaceTool));

            RuleFor(m => m.Rating)
                .InclusiveBetween(1, 5)
                .WithMessage(ErrorConst.ToolRatingMustBeBetween1And5);
        }
    }
}