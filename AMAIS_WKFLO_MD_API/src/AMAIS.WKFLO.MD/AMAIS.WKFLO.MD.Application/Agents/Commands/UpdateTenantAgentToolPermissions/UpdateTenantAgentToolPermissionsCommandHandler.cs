using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Common;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgentToolPermissions
{
    public class UpdateTenantAgentToolPermissionsCommandHandler : IRequestHandler<UpdateTenantAgentToolPermissionsCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public UpdateTenantAgentToolPermissionsCommandHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(UpdateTenantAgentToolPermissionsCommand command, CancellationToken cancellationToken)
        {
            var agent = await _db.TenantAgents
                .Include(a => a.AgentTools)
                    .ThenInclude(at => at.Tool)
                .Where(a => a.Id == command.Id && a.Tenant.Name == _tenantContext.TenantName && !a.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent == null)
                return new NotFoundResult(nameof(TenantAgent), ErrorConst.AgentNotFound);

            var linkedByToolId = AgentToolPermissionsBuilder.GetLinkedTools(agent).ToDictionary(at => at.ToolId);
            var inputs = command.Tools ?? [];

            // Validate everything first, so a bad entry never leaves the agent half-updated.
            foreach (var input in inputs)
            {
                var error = Validate(input, linkedByToolId);
                if (error != null)
                    return new InvalidResult(nameof(UpdateTenantAgentToolPermissionsCommand.Tools), error);
            }

            foreach (var input in inputs)
            {
                var agentTool = linkedByToolId[input.ToolId];

                if (agentTool.Tool.Type == CommonToolTypeConsts.MCP)
                {
                    agentTool.ToolPermission = null;
                    agentTool.FunctionPermissions = ToolPermissionJson.SerializeFunctionPermissions(
                        (input.Functions ?? [])
                            .GroupBy(f => f.Name, StringComparer.Ordinal)
                            .ToDictionary(g => g.Key, g => g.Last().Permission, StringComparer.Ordinal));
                }
                else
                {
                    agentTool.ToolPermission = input.Permission;
                    agentTool.FunctionPermissions = null;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TenantAgentToolPermissionsResult>(await AgentToolPermissionsBuilder.BuildAsync(_db, agent, cancellationToken));
        }

        /// <summary>Returns the error code for an invalid entry, or null when it can be applied.</summary>
        internal static string Validate(UpdateTenantAgentToolPermissionInput input, IReadOnlyDictionary<int, AgentTool> linkedByToolId)
        {
            if (input == null || !linkedByToolId.TryGetValue(input.ToolId, out var agentTool))
                return ErrorConst.ToolNotLinkedToAgent;

            var tool = agentTool.Tool;
            if (!tool.PermissionsEnabled)
                return ErrorConst.ToolPermissionsNotEnabled;

            if (input.Permission != null && !ToolPermissionValues.IsValid(input.Permission))
                return ErrorConst.ToolPermissionInvalid;

            var functions = input.Functions ?? [];
            if (functions.Any(f => f == null || string.IsNullOrWhiteSpace(f.Name) || !ToolPermissionValues.IsValid(f.Permission)))
                return ErrorConst.ToolPermissionInvalid;

            if (tool.Type == CommonToolTypeConsts.MCP)
            {
                if (input.Permission != null)
                    return ErrorConst.ToolPermissionNotForMcp;

                // Bug 7856: with no enabled function every function is allowed, so a permission may be saved for any
                // function the tool reported (the same set the permissions view lists).
                var allowlist = McpFunctionAllowlist.Parse(tool.EnabledFunctions);
                var discovered = allowlist.AllowsAll
                    ? new HashSet<string>(ToolPermissionJson.ParseDiscoveredFunctions(tool.DiscoveredFunctions).Select(d => d.Name), StringComparer.Ordinal)
                    : null;
                if (functions.Any(f => !allowlist.Allows(f.Name) || (discovered != null && !discovered.Contains(f.Name))))
                    return ErrorConst.ToolFunctionNotEnabled;
            }
            else if (functions.Count > 0)
            {
                return ErrorConst.ToolFunctionsOnlyForMcp;
            }

            return null;
        }
    }
}
