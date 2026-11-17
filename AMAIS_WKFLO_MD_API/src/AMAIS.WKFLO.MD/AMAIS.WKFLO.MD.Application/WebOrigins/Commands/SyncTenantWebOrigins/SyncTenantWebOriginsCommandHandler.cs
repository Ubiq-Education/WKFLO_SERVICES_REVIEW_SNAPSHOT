using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.WebOrigins.Models;
using AMAIS.WKFLO.MD.Application.WebOrigins.Services;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.WebOrigins.Commands.SyncTenantWebOrigins
{
    public class SyncTenantWebOriginsCommandHandler : IRequestHandler<SyncTenantWebOriginsCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public SyncTenantWebOriginsCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(SyncTenantWebOriginsCommand command, CancellationToken cancellationToken)
        {
            var tenant = await _db.Tenants
                .Where(m => m.Name == _tenantContext.TenantName)
                .FirstAsync(cancellationToken);

            var incoming = (command.WebOrigins ?? [])
                .Select(item => new TenantWebOriginInput(item.WebOriginType, item.Name, item.IsActive));

            var origins = await TenantWebOriginMerger.MergeAsync(_db, tenant, incoming, cancellationToken);

            return new SuccessResult<List<TenantWebOriginResult>>(_mapper.Map<List<TenantWebOriginResult>>(origins));
        }
    }
}
