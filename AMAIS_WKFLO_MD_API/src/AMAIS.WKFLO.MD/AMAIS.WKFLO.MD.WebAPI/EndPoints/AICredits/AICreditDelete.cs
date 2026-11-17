using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Commands.DeleteAICredit;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.AICredits
{
    public class AICreditDelete : BaseAsyncEndpoint<int, Result>
    {
        public AICreditDelete(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpDelete("/api/v1/ai-credit/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Main Area", "AICredit")]
        [EndpointName("AICredit.Delete")]
        [EndpointSummary("Delete an AICredit")]
        [EndpointDescription("Delete an AICredit")]
        [ProducesResponseType(typeof(SuccessResult<AICreditDeleteResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new DeleteAICreditCommand() { Id = id }, cancellationToken));
        }
    }
}
