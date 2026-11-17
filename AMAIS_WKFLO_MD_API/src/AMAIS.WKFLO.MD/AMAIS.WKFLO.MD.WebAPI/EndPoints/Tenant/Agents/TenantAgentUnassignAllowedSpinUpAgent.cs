using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UnassignAllowedSpinUpAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentUnassignAllowedSpinUpAgent : BaseAsyncEndpoint<int, int, Result>
    {
        public TenantAgentUnassignAllowedSpinUpAgent(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpDelete("/api/v1/tenant/agent/{id}/spin-up/agents/{allowedAgentId}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.UnassignAllowedSpinUpAgent")]
        [EndpointSummary("Remove an Agent from another Agent's spin-up allowlist")]
        [EndpointDescription("Remove an Agent from another Agent's spin-up allowlist, so it can no longer be notified via publish_agent_event")]
        [ProducesResponseType(typeof(SuccessResult<AllowedSpinUpAgentResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromRoute] int allowedAgentId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new UnassignAllowedSpinUpAgentCommand() { Id = id, AllowedSpinUpAgentId = allowedAgentId }, cancellationToken));
        }
    }
}
