using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Services;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.StartToolMcpOAuth
{
    public class StartToolMcpOAuthCommandHandler : IRequestHandler<StartToolMcpOAuthCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;
        private readonly IMcpOAuthConsentService _consentService;
        private readonly McpOAuthSettings _settings;
        private readonly ILogger<StartToolMcpOAuthCommandHandler> _logger;

        public StartToolMcpOAuthCommandHandler(
            IMasterDataDbContext db,
            TenantContext tenantContext,
            UserContext userContext,
            IMcpOAuthConsentService consentService,
            IOptions<McpOAuthSettings> settings,
            ILogger<StartToolMcpOAuthCommandHandler> logger)
        {
            _db = db;
            _tenantContext = tenantContext;
            _userContext = userContext;
            _consentService = consentService;
            _settings = settings.Value;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(StartToolMcpOAuthCommand command, CancellationToken cancellationToken)
        {
            // Every OAuth2 MCP tool is provisioned with its McpOAuthConnectionId (and matching RedirectUri)
            // as soon as it's saved — see McpOAuthConnectionIdProvisioner, called from
            // CreateTenantToolCommandHandler/UpdateTenantToolCommandHandler. So this lookup will only ever
            // find a tool whose connection id already matches command.Id.
            var tool = await _db.TenantTools
                .Where(m => m.McpOAuthConnectionId == command.Id && m.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (tool == null)
                return new NotFoundResult(nameof(Tool), ErrorConst.ToolNotFound);

            // Defensive fallback for any tool that predates eager provisioning — normally a no-op here.
            if (McpOAuthConnectionIdProvisioner.EnsureProvisioned(tool, _settings))
                await _db.SaveChangesAsync(cancellationToken);

            var serverConfig = TryDeserializeOAuthConfig(tool);
            if (serverConfig == null)
                return new InvalidResult(nameof(Tool.Config), ErrorConst.ToolMcpOAuthNotConfigured);

            serverConfig.Name = tool.Name;

            try
            {
                serverConfig.Validate();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid MCP OAuth config for tool {ToolId}", tool.Id);
                return new InvalidResult(nameof(Tool.Config), ErrorConst.ToolMcpOAuthNotConfigured);
            }

            try
            {
                // Shared-vs-per-user is a fixed, mutually exclusive choice per tool (see
                // McpServerConfig.OAuthScope): a PerUser tool (e.g. Canva) stores a token keyed to this real,
                // authenticated caller (UserContext.Guid) rather than one shared by the whole tenant. Never
                // resolved from the OAuth callback itself — see CompleteToolMcpOAuthCommand's remarks — only
                // ever from here. Resolved inside this try (not before it): this endpoint is [Authorize]-gated
                // so UserContext.Guid should always be resolvable, but ResolveRequestingUserId throws rather
                // than silently returning null if it somehow isn't — see its remarks for why a raw Guid.Parse
                // here would be unsafe, not just liable to a raw 500.
                var requestingUserId = McpOAuthScopeResolver.ResolveRequestingUserId(tool.Config, _userContext.Guid);
                var authorizationUri = await _consentService.BeginAsync(serverConfig, tool.McpOAuthConnectionId.Value, requestingUserId, cancellationToken);
                return new SuccessResult<ToolMcpOAuthStartResult>(new ToolMcpOAuthStartResult
                {
                    AuthorizationUrl = authorizationUri.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start MCP OAuth consent for tool {ToolId}", tool.Id);
                return new InvalidResult(nameof(Tool), ErrorConst.ToolMcpOAuthConsentFailed);
            }
        }

        private McpServerConfig TryDeserializeOAuthConfig(TenantTool tool)
        {
            if (tool.Type != CommonToolTypeConsts.MCP || string.IsNullOrWhiteSpace(tool.Config))
                return null;

            McpServerConfig config;
            try
            {
                config = JsonConvert.DeserializeObject<McpServerConfig>(tool.Config);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse MCP config for tool {ToolId}", tool.Id);
                return null;
            }

            return config?.AuthType == McpAuthType.OAuth2 ? config : null;
        }
    }
}
