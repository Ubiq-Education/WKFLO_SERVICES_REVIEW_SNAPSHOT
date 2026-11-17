using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgentToolPermissions
{
    /// <summary>
    /// Shape checks only. The checks that need the agent's linked tools (linked, permissions on, MCP vs. other
    /// type, function enabled) are done by the handler against the rows it loads anyway.
    /// </summary>
    public class UpdateTenantAgentToolPermissionsCommandValidator : BaseMasterDataValidator<UpdateTenantAgentToolPermissionsCommand>
    {
        public UpdateTenantAgentToolPermissionsCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
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
               .Must(BeExistingTenantAgent)
               .WithMessage(ErrorConst.AgentMustExist)
               .WithName(nameof(Agent));

            RuleFor(m => m.Tools)
               .NotNull()
               .WithMessage(ErrorConst.Required);

            RuleFor(m => m.Tools)
               .Must(tools => tools.Select(t => t?.ToolId).Distinct().Count() == tools.Count)
               .When(m => m.Tools != null)
               .WithMessage(ErrorConst.InvalidInput);

            RuleForEach(m => m.Tools).ChildRules(tool =>
            {
                tool.RuleFor(t => t.ToolId)
                    .GreaterThan(0)
                    .WithMessage(ErrorConst.InvalidInput);

                tool.RuleFor(t => t.Permission)
                    .Must(ToolPermissionValues.IsValid)
                    .When(t => t.Permission != null)
                    .WithMessage(ErrorConst.ToolPermissionInvalid);

                tool.RuleForEach(t => t.Functions).ChildRules(function =>
                {
                    function.RuleFor(f => f.Name)
                        .NotEmpty()
                        .WithMessage(ErrorConst.Required);

                    function.RuleFor(f => f.Permission)
                        .Must(ToolPermissionValues.IsValid)
                        .WithMessage(ErrorConst.ToolPermissionInvalid);
                });
            });
        }
    }
}
