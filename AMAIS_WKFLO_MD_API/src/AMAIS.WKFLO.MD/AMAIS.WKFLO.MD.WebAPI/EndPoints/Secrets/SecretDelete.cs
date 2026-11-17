using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.DeleteSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Secrets
{
    public class SecretDelete : BaseAsyncEndpoint<int, Result>
    {
        public SecretDelete(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpDelete("/api/v1/secret/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Main Area", "Secret")]
        [EndpointName("Secret.Delete")]
        [EndpointSummary("Delete a Secret")]
        [EndpointDescription("Delete a Secret")]
        [ProducesResponseType(typeof(SuccessResult<SecretDeleteResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new DeleteSecretCommand() { Id = id }, cancellationToken));
        }
    }
}
