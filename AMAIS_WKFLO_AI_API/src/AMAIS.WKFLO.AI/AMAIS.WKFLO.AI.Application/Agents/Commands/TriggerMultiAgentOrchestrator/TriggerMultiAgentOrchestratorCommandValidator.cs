using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Common.Validators;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.TriggerMultiAgentOrchestrator
{
    public class TriggerMultiAgentOrchestratorCommandValidator : BaseAIValidator<TriggerMultiAgentOrchestratorCommand>
    {
        public TriggerMultiAgentOrchestratorCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(ThereIsTenantInDataContext)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenant)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m.Input)
               .NotNull()
               .WithMessage(ErrorConst.Required);

            RuleFor(m => m.Input.Text)
               .NotEmpty()
               .WithMessage(ErrorConst.Required)
               .When(m => m.Input != null);

            RuleFor(m => m.LeadAgent)
               .NotNull()
               .WithMessage(ErrorConst.Required);

            RuleFor(m => m.LeadAgent.AgentRole)
               .NotEmpty()
               .WithMessage(ErrorConst.Required)
               .When(m => m.LeadAgent != null);

            RuleFor(m => m.LeadAgent.AgentId)
               .Must(BeExistingRunnableAgent)
               .WithMessage(ErrorConst.AgentNotRunnable)
               .When(m => m.LeadAgent != null);

            RuleFor(m => m.FanOutAgents)
               .NotEmpty()
               .WithMessage(ErrorConst.Required);

            RuleForEach(m => m.FanOutAgents)
               .ChildRules(step =>
               {
                   step.RuleFor(s => s.AgentRole)
                       .NotEmpty()
                       .WithMessage(ErrorConst.Required);

                   step.RuleFor(s => s.AgentId)
                       .Must(BeExistingRunnableAgent)
                       .WithMessage(ErrorConst.AgentNotRunnable);
               });

            // Every step's AgentRole must be unique — it's the human-readable label callers use to
            // tell steps apart in MultiAgentOrchestratorWorkflowOutput.FanOutResults and in logs/
            // traces. A duplicate role wouldn't collide on shared state any more (agent invocation
            // dropped its Dapr Actor hop in #6553), but callers would have no way to distinguish two
            // fan-out results carrying the same role label.
            RuleFor(m => m)
               .Must(HaveUniqueAgentRoles)
               .WithMessage(ErrorConst.AgentRoleMustBeUnique)
               .WithName(nameof(TriggerMultiAgentOrchestratorCommand.FanOutAgents));
        }

        private static bool HaveUniqueAgentRoles(TriggerMultiAgentOrchestratorCommand command)
        {
            var roles = new List<string>();

            if (command.LeadAgent != null && !string.IsNullOrWhiteSpace(command.LeadAgent.AgentRole))
            {
                roles.Add(command.LeadAgent.AgentRole);
            }

            if (command.FanOutAgents != null)
            {
                roles.AddRange(command.FanOutAgents
                    .Where(step => !string.IsNullOrWhiteSpace(step.AgentRole))
                    .Select(step => step.AgentRole));
            }

            return roles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == roles.Count;
        }
    }
}
