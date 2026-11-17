using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions
{
    /// <summary>
    /// WI 7447 / Bug 7856: the MCP function allowlist of one tool (<see cref="Tool.EnabledFunctions"/>), read with the
    /// single rule every consumer must share: when no function is enabled (NULL, <c>[]</c> or a malformed value) no
    /// allowlist is configured and every function of the tool is on it. A non-empty list allows only the listed
    /// names (ordinal, case-sensitive: MCP names are case-sensitive). The allowlist only applies while the tool's
    /// permissions are managed (<see cref="Tool.PermissionsEnabled"/>); callers check that flag first.
    /// </summary>
    public sealed class McpFunctionAllowlist
    {
        private readonly HashSet<string> _enabled;

        private McpFunctionAllowlist(HashSet<string> enabled) => _enabled = enabled;

        /// <summary>Reads the stored <see cref="Tool.EnabledFunctions"/> JSON.</summary>
        public static McpFunctionAllowlist Parse(string enabledFunctionsJson) =>
            new(ToolPermissionJson.ParseEnabledFunctions(enabledFunctionsJson));

        /// <summary>True when no function is enabled, which means every function of the tool is allowed.</summary>
        public bool AllowsAll => _enabled.Count == 0;

        /// <summary>True when <paramref name="functionName"/> is on the allowlist (always, for a non-blank name, when <see cref="AllowsAll"/>).</summary>
        public bool Allows(string functionName) =>
            !string.IsNullOrWhiteSpace(functionName) && (AllowsAll || _enabled.Contains(functionName));
    }
}
