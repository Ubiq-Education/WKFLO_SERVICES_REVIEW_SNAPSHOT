using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Tools.Commands.PublishTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.PublishTenantAgent
{
    public class PublishTenantAgentCommandHandler : IRequestHandler<PublishTenantAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly IWkfloMediator _mediator;
        private readonly IDateTimeProvider _dateTimeProvider;

        public PublishTenantAgentCommandHandler(
            IMasterDataDbContext db,
            IWkfloMapper mapper,
            TenantContext tenantContext,
            IWkfloMediator mediator,
            IDateTimeProvider dateTimeProvider)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
            _mediator = mediator;
            _dateTimeProvider = dateTimeProvider;
        }

        public async ValueTask<Result> Handle(PublishTenantAgentCommand command, CancellationToken cancellationToken)
        {
            // Get the TenantAgent first
            var tenantAgent = await _db.TenantAgents
                .Include(a => a.Tenant)
                .Include(a => a.AgentTools)
                    .ThenInclude(at => at.Tool)
                .Include(a => a.AIModel)
                .Where(a => a.Id == command.Id &&
                           a.Tenant.Name == _tenantContext.TenantName &&
                           !a.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);



            // Check if this TenantAgent has already been published
            var existingPublishedAgent = await _db.PublishedTenantAgents
                .Include(a => a.Tenant)
                .Include(a => a.AgentTools)
                    .ThenInclude(at => at.Tool)
                .Where(pa => pa.CreatedByTenantAgentId == command.Id &&
                            pa.Tenant.Name == _tenantContext.TenantName &&
                            !pa.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            PublishedTenantAgent publishedTenantAgent;
            if (existingPublishedAgent != null)
            {
                // Update existing published agent
                publishedTenantAgent = existingPublishedAgent;
                _mapper.Map(tenantAgent, publishedTenantAgent);
                // Preserve the original creation relationship
                publishedTenantAgent.CreatedByTenantAgentId = command.Id;
                tenantAgent.PublishedTenantAgentId = publishedTenantAgent.Id;
                tenantAgent.PublishedTenantAgent = publishedTenantAgent;

                // Explicitly stamp LastUpdatedOn even when the mapped fields produce no actual value
                // differences (republishing an agent unchanged since its last publish). Without this, EF's
                // change tracker sees the entity as Unchanged, so neither
                // MasterDataDbContext.SaveChangesAsync's own bookkeeping loop nor AppendAuditLogEntries (whose
                // ResolveAuditAction returns null for the Unchanged state) ever runs for it — a deliberate
                // republish action would leave zero trace, no timestamp bump and no audit row. Forcing this
                // property into the Modified set fixes both: the timestamp updates, and the audit interceptor
                // now records an Update action (Changes will likely be null, since LastUpdatedOn itself is a
                // bookkeeping field excluded from the diff — that's fine, the row existing is what matters).
                // Same fix as PublishTenantToolCommandHandler.
                publishedTenantAgent.LastUpdatedOn = _dateTimeProvider.DateTimeOffsetUtcNow();
            }
            else
            {
                // Create new published agent
                publishedTenantAgent = _mapper.Map<PublishedTenantAgent>(tenantAgent);
                publishedTenantAgent.CreatedByTenantAgentId = command.Id;
                publishedTenantAgent.Id = 0; // Reset ID to create new entity

                var tenant = await TryGetTenantAsync(_tenantContext.TenantName);
                publishedTenantAgent.Tenant = tenant;

                tenantAgent.PublishedTenantAgent = publishedTenantAgent;

                await _db.PublishedTenantAgents.AddAsync(publishedTenantAgent, cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);

            // On first publish, publishedTenantAgent.Id doesn't exist yet until the save above — and
            // TenantAgent.PublishedTenantAgentId isn't wired to any EF relationship (TenantAgent's own
            // PublishedTenantAgent nav is bound to PublishedTenantAgent.CreatedByTenantAgentId instead),
            // so it must be set explicitly here rather than relying on relationship fixup.
            if (existingPublishedAgent == null)
            {
                tenantAgent.PublishedTenantAgentId = publishedTenantAgent.Id;
                await _db.SaveChangesAsync(cancellationToken);
            }

            // Sync tags from TenantAgent to PublishedTenantAgent
            var agentTags = await _db.EntityTags
                .Include(m => m.Tag)
                .Where(m => m.EntityType == CommonConsts.TaggableEntityTypes.Agent && m.EntityId == command.Id)
                .ToListAsync(cancellationToken);

            var existingPublishedAgentTags = await _db.EntityTags
                .Where(m => m.EntityType == CommonConsts.TaggableEntityTypes.Agent && m.EntityId == publishedTenantAgent.Id)
                .ToListAsync(cancellationToken);

            if (existingPublishedAgentTags.Any())
                _db.EntityTags.RemoveRange(existingPublishedAgentTags);

            if (agentTags.Any())
            {
                await _db.EntityTags.AddRangeAsync(
                    agentTags.Select(et => new EntityTag { TagId = et.TagId, EntityType = CommonConsts.TaggableEntityTypes.Agent, EntityId = publishedTenantAgent.Id }),
                    cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);

            // Sync the spin-up allowlist from TenantAgent to PublishedTenantAgent — drop any target that
            // hasn't itself been published, since a public/share-id caller shouldn't reach a tenant-only draft agent.
            var agentAllowedSpinUpAgents = await _db.AgentAllowedSpinUpAgents
                .AsNoTracking()
                .Where(m => m.AgentId == command.Id)
                .ToListAsync(cancellationToken);

            var existingPublishedAllowedSpinUpAgents = await _db.AgentAllowedSpinUpAgents
                .Where(m => m.AgentId == publishedTenantAgent.Id)
                .ToListAsync(cancellationToken);

            if (existingPublishedAllowedSpinUpAgents.Any())
                _db.AgentAllowedSpinUpAgents.RemoveRange(existingPublishedAllowedSpinUpAgents);

            if (agentAllowedSpinUpAgents.Any())
            {
                var allowedTenantAgentIds = agentAllowedSpinUpAgents.Select(m => m.AllowedSpinUpAgentId).ToList();

                var allowedTargetPublishedIds = await _db.TenantAgents
                    .Where(m => allowedTenantAgentIds.Contains(m.Id) && m.PublishedTenantAgentId != null)
                    .Select(m => m.PublishedTenantAgentId.Value)
                    .ToListAsync(cancellationToken);

                if (allowedTargetPublishedIds.Any())
                {
                    await _db.AgentAllowedSpinUpAgents.AddRangeAsync(
                        allowedTargetPublishedIds.Select(publishedTargetId => new AgentAllowedSpinUpAgent
                        {
                            AgentId = publishedTenantAgent.Id,
                            AllowedSpinUpAgentId = publishedTargetId
                        }),
                        cancellationToken);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);

            // Publish all tenant tools associated with this agent using mediator
            if (tenantAgent.AgentTools.Any())
            {
                var tenantToolsToPublish = tenantAgent.AgentTools
                    .Where(at => at.Tool is TenantTool)
                    .Select(at => at.Tool)
                    .Cast<TenantTool>()
                    .ToList();

                if (tenantToolsToPublish.Any())
                {
                    // Remove existing agent-tool relationships for this published agent
                    var existingAgentTools = await _db.AgentTools
                        .Where(at => at.AgentId == publishedTenantAgent.Id)
                        .ToListAsync(cancellationToken);

                    _db.AgentTools.RemoveRange(existingAgentTools);
                    await _db.SaveChangesAsync(cancellationToken);

                    var newAgentTools = new List<AgentTool>();
                    var systemPrompt = tenantAgent.SystemPrompt;
                    foreach (var tenantTool in tenantToolsToPublish)
                    {
                        var publishToolCommand = new PublishTenantToolCommand { Id = tenantTool.Id };
                        var publishResult = await _mediator.Send(publishToolCommand, cancellationToken);

                        if (publishResult.ResultType == ResultType.Ok && publishResult is SuccessResult<TenantToolPublishResult> successPublishToolResult)
                        {
                            // WI 7575: the published agent must enforce the same tool permissions as the draft.
                            var sourceAgentTool = tenantAgent.AgentTools.First(at => at.ToolId == tenantTool.Id);
                            newAgentTools.Add(new AgentTool
                            {
                                AgentId = publishedTenantAgent.Id,
                                ToolId = successPublishToolResult.Data.Id,
                                ToolPermission = sourceAgentTool.ToolPermission,
                                FunctionPermissions = sourceAgentTool.FunctionPermissions
                            });

                            var pattern = $@"\{{{{tool:{tenantTool.Id}:.*?\}}}}";
                            systemPrompt = System.Text.RegularExpressions.Regex.Replace(systemPrompt, pattern, $"{{{{tool:{successPublishToolResult.Data.Id}:{successPublishToolResult.Data.Name}}}}}");
                        }
                    }

                    publishedTenantAgent.SystemPrompt = systemPrompt;

                    // Create agent-tool relationships
                    if (newAgentTools.Any())
                    {
                        await _db.AgentTools.AddRangeAsync(newAgentTools, cancellationToken);
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            return new SuccessResult<TenantAgentPublishResult>(_mapper.Map<TenantAgentPublishResult>(publishedTenantAgent));
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