namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class AgentTool
    {
        public int AgentId { get; set; }
        public Agent Agent { get; set; }

        public int ToolId { get; set; }
        public Tool Tool { get; set; }

        /// <summary>
        /// WI 7575 (non-MCP tools): the agent's permission for the whole tool - one of
        /// <see cref="Models.Tools.ToolPermissions.ToolPermissionValues"/>. Null (nothing chosen) means Block when the tool's
        /// permissions are managed (<see cref="Tool.PermissionsEnabled"/>); otherwise it is ignored.
        /// </summary>
        public string ToolPermission { get; set; }

        /// <summary>
        /// WI 7575 (MCP tools): JSON object mapping an MCP function name to its permission
        /// (<c>{ "name": "Allow|Ask|Block" }</c>). A function that is missing from the map is Block when the tool's
        /// permissions are managed; otherwise the map is ignored.
        /// </summary>
        public string FunctionPermissions { get; set; }
    }
}
