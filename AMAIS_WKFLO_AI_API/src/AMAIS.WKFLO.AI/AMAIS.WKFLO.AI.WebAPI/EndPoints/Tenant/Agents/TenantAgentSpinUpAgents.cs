using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.GetAvailableSpinUpAgentsByTenantAgentId;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentSpinUpAgents : BaseAsyncEndpoint<int, Result>
    {
        public TenantAgentSpinUpAgents(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{id}/spin-up/agents")]
        [ProducesResponseType(typeof(SuccessResult<List<AgentSpinUpTargetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [Tags("Tenant API", "Agent Chat")]
        [EndpointName("TenantAgent.GetAvailableSpinUpAgents")]
        [EndpointSummary("Get Available Spin-Up Agents")]
        [EndpointDescription("Lists the name and chat icon URL of every other active runnable agent in the same tenant — the data needed to build an @name target picker for publish_agent_event.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            var query = new GetAvailableSpinUpAgentsByTenantAgentIdQuery { Id = id };
            return AmaisWkfloResult(await Mediator.Send(query, cancellationToken));
        }
    }
}
