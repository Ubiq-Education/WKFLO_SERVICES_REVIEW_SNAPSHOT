using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using AMAIS.WKFLO.MD.Application.AICredits.Queries.GetAICreditByAIModelId;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.AICredits
{
    public class AICreditGetByAIModelId : BaseAsyncEndpoint<int, Result>
    {
        public AICreditGetByAIModelId(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpGet("/api/v1/ai-model/{aiModelId}/ai-credit")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Main Area", "AICredit")]
        [EndpointName("AICredit.GetByAIModelId")]
        [EndpointSummary("Get the AICredit configured for an AIModel")]
        [EndpointDescription("Get the AICredit configured for an AIModel. Returns 404 when the AIModel has no AICredit configured yet, so the caller can tell 'not configured' apart from an actual error.")]
        [ProducesResponseType(typeof(SuccessResult<AICreditGetResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int aiModelId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetAICreditByAIModelIdQuery() { AIModelId = aiModelId }, cancellationToken));
        }
    }
}
