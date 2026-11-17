using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Tools.Services
{
    /// <summary>
    /// Ensures an OAuth2 MCP tool has its <see cref="RunableTool.McpOAuthConnectionId"/> (and the matching
    /// <see cref="McpServerConfig.RedirectUri"/>) provisioned before anyone ever tries to connect it — every
    /// MCP OAuth endpoint (start/status/disconnect/callback) is keyed by this id, so it must exist from the
    /// moment the tool is saved, not lazily on first connect. Called from
    /// <c>CreateTenantToolCommandHandler</c>/<c>UpdateTenantToolCommandHandler</c> (the normal path) and
    /// defensively from <c>StartToolMcpOAuthCommandHandler</c> (covers any tool that predates this).
    /// </summary>
    public static class McpOAuthConnectionIdProvisioner
    {
        /// <summary>No-op for anything that isn't an OAuth2 MCP tool, or that's already fully provisioned.
        /// Returns whether <paramref name="tool"/> was changed (caller is responsible for persisting it).</summary>
        public static bool EnsureProvisioned(RunableTool tool, McpOAuthSettings settings)
        {
            if (tool.Type != CommonToolTypeConsts.MCP || string.IsNullOrWhiteSpace(tool.Config))
                return false;

            McpServerConfig config;
            try
            {
                config = JsonConvert.DeserializeObject<McpServerConfig>(tool.Config);
            }
            catch (JsonException)
            {
                return false;
            }

            if (config?.AuthType != McpAuthType.OAuth2)
                return false;

            var changed = false;

            if (tool.McpOAuthConnectionId == null)
            {
                tool.McpOAuthConnectionId = Guid.NewGuid();
                changed = true;
            }

            // Always kept in sync with the current expected value, not just backfilled when blank — there's
            // no legitimate reason for it to ever point anywhere but our own callback route, and a stale
            // value left over from an old URL shape (e.g. the old int-Tool.Id or connection-id-in-path route
            // shapes) would otherwise silently keep pointing at a URL that no longer resolves, breaking every
            // future consent attempt with a 400 from the callback endpoint. The callback route itself is a
            // single static URL shared by every connection — see McpOAuthCallbackUrlBuilder's remarks — so
            // this is the same value for every tool; it's still recomputed per-tool (rather than hardcoded
            // inline) so a change to McpOAuthSettings.PublicBaseUrl self-heals on the next save.
            var expectedRedirectUri = McpOAuthCallbackUrlBuilder.Build(settings);
            if (config.RedirectUri != expectedRedirectUri)
            {
                config.RedirectUri = expectedRedirectUri;
                changed = true;
            }

            if (changed)
                tool.Config = JsonConvert.SerializeObject(config);

            return changed;
        }
    }
}
