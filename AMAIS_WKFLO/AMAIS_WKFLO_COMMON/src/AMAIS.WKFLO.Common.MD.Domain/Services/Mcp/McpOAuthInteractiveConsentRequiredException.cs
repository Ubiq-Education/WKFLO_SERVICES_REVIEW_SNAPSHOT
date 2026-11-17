namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// Thrown by <c>McpClientService.BuildOAuthOptions</c>'s <c>AuthorizationCallbackHandler</c> — the
    /// silent/background OAuth path used whenever chat-time tool loading connects to an OAuth2-authenticated
    /// MCP server — when no cached/refreshable token is available and the SDK would otherwise need to drive a
    /// human through interactive consent. That path has no browser to drive consent through, so it throws
    /// instead of hanging or falling back to the SDK's console-based flow.
    /// </summary>
    /// <remarks>
    /// Deliberately a distinct type (replacing what used to be a plain <see cref="InvalidOperationException"/>)
    /// so callers can react specifically instead of treating it like any other connection failure:
    /// <list type="bullet">
    /// <item><description><c>McpClientService.CreateClientWithRetryAsync</c> must never retry it — retrying
    /// can never turn "needs interactive consent" into a success, so it gets the same early-exit treatment
    /// <see cref="OperationCanceledException"/> already gets.</description></item>
    /// <item><description><c>McpClientService.ConnectAsync</c> lets it propagate (rather than swallowing it
    /// into <c>return false</c> like every other exception) so its identity survives up to the caller.</description></item>
    /// <item><description><c>SemanticKernelExtensions.AddWkflowMcpToolAsync</c> catches it specifically to
    /// notify the chat user with a reconnect link (public widget sessions) or a fallback message, instead of
    /// silently failing to load the tool.</description></item>
    /// </list>
    /// </remarks>
    public sealed class McpOAuthInteractiveConsentRequiredException : Exception
    {
        public McpOAuthInteractiveConsentRequiredException(string message)
            : base(message)
        {
        }
    }
}
