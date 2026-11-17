using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantToolFunctions
{
    public class UpdateTenantToolFunctionsCommandHandler : IRequestHandler<UpdateTenantToolFunctionsCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public UpdateTenantToolFunctionsCommandHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(UpdateTenantToolFunctionsCommand command, CancellationToken cancellationToken)
        {
            var tool = await _db.TenantTools
                .Where(m => m.Id == command.Id && m.Tenant.Name == _tenantContext.TenantName && !m.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (tool == null)
                return new NotFoundResult(nameof(TenantTool), ErrorConst.ToolNotFound);

            if (tool.Type != CommonToolTypeConsts.MCP)
                return new InvalidResult(nameof(Tool.Type), ErrorConst.ToolMustBeMcp);

            // Only functions the admin could actually see may be enabled: the latest snapshot plus anything
            // already enabled (a "Removed" function can stay enabled until it is turned off explicitly).
            var enableable = ToolFunctionsBuilder.GetEnableableNames(tool);
            var requested = (command.EnabledFunctions ?? []).Distinct(StringComparer.Ordinal).ToList();
            if (requested.Any(name => !enableable.Contains(name)))
                return new InvalidResult(nameof(UpdateTenantToolFunctionsCommand.EnabledFunctions), ErrorConst.ToolFunctionNotAvailable);

            // WI 7726: display values can only be set on functions of the latest snapshot (that is where they live).
            var snapshot = ToolPermissionJson.ParseDiscoveredFunctions(tool.DiscoveredFunctions);
            // A null entry or name is never a function of the snapshot: rejected like an unknown name (not dropped).
            var displayInputs = command.Functions ?? [];
            var snapshotNames = new HashSet<string>(snapshot.Select(f => f.Name), StringComparer.Ordinal);
            if (displayInputs.Any(f => f?.Name == null || !snapshotNames.Contains(f.Name)))
                return new InvalidResult(nameof(UpdateTenantToolFunctionsCommand.Functions), ErrorConst.ToolFunctionNotAvailable);

            var displayUpdates = displayInputs
                .GroupBy(f => f.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

            tool.EnabledFunctions = ToolPermissionJson.SerializeEnabledFunctions(requested);

            // Saving means every listed function has now been reviewed: none of them is "New" any more.
            if (tool.DiscoveredFunctions != null)
            {
                foreach (var function in snapshot)
                {
                    if (displayUpdates.TryGetValue(function.Name, out var update))
                    {
                        function.DisplayName = McpFunctionDescriptor.NormalizeDisplayValue(update.DisplayName);
                        function.DisplayDescription = McpFunctionDescriptor.NormalizeDisplayValue(update.DisplayDescription);
                    }
                }

                tool.DiscoveredFunctions = ToolPermissionJson.SerializeDiscoveredFunctions(ToolFunctionsBuilder.MarkAllReviewed(snapshot));
            }

            // Draft/publish model: the published copy only changes when the tool is published.

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<ToolFunctionsResult>(ToolFunctionsBuilder.Build(tool));
        }
    }
}
