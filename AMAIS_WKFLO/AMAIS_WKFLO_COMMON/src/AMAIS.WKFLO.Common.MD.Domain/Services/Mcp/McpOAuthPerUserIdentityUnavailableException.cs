namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// Thrown by <see cref="McpOAuthScopeResolver.ResolveRequestingUserId"/> when a tool is configured for
    /// <c>McpOAuthScope.PerUser</c> but no resolvable WKFLO user identity is available for the current
    /// caller (e.g. the public chat widget's <c>UserContext.Guid</c>, still empty until Bug 7163/Task 7164
    /// land). Deliberately a distinct type rather than returning <c>null</c>: <c>null</c> is also the exact
    /// key a <c>Shared</c>-scope row is stored under, so silently returning it here would let "no identity
    /// available" fall through to reading/deleting whatever <c>Shared</c> row happens to exist for the same
    /// connection — a real, different token belonging to a different mode entirely. Every caller must
    /// explicitly decide what "no identity" means for it (skip the token store entirely, report
    /// "not connected", no-op, etc.) rather than have it decided for them by an ambiguous null.
    /// </summary>
    public sealed class McpOAuthPerUserIdentityUnavailableException : Exception
    {
        public McpOAuthPerUserIdentityUnavailableException()
            : base("This MCP tool is configured for per-user OAuth, but no resolvable WKFLO user identity is available for the current caller.")
        {
        }
    }
}
