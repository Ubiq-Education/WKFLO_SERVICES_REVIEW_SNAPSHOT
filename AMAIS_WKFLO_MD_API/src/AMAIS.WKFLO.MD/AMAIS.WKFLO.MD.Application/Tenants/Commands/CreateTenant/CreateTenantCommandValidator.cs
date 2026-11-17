using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Tenants.Commands.CreateTenant
{
    public class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
    {
        public CreateTenantCommandValidator()
        {
            RuleFor(v => v.Name)
                .MaximumLength(50)
                .NotEmpty();

            RuleFor(v => v.Region)
                .MaximumLength(50)
                .NotEmpty();
        }
    }
}
