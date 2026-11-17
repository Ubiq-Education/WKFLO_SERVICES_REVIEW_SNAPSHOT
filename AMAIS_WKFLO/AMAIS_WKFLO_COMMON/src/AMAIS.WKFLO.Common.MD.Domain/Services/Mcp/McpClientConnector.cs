using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.Net;
using System.Runtime.ExceptionServices;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// The single place every caller creates an <see cref="McpClient"/> (MD API's post-consent handshake check,
    /// AI API's chat-time tool loading and function discovery).
    /// </summary>
    /// <remarks>
    /// With <see cref="McpClientOptions.ProtocolVersion"/> left null, ModelContextProtocol 2.2.0 prefers the
    /// 2026-07-28 revision and first probes the server with <c>server/discover</c>, falling back to the
    /// <c>initialize</c> handshake only when the server drops or rejects that method as a JSON-RPC error. Some
    /// servers instead fail the probe at the HTTP level — Canva's MCP endpoint answers it with HTTP 500 — and
    /// the SDK then gives up without falling back. Pinning <c>ProtocolVersion</c> skips the probe, but a pinned
    /// version is also the minimum the client accepts, so a single pin can't serve every server. This connector
    /// keeps the SDK default first and, only after an HTTP-status failure, retries on a fresh transport pinned
    /// to each initialize-handshake version in <see cref="FallbackProtocolVersions"/>, newest first.
    /// </remarks>
    public static class McpClientConnector
    {
        /// <summary>Initialize-handshake protocol versions tried, in order, after the default attempt fails.</summary>
        public static readonly IReadOnlyList<string> FallbackProtocolVersions = ["2025-11-25", "2025-06-18", "2025-03-26"];

        /// <summary>
        /// Connects using <see cref="McpClient.CreateAsync(IClientTransport, McpClientOptions, ILoggerFactory, CancellationToken)"/>.
        /// <paramref name="transportFactory"/> is called once per attempt — a transport isn't reusable after a
        /// failed connection.
        /// </summary>
        public static Task<McpClient> ConnectAsync(
            Func<IClientTransport> transportFactory,
            McpClientOptions options,
            ILogger logger,
            string serverName,
            CancellationToken cancellationToken = default) =>
            ConnectAsync(transportFactory, options, logger, serverName,
                (transport, attemptOptions, ct) => McpClient.CreateAsync(transport, attemptOptions, cancellationToken: ct),
                cancellationToken);

        /// <summary>Same as the overload above, with the client creation step injectable for tests.</summary>
        public static async Task<McpClient> ConnectAsync(
            Func<IClientTransport> transportFactory,
            McpClientOptions options,
            ILogger logger,
            string serverName,
            Func<IClientTransport, McpClientOptions, CancellationToken, Task<McpClient>> createClientAsync,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(transportFactory);
            ArgumentNullException.ThrowIfNull(createClientAsync);

            Exception lastError;
            try
            {
                return await createClientAsync(transportFactory(), options, cancellationToken);
            }
            catch (Exception ex) when (options?.ProtocolVersion == null && IsRetryableHttpFailure(ex, cancellationToken))
            {
                // Only when the caller left the version to the SDK — an explicit pin is the caller's decision.
                lastError = ex;
                logger?.LogInformation(
                    "MCP server {ServerName} failed the default protocol negotiation with HTTP {StatusCode}; retrying with pinned protocol versions",
                    serverName, FindHttpStatus(ex));
            }

            foreach (var version in FallbackProtocolVersions)
            {
                try
                {
                    var client = await createClientAsync(transportFactory(), WithProtocolVersion(options, version), cancellationToken);
                    logger?.LogInformation("Connected to MCP server {ServerName} with pinned protocol version {ProtocolVersion}", serverName, version);
                    return client;
                }
                catch (Exception ex) when (IsRetryableHttpFailure(ex, cancellationToken) || IsVersionNegotiationFailure(ex, cancellationToken))
                {
                    lastError = ex;
                }
            }

            ExceptionDispatchInfo.Capture(lastError).Throw();
            throw lastError; // unreachable
        }

        /// <summary>
        /// An HTTP error status anywhere in the exception chain, except 401/403 (an auth problem a different
        /// protocol version can't fix). Cancellation and connection-level failures (no status) are never retried.
        /// </summary>
        internal static bool IsRetryableHttpFailure(Exception ex, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || ex is OperationCanceledException || ex is McpOAuthInteractiveConsentRequiredException)
                return false;

            var status = FindHttpStatus(ex);
            return status.HasValue && (int)status.Value >= 400 &&
                   status.Value != HttpStatusCode.Unauthorized && status.Value != HttpStatusCode.Forbidden;
        }

        // A pinned version the server negotiates away from fails with a plain McpException (no HTTP status);
        // that's exactly when the next, older version is worth trying.
        private static bool IsVersionNegotiationFailure(Exception ex, CancellationToken cancellationToken) =>
            !cancellationToken.IsCancellationRequested && ex is ModelContextProtocol.McpException && FindHttpStatus(ex) == null;

        private static HttpStatusCode? FindHttpStatus(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is HttpRequestException { StatusCode: not null } http)
                    return http.StatusCode;
            }

            return null;
        }

        private static McpClientOptions WithProtocolVersion(McpClientOptions options, string protocolVersion) =>
            options == null
                ? new McpClientOptions { ProtocolVersion = protocolVersion }
                : new McpClientOptions
                {
                    Capabilities = options.Capabilities,
                    ClientInfo = options.ClientInfo,
                    DiscoverProbeTimeout = options.DiscoverProbeTimeout,
                    Handlers = options.Handlers,
                    InitializationTimeout = options.InitializationTimeout,
                    InitializeMeta = options.InitializeMeta,
                    ProtocolVersion = protocolVersion
                };
    }
}
