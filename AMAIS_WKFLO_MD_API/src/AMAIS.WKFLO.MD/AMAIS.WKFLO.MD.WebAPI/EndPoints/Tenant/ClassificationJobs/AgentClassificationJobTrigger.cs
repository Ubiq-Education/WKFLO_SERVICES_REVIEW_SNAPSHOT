using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Commands.ScheduleClassificationJob;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.ClassificationJobs
{
    public class AgentClassificationJobTrigger : BaseAsyncEndpoint<int, Result>
    {
        public AgentClassificationJobTrigger(IWkfloMediator mediator) : base(mediator) { }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{agentId}/classification-job")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "ClassificationJob")]
        [EndpointName("ClassificationJob.Trigger")]
        [EndpointSummary("Trigger a topic classification job for an agent")]
        [EndpointDescription("Schedules a topic classification job for the given agent")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(AMAIS.WKFLO.Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int agentId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ScheduleClassificationJobCommand { AgentId = agentId }, cancellationToken));
        }
    }
}
