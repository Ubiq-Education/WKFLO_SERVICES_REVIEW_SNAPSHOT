using AMAIS.WKFLO.AI.Application.Agents.Queries.GetTenantAgentChatConfiguration;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentChatConfiguration : BaseAsyncEndpoint<int, Result>
    {
        public TenantAgentChatConfiguration(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{id}/chat/configuration")]
        [ProducesResponseType(typeof(SuccessResult<AgentChatSettingResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [Tags("Tenant API", "Agent Chat")]
        [EndpointName("TenantAgent.GetChatConfiguration")]
        [EndpointSummary("Get Tenant Agent Chat Configuration")]
        [EndpointDescription("Get configuration data required by the tenant agent chat.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            var query = new GetTenantAgentChatSettingQuery { Id = id };
            return AmaisWkfloResult(await Mediator.Send(query, cancellationToken));
        }
    }
}
