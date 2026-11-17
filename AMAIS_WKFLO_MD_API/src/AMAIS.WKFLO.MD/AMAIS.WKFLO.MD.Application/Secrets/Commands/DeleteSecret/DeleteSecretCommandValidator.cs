using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Secrets.Commands.DeleteSecret
{
    public class DeleteSecretCommandValidator : BaseMasterDataValidator<DeleteSecretCommand>
    {
        public DeleteSecretCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(BeExistingSecret)
               .WithMessage(ErrorConst.SecretMustExist)
               .WithName(nameof(Secret));
        }
    }
}
