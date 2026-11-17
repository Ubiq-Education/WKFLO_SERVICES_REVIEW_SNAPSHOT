using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.CreateTenantDxpIntegration
{
    public class CreateTenantDxpIntegrationCommandValidator : BaseMasterDataValidator<CreateTenantDxpIntegrationCommand>
    {
        public CreateTenantDxpIntegrationCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
                .MustAsync(BeNoDuplicateDxpIntegration)
                .WithMessage("A DXP integration already exists for this tenant.")
                .WithName(nameof(DxpIntegrations));
        }

        private async Task<bool> BeNoDuplicateDxpIntegration(CreateTenantDxpIntegrationCommand command, CancellationToken cancellationToken)
        {
            return !await _db.DxpIntegrations
                .AnyAsync(x => x.Tenant.Name == _tenantContext.TenantName && !x.IsDeleted, cancellationToken);
        }
    }
}
