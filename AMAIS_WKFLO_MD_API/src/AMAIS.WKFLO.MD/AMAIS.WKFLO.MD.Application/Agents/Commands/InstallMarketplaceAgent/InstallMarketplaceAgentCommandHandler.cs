using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Events.AgentInstalled;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.InstallMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.InstallMarketplaceAgent
{
    public class InstallMarketplaceAgentCommandHandler : IRequestHandler<InstallMarketplaceAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly IWkfloMediator _mediator;

        public InstallMarketplaceAgentCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, IWkfloMediator mediator, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _mediator = mediator;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(InstallMarketplaceAgentCommand command, CancellationToken cancellationToken)
        {
            // Get the marketplace agent
            var marketplaceAgent = await _db.MarketplaceAgents
                .Include(m => m.AIModel)
                .Include(m => m.AgentTools)
                    .ThenInclude(at => at.Tool)
                .Where(m => m.Id == command.Id && !m.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (marketplaceAgent == null)
            {
                return new NotFoundResult(nameof(MarketplaceAgent), ErrorConst.AgentNotFound);
            }

            // Get the current tenant
            var tenant = await _db.Tenants
                .Where(t => t.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenant == null)
            {
                return new InvalidResult(nameof(TenantContext.TenantName), ErrorConst.TenantInvalid);
            }

            // Dedupe: if this tenant already has a (non-deleted) TenantAgent installed from this exact
            // MarketplaceAgent, don't create a duplicate row - short-circuit and return the existing one.
            // This is intentionally separate from the name-collision "(Copy N)" loop below, which exists
            // for a genuinely different case: an unrelated agent that happens to share the same name.
            var existingInstalledAgent = await _db.TenantAgents
                .Where(ta => ta.TenantId == tenant.Id &&
                           ta.InstalledFromMarketplaceAgentId == marketplaceAgent.Id &&
                           !ta.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            TenantAgent tenantAgent;
            if (existingInstalledAgent != null)
            {
                tenantAgent = existingInstalledAgent;
            }
            else
            {
                // Use the mapper to create the TenantAgent from the MarketplaceAgent
                tenantAgent = _mapper.Map<TenantAgent>(marketplaceAgent);

                TenantAgent existingSameNameAgent = null;
                int index = 1;
                do
                {
                    // Check if some OTHER (unrelated) agent already occupies this name for this tenant
                    existingSameNameAgent = await _db.TenantAgents
                        .Where(ta => ta.TenantId == tenant.Id &&
                                   ta.Name == tenantAgent.Name &&
                                   !ta.IsDeleted)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (existingSameNameAgent != null)
                    {
                        tenantAgent.Name = marketplaceAgent.Name + $" (Copy {index})";
                        index++;
                    }
                } while (existingSameNameAgent != null);

                // Configure TenantAgent specific properties
                tenantAgent.Id = 0; // So a new Id is generated
                tenantAgent.TenantId = tenant.Id;
                tenantAgent.Tenant = tenant;
                tenantAgent.IsActive = true; // Active by default when installing
                tenantAgent.EnableSTM = marketplaceAgent.EnableSTM; // Copy the template's configured default
                tenantAgent.EnableLTM = false; // Configure according to needs
                tenantAgent.EnableSpinUpAgents = false; // Configure according to needs
                tenantAgent.LastRunAt = null;
                tenantAgent.MaxToolCallsPerTurn = 3;
                tenantAgent.InstalledFromMarketplaceAgentId = marketplaceAgent.Id;
                tenantAgent.AgentTools = new List<AgentTool>(); // Will be handled separately

                // Add the TenantAgent to the database
                await _db.TenantAgents.AddAsync(tenantAgent, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);

                // Install all market tenant tools associated with this agent using mediator
                // WI 7575: installed tool id -> the marketplace agent's link, so its tool permissions carry over.
                var installedTools = new List<(int ToolId, AgentTool Source)>();
                var systemPrompt = marketplaceAgent.SystemPrompt;
                if (marketplaceAgent.AgentTools.Any())
                {
                    var marketplaceToolsToInstall = marketplaceAgent.AgentTools
                        .Where(at => at.Tool is MarketplaceTool)
                        .Select(at => at.Tool)
                        .Cast<MarketplaceTool>()
                        .ToList();

                    foreach (var marketplaceTool in marketplaceToolsToInstall)
                    {
                        var installMarketplaceToolCommand = new InstallMarketplaceToolCommand { Id = marketplaceTool.Id };
                        var installResult = await _mediator.Send(installMarketplaceToolCommand, cancellationToken);

                        if (installResult.ResultType == ResultType.Ok && installResult is SuccessResult<TenantToolCreateResult> successInstallToolResult)
                        {
                            installedTools.Add((successInstallToolResult.Data.Id, marketplaceAgent.AgentTools.First(at => at.ToolId == marketplaceTool.Id)));
                            var pattern = $@"\{{{{tool:{marketplaceTool.Id}:.*?\}}}}";
                            systemPrompt = System.Text.RegularExpressions.Regex.Replace(systemPrompt, pattern, $"{{{{tool:{successInstallToolResult.Data.Id}:{successInstallToolResult.Data.Name}}}}}");
                        }
                    }

                    tenantAgent.SystemPrompt = systemPrompt;
                }

                var newAgentTools = new List<AgentTool>();

                foreach (var (toolId, source) in installedTools)
                {
                    newAgentTools.Add(new AgentTool
                    {
                        AgentId = tenantAgent.Id,
                        ToolId = toolId,
                        ToolPermission = source.ToolPermission,
                        FunctionPermissions = source.FunctionPermissions
                    });
                }

                if (newAgentTools.Any())
                {
                    await _db.AgentTools.AddRangeAsync(newAgentTools, cancellationToken);
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

            // Compute the live installation count (COUNT of non-deleted TenantAgents installed from this
            // MarketplaceAgent) rather than reading a stored counter - MarketplaceAgent.Installations no
            // longer exists as a persisted column.
            var totalInstallations = await _db.TenantAgents
                .CountAsync(ta => ta.InstalledFromMarketplaceAgentId == marketplaceAgent.Id && !ta.IsDeleted, cancellationToken);

            // Publish domain event for agent installation. This fires on EVERY successful install ACTION
            // from the calling user's perspective - whether it created a brand new TenantAgent or
            // short-circuited to an already-installed one - because UserInstall bookkeeping is per-user,
            // not per-tenant-agent: a different staff member at the same tenant clicking "install" on
            // something their tenant already has should still get their own UserInstall row.
            await _mediator.Publish(new MarketplaceAgentInstalledDomainEvent(
                marketplaceAgent.Id,
                totalInstallations,
                1
            ), cancellationToken);

            // Return the result
            return new SuccessResult<TenantAgentCreateResult>(_mapper.Map<TenantAgentCreateResult>(tenantAgent));
        }
    }
}