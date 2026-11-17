using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.InstallMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Agents
{
    public class MarketplaceAgentInstall : BaseAsyncEndpoint<int, Result>
    {
        public MarketplaceAgentInstall(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/marketplace/agents/{id}/install")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Agent")]
        [EndpointName("MarketplaceAgent.Install")]
        [EndpointSummary("Install a Marketplace Agent")]
        [EndpointDescription("Install a Marketplace Agent into the current tenant as a new Tenant Agent")]
        [ProducesResponseType(typeof(SuccessResult<TenantAgentCreateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            var command = new InstallMarketplaceAgentCommand { Id = id };
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
