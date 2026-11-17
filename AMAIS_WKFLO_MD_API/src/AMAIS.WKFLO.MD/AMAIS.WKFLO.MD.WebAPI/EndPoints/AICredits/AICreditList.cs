using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using AMAIS.WKFLO.MD.Application.AICredits.Queries.ListAICredits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.AICredits
{
    public class AICreditList : BaseAsyncEndpoint<Result>
    {
        public AICreditList(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [HttpGet("/api/v1/ai-credits")]
        [Tags("Main Area", "AICredit")]
        [EndpointName("AICredit.List")]
        [EndpointSummary("List the AICredits")]
        [EndpointDescription("List the AICredits")]
        [ProducesResponseType(typeof(SuccessResult<List<AICreditGetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListAICreditsQuery(), cancellationToken));
        }
    }
}
