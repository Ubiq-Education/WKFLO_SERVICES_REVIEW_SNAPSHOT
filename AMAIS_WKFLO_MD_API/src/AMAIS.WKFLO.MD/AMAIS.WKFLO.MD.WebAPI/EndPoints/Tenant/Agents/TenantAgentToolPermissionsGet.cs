using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentToolPermissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentToolPermissionsGet : BaseAsyncEndpoint<int, Result>
    {
        public TenantAgentToolPermissionsGet(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{id}/tools/permissions")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.GetToolPermissions")]
        [EndpointSummary("Get the tool permissions of a Tenant Agent")]
        [EndpointDescription("Lists every tool linked to the agent with its permission (Allow, Ask or Block): one permission per tool for non-MCP tools, one per enabled function for MCP tools.")]
        [ProducesResponseType(typeof(SuccessResult<TenantAgentToolPermissionsResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetTenantAgentToolPermissionsQuery() { Id = id }, cancellationToken));
        }
    }
}
