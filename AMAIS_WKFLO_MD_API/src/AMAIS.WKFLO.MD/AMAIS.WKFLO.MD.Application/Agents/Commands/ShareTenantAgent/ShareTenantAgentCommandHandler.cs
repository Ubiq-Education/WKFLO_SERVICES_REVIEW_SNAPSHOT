using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.ShareTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.ShareTenantAgent
{
    public class ShareTenantAgentCommandHandler : IRequestHandler<ShareTenantAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly IWkfloMediator _mediator;
        private readonly IDateTimeProvider _dateTimeProvider;

        public ShareTenantAgentCommandHandler(
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

        public async ValueTask<Result> Handle(ShareTenantAgentCommand command, CancellationToken cancellationToken)
        {
            // Get the TenantAgent first
            var tenantAgent = await _db.TenantAgents
                .Include(a => a.Tenant)
                .Where(a => a.Id == command.Id &&
                           a.Tenant.Name == _tenantContext.TenantName &&
                           !a.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            // Find the PublishedTenantAgent that was created from this TenantAgent
            var publishedAgent = await _db.PublishedTenantAgents
                .Include(a => a.AgentTools)
                .ThenInclude(at => at.Tool)
                .Include(a => a.AIModel)
                .Include(a => a.Tenant)
                .Include(a => a.CreatedByTenantAgent)
                .Where(a => a.CreatedByTenantAgentId == command.Id &&
                           a.Tenant.Name == _tenantContext.TenantName &&
                           !a.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (publishedAgent == null)
            {
                return new NotFoundResult(nameof(PublishedTenantAgent), ErrorConst.AgentShareNotPublished);
            }

            if (tenantAgent.LastUpdatedOn > (publishedAgent.LastUpdatedOn ?? publishedAgent.CreatedOn))
            {
                return new InvalidResult(nameof(PublishedTenantAgent), ErrorConst.AgentShareNeedsRepublish);
            }

            // Check if this published agent has already been shared to marketplace
            var existingMarketplaceAgent = await _db.MarketplaceAgents
                .Where(ma => ma.SourcePublishedAgentId == publishedAgent.Id &&
                            !ma.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            // Create new marketplace agent using AutoMapper
            MarketplaceAgent marketplaceAgent;
            if (existingMarketplaceAgent != null)
            {
                // Reuse existing marketplace agent
                marketplaceAgent = existingMarketplaceAgent;
                _mapper.Map(publishedAgent, existingMarketplaceAgent);
                marketplaceAgent.SourcePublishedAgentId = publishedAgent.Id;
                publishedAgent.MarketplaceAgent = marketplaceAgent;

                // Explicitly stamp LastUpdatedOn even when the mapped fields produce no actual value
                // differences (re-sharing an agent unchanged since its last share). Without this, EF's change
                // tracker sees the entity as Unchanged, so neither MasterDataDbContext.SaveChangesAsync's own
                // bookkeeping loop nor AppendAuditLogEntries (whose ResolveAuditAction returns null for the
                // Unchanged state) ever runs for it — a deliberate re-share action would leave zero trace, no
                // timestamp bump and no audit row. Forcing this property into the Modified set fixes both:
                // the timestamp updates, and the audit interceptor now records an Update action (Changes will
                // likely be null, since LastUpdatedOn itself is a bookkeeping field excluded from the diff —
                // that's fine, the row existing is what matters). Same fix as PublishTenantToolCommandHandler.
                marketplaceAgent.LastUpdatedOn = _dateTimeProvider.DateTimeOffsetUtcNow();
            }
            else
            {
                // Create new marketplace agent
                marketplaceAgent = _mapper.Map<MarketplaceAgent>(publishedAgent);
                // Set the SourcePublishedAgentId to track the origin of this agent
                marketplaceAgent.SourcePublishedAgentId = publishedAgent.Id;
                await _db.MarketplaceAgents.AddAsync(marketplaceAgent, cancellationToken);
                publishedAgent.MarketplaceAgent = marketplaceAgent;
            }

            // Copy the configured chat icon (if any) onto the Marketplace listing, so "Manage AI
            // Agents"-style catalog UIs can render a real icon instead of always falling back to a
            // client-generated initials avatar. AgentChatSetting.ChatbotIconPath is already persisted
            // as a ready-to-use absolute URL — it's set from UploadAgentChatIconCommandHandler's
            // AbsoluteUrl (never RelativePath) via UpdateAgentChatSettingCommand — so no base-URL
            // prefixing is needed here.
            //
            // Keyed by publishedAgent.Id, NOT tenantAgent.Id: verified UpdateAgentChatSettingCommandHandler
            // (the handler backing the only endpoint wkflo_ui's chat-settings panel actually calls —
            // PUT /api/v1/tenant/agent/{id}/chat-setting, confirmed by grepping wkflo_ui for every
            // "chat-setting" call site; the sibling POST .../chat-setting Create endpoint has no caller
            // in wkflo_ui at all) always resolves `publishedAgentId = tenantAgent.PublishedTenantAgent.Id`
            // and reads/writes AgentChatSetting rows keyed to THAT id, never tenantAgent.Id directly — see
            // that handler's own resolution at the top of Handle(). GetTenantAgentChatSettingQueryHandler
            // (the read side) does the same. AgentChatIconHelper.GetChatbotIconUrlAsync (AI API, the real
            // "source of truth" for what icon end users see in the live/public chat widget) is likewise
            // only ever called with a PublishedTenantAgent id in the live chat-run/widget/orchestration
            // paths (SemanticKernelAgentService, RunnableAISyncService, AgentEventWorkflowActivity,
            // InvokeAgentWorkflowActivity all operate on an already-resolved PublishedTenantAgent — the
            // public/widget-facing chat flow only ever reads PublishedTenantAgent rows, confirmed
            // separately). So a row keyed to tenantAgent.Id (the DRAFT id) is never populated by any live
            // write path and never read by any live read path — querying by tenantAgent.Id here, as this
            // code previously did, returns null for every agent actually configured through the product's
            // real save flow. A TenantAgent can have more than one AgentChatSetting row (widget vs.
            // non-widget config), so prefer the widget row, tie-broken by most recently updated — mirrors
            // AgentChatIconHelper.GetChatbotIconUrlAsync's own-agent branch; that helper itself isn't
            // reused here because AI.Application isn't referenced by MD.Application.
            var chatbotIconUrl = await _db.AgentChatSettings
                .AsNoTracking()
                .Where(acs => acs.AgentId == publishedAgent.Id && !acs.IsDeleted)
                .OrderByDescending(acs => acs.IsWidget)
                .ThenByDescending(acs => acs.LastUpdatedOn ?? acs.CreatedOn)
                .Select(acs => acs.ChatbotIconPath)
                .FirstOrDefaultAsync(cancellationToken);

            // publishedAgent.IconUrl (the Agent-level icon, e.g. set directly on a MarketplaceAgent via
            // the edit popup and then propagated — or any other future direct-icon source on the shared
            // Agent TPH base) is the more direct/authoritative per-agent signal, so prefer it over the
            // tenant's chat-widget icon; fall back to the chat-widget icon otherwise. Only assign when at
            // least one source has a value, so a re-share with neither set never regresses an existing
            // marketplaceAgent.IconUrl back to empty (matches the behavior already established above).
            var resolvedIconUrl = !string.IsNullOrWhiteSpace(publishedAgent.IconUrl)
                ? publishedAgent.IconUrl
                : chatbotIconUrl;

            if (!string.IsNullOrWhiteSpace(resolvedIconUrl))
            {
                marketplaceAgent.IconUrl = resolvedIconUrl;
            }

            await _db.SaveChangesAsync(cancellationToken);

            // Use mediator to share published tenant tools
            if (publishedAgent.AgentTools.Any())
            {
                // Filter only PublishedTenantTool tools
                var publishedTools = publishedAgent.AgentTools
                    .Where(at => at.Tool is PublishedTenantTool)
                    .Select(at => (PublishedTenantTool)at.Tool)
                    .Where(pt => pt.CreatedByTenantToolId.HasValue)
                    .ToList();

                if (publishedTools.Any())
                {
                    // Remove existing agent-tool relationships for this published agent
                    var existingAgentTools = await _db.AgentTools
                        .Where(at => at.AgentId == marketplaceAgent.Id)
                        .ToListAsync(cancellationToken);

                    _db.AgentTools.RemoveRange(existingAgentTools);
                    await _db.SaveChangesAsync(cancellationToken);

                    var marketplaceAgentTools = new List<AgentTool>();
                    var systemPrompt = publishedAgent.SystemPrompt;
                    foreach (var publishedTool in publishedTools)
                    {
                        // Use mediator to share each tool using ShareTenantToolCommand
                        var shareToolCommand = new ShareTenantToolCommand { Id = publishedTool.CreatedByTenantToolId.Value };
                        var shareToolResult = await _mediator.Send(shareToolCommand, cancellationToken);

                        if (shareToolResult.ResultType == ResultType.Ok && shareToolResult is SuccessResult<TenantToolShareResult> successShareToolResult)
                        {
                            // Create agent-tool relationship
                            // WI 7575: sharing carries the agent's tool permissions to the marketplace copy.
                            var sourceAgentTool = publishedAgent.AgentTools.First(at => at.ToolId == publishedTool.Id);
                            marketplaceAgentTools.Add(new AgentTool
                            {
                                AgentId = marketplaceAgent.Id,
                                ToolId = successShareToolResult.Data.Id,
                                ToolPermission = sourceAgentTool.ToolPermission,
                                FunctionPermissions = sourceAgentTool.FunctionPermissions
                            });

                            var pattern = $@"\{{{{tool:{publishedTool.Id}:.*?\}}}}";
                            systemPrompt = System.Text.RegularExpressions.Regex.Replace(systemPrompt, pattern, $"{{{{tool:{successShareToolResult.Data.Id}:{successShareToolResult.Data.Name}}}}}");
                        }
                    }

                    marketplaceAgent.SystemPrompt = systemPrompt;

                    // Create agent-tool relationships
                    if (marketplaceAgentTools.Any())
                    {
                        await _db.AgentTools.AddRangeAsync(marketplaceAgentTools, cancellationToken);
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            return new SuccessResult<TenantAgentShareResult>(_mapper.Map<TenantAgentShareResult>(marketplaceAgent));
        }
    }
}