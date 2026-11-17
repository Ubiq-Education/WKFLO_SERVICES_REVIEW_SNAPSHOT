using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.Application.Tools.Commands.DiscoverTenantToolFunctions
{
    public class DiscoverTenantToolFunctionsCommandHandler : IRequestHandler<DiscoverTenantToolFunctionsCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IMcpClientService _mcpService;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<DiscoverTenantToolFunctionsCommandHandler> _logger;

        public DiscoverTenantToolFunctionsCommandHandler(IMasterDataDbContext db, IMcpClientService mcpService, TenantContext tenantContext,
            UserContext userContext, IDateTimeProvider dateTimeProvider, ILogger<DiscoverTenantToolFunctionsCommandHandler> logger)
        {
            _db = db;
            _mcpService = mcpService;
            _tenantContext = tenantContext;
            _userContext = userContext;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(DiscoverTenantToolFunctionsCommand command, CancellationToken cancellationToken)
        {
            var tool = await _db.TenantTools
                .Where(m => m.Id == command.Id && m.Tenant.Name == _tenantContext.TenantName && !m.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (tool == null)
                return new NotFoundResult(nameof(TenantTool), ErrorConst.ToolNotFound);

            if (tool.Type != CommonToolTypeConsts.MCP)
                return new InvalidResult(nameof(Tool.Type), ErrorConst.ToolMustBeMcp);

            McpServerConfig serverConfig;
            try
            {
                serverConfig = JsonConvert.DeserializeObject<McpServerConfig>(tool.Config);
            }
            catch (JsonException)
            {
                serverConfig = null;
            }

            if (serverConfig == null)
                return new InvalidResult(nameof(Tool.Config), ErrorConst.ToolMcpUnavailable);

            // OAuth2 tools must have been connected first (MD API tools/{id}/mcp/oauth/*).
            if (serverConfig.AuthType == McpAuthType.OAuth2 && !tool.McpOAuthConnectionId.HasValue)
                return new InvalidResult(nameof(Tool), ErrorConst.ToolMcpOAuthNotConnected);

            // A dedicated registry key so this one-off connection never collides with a chat-time one.
            serverConfig.Id = $"discover-{tool.Id}-{Guid.NewGuid():N}";
            serverConfig.Name = tool.Name;

            IList<McpClientTool> functions;
            try
            {
                var requestingUserId = McpOAuthScopeResolver.ResolveRequestingUserId(tool.Config, _userContext?.Guid);
                _mcpService.AddServerConfig(serverConfig, tool.McpOAuthConnectionId, requestingUserId);

                if (!await _mcpService.ConnectAsync(serverConfig.Id, cancellationToken))
                    return new InvalidResult(nameof(Tool), ErrorConst.ToolMcpUnavailable);

                functions = await _mcpService.ListToolsAsync(serverConfig.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is McpOAuthInteractiveConsentRequiredException or McpOAuthPerUserIdentityUnavailableException)
            {
                // The caller (for a per-user tool: this admin) has no usable OAuth connection to this server.
                return new InvalidResult(nameof(Tool), ErrorConst.ToolMcpOAuthNotConnected);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Discovering the functions of MCP tool {ToolId} failed", tool.Id);
                return new InvalidResult(nameof(Tool), ErrorConst.ToolMcpUnavailable);
            }
            finally
            {
                await DisconnectQuietlyAsync(serverConfig.Id);
            }

            var fresh = functions.Select(f => new McpFunctionDescriptor
            {
                Name = f.Name,
                Description = f.Description,
                InputSchema = ParseSchema(f.JsonSchema)
            });

            var snapshot = ToolFunctionsBuilder.MergeSnapshot(
                ToolPermissionJson.ParseDiscoveredFunctions(tool.DiscoveredFunctions),
                fresh,
                ToolPermissionJson.ParseEnabledFunctions(tool.EnabledFunctions));

            tool.DiscoveredFunctions = ToolPermissionJson.SerializeDiscoveredFunctions(snapshot);
            tool.FunctionsDiscoveredOn = _dateTimeProvider.DateTimeOffsetUtcNow();

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<ToolFunctionsResult>(ToolFunctionsBuilder.Build(tool));
        }

        private static JToken ParseSchema(System.Text.Json.JsonElement schema)
        {
            try
            {
                return schema.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null
                    ? null
                    : JToken.Parse(schema.GetRawText());
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task DisconnectQuietlyAsync(string serverId)
        {
            try
            {
                await _mcpService.DisconnectAsync(serverId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Disconnecting discovery connection {ServerId} failed", serverId);
            }
        }
    }
}
