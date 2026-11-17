using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using AMAIS.WKFLO.MD.Application.AICredits.Queries.GetAICreditById;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.AICredits
{
    public class AICreditGet : BaseAsyncEndpoint<int, Result>
    {
        public AICreditGet(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpGet("/api/v1/ai-credit/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Main Area", "AICredit")]
        [EndpointName("AICredit.Get")]
        [EndpointSummary("Get an AICredit by Id")]
        [EndpointDescription("Get an AICredit by Id")]
        [ProducesResponseType(typeof(SuccessResult<AICreditGetResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetAICreditByIdQuery() { Id = id }, cancellationToken));
        }
    }
}
