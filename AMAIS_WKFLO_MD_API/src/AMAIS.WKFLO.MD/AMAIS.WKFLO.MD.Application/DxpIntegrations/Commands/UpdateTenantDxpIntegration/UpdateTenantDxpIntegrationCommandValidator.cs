using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.UpdateTenantDxpIntegration
{
    public class UpdateTenantDxpIntegrationCommandValidator : BaseMasterDataValidator<UpdateTenantDxpIntegrationCommand>
    {
        public UpdateTenantDxpIntegrationCommandValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
                .MustAsync(BeExistingDxpIntegration)
                .WithMessage("DXP integration not found.")
                .WithName(nameof(DxpIntegration));
        }

        private async Task<bool> BeExistingDxpIntegration(UpdateTenantDxpIntegrationCommand command, CancellationToken cancellationToken)
        {
            return await _db.DxpIntegrations
                .AnyAsync(x => x.Id == command.Id && x.Tenant.Name == _tenantContext.TenantName && !x.IsDeleted, cancellationToken);
        }
    }
}
