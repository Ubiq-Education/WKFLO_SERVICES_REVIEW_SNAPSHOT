using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.PublishTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentPublish : BaseAsyncEndpoint<int, Result>
    {
        public TenantAgentPublish(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{id}/publish")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.Publish")]
        [EndpointSummary("Publish a Tenant Agent")]
        [EndpointDescription("Publishes a Tenant Agent, creating a published version that can be shared to the marketplace")]
        [ProducesResponseType(typeof(SuccessResult<TenantAgentPublishResult>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            var command = new PublishTenantAgentCommand { Id = id };
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
