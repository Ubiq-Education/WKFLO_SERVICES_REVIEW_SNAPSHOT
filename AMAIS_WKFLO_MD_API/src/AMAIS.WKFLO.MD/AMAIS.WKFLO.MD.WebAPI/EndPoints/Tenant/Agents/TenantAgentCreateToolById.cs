using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgentToolById;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentCreateToolById : BaseAsyncEndpoint<int, int, Result>
    {
        public TenantAgentCreateToolById(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{id}/tool/{toolId}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.CreateToolById")]
        [EndpointSummary("Create a Tenant Agent Tool by Id")]
        [EndpointDescription("Create a Tenant Agent Tool by Id")]
        [ProducesResponseType(typeof(SuccessResult<TenantAgentToolCreateResult>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromRoute] int toolId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new CreateTenantAgentToolByIdCommand() { Id = id, ToolId = toolId }, cancellationToken));
        }
    }
}
