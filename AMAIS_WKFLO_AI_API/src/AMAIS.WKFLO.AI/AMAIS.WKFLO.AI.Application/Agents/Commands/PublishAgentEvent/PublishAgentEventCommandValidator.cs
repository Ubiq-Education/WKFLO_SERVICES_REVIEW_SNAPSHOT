using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Common.Validators;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent
{
    public class PublishAgentEventCommandValidator : BaseAIValidator<PublishAgentEventCommand>
    {
        public PublishAgentEventCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m.Input)
               .NotNull()
               .WithMessage(ErrorConst.Required);

            RuleFor(m => m.Input.Text)
               .NotEmpty()
               .WithMessage(ErrorConst.Required)
               .When(m => m.Input != null);

            RuleFor(m => m)
               .Must(ThereIsTenantInDataContext)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenant)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingRunnableAgent)
               .WithMessage(ErrorConst.AgentNotRunnable)
               .WithName(nameof(Agent));
        }
    }
}
