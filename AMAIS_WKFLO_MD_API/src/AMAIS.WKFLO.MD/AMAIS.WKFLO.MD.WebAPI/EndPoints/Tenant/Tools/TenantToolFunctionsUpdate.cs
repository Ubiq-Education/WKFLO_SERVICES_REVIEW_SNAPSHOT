using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantToolFunctions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tools
{
    public class TenantToolFunctionsUpdate : BaseAsyncEndpoint<int, UpdateTenantToolFunctionsCommand, Result>
    {
        public TenantToolFunctionsUpdate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPut("/api/v1/tenant/tool/{id}/functions")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tool")]
        [EndpointName("TenantTool.UpdateFunctions")]
        [EndpointSummary("Save the MCP function allowlist of a Tenant Tool")]
        [EndpointDescription("Replaces the enabled MCP functions of the tool (anything not listed is disabled) on the tenant tool. Optional `functions` sets friendly display name/description per function (display only; omit to leave them unchanged). Reaches the published copy when the tool (or an agent using it) is published. MCP tools only.")]
        [ProducesResponseType(typeof(SuccessResult<ToolFunctionsResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, UpdateTenantToolFunctionsCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
