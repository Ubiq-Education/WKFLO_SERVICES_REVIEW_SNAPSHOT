using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Agents.Common
{
    /// <summary>WI 7575: builds the agent tool permissions view shared by the GET and PUT handlers.</summary>
    public static class AgentToolPermissionsBuilder
    {
        public static async Task<TenantAgentToolPermissionsResult> BuildAsync(IMasterDataDbContext db, TenantAgent agent, CancellationToken cancellationToken)
        {
            var linked = GetLinkedTools(agent);
            var toolIds = linked.Select(at => at.ToolId).ToList();

            // Other tenant agents sharing the same tools ("Also used by").
            var usedBy = await db.AgentTools
                    .Where(at => toolIds.Contains(at.ToolId)
                        && at.AgentId != agent.Id
                        && at.Agent is TenantAgent
                        && !at.Agent.IsDeleted)
                    .Select(at => new { at.ToolId, AgentId = at.Agent.Id, AgentName = at.Agent.Name })
                    .ToListAsync(cancellationToken);

            var tools = linked
                .Select(at => BuildTool(at, usedBy
                    .Where(u => u.ToolId == at.ToolId)
                    .OrderBy(u => u.AgentName)
                    .Select(u => new TenantAgentToolUsedByAgentResult { Id = u.AgentId, Name = u.AgentName })
                    .ToList()))
                .OrderBy(t => t.Name)
                .ThenBy(t => t.ToolId)
                .ToList();

            return new TenantAgentToolPermissionsResult { Tools = tools };
        }

        /// <summary>The agent's links to non-deleted runnable tools (soft-deleted tools are hidden by the query filter).</summary>
        public static List<AgentTool> GetLinkedTools(Agent agent) =>
            (agent.AgentTools ?? [])
                .Where(at => at.Tool is RunableTool && !at.Tool.IsDeleted)
                .ToList();

        internal static TenantAgentToolPermissionResult BuildTool(AgentTool agentTool, List<TenantAgentToolUsedByAgentResult> usedBy)
        {
            var tool = (RunableTool)agentTool.Tool;
            var isMcp = tool.Type == CommonToolTypeConsts.MCP;

            var result = new TenantAgentToolPermissionResult
            {
                ToolId = tool.Id,
                Name = tool.Name,
                Description = tool.Description,
                Type = tool.Type,
                IsActive = tool.IsActive,
                PermissionsEnabled = tool.PermissionsEnabled,
                UsedByAgents = usedBy ?? [],
                Permission = !isMcp && tool.PermissionsEnabled ? ToolPermissionValues.NormalizeOrBlock(agentTool.ToolPermission) : null
            };

            if (isMcp)
            {
                // Bug 7856: the allowlist only applies when permissions are managed; a tool with no enabled function
                // allows (and so lists) every discovered function.
                var allowlist = McpFunctionAllowlist.Parse(tool.EnabledFunctions);
                var saved = ToolPermissionJson.ParseFunctionPermissions(agentTool.FunctionPermissions);

                result.Functions = ToolPermissionJson.ParseDiscoveredFunctions(tool.DiscoveredFunctions)
                    .Where(f => !tool.PermissionsEnabled || allowlist.Allows(f.Name))
                    .Select(f => new TenantAgentToolFunctionPermissionResult
                    {
                        Name = f.Name,
                        Description = f.Description,
                        DisplayName = f.DisplayName,
                        DisplayDescription = f.DisplayDescription,
                        Permission = tool.PermissionsEnabled
                            ? (saved.TryGetValue(f.Name, out var permission) ? permission : ToolPermissionValues.Default)
                            : null
                    })
                    .ToList();
            }

            return result;
        }
    }
}
