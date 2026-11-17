using AMAIS.WKFLO.AI.Application.Tools.Commands.DiscoverTenantToolFunctions;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Tools
{
    public class TenantToolFunctionsDiscover : BaseAsyncEndpoint<int, Result>
    {
        public TenantToolFunctionsDiscover(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/tool/{id}/functions/discover")]
        [ProducesResponseType(typeof(SuccessResult<ToolFunctionsResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Tenant API", "Tool")]
        [EndpointName("TenantTool.DiscoverFunctions")]
        [EndpointSummary("Refresh the function list of an MCP Tenant Tool from its server")]
        [EndpointDescription("Connects to the MCP server with the tool's configuration (for a per-user OAuth tool: the calling user's own connection), runs tools/list and saves the result as the tool's function snapshot. Returns the same shape as the MD API GET tenant/tool/{id}/functions. 400 WKFLOAI0405 when the OAuth connection is missing, WKFLOAI0406 when the server cannot be reached.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new DiscoverTenantToolFunctionsCommand { Id = id }, cancellationToken));
        }
    }
}
