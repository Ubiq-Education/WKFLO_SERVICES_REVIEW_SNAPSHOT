using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UploadAgentChatIcon
{
    // Mirrors UpdateAgentChatSettingCommandValidator: the validator only checks tenant existence;
    // the tenant-agent existence/ownership check happens at handler level (see
    // UploadAgentChatIconCommandHandler), same as UpdateAgentChatSettingCommandHandler does.
    public class UploadAgentChatIconCommandValidator : BaseMasterDataValidator<UploadAgentChatIconCommand>
    {
        public UploadAgentChatIconCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(ThereIsTenantInDataContext)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenant)
               .WithMessage(ErrorConst.TenantMustExist)
               .WithName(nameof(TenantContext.TenantName));
        }
    }
}
