using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.AIModels.Queries.GetAIModelById;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.AIModels
{
    public class AIModelGet : BaseAsyncEndpoint<int, Result>
    {
        public AIModelGet(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpGet("/api/v1/ai-model/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Main Area", "AIModel")]
        [EndpointName("AIModel.Get")]
        [EndpointSummary("Get an AIModel by Id")]
        [EndpointDescription("Get an AIModel by Id")]
        [ProducesResponseType(typeof(SuccessResult<AIModelGetResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetAIModelByIdQuery() { Id = id }, cancellationToken));
        }
    }
}
