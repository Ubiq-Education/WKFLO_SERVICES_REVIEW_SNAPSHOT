using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions
{
    /// <summary>
    /// WI 7447/7575: the effective permission of every tool (and MCP function) of one agent for one run,
    /// computed once from the agent's <see cref="AgentTool"/> rows. Rules, in order:
    /// <list type="number">
    /// <item><see cref="Tool.PermissionsEnabled"/> = false: Allow. Bug 7856: "Manage permissions" is the single access
    /// switch, so for MCP this also ignores the function allowlist (the stored selection is kept for when it is turned back on).</item>
    /// <item>MCP (permissions managed): a function that is not on the tool's allowlist (<see cref="McpFunctionAllowlist"/>)
    /// is Block. Bug 7856: a tool with no enabled function (NULL or <c>[]</c>) has every one of its functions on it.</item>
    /// <item>The agent's saved permission (MCP: per function; other types: whole tool); nothing (or an invalid value)
    /// saved = Block (<see cref="ToolPermissionValues.Default"/>).</item>
    /// <item>Ask becomes Block when nobody can answer an approval request in this run
    /// (<see cref="ApprovalsAvailable"/> = false: voice, sub-agent/spin-up runs, background runs).</item>
    /// </list>
    /// Plugins whose name does not carry a tool id (e.g. the spin-up publisher) are not tools: Allow.
    /// </summary>
    public sealed class ToolPermissionPolicy
    {
        private readonly Dictionary<int, ToolEntry> _tools;

        // Bug 7722: tools (non-MCP, key "toolId") / MCP functions (key "toolId:function") blocked for this run only.
        private readonly HashSet<string> _blockedForThisRun;

        private ToolPermissionPolicy(Dictionary<int, ToolEntry> tools, bool approvalsAvailable, HashSet<string> blockedForThisRun = null)
        {
            _tools = tools;
            ApprovalsAvailable = approvalsAvailable;
            _blockedForThisRun = blockedForThisRun ?? new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Bug 7722: a copy of this policy in which the given tool (non-MCP: the whole tool; MCP: that function) is Block.
        /// Used for the turn resumed after the person denied that call, so the model is not offered it again in that
        /// turn. The original policy is unchanged; a later turn builds a fresh one and asks again.
        /// </summary>
        public ToolPermissionPolicy WithBlockedForThisRun(int toolId, string mcpFunctionName)
        {
            var blocked = new HashSet<string>(_blockedForThisRun, StringComparer.Ordinal) { BlockKey(toolId, IsMcpTool(toolId) ? mcpFunctionName : null) };
            return new ToolPermissionPolicy(_tools, ApprovalsAvailable, blocked);
        }

        private static string BlockKey(int toolId, string mcpFunctionName) =>
            string.IsNullOrEmpty(mcpFunctionName) ? toolId.ToString() : $"{toolId}:{mcpFunctionName}";

        /// <summary>True when someone can approve an Ask call during this run (interactive text chat).</summary>
        public bool ApprovalsAvailable { get; }

        /// <summary>A policy that allows everything (used when an agent has no tools or none is loaded).</summary>
        public static ToolPermissionPolicy AllowAll { get; } = new([], approvalsAvailable: false);

        public static ToolPermissionPolicy Build(Agent agent, bool approvalsAvailable)
        {
            var tools = new Dictionary<int, ToolEntry>();
            foreach (var agentTool in agent?.AgentTools ?? [])
            {
                if (agentTool?.Tool == null || tools.ContainsKey(agentTool.Tool.Id))
                    continue;

                var tool = agentTool.Tool;
                tools[tool.Id] = new ToolEntry(
                    IsMcp: tool.Type == CommonToolTypeConsts.MCP,
                    PermissionsEnabled: tool.PermissionsEnabled,
                    ToolPermission: ToolPermissionValues.NormalizeOrBlock(agentTool.ToolPermission),
                    FunctionPermissions: ToolPermissionJson.ParseFunctionPermissions(agentTool.FunctionPermissions),
                    Allowlist: McpFunctionAllowlist.Parse(tool.EnabledFunctions));
            }

            return new ToolPermissionPolicy(tools, approvalsAvailable);
        }

        /// <summary>
        /// Whole-tool permission (non-MCP tools). Unknown tools and tools whose permissions are not managed are Allow;
        /// a managed tool with nothing saved is Block.
        /// </summary>
        public string GetToolPermission(int toolId)
        {
            if (_blockedForThisRun.Contains(BlockKey(toolId, null)))
                return ToolPermissionValues.Block;

            if (!_tools.TryGetValue(toolId, out var entry) || entry.IsMcp || !entry.PermissionsEnabled)
                return ToolPermissionValues.Allow;

            return ApplyApprover(entry.ToolPermission);
        }

        /// <summary>
        /// Permission of one MCP function. Bug 7856: Allow when the tool's permissions are not managed (the allowlist is
        /// ignored); otherwise Block when the function is not on the tool's allowlist (a tool with no enabled function
        /// allows all of its functions), then the agent's saved permission.
        /// </summary>
        public string GetMcpFunctionPermission(int toolId, string functionName)
        {
            if (!_tools.TryGetValue(toolId, out var entry) || !entry.IsMcp)
                return ToolPermissionValues.Block;

            if (string.IsNullOrWhiteSpace(functionName) || _blockedForThisRun.Contains(BlockKey(toolId, functionName)))
                return ToolPermissionValues.Block;

            if (!entry.PermissionsEnabled)
                return ToolPermissionValues.Allow;

            if (!entry.Allowlist.Allows(functionName))
                return ToolPermissionValues.Block;

            return ApplyApprover(entry.FunctionPermissions.TryGetValue(functionName, out var permission) ? permission : ToolPermissionValues.Default);
        }

        /// <summary>Permission of a kernel function, resolved from its plugin name (<c>plugin_{toolId}_{Type}[_suffix]</c>).</summary>
        public string GetPermission(string pluginName, string functionName)
        {
            if (!TryParseToolId(pluginName, out var toolId) || !_tools.TryGetValue(toolId, out var entry))
                return ToolPermissionValues.Allow;

            return entry.IsMcp ? GetMcpFunctionPermission(toolId, functionName) : GetToolPermission(toolId);
        }

        public bool IsMcpTool(int toolId) => _tools.TryGetValue(toolId, out var entry) && entry.IsMcp;

        /// <summary>
        /// Plugin names are built as <c>plugin_{toolId}_{toolType}</c>, optionally with a further suffix
        /// (Integration_Amais sub-plugins). Same parsing as <c>SemanticKernelLargeToolOutputFilter</c>.
        /// </summary>
        public static bool TryParseToolId(string pluginName, out int toolId)
        {
            toolId = 0;
            if (string.IsNullOrEmpty(pluginName) || !pluginName.StartsWith("plugin_", StringComparison.Ordinal))
                return false;

            var parts = pluginName.Split('_', 3);
            return parts.Length >= 2 && int.TryParse(parts[1], out toolId);
        }

        private string ApplyApprover(string permission) =>
            permission == ToolPermissionValues.Ask && !ApprovalsAvailable ? ToolPermissionValues.Block : permission;

        private sealed record ToolEntry(bool IsMcp, bool PermissionsEnabled, string ToolPermission,
            IReadOnlyDictionary<string, string> FunctionPermissions, McpFunctionAllowlist Allowlist);
    }
}
