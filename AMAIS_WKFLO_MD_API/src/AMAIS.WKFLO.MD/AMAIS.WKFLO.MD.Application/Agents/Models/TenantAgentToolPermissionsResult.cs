namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    /// <summary>WI 7575: GET/PUT <c>tenant/agent/{id}/tools/permissions</c>.</summary>
    public class TenantAgentToolPermissionsResult
    {
        /// <summary>Every non-deleted tool linked to the agent (no type filtering), ordered by name.</summary>
        public List<TenantAgentToolPermissionResult> Tools { get; set; } = [];
    }

    public class TenantAgentToolPermissionResult
    {
        public int ToolId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public bool IsActive { get; set; }
        public bool PermissionsEnabled { get; set; }

        /// <summary>Other non-deleted tenant agents linked to the same tool.</summary>
        public List<TenantAgentToolUsedByAgentResult> UsedByAgents { get; set; } = [];

        /// <summary>
        /// Non-MCP tools only: Allow | Ask | Block (Block when nothing was saved). Null for MCP tools and when
        /// <see cref="PermissionsEnabled"/> is false.
        /// </summary>
        public string Permission { get; set; }

        /// <summary>
        /// MCP tools only (null for every other type): the tool's ENABLED functions that are in its latest
        /// snapshot, in snapshot order.
        /// </summary>
        public List<TenantAgentToolFunctionPermissionResult> Functions { get; set; }
    }

    public class TenantAgentToolUsedByAgentResult
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class TenantAgentToolFunctionPermissionResult
    {
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>WI 7726: admin-chosen friendly name, or null (display <see cref="Name"/>). Display only - <see cref="Name"/> stays the key.</summary>
        public string DisplayName { get; set; }

        /// <summary>WI 7726: admin-chosen friendly description, or null (display <see cref="Description"/>).</summary>
        public string DisplayDescription { get; set; }

        /// <summary>Allow | Ask | Block (Block when nothing was saved); null when the tool's permissions are off.</summary>
        public string Permission { get; set; }
    }
}
