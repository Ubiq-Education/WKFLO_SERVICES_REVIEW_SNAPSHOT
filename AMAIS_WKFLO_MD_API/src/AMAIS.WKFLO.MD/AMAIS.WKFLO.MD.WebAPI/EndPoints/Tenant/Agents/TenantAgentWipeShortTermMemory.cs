using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.WipeShortTermMemoryTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentWipeShortTermMemory : BaseAsyncEndpoint<int, Result>
    {
        public TenantAgentWipeShortTermMemory(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpDelete("/api/v1/tenant/agent/{id}/memory/short-term")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.WipeShortTermMemory")]
        [EndpointSummary("Wipe Short Term Memory of a Tenant Agent")]
        [EndpointDescription("Deletes all short term memory (chat history) for a Tenant Agent across all users")]
        [ProducesResponseType(typeof(SuccessResult<TenantAgentWipeShortTermMemoryResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new WipeShortTermMemoryTenantAgentCommand() { Id = id }, cancellationToken));
        }
    }
}
