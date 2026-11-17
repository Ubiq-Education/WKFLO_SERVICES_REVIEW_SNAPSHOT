using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tenants.Models;
using AMAIS.WKFLO.MD.Application.WebOrigins.Models;
using AMAIS.WKFLO.MD.Application.WebOrigins.Services;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tenants.Commands.CreateTenant
{
    public class CreateTenantCommandHandler : IRequestHandler<CreateTenantCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public CreateTenantCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(CreateTenantCommand command, CancellationToken cancellationToken)
        {
            var existingTenant = await _db.Tenants
                .FirstOrDefaultAsync(t => t.Name == command.Name, cancellationToken);

            if (existingTenant != null)
            {
                var existingResult = _mapper.Map<TenantCreateResult>(existingTenant);
                existingResult.Code = 200;

                var incoming = MergeWithDefaults(command.WebOrigins ?? [])
                    .Select(item => new TenantWebOriginInput(item.WebOriginType, item.Name, item.IsActive));

                var origins = await TenantWebOriginMerger.MergeAsync(_db, existingTenant, incoming, cancellationToken);
                existingResult.WebOrigins = _mapper.Map<List<TenantWebOriginResult>>(origins);

                return new SuccessResult<TenantCreateResult>(existingResult);
            }

            var tenant = _mapper.Map<Tenant>(command);
            tenant.Guid = Guid.NewGuid();
            _db.Tenants.Add(tenant);

            var webOrigins = BuildWebOrigins(MergeWithDefaults(command.WebOrigins ?? []), tenant);
            if (webOrigins.Count > 0)
                _db.WebOrigins.AddRange(webOrigins);

            await _db.SaveChangesAsync(cancellationToken);

            var createResult = _mapper.Map<TenantCreateResult>(tenant);
            createResult.Code = 201;
            createResult.WebOrigins = _mapper.Map<List<TenantWebOriginResult>>(webOrigins);

            return new SuccessResult<TenantCreateResult>(createResult);
        }

        private static List<CreateTenantWebOriginItem> MergeWithDefaults(List<CreateTenantWebOriginItem> items)
        {
            var defaults = GetDefaultOrigins();
            var missing = defaults.Where(d => items.All(i => i.Name != d.Name));
            return [.. items, .. missing];
        }

        private static List<CreateTenantWebOriginItem> GetDefaultOrigins()
        {
            var raw = Environment.GetEnvironmentVariable("DEFAULT_WEB_ORIGINS");
            if (string.IsNullOrEmpty(raw)) return [];

            return raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(url => new CreateTenantWebOriginItem { Name = url, WebOriginType = "SignalR", IsActive = true })
                .ToList();
        }

        private static List<WebOrigin> BuildWebOrigins(IEnumerable<CreateTenantWebOriginItem> items, Tenant tenant) =>
            items.Select(item => new WebOrigin
            {
                WebOriginType = item.WebOriginType,
                Name = item.Name,
                IsActive = item.IsActive,
                Tenant = tenant
            }).ToList();
    }
}
