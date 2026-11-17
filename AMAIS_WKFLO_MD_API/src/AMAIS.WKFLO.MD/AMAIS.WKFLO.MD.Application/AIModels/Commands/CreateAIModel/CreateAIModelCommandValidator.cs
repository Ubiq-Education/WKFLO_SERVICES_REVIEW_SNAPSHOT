using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel
{
    public class CreateAIModelCommandValidator : BaseMasterDataValidator<CreateAIModelCommand>
    {
        public CreateAIModelCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(AuthModeValidForProvider)
               .WithMessage(ErrorConst.AIModelAuthModeInvalidForProvider)
               .WithName(nameof(CreateAIModelCommand.AuthMode));
        }

        private static bool AuthModeValidForProvider(CreateAIModelCommand command)
        {
            var isManagedIdentity = Enum.TryParse<AuthModeEnum>(command.AuthMode, true, out var authMode) && authMode == AuthModeEnum.managedIdentity;
            if (!isManagedIdentity)
            {
                return true;
            }

            return Enum.TryParse<ProviderEnum>(command.Provider, true, out var provider) && provider == ProviderEnum.azure;
        }
    }
}
