using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.TenantSettings.Commands.UpdateTenantSettings
{
    public class UpdateTenantSettingsCommandValidator : BaseMasterDataValidator<UpdateTenantSettingsCommand>
    {
        public UpdateTenantSettingsCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m.TimeZone)
                .Must(value => TimeZoneIdNormaliser.TryNormalise(value, out _))
                .When(m => m.TimeZone != null)
                .WithMessage(ErrorConst.TenantSettingTimeZoneInvalid);

            RuleFor(m => m.TimeFormat)
                .Must(value => TimeFormatNormaliser.TryNormalise(value, out _))
                .When(m => m.TimeFormat != null)
                .WithMessage(ErrorConst.TenantSettingTimeFormatInvalid);

            RuleFor(m => m.DateFormat)
                .Must(value => DateFormatNormaliser.TryNormalise(value, out _))
                .When(m => m.DateFormat != null)
                .WithMessage(ErrorConst.TenantSettingDateFormatInvalid);
        }
    }
}
