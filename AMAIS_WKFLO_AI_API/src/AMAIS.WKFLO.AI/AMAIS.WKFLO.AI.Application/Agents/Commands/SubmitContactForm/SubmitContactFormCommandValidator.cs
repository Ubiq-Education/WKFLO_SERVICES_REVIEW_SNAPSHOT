using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Common.Validators;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.SubmitContactForm
{
    public class SubmitContactFormCommandValidator : BaseAIValidator<SubmitContactFormCommand>
    {
        public SubmitContactFormCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
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
               .Must(BeExistingAgent)
               .WithMessage(ErrorConst.AgentMustExist)
               .WithName(nameof(Agent));

            RuleFor(m => m.SessionId)
                .NotEmpty()
                .WithMessage(ErrorConst.Required);

            RuleFor(m => m.SubjectId)
                .NotEmpty()
                .WithMessage(ErrorConst.Required);
        }
    }
}
