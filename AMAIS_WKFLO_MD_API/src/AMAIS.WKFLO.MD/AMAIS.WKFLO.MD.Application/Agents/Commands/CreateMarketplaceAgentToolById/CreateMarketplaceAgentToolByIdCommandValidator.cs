using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgentToolById
{
    public class CreateMarketplaceAgentToolByIdCommandValidator : BaseMasterDataValidator<CreateMarketplaceAgentToolByIdCommand>
    {
        public CreateMarketplaceAgentToolByIdCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
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
               .Must(BeExisting)
               .WithMessage(ErrorConst.AgentAndToolMustExist)
               .WithName(nameof(Agent));
        }

        public bool BeExisting(CreateMarketplaceAgentToolByIdCommand command)
        {
            var anyAgent = _db
                .MarketplaceAgents
                .Where(m => m.Id == command.Id)
                .Any();

            var anyTool = _db
                .MarketplaceTools
                .Where(m => m.Id == command.ToolId)
                .Any();

            return anyAgent && anyTool;
        }
    }
}
