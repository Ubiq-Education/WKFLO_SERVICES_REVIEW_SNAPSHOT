using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool
{
    public class UpdateTenantToolCommandValidator : BaseMasterDataValidator<UpdateTenantToolCommand>
    {
        public UpdateTenantToolCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
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
               .Must(BeExistingTenantTool)
               .WithMessage(ErrorConst.ToolMustExist)
               .WithName(nameof(Tool));

            RuleFor(m => m.MaxToolOutputLength)
               .GreaterThan(0)
               .When(m => m.MaxToolOutputLength.HasValue)
               .WithMessage(ErrorConst.MustBePositive);
        }
    }
}
