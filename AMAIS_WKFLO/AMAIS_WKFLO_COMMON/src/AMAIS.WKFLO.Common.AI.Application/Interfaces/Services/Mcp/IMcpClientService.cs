using AMAIS.WKFLO.Common.Models.Config;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp
{
    public interface IMcpClientService : IDisposable
    {
        /// <summary>
        /// Registers <paramref name="config"/> under its own <see cref="McpServerConfig.Id"/> — this class's
        /// own multi-server registry key, unrelated to any Tool. <paramref name="mcpOAuthConnectionId"/> is
        /// the owning <c>RunableTool.McpOAuthConnectionId</c>, required only when
        /// <c>config.AuthType == McpAuthType.OAuth2</c> (OAuth token storage/status tracking is keyed by it,
        /// never by <see cref="McpServerConfig.Id"/> or any specific <c>Tool.Id</c>).
        /// <paramref name="userId"/> is the WKFLO user to scope the OAuth2 token to when
        /// <c>config.OAuthScope == McpOAuthScope.PerUser</c> (e.g. Canva); left null for the ordinary
        /// tenant-shared mode (e.g. Humantic) and ignored entirely for non-OAuth2 configs.
        /// </summary>
        void AddServerConfig(McpServerConfig config, Guid? mcpOAuthConnectionId = null, Guid? userId = null);
        Task<IList<McpClientTool>> ListToolsAsync(string serverId, CancellationToken cancellationToken = default);
        Task<CallToolResult> CallToolAsync(string serverId, string toolName, Dictionary<string, object> parameters, CancellationToken cancellationToken = default);
        Task<bool> ConnectAsync(string serverId, CancellationToken cancellationToken = default);
        Task DisconnectAsync(string serverId);
        Task DisconnectAllAsync();
    }
}
