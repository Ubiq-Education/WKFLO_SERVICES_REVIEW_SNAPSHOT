using AMAIS.WKFLO.AI.Application.Agents.Commands.RunTenantAgentById;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentRun : BaseAsyncEndpoint<int, RunTenantAgentByIdCommand, Result>
    {
        public TenantAgentRun(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{id}/run")]
        [RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
        [ProducesResponseType(typeof(SuccessResult<TenantAgentDataResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.Get")]
        [EndpointSummary("Get a Tenant Agent by Id")]
        [EndpointDescription("Get a Tenant Agent by Id")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, RunTenantAgentByIdCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            command.Input?.Files.ClearServerOnlyFields();
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}

