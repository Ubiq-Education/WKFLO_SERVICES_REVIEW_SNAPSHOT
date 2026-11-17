using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.CreateTenantDxpIntegration
{
    public class CreateTenantDxpIntegrationCommandHandler : IRequestHandler<CreateTenantDxpIntegrationCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public CreateTenantDxpIntegrationCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(CreateTenantDxpIntegrationCommand command, CancellationToken cancellationToken)
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenant == null)
                return new NotFoundResult();

            var integration = _mapper.Map<DxpIntegration>(command);
            integration.TenantId = tenant.Id;

            await _db.DxpIntegrations.AddAsync(integration, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<DxpIntegrationCreateResult>(_mapper.Map<DxpIntegrationCreateResult>(integration));
        }
    }
}
