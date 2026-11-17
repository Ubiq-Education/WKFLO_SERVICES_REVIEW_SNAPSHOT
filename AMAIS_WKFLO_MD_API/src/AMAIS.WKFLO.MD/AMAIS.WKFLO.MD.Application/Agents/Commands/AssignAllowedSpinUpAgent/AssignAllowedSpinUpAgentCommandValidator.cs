using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.AssignAllowedSpinUpAgent
{
    public class AssignAllowedSpinUpAgentCommandValidator : BaseMasterDataValidator<AssignAllowedSpinUpAgentCommand>
    {
        public AssignAllowedSpinUpAgentCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(ThereIsTenantInDataContext)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenant)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingAndBelongToSameTenant)
               .WithMessage(ErrorConst.AllowedSpinUpAgentMustExist)
               .WithName(nameof(TenantAgent));
        }

        public bool BeExistingAndBelongToSameTenant(AssignAllowedSpinUpAgentCommand command)
        {
            var anyAgent = _db
                .TenantAgents
                .Where(m => m.Id == command.Id && m.Tenant.Name == _tenantContext.TenantName)
                .Any();

            var anyAllowedAgent = _db
                .TenantAgents
                .Where(m => m.Id == command.AllowedSpinUpAgentId && m.Tenant.Name == _tenantContext.TenantName)
                .Any();

            return anyAgent && anyAllowedAgent;
        }
    }
}
