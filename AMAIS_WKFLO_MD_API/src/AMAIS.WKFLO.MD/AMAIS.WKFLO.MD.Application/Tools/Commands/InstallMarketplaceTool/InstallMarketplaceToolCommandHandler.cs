using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Events.ToolInstalled;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.InstallMarketplaceTool
{
    /// <summary>
    /// Handler for installing a marketplace tool into the current tenant
    /// </summary>
    public class InstallMarketplaceToolCommandHandler : IRequestHandler<InstallMarketplaceToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly IWkfloMediator _mediator;

        public InstallMarketplaceToolCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, IWkfloMediator mediator, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _mediator = mediator;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(InstallMarketplaceToolCommand command, CancellationToken cancellationToken)
        {
            // Get the marketplace tool
            var marketplaceTool = await _db.MarketplaceTools
                .Where(m => m.Id == command.Id && !m.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (marketplaceTool == null)
            {
                return new NotFoundResult(nameof(MarketplaceTool), ErrorConst.ToolNotFound);
            }

            // Get the current tenant
            var tenant = await _db.Tenants
                .Where(t => t.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenant == null)
            {
                return new InvalidResult(nameof(TenantContext.TenantName), ErrorConst.TenantInvalid);
            }

            // Dedupe: if this tenant already has a (non-deleted) TenantTool installed from this exact
            // MarketplaceTool, don't create a duplicate row - short-circuit and return the existing one.
            // This is intentionally separate from the name-collision "(Copy N)" loop below, which exists
            // for a genuinely different case: an unrelated tool that happens to share the same name.
            var existingInstalledTool = await _db.TenantTools
                .Where(tt => tt.TenantId == tenant.Id &&
                           tt.InstalledFromMarketplaceToolId == marketplaceTool.Id &&
                           !tt.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            TenantTool tenantTool;
            if (existingInstalledTool != null)
            {
                tenantTool = existingInstalledTool;
            }
            else
            {
                // Use the mapper to create the TenantTool from the MarketplaceTool
                tenantTool = _mapper.Map<TenantTool>(marketplaceTool);

                TenantTool existingSameNameTool = null;
                int index = 1;
                do
                {
                    // Check if some OTHER (unrelated) tool already occupies this name for this tenant
                    existingSameNameTool = await _db.TenantTools
                        .Where(tt => tt.TenantId == tenant.Id &&
                                   tt.Name == tenantTool.Name &&
                                   !tt.IsDeleted)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (existingSameNameTool != null)
                    {
                        tenantTool.Name = marketplaceTool.Name + $" (Copy {index})";
                        index++;
                    }
                } while (existingSameNameTool != null);

                // Configure TenantTool specific properties
                tenantTool.Id = 0; // So a new Id is generated
                tenantTool.TenantId = tenant.Id;
                tenantTool.Tenant = tenant;
                tenantTool.IsActive = true; // Active by default when installing
                tenantTool.InstalledFromMarketplaceToolId = marketplaceTool.Id;

                ClearOAuthSecretsIfApplicable(tenantTool);

                // Add the TenantTool to the database
                await _db.TenantTools.AddAsync(tenantTool, cancellationToken);

                await _db.SaveChangesAsync(cancellationToken);
            }

            // Compute the live installation count (COUNT of non-deleted TenantTools installed from this
            // MarketplaceTool) rather than reading a stored counter - MarketplaceTool.Installations no
            // longer exists as a persisted column.
            var totalInstallations = await _db.TenantTools
                .CountAsync(tt => tt.InstalledFromMarketplaceToolId == marketplaceTool.Id && !tt.IsDeleted, cancellationToken);

            // Publish domain event for tool installation. Fires on EVERY successful install ACTION from
            // the calling user's perspective - see the identical comment in
            // InstallMarketplaceAgentCommandHandler for why the short-circuit path still fires it.
            await _mediator.Publish(new MarketplaceToolInstalledDomainEvent(
                marketplaceTool.Id,
                totalInstallations,
                1
            ), cancellationToken);

            // Return the result
            return new SuccessResult<TenantToolCreateResult>(_mapper.Map<TenantToolCreateResult>(tenantTool));
        }

        /// <summary>
        /// Installing copies <see cref="Tool.Config"/> verbatim (via <see cref="IWkfloMapper"/> above) — for
        /// an OAuth2 MCP tool that would carry a plaintext preconfigured <c>ClientId</c>/<c>ClientSecret</c>
        /// and a <c>RedirectUri</c> pointing at a different tenant's tool into this brand-new
        /// <see cref="TenantTool"/>. Strips them so this fresh install requires its own consent. Independent
        /// of <c>ShareTenantToolCommandHandler</c>'s own stripping — <c>CreateMarketplaceToolCommand</c> can
        /// create a <see cref="MarketplaceTool"/> bypassing Share entirely, so Install can't assume Share
        /// already ran.
        /// </summary>
        private static void ClearOAuthSecretsIfApplicable(TenantTool tenantTool)
        {
            if (tenantTool.Type != CommonToolTypeConsts.MCP)
                return;

            McpServerConfig serverConfig;
            try
            {
                serverConfig = JsonConvert.DeserializeObject<McpServerConfig>(tenantTool.Config);
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
            tenantTool.Config = JsonConvert.SerializeObject(serverConfig);
        }
    }
}