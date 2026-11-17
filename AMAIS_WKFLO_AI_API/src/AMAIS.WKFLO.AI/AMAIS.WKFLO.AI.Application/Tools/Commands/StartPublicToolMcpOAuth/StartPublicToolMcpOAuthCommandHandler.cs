using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.AI.Application.Tools.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.Application.Tools.Commands.StartPublicToolMcpOAuth
{
    public class StartPublicToolMcpOAuthCommandHandler : IRequestHandler<StartPublicToolMcpOAuthCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly UserContext _userContext;
        private readonly IMcpOAuthConsentService _consentService;
        private readonly ILogger<StartPublicToolMcpOAuthCommandHandler> _logger;

        public StartPublicToolMcpOAuthCommandHandler(
            IMasterDataDbContext db,
            UserContext userContext,
            IMcpOAuthConsentService consentService,
            ILogger<StartPublicToolMcpOAuthCommandHandler> logger)
        {
            _db = db;
            _userContext = userContext;
            _consentService = consentService;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(StartPublicToolMcpOAuthCommand command, CancellationToken cancellationToken)
        {
            // Resolves the agent the exact same way CheckPublicAgentAuthorizationRequirementQueryHandler
            // does (ShareId -> AgentChatSetting -> Agent), then confirms command.Id (the
            // McpOAuthConnectionId) is actually attached to *this* agent before ever reaching
            // BeginAsync — anything else (wrong shareId, a connection id that belongs to some other
            // agent/tenant) is a 404, never a lookup that could hijack someone else's connection.
            var chatSetting = await _db.AgentChatSettings
                .Include(acs => acs.Agent)
                    .ThenInclude(a => a.AgentTools)
                        .ThenInclude(at => at.Tool)
                .Where(acs => acs.ShareId == command.ShareId && !acs.IsDeleted)
                .AsSplitQuery()
                .FirstOrDefaultAsync(cancellationToken);

            if (chatSetting?.Agent == null)
                return new NotFoundResult(nameof(Agent), ErrorConst.AgentNotFound);

            var tool = chatSetting.Agent.AgentTools?
                .Select(at => at.Tool)
                .OfType<RunableTool>()
                .FirstOrDefault(t => t.McpOAuthConnectionId == command.Id);

            if (tool == null)
                return new NotFoundResult(nameof(Tool), ErrorConst.ToolNotFound);

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
                // The AmaisJwtOnly gate on this route guarantees UserContext.Guid is a real, resolvable
                // WKFLO user identity — see WebChatAuthorizationAttribute — so a PerUser-scoped tool (e.g.
                // Canva) can always be keyed to it, unlike the default AmaisJwtOrDatabase share-id-only path.
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

        private McpServerConfig TryDeserializeOAuthConfig(RunableTool tool)
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
