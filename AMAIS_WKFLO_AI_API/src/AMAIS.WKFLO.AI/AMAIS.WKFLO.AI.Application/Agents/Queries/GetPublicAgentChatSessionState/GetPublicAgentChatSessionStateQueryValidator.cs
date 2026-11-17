using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Common.Validators;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.GetPublicAgentChatSessionState
{
    public class GetPublicAgentChatSessionStateQueryValidator : BaseAIValidator<GetPublicAgentChatSessionStateQuery>
    {
        public GetPublicAgentChatSessionStateQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
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
               .Must(BeExistingTenantAgent)
               .WithMessage(ErrorConst.AgentMustExist)
               .WithName(nameof(Agent));
        }
    }
}
