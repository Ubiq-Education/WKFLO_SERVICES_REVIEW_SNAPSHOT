using AMAIS.WKFLO.Common.Models.Config;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// Resolves the effective requesting-user id for an MCP OAuth2 tool's token store operations, from its
    /// configured <see cref="McpServerConfig.OAuthScope"/>. Shared-vs-per-user is a fixed, mutually exclusive
    /// choice per tool (see that property's remarks): a <see cref="McpOAuthScope.Shared"/> tool (e.g.
    /// Humantic) always resolves to null (the one connection-wide row, matching pre-existing behavior); a
    /// <see cref="McpOAuthScope.PerUser"/> tool (e.g. Canva) resolves to the calling user's own id, so every
    /// read/write only ever touches that user's row.
    /// </summary>
    /// <remarks>
    /// Lives in Common (not the MD API's own Application layer) so both the MD API's admin-dashboard OAuth
    /// handlers (<c>StartToolMcpOAuthCommandHandler</c>/<c>GetToolMcpOAuthStatusQueryHandler</c>/
    /// <c>DisconnectToolMcpOAuthCommandHandler</c>) and the AI API's chat-time tool loading
    /// (<c>SemanticKernelExtensions.AddWkflowMcpToolAsync</c>) resolve a tool's requesting user identically,
    /// with no chance of the two APIs drifting out of sync on the logic.
    ///
    /// This is also the one place enforcing that a <c>PerUser</c> tool's token operations can never touch the
    /// <c>Shared</c>-scope row: <c>null</c> is the exact key a <c>Shared</c> row is stored under (see
    /// <c>ToolOAuthTokenConfiguration</c>'s filtered indexes), so a <c>PerUser</c> tool with an unresolvable
    /// identity must never resolve to <c>null</c> too — that would let it silently read/delete whatever
    /// <c>Shared</c> row happens to exist for the same connection, e.g. left over from before the tool was
    /// switched to <c>PerUser</c>. It throws instead, forcing every caller to decide explicitly what "no
    /// identity available" means for it.
    /// </remarks>
    public static class McpOAuthScopeResolver
    {
        /// <param name="toolConfigJson">The tool's raw <c>Tool.Config</c> JSON.</param>
        /// <param name="userContextGuid"><c>UserContext.Guid</c> of the calling user. For the MD API's
        /// <c>[Authorize]</c>-gated OAuth handlers this is always resolvable. For the AI API's chat-time path
        /// it can legitimately be null/empty (e.g. the public chat widget, still blocked on Bug 7163/Task 7164
        /// resolving a real visitor identity).</param>
        /// <exception cref="McpOAuthPerUserIdentityUnavailableException">The tool is <see cref="McpOAuthScope.PerUser"/>
        /// but <paramref name="userContextGuid"/> isn't a parseable id. Never returns <c>null</c> for this
        /// case — see the class remarks for why that would be unsafe.</exception>
        public static Guid? ResolveRequestingUserId(string toolConfigJson, string userContextGuid)
        {
            if (!IsPerUserScope(toolConfigJson))
                return null;

            if (!Guid.TryParse(userContextGuid, out var userId))
                throw new McpOAuthPerUserIdentityUnavailableException();

            return userId;
        }

        private static bool IsPerUserScope(string toolConfigJson)
        {
            if (string.IsNullOrWhiteSpace(toolConfigJson))
                return false;

            try
            {
                var config = JsonConvert.DeserializeObject<McpServerConfig>(toolConfigJson);
                return config?.OAuthScope == McpOAuthScope.PerUser;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
