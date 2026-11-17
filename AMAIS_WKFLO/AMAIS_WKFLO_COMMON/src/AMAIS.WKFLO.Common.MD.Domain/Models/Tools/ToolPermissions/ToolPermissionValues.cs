namespace AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions
{
    /// <summary>
    /// WI 7575: the three agent tool permission values, stored verbatim in
    /// <see cref="Entities.AgentTool.ToolPermission"/> and as the values of
    /// <see cref="Entities.AgentTool.FunctionPermissions"/>.
    /// </summary>
    public static class ToolPermissionValues
    {
        /// <summary>The agent may always call the tool/function.</summary>
        public const string Allow = "Allow";

        /// <summary>Every call needs approval from whoever is chatting (unless a conversation grant exists).</summary>
        public const string Ask = "Ask";

        /// <summary>The tool/function is never offered to the model.</summary>
        public const string Block = "Block";

        public static readonly IReadOnlyList<string> All = [Allow, Ask, Block];

        /// <summary>True for exactly one of <see cref="All"/> (case-sensitive, the stored form).</summary>
        public static bool IsValid(string value) => value is Allow or Ask or Block;

        /// <summary>
        /// The permission of a permission-managed tool/function when nothing (or something invalid) has been saved:
        /// <see cref="Block"/> (decision 3 of the feature plan). Enabling a tool's permissions, or an MCP function,
        /// never writes a value - an admin must choose Allow or Ask explicitly. Tools with
        /// <see cref="Entities.Tool.PermissionsEnabled"/> = false are not permission-managed and behave as Allow.
        /// </summary>
        public const string Default = Block;

        /// <summary>Reads a stored value back; null/empty/unknown values fall back to <see cref="Default"/> (Block).</summary>
        public static string NormalizeOrBlock(string value) => IsValid(value) ? value : Default;
    }
}
