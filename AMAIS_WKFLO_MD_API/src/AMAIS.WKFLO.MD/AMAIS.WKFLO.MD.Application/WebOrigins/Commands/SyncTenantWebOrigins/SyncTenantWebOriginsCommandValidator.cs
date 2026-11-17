using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.WebOrigins.Commands.SyncTenantWebOrigins
{
    public class SyncTenantWebOriginsCommandValidator : BaseMasterDataValidator<SyncTenantWebOriginsCommand>
    {
        public SyncTenantWebOriginsCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
                .Must(ThereIsTenantInDataContext)
                .WithMessage(ErrorConst.TenantInvalid)
                .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
                .Must(BeExistingTenant)
                .WithMessage(ErrorConst.TenantMustExist)
                .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m.WebOrigins)
                .NotNull()
                .WithMessage(ErrorConst.Required);

            RuleForEach(m => m.WebOrigins)
                .ChildRules(item =>
                {
                    item.RuleFor(i => i.WebOriginType)
                        .NotEmpty()
                        .WithMessage(ErrorConst.Required)
                        .MaximumLength(100)
                        .WithMessage(ErrorConst.MaximumLength);

                    item.RuleFor(i => i.Name)
                        .NotEmpty()
                        .WithMessage(ErrorConst.Required)
                        .MaximumLength(500)
                        .WithMessage(ErrorConst.MaximumLength);
                });
        }
    }
}
