using AMAIS.WKFLO.AI.Application.Agents.Queries.GetTenantJwtToken;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantJwtToken : BaseAsyncEndpoint<Result>
    {
        public TenantJwtToken(IWkfloMediator mediator) : base(mediator)
        {
        }

        [AllowAnonymous]
        [HttpGet("/api/v1/tenant/jwt/token")]
        [ProducesResponseType(typeof(SuccessResult<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [Tags("Tenant API", "JWT")]
        [EndpointName("TenantAgent.GetJwtToken")]
        [EndpointSummary("Get Tenant JWT Token")]
        [EndpointDescription("Get Tenant JWT Token to be used on the chat token security behaviour")]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetTenantJwtTokenQuery { }, cancellationToken));
        }
    }
}
