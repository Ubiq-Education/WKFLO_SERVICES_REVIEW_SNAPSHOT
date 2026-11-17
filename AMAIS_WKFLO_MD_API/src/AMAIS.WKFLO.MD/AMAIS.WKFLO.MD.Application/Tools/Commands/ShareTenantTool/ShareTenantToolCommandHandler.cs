using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.ShareTenantTool
{
    public class ShareTenantToolCommandHandler : IRequestHandler<ShareTenantToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly IDateTimeProvider _dateTimeProvider;

        public ShareTenantToolCommandHandler(
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

        public async ValueTask<Result> Handle(ShareTenantToolCommand command, CancellationToken cancellationToken)
        {
            // STEP 1: Get the TenantTool first (command.Id refers to TenantTool)
            var tenantTool = await _db.TenantTools
                .Include(t => t.Tenant)
                .Where(t => t.Id == command.Id &&
                           t.Tenant.Name == _tenantContext.TenantName &&
                           !t.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            // STEP 2: Find the PublishedTenantTool that was created from this TenantTool
            var publishedTool = await _db.PublishedTenantTools
                .Include(t => t.Tenant)
                .Include(t => t.CreatedByTenantTool)
                .Where(t => t.CreatedByTenantToolId == command.Id &&
                           t.Tenant.Name == _tenantContext.TenantName &&
                           !t.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (publishedTool == null)
            {
                return new NotFoundResult(nameof(PublishedTenantTool), ErrorConst.ToolShareNotPublished);
            }

            if (tenantTool.LastUpdatedOn > (publishedTool.LastUpdatedOn ?? publishedTool.CreatedOn))
            {
                return new InvalidResult(nameof(PublishedTenantTool), ErrorConst.ToolShareNeedsRepublish);
            }

            // STEP 3: Check if this published tool has already been shared to marketplace
            var existingMarketplaceTool = await _db.MarketplaceTools
                .Where(mt => mt.SourcePublishedToolId == publishedTool.Id &&
                            !mt.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            // STEP 4: Create new marketplace tool using AutoMapper
            MarketplaceTool marketplaceTool;
            if (existingMarketplaceTool != null)
            {
                // Reuse existing marketplace tool
                marketplaceTool = existingMarketplaceTool;
                _mapper.Map(publishedTool, marketplaceTool);
                marketplaceTool.SourcePublishedToolId = publishedTool.Id;
                publishedTool.MarketplaceTool = marketplaceTool;

                // Explicitly stamp LastUpdatedOn even when the mapped fields produce no actual value
                // differences (re-sharing a tool unchanged since its last share). Without this, EF's change
                // tracker sees the entity as Unchanged, so neither MasterDataDbContext.SaveChangesAsync's own
                // bookkeeping loop nor AppendAuditLogEntries (whose ResolveAuditAction returns null for the
                // Unchanged state) ever runs for it — a deliberate re-share action would leave zero trace, no
                // timestamp bump and no audit row. Forcing this property into the Modified set fixes both:
                // the timestamp updates, and the audit interceptor now records an Update action (Changes will
                // likely be null, since LastUpdatedOn itself is a bookkeeping field excluded from the diff —
                // that's fine, the row existing is what matters). Same fix as PublishTenantToolCommandHandler.
                marketplaceTool.LastUpdatedOn = _dateTimeProvider.DateTimeOffsetUtcNow();
            }
            else
            {
                // Create new marketplace tool using AutoMapper
                marketplaceTool = _mapper.Map<MarketplaceTool>(publishedTool);
                // Set the SourcePublishedToolId to track the origin of this tool
                marketplaceTool.SourcePublishedToolId = publishedTool.Id;
                await _db.MarketplaceTools.AddAsync(marketplaceTool, cancellationToken);
                publishedTool.MarketplaceTool = marketplaceTool;
            }

            ClearOAuthSecretsIfApplicable(publishedTool, marketplaceTool);

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TenantToolShareResult>(_mapper.Map<TenantToolShareResult>(marketplaceTool));
        }

        /// <summary>
        /// Sharing copies <see cref="Tool.Config"/> verbatim (via <see cref="IWkfloMapper"/> above) — for an
        /// OAuth2 MCP tool that would leak a plaintext preconfigured <c>ClientId</c>/<c>ClientSecret</c> to
        /// every tenant that installs it from the marketplace, and carry over a <c>RedirectUri</c> that's
        /// meaningless (and wrong) once installed under a different tenant's own Tool. Strips them so the
        /// marketplace copy forces a fresh consent per installing tenant instead. Independent of
        /// <c>InstallMarketplaceToolCommandHandler</c>'s own stripping — <c>CreateMarketplaceToolCommand</c>
        /// can create a <see cref="MarketplaceTool"/> bypassing this handler entirely, so Install can't
        /// assume this already ran.
        /// </summary>
        private static void ClearOAuthSecretsIfApplicable(PublishedTenantTool publishedTool, MarketplaceTool marketplaceTool)
        {
            if (publishedTool.Type != CommonToolTypeConsts.MCP)
                return;

            McpServerConfig serverConfig;
            try
            {
                serverConfig = JsonConvert.DeserializeObject<McpServerConfig>(marketplaceTool.Config);
            }
            catch (JsonException)
            {
                return;
            }

            // Credentials are stripped whatever the auth type: a stray clientId/clientSecret left on a non-OAuth2
            // config must not travel to other tenants either. Configs with nothing to strip are left untouched.
            if (serverConfig == null ||
                (serverConfig.AuthType != McpAuthType.OAuth2 &&
                 string.IsNullOrEmpty(serverConfig.ClientId) && string.IsNullOrEmpty(serverConfig.ClientSecret)))
                return;

            serverConfig.ClearOAuthSecrets();
            marketplaceTool.Config = JsonConvert.SerializeObject(serverConfig);
        }
    }
}