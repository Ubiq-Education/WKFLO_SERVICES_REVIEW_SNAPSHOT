using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.AIModels.Queries.ListAIModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.AIModels
{
    public class AIModelList : BaseAsyncEndpoint<Result>
    {
        public AIModelList(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [HttpGet("/api/v1/ai-models")]
        //[Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Main Area", "AIModel")]
        [EndpointName("AIModel.List")]
        [EndpointSummary("List the AIModels")]
        [EndpointDescription("List the AIModels")]
        [ProducesResponseType(typeof(SuccessResult<List<AIModelGetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListAIModelsQuery(), cancellationToken));
        }
    }
}
