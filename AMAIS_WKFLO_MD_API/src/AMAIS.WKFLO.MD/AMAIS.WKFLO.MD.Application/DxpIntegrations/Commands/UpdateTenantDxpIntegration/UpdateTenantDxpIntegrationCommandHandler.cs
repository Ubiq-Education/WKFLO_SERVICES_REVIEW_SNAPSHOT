using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.UpdateTenantDxpIntegration
{
    public class UpdateTenantDxpIntegrationCommandHandler : IRequestHandler<UpdateTenantDxpIntegrationCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public UpdateTenantDxpIntegrationCommandHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(UpdateTenantDxpIntegrationCommand command, CancellationToken cancellationToken)
        {
            var integration = await _db.DxpIntegrations
                .Where(x => x.Id == command.Id && x.Tenant.Name == _tenantContext.TenantName && !x.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (integration == null)
                return new NotFoundResult();

            integration.DxpTenantName = command.DxpTenantName;
            integration.Domain = command.Domain;
            integration.ApiKey = command.ApiKey;
            integration.IsActive = command.IsActive;

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<DxpIntegrationUpdateResult>(new DxpIntegrationUpdateResult
            {
                Id = integration.Id,
                DxpTenantName = integration.DxpTenantName,
                Domain = integration.Domain,
                ApiKey = integration.ApiKey,
                IsActive = integration.IsActive,
                CreatedOn = integration.CreatedOn,
                LastUpdatedOn = integration.LastUpdatedOn
            });
        }
    }
}
