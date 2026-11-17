using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.AICredits.Commands.CreateAICredit
{
    public class CreateAICreditCommandValidator : BaseMasterDataValidator<CreateAICreditCommand>
    {
        public CreateAICreditCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            // WI 7384: MultiplierIn/MultiplierOut must be greater than 0 - fractional multipliers
            // (e.g. 0.01) are intentional, to allow a small number of credits per token.
            RuleFor(m => m.MultiplierIn)
                .GreaterThan(0)
                .WithMessage(ErrorConst.AICreditMultiplierMustBePositive);

            RuleFor(m => m.MultiplierOut)
                .GreaterThan(0)
                .WithMessage(ErrorConst.AICreditMultiplierMustBePositive);

            RuleFor(m => m.TokensIn)
                .GreaterThanOrEqualTo(0)
                .WithMessage(ErrorConst.AICreditTokensMustBeNonNegative);

            RuleFor(m => m.TokensOut)
                .GreaterThanOrEqualTo(0)
                .WithMessage(ErrorConst.AICreditTokensMustBeNonNegative);

            RuleFor(m => m.CostInMTok)
                .GreaterThanOrEqualTo(0)
                .WithMessage(ErrorConst.AICreditCostMustBeNonNegative);

            RuleFor(m => m.CostOutMTok)
                .GreaterThanOrEqualTo(0)
                .WithMessage(ErrorConst.AICreditCostMustBeNonNegative);

            RuleFor(m => m)
               .Must(BeExistingAIModelForCredit)
               .WithMessage(ErrorConst.AIModelMustExistForAICredit)
               .WithName(nameof(CreateAICreditCommand.AIModelId));

            RuleFor(m => m)
               .Must(NotAlreadyHaveAICredit)
               .WithMessage(ErrorConst.AICreditAlreadyExistsForAIModel)
               .WithName(nameof(CreateAICreditCommand.AIModelId));
        }

        private bool BeExistingAIModelForCredit(CreateAICreditCommand command)
        {
            return _db.AIModels.Any(m => m.Id == command.AIModelId && !m.IsDeleted);
        }

        private bool NotAlreadyHaveAICredit(CreateAICreditCommand command)
        {
            return !_db.AICredits.Any(c => c.AIModelId == command.AIModelId && !c.IsDeleted);
        }
    }
}
