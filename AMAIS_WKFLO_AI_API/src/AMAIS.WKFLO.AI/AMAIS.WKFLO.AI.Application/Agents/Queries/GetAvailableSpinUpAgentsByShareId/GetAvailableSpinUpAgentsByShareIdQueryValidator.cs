using AMAIS.WKFLO.AI.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.GetAvailableSpinUpAgentsByShareId
{
    public class GetAvailableSpinUpAgentsByShareIdQueryValidator : AbstractValidator<GetAvailableSpinUpAgentsByShareIdQuery>
    {
        public GetAvailableSpinUpAgentsByShareIdQueryValidator()
        {
            RuleFor(m => m.ShareId)
               .NotEmpty()
               .WithMessage(ErrorConst.Required);
        }
    }
}
