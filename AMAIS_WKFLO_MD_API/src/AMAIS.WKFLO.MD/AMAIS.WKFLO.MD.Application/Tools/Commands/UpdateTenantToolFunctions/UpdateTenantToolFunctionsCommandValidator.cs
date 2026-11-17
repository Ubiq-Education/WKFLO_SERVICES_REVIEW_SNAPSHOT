using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantToolFunctions
{
    public class UpdateTenantToolFunctionsCommandValidator : BaseMasterDataValidator<UpdateTenantToolFunctionsCommand>
    {
        public UpdateTenantToolFunctionsCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(ThereIsTenantInDataContext)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenant)
               .WithMessage(ErrorConst.TenantMustExist)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenantTool)
               .WithMessage(ErrorConst.ToolMustExist)
               .WithName(nameof(Tool));

            RuleFor(m => m.EnabledFunctions)
               .NotNull()
               .WithMessage(ErrorConst.Required);

            RuleForEach(m => m.EnabledFunctions)
               .NotEmpty()
               .WithMessage(ErrorConst.InvalidInput);

            // WI 7726: optional friendly display values (null = unchanged).
            RuleFor(m => m.Functions)
               .Must(functions => functions.Select(f => f?.Name).Distinct(StringComparer.Ordinal).Count() == functions.Count)
               .When(m => m.Functions != null)
               .WithMessage(ErrorConst.InvalidInput);

            RuleForEach(m => m.Functions)
               .NotNull()
               .WithMessage(ErrorConst.InvalidInput);

            RuleForEach(m => m.Functions).ChildRules(function =>
            {
                function.RuleFor(f => f.Name)
                    .NotEmpty()
                    .WithMessage(ErrorConst.Required);

                function.RuleFor(f => f.DisplayName)
                    .Must(v => (McpFunctionDescriptor.NormalizeDisplayValue(v)?.Length ?? 0) <= McpFunctionDescriptor.DisplayNameMaxLength)
                    .WithMessage(ErrorConst.MaximumLength);

                function.RuleFor(f => f.DisplayDescription)
                    .Must(v => (McpFunctionDescriptor.NormalizeDisplayValue(v)?.Length ?? 0) <= McpFunctionDescriptor.DisplayDescriptionMaxLength)
                    .WithMessage(ErrorConst.MaximumLength);
            });
        }
    }
}
