using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.AI.Application.Services.Mcp
{
    public class McpClientService : IMcpClientService, IDisposable
    {
        private readonly ILogger<McpClientService> _logger;
        private readonly IMcpOAuthTokenStore _tokenStore;
        private readonly IMcpTokenProtector _protector;
        private readonly Dictionary<string, McpServerConfig> _serverConfigs = new Dictionary<string, McpServerConfig>();
        // McpServerConfig.Id is purely this class's own multi-server registry key and carries no
        // relationship to a Tool; the owning connection's id (RunableTool.McpOAuthConnectionId, needed only
        // for OAuth2 token storage/status tracking — shared by a TenantTool and its published copy) is
        // tracked separately here instead, keyed the same way.
        private readonly Dictionary<string, Guid?> _connectionIdsByServerId = new Dictionary<string, Guid?>();
        // Only ever meaningful alongside a non-null connection id for an OAuth2 config whose OAuthScope is
        // PerUser (e.g. Canva); null otherwise, including for the ordinary tenant-shared mode (e.g. Humantic).
        private readonly Dictionary<string, Guid?> _userIdsByServerId = new Dictionary<string, Guid?>();
        private readonly Dictionary<string, McpClient> _clients = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public McpClientService(ILogger<McpClientService> logger, IMcpOAuthTokenStore tokenStore, IMcpTokenProtector protector)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        }

        public void AddServerConfig(McpServerConfig config, Guid? mcpOAuthConnectionId = null, Guid? userId = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate();
            _serverConfigs[config.Id] = config;
            _connectionIdsByServerId[config.Id] = mcpOAuthConnectionId;
            _userIdsByServerId[config.Id] = userId;
        }

        public async Task<bool> ConnectAsync(string serverId, CancellationToken cancellationToken = default)
        {
            if (!_serverConfigs.TryGetValue(serverId, out var config))
            {
                _logger.LogError("Server configuration not found for ID: {ServerId}", serverId);
                return false;
            }

            _connectionIdsByServerId.TryGetValue(serverId, out var connectionId);
            _userIdsByServerId.TryGetValue(serverId, out var userId);

            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                if (_clients.ContainsKey(serverId))
                {
                    _logger.LogDebug("Client for server {ServerId} already exists", serverId);
                    return true;
                }

                _logger.LogInformation("Connecting to MCP server {ServerName} ({ServerId})", config.Name, serverId);

                var client = await CreateClientWithRetryAsync(config, connectionId, userId, cancellationToken);
                if (client != null)
                {
                    _clients[serverId] = client;
                    _logger.LogInformation("Successfully connected to MCP server {ServerName}", config.Name);

                    if (config.AuthType == McpAuthType.OAuth2 && connectionId.HasValue)
                        await RecordConnectionResultAsync(connectionId.Value, userId, succeeded: true, error: null, cancellationToken);

                    return true;
                }

                return false;
            }
            catch (McpOAuthInteractiveConsentRequiredException ex)
            {
                // Deliberately NOT swallowed into `return false` like every other exception below: the
                // connection outcome (and, for OAuth2, its status) is still recorded exactly as before, but
                // this specific failure is rethrown so callers (AddWkflowMcpToolAsync) can learn *why* it
                // failed and react — e.g. notify the chat user with a reconnect link — instead of only
                // learning that it did.
                _logger.LogWarning(ex, "MCP server {ServerName} ({ServerId}) needs interactive OAuth consent to reconnect", config.Name, serverId);

                if (config.AuthType == McpAuthType.OAuth2 && connectionId.HasValue)
                    await RecordConnectionResultAsync(connectionId.Value, userId, succeeded: false, error: ex.Message, cancellationToken);

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MCP server {ServerName} ({ServerId})", config.Name, serverId);

                // connectionId can be null here for an OAuth2 config that was never given one — that
                // combination should never happen once callers are correct (BuildOAuthOptions throws a clear
                // exception for it, which is what we're catching); nothing valid to record against in that case.
                if (config.AuthType == McpAuthType.OAuth2 && connectionId.HasValue)
                    await RecordConnectionResultAsync(connectionId.Value, userId, succeeded: false, error: ex.Message, cancellationToken);

                return false;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Skipped for non-OAuth2 tools: they never have a ToolOAuthToken row (RecordConnectionResultAsync
        // would just no-op), and this is the much more common case — no point in the extra DB round-trip.
        private async Task RecordConnectionResultAsync(Guid connectionId, Guid? userId, bool succeeded, string error, CancellationToken cancellationToken)
        {
            try
            {
                await _tokenStore.RecordConnectionResultAsync(connectionId, succeeded, error, userId, cancellationToken);
            }
            catch (Exception ex)
            {
                // Never let a failure to record the outcome mask the connection outcome itself, which the
                // caller already has via this method's own success/failure branch.
                _logger.LogError(ex, "Failed to record MCP OAuth connection result for connection {ConnectionId}", connectionId);
            }
        }

        // internal (not private), with an injectable createClientAsync, purely so tests can exercise the
        // retry/early-exit behavior deterministically (e.g. force McpOAuthInteractiveConsentRequiredException
        // on the very first attempt) without a real MCP server to connect to. Left null (the default), this
        // calls the real SDK exactly as before — no behavior change for any production caller.
        internal async Task<McpClient> CreateClientWithRetryAsync(McpServerConfig config, Guid? connectionId, Guid? userId, CancellationToken cancellationToken,
            Func<IClientTransport, CancellationToken, Task<McpClient>> createClientAsync = null)
        {
            // All connections go through McpClientConnector, which retries with pinned protocol versions when
            // a server fails the SDK's default server/discover probe at the HTTP level (e.g. Canva's HTTP 500).
            Func<IClientTransport, McpClientOptions, CancellationToken, Task<McpClient>> create = createClientAsync == null
                ? (transport, options, ct) => McpClient.CreateAsync(transport, options, cancellationToken: ct)
                : (transport, _, ct) => createClientAsync(transport, ct);

            var maxRetries = config.MaxRetries;
            var baseDelay = TimeSpan.FromSeconds(1);

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogDebug("Attempting to connect to {ServerName} (attempt {Attempt}/{MaxRetries})",
                        config.Name, attempt + 1, maxRetries + 1);

                    using var timeoutCts = new CancellationTokenSource(config.ConnectionTimeout);
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    McpClient client;
                    try
                    {
                        // Bounded by config.ConnectionTimeout as well as the caller's token.
                        client = await McpClientConnector.ConnectAsync(
                            () => CreateTransport(config, connectionId, userId), null, _logger, config.Name, create, combinedCts.Token);
                    }
                    catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        // Our own timeout, not the caller cancelling: surface it as a timeout so it goes through
                        // the normal retry/failure path below instead of being reported as a cancellation.
                        throw new TimeoutException($"Connecting to MCP server '{config.Name}' timed out after {config.ConnectionTimeout}.", ex);
                    }

                    // Test the connection by listing tools
                    // var tools = await client.ListToolsAsync();

                    return client;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Connection attempt cancelled for {ServerName}", config.Name);
                    throw;
                }
                catch (McpOAuthInteractiveConsentRequiredException)
                {
                    // Retrying can never turn "needs interactive consent" into a success — bypass the retry
                    // loop entirely, same early-exit treatment OperationCanceledException gets above.
                    throw;
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                    _logger.LogWarning(ex, "Connection attempt {Attempt} failed for {ServerName}. Retrying in {Delay}ms",
                        attempt + 1, config.Name, delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "All connection attempts failed for {ServerName}", config.Name);
                    throw;
                }
            }

            return null;
        }

        // internal (not private) so McpClientServiceTests (InternalsVisibleTo AMAIS.WKFLO.AI.UnitTests) can
        // exercise the OAuth/static-header/Stdio transport-creation branching directly. connectionId is only
        // ever read when config.AuthType == McpAuthType.OAuth2 (see BuildOAuthOptions); irrelevant otherwise.
        internal IClientTransport CreateTransport(McpServerConfig config, Guid? connectionId = null, Guid? userId = null)
        {
            return config.TransportType switch
            {
                TransportType.Stdio => new StdioClientTransport(
                    new StdioClientTransportOptions
                    {
                        Name = config.Name,
                        Command = config.Command,
                        Arguments = ResolveArguments(config.Arguments),
                        EnvironmentVariables = config.EnvironmentVariables,
                        WorkingDirectory = RepositoryRoot
                    }),
                TransportType.Http => new HttpClientTransport(new HttpClientTransportOptions()
                {
                    Endpoint = new Uri(config.Endpoint),
                    // OAuth2 servers are pinned to StreamableHttp with no standalone GET stream: a stateless
                    // third-party server we don't control (e.g. mcp.humantic.ai) can 405 on the legacy-SSE GET
                    // AutoDetect falls back to, or on the standalone GET stream Streamable HTTP itself opens by
                    // default for server-initiated pushes — every OAuth2 server this connects to only needs
                    // POST-per-request. Non-OAuth2 servers (e.g. AMAIS's own MCP server) keep AutoDetect/default
                    // behavior unchanged.
                    TransportMode = config.AuthType == McpAuthType.OAuth2 ? HttpTransportMode.StreamableHttp : HttpTransportMode.AutoDetect,
                    EnableStandaloneGetStream = config.AuthType != McpAuthType.OAuth2,
                    // A static bearer/API-key header and OAuth2 are mutually exclusive auth mechanisms for a
                    // given server; only wire the one the config actually asks for.
                    AdditionalHeaders = config.AuthType == McpAuthType.OAuth2 ? null : config.AdditionalHeaders,
                    OAuth = config.AuthType == McpAuthType.OAuth2 ? BuildOAuthOptions(config, connectionId, userId) : null,
                    ConnectionTimeout = TimeSpan.FromSeconds(300.0)
                }),
                _ => throw new ArgumentException($"Unsupported transport type: {config.TransportType}")
            };
        }

        /// <summary>
        /// Builds the SDK's OAuth client options for the silent/background connection path used whenever any
        /// caller (chat-time tool loading, reconnects, etc.) connects to an OAuth2-authenticated MCP server.
        /// This path only ever relies on a cached/refreshable token from <see cref="_tokenStore"/> — it can
        /// never drive a human through interactive consent itself (there's no browser here), so
        /// <see cref="ClientOAuthOptions.AuthorizationCallbackHandler"/> is set to a handler that throws
        /// <see cref="McpOAuthInteractiveConsentRequiredException"/> immediately instead. Left unset, the
        /// ModelContextProtocol SDK falls back to its own built-in console-based flow (prints a URL and blocks
        /// on <c>Console.ReadLine()</c>) whenever the cached token can't be silently refreshed — which can
        /// never complete in a deployed process with no attached console, and would hang the request instead
        /// of failing cleanly. This throw is the signal, not the end of the story: <c>ConnectAsync</c>
        /// deliberately rethrows this specific exception type (unlike every other connection failure, which it
        /// swallows into a bare <c>false</c>) so <c>SemanticKernelExtensions.AddWkflowMcpToolAsync</c> can
        /// catch it and notify the chat user with a link to reconnect — see
        /// <c>SendMcpOAuthReconnectNotificationAsync</c>. Only when that can't build a real link (no
        /// resolvable WKFLO identity, or a tenant-authenticated session rather than the public widget) does the
        /// user actually have to fall back to the tenant app's own interactive OAuth consent endpoints.
        /// </summary>
        // internal (not private) so McpClientServiceTests can assert the client secret is decrypted here.
        internal ClientOAuthOptions BuildOAuthOptions(McpServerConfig config, Guid? connectionId, Guid? userId)
        {
            if (!connectionId.HasValue)
                throw new InvalidOperationException(
                    $"MCP server '{config.Id}' is configured for OAuth2 but AddServerConfig was never given the owning connection id. " +
                    "OAuth2 token storage/status tracking is keyed by RunableTool.McpOAuthConnectionId, not McpServerConfig.Id — this should never happen once callers are correct.");

            // Stored encrypted (enc:v1:) since US 7849; legacy plaintext values pass through unchanged.
            var clientSecret = McpClientSecretHelper.Resolve(_protector, config.ClientSecret);

            return new ClientOAuthOptions
            {
                ClientId = string.IsNullOrWhiteSpace(config.ClientId) ? null : config.ClientId,
                ClientSecret = string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret,
                Scopes = config.Scopes,
                RedirectUri = string.IsNullOrWhiteSpace(config.RedirectUri) ? null : new Uri(config.RedirectUri),
                TokenCache = new McpOAuthTokenCacheAdapter(_tokenStore, connectionId.Value, userId),
                AuthorizationCallbackHandler = (context, handlerCancellationToken) => throw new McpOAuthInteractiveConsentRequiredException(
                    $"MCP server '{config.Id}' needs interactive authorization to reconnect, but this background connection path " +
                    "has no browser to drive consent through — the caller (AddWkflowMcpToolAsync) is expected to catch this and " +
                    "notify the chat user with a reconnect link, falling back to the tenant app's MCP OAuth consent endpoints only " +
                    "when no such link can be built for this session.")
            };
        }

        private string[] ResolveArguments(string[] arguments)
        {
            // Resolve any relative paths in arguments to be relative to repository root
            return arguments.Select(arg =>
            {
                // If it's a project path, ensure it's relative to repository root
                if (arg.StartsWith("../") || arg.StartsWith("..\\"))
                {
                    // Convert relative path to absolute, then back to relative from repo root
                    var repoRoot = RepositoryRoot;
                    var currentDir = Path.GetDirectoryName(AppContext.BaseDirectory);
                    var absolutePath = Path.GetFullPath(Path.Combine(currentDir ?? "", arg));
                    return Path.GetRelativePath(repoRoot, absolutePath);
                }
                return arg;
            }).ToArray();
        }

        private static string RepositoryRoot
        {
            get
            {
                // Start from the current directory and search upward for the solution file
                var currentDir = new DirectoryInfo(AppContext.BaseDirectory);

                while (currentDir != null)
                {
                    // Look for .sln file or .git directory
                    if (currentDir.GetFiles("*.sln").Any() ||
                        currentDir.GetDirectories(".git").Any())
                    {
                        return currentDir.FullName;
                    }
                    currentDir = currentDir.Parent;
                }

                // Fallback to current directory
                return Directory.GetCurrentDirectory();
            }
        }

        private static List<string> FindSimilarToolNames(string inputTool, List<string> availableTools)
        {
            var suggestions = new List<string>();
            var inputLower = inputTool.ToLowerInvariant();

            // First, look for exact case-insensitive matches
            var exactMatch = availableTools.FirstOrDefault(t => t.Equals(inputLower, StringComparison.InvariantCultureIgnoreCase));
            if (exactMatch != null && exactMatch != inputTool)
            {
                suggestions.Add(exactMatch);
            }

            // Then look for tools that start with the input (case-insensitive)
            var startsWith = availableTools
                .Where(t => t.ToLowerInvariant().StartsWith(inputLower) && !t.Equals(inputLower, StringComparison.InvariantCultureIgnoreCase))
                .Take(3)
                .ToList();
            suggestions.AddRange(startsWith);

            // Finally, look for tools that contain the input (case-insensitive)
            if (suggestions.Count < 3)
            {
                var contains = availableTools
                    .Where(t => t.ToLowerInvariant().Contains(inputLower) &&
                               !suggestions.Contains(t) &&
                               t.ToLowerInvariant() != inputLower)
                    .Take(3 - suggestions.Count)
                    .ToList();
                suggestions.AddRange(contains);
            }

            return suggestions.Distinct().Take(3).ToList();
        }

        public async Task<IList<McpClientTool>> ListToolsAsync(string serverId, CancellationToken cancellationToken = default)
        {
            var client = await GetClientAsync(serverId, cancellationToken);
            if (client == null)
            {
                _logger.LogError("Client not available for server {ServerId}", serverId);
                return Array.Empty<McpClientTool>();
            }

            try
            {
                _logger.LogDebug("Listing tools for server {ServerId}", serverId);
                var tools = await client.ListToolsAsync();
                _logger.LogInformation("Found {ToolCount} tools for server {ServerId}", tools.Count, serverId);
                return tools;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list tools for server {ServerId}", serverId);
                throw;
            }
        }

        public async Task<CallToolResult> CallToolAsync(
            string serverId,
            string toolName,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            var client = await GetClientAsync(serverId, cancellationToken);
            if (client == null)
            {
                throw new InvalidOperationException($"Client not available for server {serverId}");
            }

            try
            {
                _logger.LogInformation("Calling tool {ToolName} on server {ServerId} with parameters: {Parameters}",
                    toolName, serverId, JsonConvert.SerializeObject(parameters));

                var result = await client.CallToolAsync(toolName, parameters, null);

                _logger.LogInformation("Tool {ToolName} executed successfully on server {ServerId}", toolName, serverId);
                return result;
            }
            catch (ModelContextProtocol.McpException ex) when (ex.Message.Contains("Unknown tool"))
            {
                _logger.LogError(ex, "Failed to call tool {ToolName} on server {ServerId}", toolName, serverId);

                // Try to provide helpful suggestions for similar tool names
                try
                {
                    var availableTools = await client.ListToolsAsync();
                    var suggestions = FindSimilarToolNames(toolName, availableTools.Select(t => t.Name).ToList());

                    if (suggestions.Any())
                    {
                        var suggestionText = string.Join(", ", suggestions);
                        throw new InvalidOperationException($"Unknown tool '{toolName}'. Did you mean: {suggestionText}? (Tool names are case-sensitive)", ex);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unknown tool '{toolName}'. Tool names are case-sensitive. Use 'list {serverId}' to see available tools.", ex);
                    }
                }
                catch (Exception suggestionEx) when (suggestionEx is not InvalidOperationException)
                {
                    _logger.LogWarning(suggestionEx, "Could not generate tool suggestions for {ToolName}", toolName);
                    throw new InvalidOperationException($"Unknown tool '{toolName}'. Tool names are case-sensitive. Use 'list {serverId}' to see available tools.", ex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to call tool {ToolName} on server {ServerId}", toolName, serverId);
                throw;
            }
        }

        private async Task<McpClient> GetClientAsync(string serverId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                if (_clients.TryGetValue(serverId, out var client))
                {
                    return client;
                }

                // Try to connect if not already connected
                var connected = await ConnectAsync(serverId, cancellationToken);
                return connected && _clients.TryGetValue(serverId, out client) ? client : null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task DisconnectAsync(string serverId)
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_clients.TryGetValue(serverId, out var client))
                {
                    _logger.LogInformation("Disconnecting from server {ServerId}", serverId);
                    await client.DisposeAsync();
                    _clients.Remove(serverId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from server {ServerId}", serverId);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task DisconnectAllAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                var disconnectTasks = _clients.Keys.Select(serverId => DisconnectClient(serverId)).ToArray();
                await Task.WhenAll(disconnectTasks);
                _clients.Clear();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task DisconnectClient(string serverId)
        {
            try
            {
                if (_clients.TryGetValue(serverId, out var client))
                {
                    await client.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting client {ServerId}", serverId);
            }
        }

        public void Dispose()
        {
            DisconnectAllAsync().GetAwaiter().GetResult();
            _semaphore.Dispose();
        }
    }
}
