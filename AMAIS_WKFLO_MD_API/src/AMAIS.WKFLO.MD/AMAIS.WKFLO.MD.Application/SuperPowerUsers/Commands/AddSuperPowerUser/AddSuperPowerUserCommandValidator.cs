using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.SuperPowerUsers.Commands.AddSuperPowerUser
{
    public class AddSuperPowerUserCommandValidator : AbstractValidator<AddSuperPowerUserCommand>
    {
        public AddSuperPowerUserCommandValidator()
        {
            RuleFor(m => m.Email)
                .EmailAddress()
                .When(m => !string.IsNullOrWhiteSpace(m.Email))
                .WithMessage(ErrorConst.InvalidEmail);
        }
    }
}
