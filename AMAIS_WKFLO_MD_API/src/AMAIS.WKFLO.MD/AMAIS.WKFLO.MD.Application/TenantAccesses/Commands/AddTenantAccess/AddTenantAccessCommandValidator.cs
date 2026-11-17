using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.TenantAccesses.Commands.AddTenantAccess
{
    public class AddTenantAccessCommandValidator : AbstractValidator<AddTenantAccessCommand>
    {
        public AddTenantAccessCommandValidator()
        {
            RuleFor(m => m.Email)
                .EmailAddress()
                .When(m => !string.IsNullOrWhiteSpace(m.Email))
                .WithMessage(ErrorConst.InvalidEmail);
        }
    }
}
