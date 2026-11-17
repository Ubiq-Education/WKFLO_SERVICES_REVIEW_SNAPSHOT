using AMAIS.WKFLO.Common.AI.Application.Common.Validators;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessageTenantAgentById
{
    public class VoiceMessageTenantAgentByIdCommandValidator : BaseAIValidator<VoiceMessageTenantAgentByIdCommand>
    {
        public VoiceMessageTenantAgentByIdCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(x => x.AgentId).GreaterThan(0).WithErrorCode(CommonErrorConst.Required);
            RuleFor(x => x.SessionId).NotEmpty().WithErrorCode(CommonErrorConst.Required);
            RuleFor(x => x.Text).NotEmpty().WithErrorCode(CommonErrorConst.Required);
        }
    }
}
