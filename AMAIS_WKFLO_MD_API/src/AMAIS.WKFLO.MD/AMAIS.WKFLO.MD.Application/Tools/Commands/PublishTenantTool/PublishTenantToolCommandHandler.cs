using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.PublishTenantTool
{
    public class PublishTenantToolCommandHandler : IRequestHandler<PublishTenantToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly IDateTimeProvider _dateTimeProvider;

        public PublishTenantToolCommandHandler(
            IMasterDataDbContext db,
            IWkfloMapper mapper,
            TenantContext tenantContext,
            IDateTimeProvider dateTimeProvider)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
            _dateTimeProvider = dateTimeProvider;
        }

        public async ValueTask<Result> Handle(PublishTenantToolCommand command, CancellationToken cancellationToken)
        {
            // STEP 1: Get the TenantTool first
            var tenantTool = await _db.TenantTools
                .Include(t => t.Tenant)
                .Where(t => t.Id == command.Id &&
                           t.Tenant.Name == _tenantContext.TenantName &&
                           !t.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            // STEP 2: Check if this TenantTool has already been published
            var existingPublishedTool = await _db.PublishedTenantTools
                .Where(pt => pt.CreatedByTenantToolId == command.Id &&
                            pt.Tenant.Name == _tenantContext.TenantName &&
                            !pt.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            PublishedTenantTool publishedTenantTool;
            if (existingPublishedTool != null)
            {
                // Update existing published tool
                publishedTenantTool = existingPublishedTool;
                _mapper.Map(tenantTool, publishedTenantTool);
                // Preserve the original creation relationship
                publishedTenantTool.CreatedByTenantToolId = command.Id;
                tenantTool.PublishedTenantTool = publishedTenantTool;

                // Explicitly stamp LastUpdatedOn even when the mapped fields produce no actual value
                // differences (republishing a tool unchanged since its last publish). Without this, EF's
                // change tracker sees the entity as Unchanged, so neither
                // MasterDataDbContext.SaveChangesAsync's own bookkeeping loop nor AppendAuditLogEntries (whose
                // ResolveAuditAction returns null for the Unchanged state) ever runs for it — a deliberate
                // republish action would leave zero trace, no timestamp bump and no audit row. Forcing this
                // property into the Modified set fixes both: the timestamp updates, and the audit interceptor
                // now records an Update action (Changes will likely be null, since LastUpdatedOn itself is a
                // bookkeeping field excluded from the diff — that's fine, the row existing is what matters).
                publishedTenantTool.LastUpdatedOn = _dateTimeProvider.DateTimeOffsetUtcNow();
            }
            else
            {
                // Create new published tool
                publishedTenantTool = _mapper.Map<PublishedTenantTool>(tenantTool);
                publishedTenantTool.CreatedByTenantToolId = command.Id;
                publishedTenantTool.Id = 0; // Reset ID to create new entity

                var tenant = await TryGetTenantAsync(_tenantContext.TenantName);
                publishedTenantTool.Tenant = tenant;

                tenantTool.PublishedTenantTool = publishedTenantTool;

                await _db.PublishedTenantTools.AddAsync(publishedTenantTool, cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);

            // Sync tags from TenantTool to PublishedTenantTool
            var toolTags = await _db.EntityTags
                .Where(m => m.EntityType == CommonConsts.TaggableEntityTypes.Tool && m.EntityId == command.Id)
                .ToListAsync(cancellationToken);

            var existingPublishedToolTags = await _db.EntityTags
                .Where(m => m.EntityType == CommonConsts.TaggableEntityTypes.Tool && m.EntityId == publishedTenantTool.Id)
                .ToListAsync(cancellationToken);

            if (existingPublishedToolTags.Any())
                _db.EntityTags.RemoveRange(existingPublishedToolTags);

            if (toolTags.Any())
                await _db.EntityTags.AddRangeAsync(
                    toolTags.Select(et => new EntityTag { TagId = et.TagId, EntityType = CommonConsts.TaggableEntityTypes.Tool, EntityId = publishedTenantTool.Id }),
                    cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TenantToolPublishResult>(_mapper.Map<TenantToolPublishResult>(publishedTenantTool));
        }

        protected async Task<Tenant> TryGetTenantAsync(string tenant)
        {
            var tenantDB = await _db.Tenants.Where(m => m.Name == tenant).FirstOrDefaultAsync();
            if (tenantDB == null)
            {
                return new Tenant() { Name = tenant };
            }
            else
            {
                return tenantDB;
            }
        }
    }
}