using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgentToolPermissions
{
    /// <summary>
    /// WI 7575: replaces the agent's permission settings of every tool included in <see cref="Tools"/>
    /// (tools not included keep their settings). Does not publish - the caller publishes the agent so the
    /// published copy picks the new settings up.
    /// </summary>
    public class UpdateTenantAgentToolPermissionsCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        /// <summary>Agent id, set from the route.</summary>
        [JsonIgnore]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public List<UpdateTenantAgentToolPermissionInput> Tools { get; set; } = [];
    }

    public class UpdateTenantAgentToolPermissionInput
    {
        public int ToolId { get; set; }

        /// <summary>Non-MCP tools: Allow | Ask | Block. Null clears the choice, which reads back as Block. Must be null for MCP tools.</summary>
        public string Permission { get; set; }

        /// <summary>
        /// MCP tools: the full set of per-function permissions (functions not listed read back as Block).
        /// Every name must be an enabled function of the tool. Must be null or empty for other tool types.
        /// </summary>
        public List<UpdateTenantAgentToolFunctionPermissionInput> Functions { get; set; }
    }

    public class UpdateTenantAgentToolFunctionPermissionInput
    {
        public string Name { get; set; }

        /// <summary>Allow | Ask | Block.</summary>
        public string Permission { get; set; }
    }
}
