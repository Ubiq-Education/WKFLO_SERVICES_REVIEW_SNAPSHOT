using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Models;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Queries.GetClassificationJobStatus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.ClassificationJobs
{
    public class TenantClassificationJobGet : BaseAsyncEndpoint<int, Result>
    {
        public TenantClassificationJobGet(IWkfloMediator mediator) : base(mediator) { }

        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{agentId}/classification-job")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "ClassificationJob")]
        [EndpointName("ClassificationJob.Get")]
        [EndpointSummary("Get the classification job status for an agent")]
        [EndpointDescription("Returns the most recent topic classification job for the given agent")]
        [ProducesResponseType(typeof(SuccessResult<ClassificationJobStatusResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(AMAIS.WKFLO.Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int agentId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetClassificationJobStatusQuery { AgentId = agentId }, cancellationToken));
        }
    }
}
