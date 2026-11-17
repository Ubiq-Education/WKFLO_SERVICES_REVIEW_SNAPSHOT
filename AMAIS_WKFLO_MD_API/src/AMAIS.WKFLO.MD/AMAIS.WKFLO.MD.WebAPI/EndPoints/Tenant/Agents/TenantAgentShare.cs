using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.ShareTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentShare : BaseAsyncEndpoint<int, Result>
    {
        public TenantAgentShare(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{id}/share")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.Share")]
        [EndpointSummary("Share a Tenant Agent to Marketplace")]
        [EndpointDescription("Shares a Tenant Agent to the Marketplace, making it available for other tenants")]
        [ProducesResponseType(typeof(SuccessResult<TenantAgentShareResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            var command = new ShareTenantAgentCommand { Id = id };
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
