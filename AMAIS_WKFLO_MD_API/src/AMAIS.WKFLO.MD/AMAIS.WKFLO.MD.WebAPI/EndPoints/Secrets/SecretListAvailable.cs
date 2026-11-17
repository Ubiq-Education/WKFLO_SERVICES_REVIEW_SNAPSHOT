using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using AMAIS.WKFLO.MD.Application.Secrets.Queries.ListAvailableSecrets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Secrets
{
    public class SecretListAvailable : BaseAsyncEndpoint<Result>
    {
        public SecretListAvailable(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpGet("/api/v1/secrets/available")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Main Area", "Secret")]
        [EndpointName("Secret.ListAvailable")]
        [EndpointSummary("List the available Secrets")]
        [EndpointDescription("List the available Secrets")]
        [ProducesResponseType(typeof(SuccessResult<List<SecretGetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListAvailableSecretsQuery(), cancellationToken));
        }
    }
}
