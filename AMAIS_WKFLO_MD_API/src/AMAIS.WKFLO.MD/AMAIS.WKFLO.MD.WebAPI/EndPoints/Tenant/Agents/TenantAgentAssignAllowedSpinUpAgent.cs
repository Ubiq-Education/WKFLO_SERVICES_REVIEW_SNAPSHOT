using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.AssignAllowedSpinUpAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentAssignAllowedSpinUpAgent : BaseAsyncEndpoint<int, int, Result>
    {
        public TenantAgentAssignAllowedSpinUpAgent(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{id}/spin-up/agents/{allowedAgentId}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.AssignAllowedSpinUpAgent")]
        [EndpointSummary("Allow an Agent to spin up another Agent")]
        [EndpointDescription("Add an Agent to another Agent's spin-up allowlist, so it can be notified via publish_agent_event")]
        [ProducesResponseType(typeof(SuccessResult<AllowedSpinUpAgentResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromRoute] int allowedAgentId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new AssignAllowedSpinUpAgentCommand() { Id = id, AllowedSpinUpAgentId = allowedAgentId }, cancellationToken));
        }
    }
}
