using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.AICredits.Commands.UpdateAICredit
{
    public class UpdateAICreditCommandValidator : BaseMasterDataValidator<UpdateAICreditCommand>
    {
        public UpdateAICreditCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(BeExistingAICredit)
               .WithMessage(ErrorConst.AICreditMustExist)
               .WithName(nameof(AICredit));

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
               .WithName(nameof(UpdateAICreditCommand.AIModelId));

            RuleFor(m => m)
               .Must(NotConflictWithAnotherAICredit)
               .WithMessage(ErrorConst.AICreditAlreadyExistsForAIModel)
               .WithName(nameof(UpdateAICreditCommand.AIModelId));
        }

        private bool BeExistingAIModelForCredit(UpdateAICreditCommand command)
        {
            return _db.AIModels.Any(m => m.Id == command.AIModelId && !m.IsDeleted);
        }

        private bool NotConflictWithAnotherAICredit(UpdateAICreditCommand command)
        {
            return !_db.AICredits.Any(c => c.AIModelId == command.AIModelId && c.Id != command.Id && !c.IsDeleted);
        }
    }
}
